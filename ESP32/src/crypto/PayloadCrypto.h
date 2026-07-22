#pragma once

// =============================================================================
//  PayloadCrypto  -  Hybrid end-to-end encryption for outgoing MQTT payloads.
// -----------------------------------------------------------------------------
//  Responsibility (single!): turn a plaintext string into the encrypted JSON
//  "envelope" that gets published to the broker. It mirrors the desktop
//  CryptoBox (desktop/crypto_box.py) exactly, so the Python subscriber can
//  decrypt what this device sends.
//
//  Scheme (hybrid / KEM-DEM):
//    1. A fresh random 256-bit AES key is generated for every message.
//    2. The plaintext is sealed with AES-256-GCM (authenticated encryption).
//    3. That one-time AES key is encrypted with RSA-OAEP (SHA-256) using the
//       receiver's PUBLIC key, so only the desktop's PRIVATE key can recover it.
//
//  Result: the broker (and the whole network path) only ever see ciphertext -
//  true end-to-end encryption of the GNSS positions.
//
//  Wire format (compact JSON, all binary fields base64):
//    {"alg":"RSA-OAEP-SHA256+AES-256-GCM",
//     "k":"<RSA-OAEP encrypted AES key>",
//     "iv":"<12-byte GCM nonce>",
//     "ct":"<AES-GCM ciphertext>",
//     "tag":"<16-byte GCM tag>"}
// =============================================================================

#include <string>

#include "mbedtls/ctr_drbg.h"
#include "mbedtls/entropy.h"
#include "mbedtls/pk.h"

class PayloadCrypto {
 public:
  // Borrows the PEM public-key string (does not copy) - it must outlive this
  // object. With Config.h that is automatic (it is a constexpr global).
  explicit PayloadCrypto(const char* receiverPublicKeyPem);
  ~PayloadCrypto();

  // Owns mbedTLS contexts, which are not safe to copy - and there is never a
  // reason to duplicate one anyway.
  PayloadCrypto(const PayloadCrypto&)            = delete;
  PayloadCrypto& operator=(const PayloadCrypto&) = delete;

  // Encrypt `plaintext`. On success, writes the JSON envelope to `envelopeOut`
  // and returns true. Returns false on any cryptographic error (e.g. the public
  // key in Config.h has not been filled in).
  bool encrypt(const std::string& plaintext, std::string& envelopeOut);

 private:
  // One-time, lazy setup of `rng_` and `pk_` (see the .cpp for why it is lazy
  // and not done in the constructor). Returns false if seeding or key parsing
  // failed; safe to call again on the next message.
  bool ensureReady();

  const char* publicKeyPem_;  // receiver RSA public key, PEM text

  // Seeded once, then reused for every message. Both the entropy poll and the
  // PEM parse are expensive in time *and* in stack, so paying for them per fix
  // was what pushed the main task over its stack limit.
  mbedtls_entropy_context  entropy_;
  mbedtls_ctr_drbg_context rng_;
  mbedtls_pk_context       pk_;
  bool                     ready_;  // rng_ seeded and pk_ parsed
};
