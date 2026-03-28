import { get, post, del, getText } from './client'
import type { SshPublicKey } from '../types/sshKeys'

export const listKeys = (dn: string) =>
  get<SshPublicKey[]>(`/ssh-keys/${encodeURIComponent(dn)}`)

export const addKey = (dn: string, publicKey: string) =>
  post<SshPublicKey>(`/ssh-keys/${encodeURIComponent(dn)}`, { publicKey })

export const deleteKey = (id: string, userDn: string) =>
  del(`/ssh-keys/key/${encodeURIComponent(id)}?userDn=${encodeURIComponent(userDn)}`)

export const getAuthorizedKeys = (username: string) =>
  getText(`/ssh-keys/authorized/${encodeURIComponent(username)}`)
