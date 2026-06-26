#include "crypto/PayloadCrypto.h"

#include <cstring>

#include "cJSON.h"
#include "esp_log.h"
#include "mbedtls/base64.h"
#include "mbedtls/ctr_drbg.h"
#include "mbedtls/entropy.h"
#include "mbedtls/gcm.h"
#include "mbedtls/pk.h"
#include "mbedtls/platform_util.h"
#include "mbedtls/rsa.h"

static const char* TAG = "PayloadCrypto";

namespace {

  constexpr size_t kAesKeyBytes = 32;  // AES-256
  constexpr size_t kNonceBytes  = 12;  // 96-bit GCM nonce
  constexpr size_t kTagBytes    = 16;  // 128-bit GCM authentication tag
  constexpr char   kAlgorithm[] = "RSA-OAEP-SHA256+AES-256-GCM";

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

PayloadCrypto::PayloadCrypto(const char* receiverPublicKeyPem)
    : publicKeyPem_(receiverPublicKeyPem) {}

bool PayloadCrypto::encrypt(const std::string& plaintext,
                            std::string& envelopeOut) {
  bool ok = false;

  // mbedTLS contexts - all initialised here and freed once at the end.
  mbedtls_entropy_context  entropy;
  mbedtls_ctr_drbg_context rng;
  mbedtls_pk_context       pk;
  mbedtls_gcm_context      gcm;
  mbedtls_entropy_init(&entropy);
  mbedtls_ctr_drbg_init(&rng);
  mbedtls_pk_init(&pk);
  mbedtls_gcm_init(&gcm);

  // Working buffers. encKey is sized for RSA-4096 (512 bytes) so it comfortably
  // fits the RSA-3072 ciphertext (384 bytes) the desktop key produces.
  unsigned char aesKey[kAesKeyBytes];
  unsigned char nonce[kNonceBytes];
  unsigned char tag[kTagBytes];
  unsigned char encKey[512];
  size_t        encKeyLen = 0;
  std::string   ciphertext(plaintext.size(), '\0');

  // A single-pass block we can `break` out of on the first error; cleanup below
  // then always runs (no goto, no leaked contexts).
  do {
    // -- Seed the random generator from the ESP32 hardware entropy source. ----
    static const char kPers[] = "payload-crypto";
    if (mbedtls_ctr_drbg_seed(&rng, mbedtls_entropy_func, &entropy,
                              reinterpret_cast<const unsigned char*>(kPers),
                              sizeof(kPers) - 1) != 0) {
      ESP_LOGE(TAG, "RNG seed failed");
      break;
    }

    // -- 1. Fresh one-time AES-256 key + 96-bit nonce. ------------------------
    if (mbedtls_ctr_drbg_random(&rng, aesKey, sizeof(aesKey)) != 0 ||
        mbedtls_ctr_drbg_random(&rng, nonce, sizeof(nonce)) != 0) {
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
    if (mbedtls_pk_parse_public_key(
            &pk, reinterpret_cast<const unsigned char*>(publicKeyPem_),
            strlen(publicKeyPem_) + 1) != 0) {
      ESP_LOGE(TAG, "public key parse failed - is kReceiverPublicKeyPem set?");
      break;
    }
    mbedtls_rsa_context* rsa = mbedtls_pk_rsa(pk);
    if (rsa == nullptr) {
      ESP_LOGE(TAG, "public key is not RSA");
      break;
    }
    // Select OAEP padding with SHA-256 to match the desktop side.
    if (mbedtls_rsa_set_padding(rsa, MBEDTLS_RSA_PKCS_V21, MBEDTLS_MD_SHA256) != 0) {
      ESP_LOGE(TAG, "RSA set_padding failed");
      break;
    }
    if (mbedtls_rsa_rsaes_oaep_encrypt(rsa, mbedtls_ctr_drbg_random, &rng,
                                       /*label=*/nullptr, /*label_len=*/0,
                                       sizeof(aesKey), aesKey, encKey) != 0) {
      ESP_LOGE(TAG, "RSA-OAEP encrypt failed");
      break;
    }
    encKeyLen = mbedtls_rsa_get_len(rsa);

    // -- 4. Pack everything (base64) into the JSON envelope. ------------------
    cJSON* root = cJSON_CreateObject();
    if (root == nullptr) {
      ESP_LOGE(TAG, "out of memory building envelope");
      break;
    }
    const std::string kB64   = base64Encode(encKey, encKeyLen);
    const std::string ivB64  = base64Encode(nonce, kNonceBytes);
    const std::string ctB64  = base64Encode(
        reinterpret_cast<const unsigned char*>(ciphertext.data()),
        ciphertext.size());
    const std::string tagB64 = base64Encode(tag, kTagBytes);

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
  mbedtls_platform_zeroize(aesKey, sizeof(aesKey));
  mbedtls_gcm_free(&gcm);
  mbedtls_pk_free(&pk);
  mbedtls_ctr_drbg_free(&rng);
  mbedtls_entropy_free(&entropy);
  return ok;
}
