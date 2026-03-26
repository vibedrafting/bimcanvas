export type SettingsGroupKey = 'server' | 'web' | 'agent' | 'ccr'
export type SettingsApplyMode = 'immediate' | 'restart'

export interface SettingsFieldMeta {
  path: string
  label: string
  applyMode: SettingsApplyMode
  sensitive: boolean
}

export interface SettingsGroup {
  key: SettingsGroupKey
  title: string
  sourceFile: string
  applyMode: SettingsApplyMode
  requiresRestart: boolean
  values: Record<string, any>
  fields: SettingsFieldMeta[]
}

export interface SettingsSnapshot {
  server: SettingsGroup
  web: SettingsGroup
  agent: SettingsGroup
  ccr: SettingsGroup
}

export interface UpdateSettingsRequest {
  server?: Record<string, any>
  web?: Record<string, any>
  agent?: Record<string, any>
  ccr?: Record<string, any>
}

export interface UpdateSettingsResponse {
  success: boolean
  changedGroups: string[]
  restartRequiredGroups: string[]
  settings: SettingsSnapshot
}
