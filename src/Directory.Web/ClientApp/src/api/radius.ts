import { get, put, post, del } from './client'
import type { RadiusSettings, RadiusClient, RadiusLogEntry } from '../types/radius'

// ── Settings ──────────────────────────────────────────────────

export const getSettings = () =>
  get<RadiusSettings>('/radius/settings')

export const updateSettings = (settings: Partial<RadiusSettings>) =>
  put<RadiusSettings>('/radius/settings', settings)

// ── Clients ───────────────────────────────────────────────────

export const getClients = () =>
  get<RadiusClient[]>('/radius/clients')

export const addClient = (client: Partial<RadiusClient>) =>
  post<RadiusClient>('/radius/clients', client)

export const updateClient = (id: string, client: Partial<RadiusClient>) =>
  put<RadiusClient>(`/radius/clients/${encodeURIComponent(id)}`, client)

export const deleteClient = (id: string) =>
  del(`/radius/clients/${encodeURIComponent(id)}`)

// ── Log ───────────────────────────────────────────────────────

export const getLog = () =>
  get<RadiusLogEntry[]>('/radius/log')
