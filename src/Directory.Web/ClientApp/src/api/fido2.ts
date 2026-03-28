import { get, post, put, del } from './client'
import type {
  Fido2CredentialSummary,
  PublicKeyCredentialCreationOptions,
  PublicKeyCredentialRequestOptions,
  Fido2RegistrationResult,
  Fido2AuthenticationResult,
} from '../types/fido2'

export const beginRegistration = (userDn: string) =>
  post<PublicKeyCredentialCreationOptions>('/fido2/register/begin', { userDn })

export const completeRegistration = (userDn: string, attestation: unknown) =>
  post<Fido2RegistrationResult>('/fido2/register/complete', { userDn, attestation })

export const beginAuthentication = (userDn: string) =>
  post<PublicKeyCredentialRequestOptions>('/fido2/authenticate/begin', { userDn })

export const completeAuthentication = (userDn: string, assertion: unknown) =>
  post<Fido2AuthenticationResult>('/fido2/authenticate/complete', { userDn, assertion })

export const listCredentials = (dn: string) =>
  get<Fido2CredentialSummary[]>(`/fido2/credentials/${encodeURIComponent(dn)}`)

export const deleteCredential = (dn: string, credentialId: string) =>
  del(`/fido2/credentials/${encodeURIComponent(dn)}/${encodeURIComponent(credentialId)}`)

export const renameCredential = (dn: string, credentialId: string, name: string) =>
  put(`/fido2/credentials/${encodeURIComponent(dn)}/${encodeURIComponent(credentialId)}`, { name })

// --- WebAuthn browser helpers ---

/** Convert a Base64URL string to an ArrayBuffer */
function base64urlToBuffer(base64url: string): ArrayBuffer {
  const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/')
  const padLen = (4 - (base64.length % 4)) % 4
  const padded = base64 + '='.repeat(padLen)
  const binary = atob(padded)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  return bytes.buffer
}

/** Convert an ArrayBuffer to a Base64URL string */
function bufferToBase64url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer)
  let binary = ''
  for (const b of bytes) binary += String.fromCharCode(b)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

/**
 * Perform the WebAuthn registration ceremony using navigator.credentials.create().
 * Calls the server to get creation options, invokes the browser API, then sends
 * the attestation response back for verification.
 */
export async function registerSecurityKey(userDn: string, deviceName?: string): Promise<Fido2RegistrationResult> {
  const options = await beginRegistration(userDn)

  // Convert server options to the format expected by navigator.credentials.create()
  const publicKey: PublicKeyCredentialCreationOptions = {
    ...options,
    challenge: base64urlToBuffer(options.challenge) as any,
    user: {
      ...options.user,
      id: base64urlToBuffer(options.user.id) as any,
    },
    excludeCredentials: (options.excludeCredentials || []).map(c => ({
      ...c,
      id: base64urlToBuffer(c.id) as any,
    })),
  }

  const credential = await navigator.credentials.create({ publicKey: publicKey as any }) as any
  if (!credential) throw new Error('Registration was cancelled')

  const attestation = {
    id: credential.id,
    rawId: bufferToBase64url(credential.rawId),
    type: credential.type,
    response: {
      clientDataJSON: bufferToBase64url(credential.response.clientDataJSON),
      attestationObject: bufferToBase64url(credential.response.attestationObject),
    },
    deviceName: deviceName || undefined,
  }

  return completeRegistration(userDn, attestation)
}

/**
 * Perform the WebAuthn authentication ceremony using navigator.credentials.get().
 */
export async function authenticateWithSecurityKey(userDn: string): Promise<Fido2AuthenticationResult> {
  const options = await beginAuthentication(userDn)

  const publicKey = {
    ...options,
    challenge: base64urlToBuffer(options.challenge),
    allowCredentials: (options.allowCredentials || []).map(c => ({
      ...c,
      id: base64urlToBuffer(c.id),
    })),
  }

  const credential = await navigator.credentials.get({ publicKey: publicKey as any }) as any
  if (!credential) throw new Error('Authentication was cancelled')

  const assertion = {
    id: credential.id,
    rawId: bufferToBase64url(credential.rawId),
    type: credential.type,
    response: {
      clientDataJSON: bufferToBase64url(credential.response.clientDataJSON),
      authenticatorData: bufferToBase64url(credential.response.authenticatorData),
      signature: bufferToBase64url(credential.response.signature),
      userHandle: credential.response.userHandle
        ? bufferToBase64url(credential.response.userHandle)
        : undefined,
    },
  }

  return completeAuthentication(userDn, assertion)
}
