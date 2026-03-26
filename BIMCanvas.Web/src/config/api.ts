const normalizeBase = (value: string | undefined) => {
  const trimmed = value?.trim() ?? ''
  return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed
}

const getBrowserBase = (port?: string) => {
  if (typeof window === 'undefined') {
    return ''
  }

  const { protocol, hostname, port: currentPort } = window.location
  const resolvedPort = port ?? currentPort
  return normalizeBase(`${protocol}//${hostname}${resolvedPort ? `:${resolvedPort}` : ''}`)
}

const serverBaseFromEnv = normalizeBase(import.meta.env.VITE_SERVER_URL)
const agentBaseFromEnv = normalizeBase(import.meta.env.VITE_AGENT_URL)

export const SERVER_BASE =
  serverBaseFromEnv ||
  (import.meta.env.DEV ? getBrowserBase('5000') : '')

export const SERVER_API = `${SERVER_BASE}/api`
export const SIGNALR_HUB = `${SERVER_BASE}/hubs/canvas`
export const AGENT_API = agentBaseFromEnv || getBrowserBase('8865')
