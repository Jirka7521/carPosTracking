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

class PayloadCrypto {
 public:
  // Borrows the PEM public-key string (does not copy) - it must outlive this
  // object. With Config.h that is automatic (it is a constexpr global).
  explicit PayloadCrypto(const char* receiverPublicKeyPem);

  // Encrypt `plaintext`. On success, writes the JSON envelope to `envelopeOut`
  // and returns true. Returns false on any cryptographic error (e.g. the public
  // key in Config.h has not been filled in).
  bool encrypt(const std::string& plaintext, std::string& envelopeOut);

 private:
  const char* publicKeyPem_;  // receiver RSA public key, PEM text
};
