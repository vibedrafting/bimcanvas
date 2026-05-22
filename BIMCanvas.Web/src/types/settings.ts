export type SettingsGroupKey = 'server' | 'web' | 'agent' | 'ccr'
export type SettingsApplyMode = 'immediate' | 'restart'

export type SettingsFieldType = 'string' | 'number' | 'bool' | 'enum' | 'json'

export interface SettingsFieldMeta {
  path: string
  label: string
  /** 控件类型；复杂字段（json）降级为内嵌 JSON 编辑器 */
  type: SettingsFieldType
  /** enum 类型的可选值（其它类型为 null/缺省） */
  enumValues?: string[] | null
  /** 字段说明（"描述符供文档"：仅在可视化模式由控件旁提示渲染） */
  help?: string | null
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
  runtime: SettingsRuntime
}

export interface RuntimeServiceEndpoint {
  key: string
  title: string
  managedByServer: boolean
  autoShifted: boolean
  configuredUrl: string
  actualUrl: string
  configuredPort: number | null
  actualPort: number | null
}

export interface SettingsRuntime {
  mode: 'direct' | 'ccr'
  effectiveDefaultModelPath: string
  effectiveDefaultModelValue: string
  dockerManagedRestart: boolean
  restartBehavior: 'docker-auto' | 'manual'
  restartHint: string
  server: RuntimeServiceEndpoint
  web: RuntimeServiceEndpoint
  agent: RuntimeServiceEndpoint
  ccr: RuntimeServiceEndpoint
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
