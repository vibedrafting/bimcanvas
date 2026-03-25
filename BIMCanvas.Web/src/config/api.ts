const normalizeBase = (value: string | undefined) => {
  const trimmed = value?.trim() ?? ''
  return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed
}

export const SERVER_BASE = normalizeBase(import.meta.env.VITE_SERVER_URL)
const agentBase = normalizeBase(import.meta.env.VITE_AGENT_URL)

export const SERVER_API = `${SERVER_BASE}/api`
export const SIGNALR_HUB = `${SERVER_BASE}/hubs/canvas`
export const AGENT_API = agentBase || `${SERVER_BASE}/agent`
