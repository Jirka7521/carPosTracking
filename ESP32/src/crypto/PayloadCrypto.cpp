#include "crypto/PayloadCrypto.h"

#include <cstring>
#include <vector>

#include "cJSON.h"
#include "esp_log.h"
#include "mbedtls/base64.h"
#include "mbedtls/gcm.h"
#include "mbedtls/platform_util.h"
#include "mbedtls/rsa.h"

static const char* TAG = "PayloadCrypto";

namespace {

  constexpr size_t kAesKeyBytes = 32;  // AES-256
  constexpr size_t kNonceBytes  = 12;  // 96-bit GCM nonce
  constexpr size_t kTagBytes    = 16;  // 128-bit GCM authentication tag
  constexpr char   kAlgorithm[] = "RSA-OAEP-SHA256+AES-256-GCM";

  // Correlation id: 8 random bytes rendered as 16 LOWERCASE hex characters.
  // The API validates this exact shape (EnvelopeCodec::IdLength and its hex
  // check), so the two sides must agree - widening it here breaks ingestion.
  constexpr size_t kEnvelopeIdBytes = 8;
  constexpr size_t kEnvelopeIdChars = kEnvelopeIdBytes * 2;

  // Encode `len` raw bytes to a base64 std::string (empty string on failure).
  std::string base64Encode(const unsigned char* data, size_t len) {
    // First call with a null buffer asks mbedTLS for the required size.
    size_t needed = 0;
    mbedtls_base64_encode(nullptr, 0, &needed, data, len);

    std::string out(needed, '\0');
    size_t written = 0;
    if (mbedtls_base64_encode(reinterpret_cast<unsigned char*>(&out[0]), needed,
                              &written, data, len) != 0) {
      return std::string();
    }
    out.resize(written);  // drop the trailing slack/NUL the sizing call reserved
    return out;
  }

}  // namespace

bool PayloadCrypto::makeEnvelopeId(char* out) {
  // Drawn from the same seeded DRBG as the AES key and nonce rather than a plain
  // PRNG: 8 random bytes only stay collision-free across a 20 000-entry backlog
  // if they are genuinely random, and a repeated id would let one ack clear the
  // wrong fix off the card.
  unsigned char raw[kEnvelopeIdBytes];
  if (mbedtls_ctr_drbg_random(&rng_, raw, sizeof(raw)) != 0) {
    ESP_LOGE(TAG, "random envelope id failed");
    return false;
  }

  static const char kHexDigits[] = "0123456789abcdef";
  for (size_t i = 0; i < kEnvelopeIdBytes; ++i) {
    out[i * 2]     = kHexDigits[(raw[i] >> 4) & 0x0F];
    out[i * 2 + 1] = kHexDigits[raw[i] & 0x0F];
  }
  out[kEnvelopeIdChars] = '\0';
  return true;
}

PayloadCrypto::PayloadCrypto(const char* receiverPublicKeyPem)
    : publicKeyPem_(receiverPublicKeyPem), ready_(false) {
  // Only initialise the contexts here. Seeding and key parsing happen lazily in
  // ensureReady(): they can fail, a constructor has no way to report that, and
  // this object is a static in app_main() - we do not want an entropy poll
  // running before the rest of the system is up.
  mbedtls_entropy_init(&entropy_);
  mbedtls_ctr_drbg_init(&rng_);
  mbedtls_pk_init(&pk_);
}

PayloadCrypto::~PayloadCrypto() {
  mbedtls_pk_free(&pk_);
  mbedtls_ctr_drbg_free(&rng_);
  mbedtls_entropy_free(&entropy_);
}

bool PayloadCrypto::ensureReady() {
  if (ready_) {
    return true;  // already seeded and parsed - the common case
  }

  // -- Seed the random generator from the ESP32 hardware entropy source. ------
  // Done once for the lifetime of the device: CTR_DRBG reseeds itself as it is
  // consumed, so a fresh poll per message buys nothing and costs both time and
  // a deep, transient stack burst.
  static const char kPers[] = "payload-crypto";
  if (mbedtls_ctr_drbg_seed(&rng_, mbedtls_entropy_func, &entropy_,
                            reinterpret_cast<const unsigned char*>(kPers),
                            sizeof(kPers) - 1) != 0) {
    ESP_LOGE(TAG, "RNG seed failed");
    return false;
  }

  // -- Parse the receiver's public key once and keep it. ----------------------
  if (mbedtls_pk_parse_public_key(
          &pk_, reinterpret_cast<const unsigned char*>(publicKeyPem_),
          strlen(publicKeyPem_) + 1) != 0) {
    ESP_LOGE(TAG, "public key parse failed - is kReceiverPublicKeyPem set?");
    return false;
  }
  mbedtls_rsa_context* rsa = mbedtls_pk_rsa(pk_);
  if (rsa == nullptr) {
    ESP_LOGE(TAG, "public key is not RSA");
    return false;
  }
  // Select OAEP padding with SHA-256 to match the desktop side. Padding is a
  // property of the context, so setting it here covers every later message.
  if (mbedtls_rsa_set_padding(rsa, MBEDTLS_RSA_PKCS_V21, MBEDTLS_MD_SHA256) !=
      0) {
    ESP_LOGE(TAG, "RSA set_padding failed");
    return false;
  }

  ESP_LOGI(TAG, "crypto ready (RSA-%u key).",
           (unsigned)(mbedtls_rsa_get_len(rsa) * 8));
  ready_ = true;
  return true;
}

bool PayloadCrypto::encrypt(const std::string& plaintext,
                            std::string& envelopeOut) {
  if (!ensureReady()) {
    return false;  // ensureReady() already logged the reason
  }
  mbedtls_rsa_context* rsa = mbedtls_pk_rsa(pk_);

  bool ok = false;

  // The only per-message context. GCM is cheap to set up, and keeping it local
  // means a failed message cannot leave a half-configured cipher behind.
  mbedtls_gcm_context gcm;
  mbedtls_gcm_init(&gcm);

  // Working buffers. The RSA ciphertext is exactly one key length (384 bytes
  // for RSA-3072, 512 for RSA-4096) and lives on the heap - it is far too big
  // to sit on the main task's stack next to an RSA modexp.
  unsigned char aesKey[kAesKeyBytes];
  unsigned char nonce[kNonceBytes];
  unsigned char tag[kTagBytes];
  const size_t  encKeyLen = mbedtls_rsa_get_len(rsa);
  std::vector<unsigned char> encKey(encKeyLen);
  std::string                ciphertext(plaintext.size(), '\0');

  // A single-pass block we can `break` out of on the first error; cleanup below
  // then always runs (no goto, no leaked contexts).
  do {
    // -- 1. Fresh one-time AES-256 key + 96-bit nonce. ------------------------
    if (mbedtls_ctr_drbg_random(&rng_, aesKey, sizeof(aesKey)) != 0 ||
        mbedtls_ctr_drbg_random(&rng_, nonce, sizeof(nonce)) != 0) {
      ESP_LOGE(TAG, "random key/nonce failed");
      break;
    }

    // -- 2. AES-256-GCM encrypt the plaintext (produces ciphertext + tag). ----
    if (mbedtls_gcm_setkey(&gcm, MBEDTLS_CIPHER_ID_AES, aesKey,
                           kAesKeyBytes * 8) != 0) {
      ESP_LOGE(TAG, "GCM setkey failed");
      break;
    }
    if (mbedtls_gcm_crypt_and_tag(
            &gcm, MBEDTLS_GCM_ENCRYPT, plaintext.size(), nonce, kNonceBytes,
            /*aad=*/nullptr, /*aad_len=*/0,
            reinterpret_cast<const unsigned char*>(plaintext.data()),
            reinterpret_cast<unsigned char*>(&ciphertext[0]), kTagBytes,
            tag) != 0) {
      ESP_LOGE(TAG, "GCM encrypt failed");
      break;
    }

    // -- 3. RSA-OAEP(SHA-256) encrypt the one-time AES key. -------------------
    // The key was parsed and its padding set once, in ensureReady().
    if (mbedtls_rsa_rsaes_oaep_encrypt(rsa, mbedtls_ctr_drbg_random, &rng_,
                                       /*label=*/nullptr, /*label_len=*/0,
                                       sizeof(aesKey), aesKey,
                                       encKey.data()) != 0) {
      ESP_LOGE(TAG, "RSA-OAEP encrypt failed");
      break;
    }

    // -- 4. Pack everything (base64) into the JSON envelope. ------------------
    cJSON* root = cJSON_CreateObject();
    if (root == nullptr) {
      ESP_LOGE(TAG, "out of memory building envelope");
      break;
    }
    const std::string kB64   = base64Encode(encKey.data(), encKey.size());
    const std::string ivB64  = base64Encode(nonce, kNonceBytes);
    const std::string ctB64  = base64Encode(
        reinterpret_cast<const unsigned char*>(ciphertext.data()),
        ciphertext.size());
    const std::string tagB64 = base64Encode(tag, kTagBytes);

    // The correlation id rides OUTSIDE the ciphertext on purpose: the API echoes
    // it in the delivery ack, and FixQueue stores this envelope verbatim, so the
    // id is still there after a reboot when we come to match an ack against a
    // backlog sealed days earlier. It names a message, never its contents, so
    // exposing it to the broker costs nothing.
    char idHex[kEnvelopeIdChars + 1];
    if (!makeEnvelopeId(idHex)) {
      cJSON_Delete(root);
      break;
    }

    cJSON_AddStringToObject(root, "id", idHex);
    cJSON_AddStringToObject(root, "alg", kAlgorithm);
    cJSON_AddStringToObject(root, "k", kB64.c_str());
    cJSON_AddStringToObject(root, "iv", ivB64.c_str());
    cJSON_AddStringToObject(root, "ct", ctB64.c_str());
    cJSON_AddStringToObject(root, "tag", tagB64.c_str());

    char* printed = cJSON_PrintUnformatted(root);
    if (printed != nullptr) {
      envelopeOut.assign(printed);
      cJSON_free(printed);
      ok = true;
    }
    cJSON_Delete(root);
  } while (false);

  // Cleanup (always runs). Wipe the AES key so it does not linger on the stack.
  // rng_/pk_ are long-lived members and are freed in the destructor.
  mbedtls_platform_zeroize(aesKey, sizeof(aesKey));
  mbedtls_gcm_free(&gcm);
  return ok;
}
