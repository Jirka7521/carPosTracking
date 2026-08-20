// ---------------------------------------------------------------------------
// Generates a device's ack key pair in the browser, with WebCrypto.
//
// WHY HERE AND NOT ON THE SERVER — the ack direction is the mirror image of
// telemetry. For a position the device encrypts and the API decrypts, so the
// API holds the private key. For a delivery ack the API encrypts and the
// *device* decrypts, so the private half belongs to the device alone and the
// server must never hold it. Generating the pair here keeps that true: only the
// public half is ever sent to the API, and the private half goes straight into
// the Config.h the operator downloads.
//
// The output has to be byte-compatible with two other pieces of the system:
//   - the firmware's mbedTLS parse of kDeviceAckPrivateKeyPem, and
//   - .NET's RSA.ImportFromPem on the API when the public half is stored.
// Both expect what `openssl genpkey -algorithm RSA` produces — PKCS#8 for the
// private half ("BEGIN PRIVATE KEY") and SPKI for the public one — which is
// exactly what WebCrypto's 'pkcs8' and 'spki' exports are.
//
// RSA-3072 is not negotiable: it is the size the firmware, the API and the
// import CLI all pin to. Generating one takes a second or two of the main
// thread's time, which is why the caller shows a spinner.
// ---------------------------------------------------------------------------

export type AckKeyPair = {
  // PKCS#8. Goes into Config.h and nowhere else — never into a request, never
  // into storage.
  privateKeyPem: string
  // SPKI. The only half that is sent to the API.
  publicKeyPem: string
}

const RSA_MODULUS_BITS = 3072

// 65537, big-endian — the standard public exponent, and the one OpenSSL uses.
const PUBLIC_EXPONENT = new Uint8Array([0x01, 0x00, 0x01])

// WebCrypto is only exposed in a secure context (https, or localhost during
// development). Checked up front so the UI can explain why the button is
// disabled instead of failing at the click.
export function isKeyGenerationAvailable(): boolean {
  return typeof crypto !== 'undefined' && typeof crypto.subtle !== 'undefined'
}

export async function generateAckKeyPair(): Promise<AckKeyPair> {
  if (!isKeyGenerationAvailable()) {
    throw new Error(
      'Key generation needs a secure context (https, or localhost). ' +
        'Open the dashboard over https, or generate the pair with openssl instead.',
    )
  }

  // RSA-OAEP with SHA-256 — the algorithm the API seals acks with. The key
  // material itself is just an RSA key, but naming the usage here is what lets
  // WebCrypto export it at all.
  const pair = await crypto.subtle.generateKey(
    {
      name: 'RSA-OAEP',
      modulusLength: RSA_MODULUS_BITS,
      publicExponent: PUBLIC_EXPONENT,
      hash: 'SHA-256',
    },
    // Extractable: the entire point is to export both halves once.
    true,
    ['encrypt', 'decrypt'],
  )

  const privateKey = await crypto.subtle.exportKey('pkcs8', pair.privateKey)
  const publicKey = await crypto.subtle.exportKey('spki', pair.publicKey)

  return {
    privateKeyPem: toPem(privateKey, 'PRIVATE KEY'),
    publicKeyPem: toPem(publicKey, 'PUBLIC KEY'),
  }
}

// Wraps DER bytes in a PEM armour with the conventional 64-character body
// lines. Line width matters: mbedTLS and .NET both accept other widths, but an
// operator comparing this against an openssl-produced file should see the same
// shape.
function toPem(der: ArrayBuffer, label: string): string {
  const base64 = toBase64(der)
  const lines: string[] = []

  for (let index = 0; index < base64.length; index += 64) {
    lines.push(base64.slice(index, index + 64))
  }

  return `-----BEGIN ${label}-----\n${lines.join('\n')}\n-----END ${label}-----\n`
}

function toBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer)
  let binary = ''

  // Byte at a time rather than String.fromCharCode(...bytes): a key is only a
  // couple of KB, but spreading a large array into an argument list is the
  // classic way to blow the call stack.
  for (const byte of bytes) {
    binary += String.fromCharCode(byte)
  }

  return btoa(binary)
}
