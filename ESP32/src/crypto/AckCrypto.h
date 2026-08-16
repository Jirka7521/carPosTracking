#pragma once

// =============================================================================
//  AckCrypto  -  Open the encrypted delivery acks the API sends back to us.
// -----------------------------------------------------------------------------
//  Responsibility (single!): turn one encrypted JSON envelope arriving on the
//  ack topic back into its plaintext. It is the exact mirror image of
//  PayloadCrypto, and deliberately a separate class:
//
//      PayloadCrypto : plaintext --> envelope   (seal TO the server)
//      AckCrypto     : envelope  --> plaintext  (open FROM the server)
//
//  Keeping them apart means PayloadCrypto stays single-purpose and byte-for-byte
//  compatible with the desktop CryptoBox, which knows nothing about acks.
//
//  Key direction - the important difference:
//    For telemetry this device holds the receiver's PUBLIC key and the server
//    holds the private half, so we can encrypt but never read what we sent. For
//    acks that inverts: this device holds its OWN PRIVATE key
//    (kDeviceAckPrivateKeyPem) and the server holds only the public half. That
//    private key is generated off-server and pasted into the git-ignored
//    Config.h by hand - it must never exist in the API database, in a
//    provisioning response, or in Config.example.h.
//
//  Scheme (identical to the outbound direction, just run backwards):
//    1. RSA-OAEP(SHA-256) unwraps the one-time AES-256 key with our private key.
//    2. AES-256-GCM authenticates and decrypts the payload.
//  The GCM tag is what makes an ack trustworthy: without the private key nobody
//  can forge a message that authenticates, so a broker or a stolen account
//  cannot fake a confirmation and make us delete undelivered fixes.
//
//  Wire format accepted (the same envelope PayloadCrypto emits, minus "id"):
//    {"alg":"RSA-OAEP-SHA256+AES-256-GCM","k":..,"iv":..,"ct":..,"tag":..}
// =============================================================================

#include <string>

#include "mbedtls/ctr_drbg.h"
#include "mbedtls/entropy.h"
#include "mbedtls/pk.h"

class AckCrypto {
 public:
  // Borrows the PEM private-key string (does not copy) - it must outlive this
  // object. With Config.h that is automatic (it is a constexpr global).
  explicit AckCrypto(const char* devicePrivateKeyPem);
  ~AckCrypto();

  // Owns mbedTLS contexts, which are not safe to copy.
  AckCrypto(const AckCrypto&)            = delete;
  AckCrypto& operator=(const AckCrypto&) = delete;

  // Decrypt `envelopeJson`. On success writes the plaintext to `plaintextOut`
  // and returns true. Returns false for anything unusable - malformed JSON, a
  // wrong algorithm, a bad base64 field, or a failed GCM tag check. A false
  // return is never fatal: the caller simply treats the ack as not received and
  // lets the normal retry path deal with it.
  bool decrypt(const std::string& envelopeJson, std::string& plaintextOut);

 private:
  // One-time, lazy seeding of `rng_` and parse of `pk_`. Lazy for the same
  // reasons as PayloadCrypto: it can fail, a constructor cannot report that, and
  // the entropy poll should not run before the rest of the system is up.
  // Returns false on failure; safe to call again on the next message.
  bool ensureReady();

  const char* privateKeyPem_;  // this device's RSA private key, PEM text

  // Seeded once and reused. RSA private-key operations need an RNG for blinding,
  // so `rng_` is not optional here even though we never generate a key.
  mbedtls_entropy_context  entropy_;
  mbedtls_ctr_drbg_context rng_;
  mbedtls_pk_context       pk_;
  bool                     ready_;  // rng_ seeded and pk_ parsed
};
