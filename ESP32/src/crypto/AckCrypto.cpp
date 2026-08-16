#include "crypto/AckCrypto.h"

#include <cstring>
#include <vector>

#include "cJSON.h"
#include "esp_log.h"
#include "mbedtls/base64.h"
#include "mbedtls/gcm.h"
#include "mbedtls/platform_util.h"
#include "mbedtls/rsa.h"

static const char* TAG = "AckCrypto";

namespace {

  constexpr size_t kAesKeyBytes = 32;  // AES-256
  constexpr size_t kNonceBytes  = 12;  // 96-bit GCM nonce
  constexpr size_t kTagBytes    = 16;  // 128-bit GCM authentication tag
  constexpr char   kAlgorithm[] = "RSA-OAEP-SHA256+AES-256-GCM";

  // Bound on the ciphertext we will even attempt to open. An ack names message
  // ids, so it is a few hundred bytes; anything far larger is a malformed or
  // hostile message and is cheaper to drop than to allocate for.
  constexpr size_t kMaxCiphertextBytes = 8192;

  // Decode a base64 std::string into `out`. Returns false when the input is not
  // valid base64 or would exceed `maxBytes`.
  bool base64Decode(const char* text, std::vector<unsigned char>& out,
                    size_t maxBytes) {
    if (text == nullptr) {
      return false;
    }
    const size_t textLen = strlen(text);

    // First call with a null buffer asks mbedTLS for the required size.
    size_t needed = 0;
    mbedtls_base64_decode(nullptr, 0, &needed,
                          reinterpret_cast<const unsigned char*>(text), textLen);
    if (needed == 0 || needed > maxBytes) {
      return false;
    }

    out.resize(needed);
    size_t written = 0;
    if (mbedtls_base64_decode(out.data(), out.size(), &written,
                              reinterpret_cast<const unsigned char*>(text),
                              textLen) != 0) {
      return false;
    }
    out.resize(written);
    return true;
  }

  // Read a required string member from a cJSON object (nullptr when absent or
  // not a string).
  const char* stringMember(const cJSON* root, const char* name) {
    const cJSON* item = cJSON_GetObjectItemCaseSensitive(root, name);
    return cJSON_IsString(item) ? item->valuestring : nullptr;
  }

}  // namespace

AckCrypto::AckCrypto(const char* devicePrivateKeyPem)
    : privateKeyPem_(devicePrivateKeyPem), ready_(false) {
  mbedtls_entropy_init(&entropy_);
  mbedtls_ctr_drbg_init(&rng_);
  mbedtls_pk_init(&pk_);
}

AckCrypto::~AckCrypto() {
  mbedtls_pk_free(&pk_);
  mbedtls_ctr_drbg_free(&rng_);
  mbedtls_entropy_free(&entropy_);
}

bool AckCrypto::ensureReady() {
  if (ready_) {
    return true;  // already seeded and parsed - the common case
  }

  static const char kPers[] = "ack-crypto";
  if (mbedtls_ctr_drbg_seed(&rng_, mbedtls_entropy_func, &entropy_,
                            reinterpret_cast<const unsigned char*>(kPers),
                            sizeof(kPers) - 1) != 0) {
    ESP_LOGE(TAG, "RNG seed failed");
    return false;
  }

  // Parse our own private key once and keep it. The +1 includes the NUL, which
  // mbedTLS requires for the PEM form.
  if (mbedtls_pk_parse_key(
          &pk_, reinterpret_cast<const unsigned char*>(privateKeyPem_),
          strlen(privateKeyPem_) + 1, /*pwd=*/nullptr, /*pwdlen=*/0,
          mbedtls_ctr_drbg_random, &rng_) != 0) {
    ESP_LOGE(TAG,
             "private key parse failed - is kDeviceAckPrivateKeyPem set in "
             "Config.h?");
    return false;
  }

  mbedtls_rsa_context* rsa = mbedtls_pk_rsa(pk_);
  if (rsa == nullptr) {
    ESP_LOGE(TAG, "ack private key is not RSA");
    return false;
  }

  // OAEP with SHA-256, matching what the API seals with. Set once, here, so the
  // per-message path does no context configuration.
  if (mbedtls_rsa_set_padding(rsa, MBEDTLS_RSA_PKCS_V21, MBEDTLS_MD_SHA256) !=
      0) {
    ESP_LOGE(TAG, "RSA padding setup failed");
    return false;
  }

  ready_ = true;
  return true;
}

bool AckCrypto::decrypt(const std::string& envelopeJson,
                        std::string& plaintextOut) {
  if (!ensureReady()) {
    return false;  // ensureReady() already logged the reason
  }

  cJSON* root = cJSON_Parse(envelopeJson.c_str());
  if (root == nullptr) {
    ESP_LOGW(TAG, "ack is not valid JSON");
    return false;
  }

  bool ok = false;

  mbedtls_gcm_context gcm;
  mbedtls_gcm_init(&gcm);

  unsigned char aesKey[kAesKeyBytes];

  // Single-pass block we can `break` out of on the first error; the cleanup
  // below then always runs (no goto, no leaked contexts).
  do {
    const char* alg = stringMember(root, "alg");
    if (alg == nullptr || strcmp(alg, kAlgorithm) != 0) {
      ESP_LOGW(TAG, "ack has a wrong or missing algorithm");
      break;
    }

    std::vector<unsigned char> encKey;
    std::vector<unsigned char> nonce;
    std::vector<unsigned char> ciphertext;
    std::vector<unsigned char> tag;

    // Exact lengths where the format fixes them - a deviation is corruption or
    // tampering, not a variant worth accommodating.
    if (!base64Decode(stringMember(root, "k"), encKey, 1024) ||
        !base64Decode(stringMember(root, "iv"), nonce, kNonceBytes) ||
        !base64Decode(stringMember(root, "ct"), ciphertext,
                      kMaxCiphertextBytes) ||
        !base64Decode(stringMember(root, "tag"), tag, kTagBytes)) {
      ESP_LOGW(TAG, "ack has a missing or malformed field");
      break;
    }
    if (nonce.size() != kNonceBytes || tag.size() != kTagBytes ||
        ciphertext.empty()) {
      ESP_LOGW(TAG, "ack field has an unexpected length");
      break;
    }

    // -- 1. RSA-OAEP(SHA-256) unwrap the one-time AES key. --------------------
    mbedtls_rsa_context* rsa    = mbedtls_pk_rsa(pk_);
    size_t               keyLen = 0;
    if (mbedtls_rsa_rsaes_oaep_decrypt(rsa, mbedtls_ctr_drbg_random, &rng_,
                                       /*label=*/nullptr, /*label_len=*/0,
                                       &keyLen, encKey.data(), aesKey,
                                       sizeof(aesKey)) != 0) {
      // Expected whenever an ack was sealed to a different key than the one this
      // device was flashed with - a warning, not an error: we simply retry.
      ESP_LOGW(TAG, "RSA-OAEP unwrap failed (wrong ack key?)");
      break;
    }
    if (keyLen != kAesKeyBytes) {
      ESP_LOGW(TAG, "unwrapped ack key has unexpected length %u",
               (unsigned)keyLen);
      break;
    }

    // -- 2. AES-256-GCM authenticate and decrypt. -----------------------------
    if (mbedtls_gcm_setkey(&gcm, MBEDTLS_CIPHER_ID_AES, aesKey,
                           kAesKeyBytes * 8) != 0) {
      ESP_LOGE(TAG, "GCM setkey failed");
      break;
    }

    std::string plaintext(ciphertext.size(), '\0');
    // auth_decrypt verifies the tag BEFORE returning success, so a forged or
    // altered ack never reaches the parser above us.
    if (mbedtls_gcm_auth_decrypt(
            &gcm, ciphertext.size(), nonce.data(), kNonceBytes,
            /*aad=*/nullptr, /*aad_len=*/0, tag.data(), kTagBytes,
            ciphertext.data(),
            reinterpret_cast<unsigned char*>(&plaintext[0])) != 0) {
      ESP_LOGW(TAG, "ack failed authentication - ignoring it");
      break;
    }

    plaintextOut.swap(plaintext);
    ok = true;
  } while (false);

  // Cleanup (always runs). Wipe the AES key so it does not linger on the stack.
  mbedtls_platform_zeroize(aesKey, sizeof(aesKey));
  mbedtls_gcm_free(&gcm);
  cJSON_Delete(root);
  return ok;
}
