<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { SettingsService, type LlmEndpointTestResult } from '../../services/SettingsService'
import GlassButton from './base/GlassButton.vue'
import GlassSelect from './base/GlassSelect.vue'
import { useSystemStore } from '../../stores/systemStore'
import type {
  SettingsGroup,
  SettingsGroupKey,
  RuntimeServiceEndpoint,
  SettingsRuntime,
  SettingsSnapshot,
  UpdateSettingsRequest
} from '../../types/settings'

type ProviderKey = 'claude' | 'openai'
type ClaudeAlias = 'opus' | 'sonnet' | 'haiku'
type ModelMappingEntry = { id: string; label: string }

interface GroupDraft {
  title: string
  sourceFile: string
  values: Record<string, any>
  jsonText: string
  jsonError: string | null
}

const emit = defineEmits<{
  (e: 'close'): void
}>()

const CLAUDE_ALIAS_ORDER: ClaudeAlias[] = ['opus', 'sonnet', 'haiku']
const CLAUDE_MODEL_DEFAULTS: Record<ClaudeAlias, { id: string; label: string }> = {
  opus: { id: 'claude-opus-4-6', label: 'Opus' },
  sonnet: { id: 'claude-sonnet-4-20250514', label: 'Sonnet' },
  haiku: { id: 'claude-haiku-4-5-20251001', label: 'Haiku' }
}
const OPENAI_MODEL_DEFAULTS: Record<string, { id: string; label: string }> = {
  'gpt-5': { id: 'gpt-5', label: 'GPT-5' }
}

const groupKeys: SettingsGroupKey[] = ['server', 'web', 'agent', 'ccr']
const createRuntimeEndpoint = (key: string, title: string): RuntimeServiceEndpoint => ({
  key,
  title,
  managedByServer: false,
  autoShifted: false,
  configuredUrl: '',
  actualUrl: '',
  configuredPort: null,
  actualPort: null
})

const defaultRuntime: SettingsRuntime = {
  mode: 'direct',
  effectiveDefaultModelPath: 'agent.claude.defaultModel',
  effectiveDefaultModelValue: '',
  dockerManagedRestart: false,
  restartBehavior: 'manual',
  restartHint: '当前环境未检测到 Docker 自动重启，点击重启后需要手动重新启动服务。',
  server: createRuntimeEndpoint('server', 'Server'),
  web: createRuntimeEndpoint('web', 'Web'),
  agent: createRuntimeEndpoint('agent', 'Agent'),
  ccr: createRuntimeEndpoint('ccr', 'CCR')
}

const runtimeProviderOptions = [
  { value: 'claude', label: 'Claude' },
  { value: 'openai', label: 'OpenAI' }
]

const effortOptions = [
  { value: 'low', label: 'Low' },
  { value: 'medium', label: 'Medium' },
  { value: 'high', label: 'High' },
  { value: 'max', label: 'Max' }
]

const thinkingOptions = [
  { value: 'off', label: 'Off' },
  { value: 'adaptive', label: 'Adaptive' }
]

const openAiApiModeOptions = [
  { value: 'chat_completions', label: 'Chat Completions' },
  { value: 'responses', label: 'Responses' }
]

const openAiTracingOptions = [
  { value: 'auto', label: 'Auto' },
  { value: 'disabled', label: 'Disabled' },
  { value: 'enabled', label: 'Enabled' }
]

const logLevelOptions = [
  { value: 'debug', label: 'Debug' },
  { value: 'info', label: 'Info' },
  { value: 'warn', label: 'Warn' },
  { value: 'error', label: 'Error' }
]

const systemStore = useSystemStore()

const runtime = ref<SettingsRuntime>({ ...defaultRuntime })
const isLoading = ref(true)
const isSaving = ref(false)
const showSecrets = ref(false)
const saveMessage = ref<string | null>(null)
const saveError = ref<string | null>(null)
const loadError = ref<string | null>(null)
const isClaudeTesting = ref(false)
const isOpenAiTesting = ref(false)
const claudeTestResult = ref<LlmEndpointTestResult | null>(null)
const openAiTestResult = ref<LlmEndpointTestResult | null>(null)
const isMounted = ref(false)

// ── 双模式编辑（可视化 / 源文件）──
const unifiedFileName = 'instance.config.json'
const SETTINGS_EDITOR_MODE_KEY = 'bimcanvas:settings-editor-mode'
function loadSavedEditorMode(): 'visual' | 'source' {
  try {
    return localStorage.getItem(SETTINGS_EDITOR_MODE_KEY) === 'source' ? 'source' : 'visual'
  } catch {
    return 'visual'
  }
}
const editorMode = ref<'visual' | 'source'>(loadSavedEditorMode())
const sourceText = ref('')
const sourceError = ref<string | null>(null)
const sourceTextareaRef = ref<HTMLTextAreaElement | null>(null)
// 源模式中出现的、非 server/web/agent/ccr 的顶层 key，原样保留以实现无损往返
const unifiedExtra = ref<Record<string, any>>({})
// 上次加载/保存的统一配置快照，用作保存确认弹窗的 diff 基线
const lastSavedUnified = ref<Record<string, any>>({})

// 保存确认（Diff）弹窗
const showDiffModal = ref(false)
const pendingPayload = ref<UpdateSettingsRequest | null>(null)

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value))
}

function formatJson(value: unknown) {
  return JSON.stringify(value, null, 2)
}

function capitalize(value: string) {
  return value.charAt(0).toUpperCase() + value.slice(1)
}

function isPlainObject(value: unknown): value is Record<string, any> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function normalizeProvider(value: unknown): ProviderKey {
  return String(value ?? '').trim().toLowerCase() === 'openai' ? 'openai' : 'claude'
}

// 工具权限 v3.3 §3 Phase 6:tools / agents 字段从 config.json 移到 plugin manifest 接管,
// 前端不再 normalize / default 这两个字段(Web 设置面板也不再编辑它们)。
// 旧的 normalizeAllowDeny / normalizeTools / normalizeAgents 三个函数已删除。

function createClaudeDefaults() {
  return {
    baseUrl: '',
    apiKey: '',
    defaultModel: 'opus',
    defaultEffort: 'low',
    defaultThinking: 'adaptive',
    maxThinkingTokens: 8000,
    modelMapping: clone(CLAUDE_MODEL_DEFAULTS),
    // claude-agent-sdk ClaudeAgentOptions.env: 传给 Claude Code CLI subprocess 的环境变量。
    // 仿 ~/.claude/settings.json env 段;key/value 都必须是字符串。
    env: {} as Record<string, string>
  }
}

function createOpenAiDefaults() {
  return {
    baseUrl: '',
    apiKey: '',
    defaultModel: 'gpt-5',
    apiMode: 'chat_completions',
    disableTracing: null as boolean | null,
    modelMapping: clone(OPENAI_MODEL_DEFAULTS)
  }
}

function normalizeClaudeEnv(raw: unknown): Record<string, string> {
  if (!isPlainObject(raw)) {
    return {}
  }
  const result: Record<string, string> = {}
  for (const [key, value] of Object.entries(raw)) {
    const normalizedKey = String(key).trim()
    if (!normalizedKey) {
      continue
    }
    // SDK 要求 dict[str,str];数字/布尔强转字符串以容错(后端 schema 校验仍会要求 string)
    result[normalizedKey] = value === null || value === undefined ? '' : String(value)
  }
  return result
}

function normalizeClaudeModelMapping(raw: unknown) {
  const base = clone(CLAUDE_MODEL_DEFAULTS)
  if (!isPlainObject(raw)) {
    return base
  }

  for (const alias of CLAUDE_ALIAS_ORDER) {
    const entry = raw[alias]
    if (isPlainObject(entry)) {
      if (typeof entry.id === 'string') {
        base[alias].id = entry.id
      }
      if (typeof entry.label === 'string' && entry.label.trim()) {
        base[alias].label = entry.label
      }
    }
  }

  return base
}

function normalizeOpenAiModelMapping(raw: unknown) {
  const source = isPlainObject(raw) ? raw : OPENAI_MODEL_DEFAULTS
  const result: Record<string, { id: string; label: string }> = {}

  for (const [key, entry] of Object.entries(source)) {
    const normalizedKey = String(key).trim()
    if (!normalizedKey) {
      continue
    }

    if (isPlainObject(entry)) {
      result[normalizedKey] = {
        id: typeof entry.id === 'string' && entry.id.trim() ? entry.id : normalizedKey,
        label: typeof entry.label === 'string' && entry.label.trim() ? entry.label : normalizedKey
      }
      continue
    }

    result[normalizedKey] = {
      id: normalizedKey,
      label: normalizedKey
    }
  }

  return Object.keys(result).length > 0 ? result : clone(OPENAI_MODEL_DEFAULTS)
}

function normalizeOpenAiApiMode(value: unknown) {
  const normalized = String(value ?? '').trim().toLowerCase().replace(/-/g, '_')
  return normalized === 'responses' ? 'responses' : 'chat_completions'
}

function normalizeDisableTracing(value: unknown): boolean | null {
  if (value === null) {
    return null
  }
  if (typeof value === 'boolean') {
    return value
  }
  return null
}

function normalize(group: SettingsGroupKey, raw: Record<string, any>) {
  const value = clone(raw ?? {})

  if (group === 'server') {
    value.agent ??= {}
    value.startup ??= {}
    value.ccr ??= {}
    value.server ??= {}
    value.server.port ??= 5000
    value.web ??= {}
    value.web.port ??= 5173
    value.agent.autoStart ??= true
    value.agent.baseUrl ??= ''
    value.agent.port ??= 8865
    value.startup.openBrowser ??= false
    value.startup.browserPath ??= ''
    value.ccr.enabled ??= false
    value.ccr.autoStart ??= true
    value.ccr.host ??= '127.0.0.1'
    value.ccr.port ??= 3456
  }

  if (group === 'web') {
    value.layerPresets ??= {}
    value.layerPresets.User ??= { enabledLayers: [] }
    value.layerPresets.Agent ??= { enabledLayers: [] }
    value.layerPresets.User.enabledLayers = Array.isArray(value.layerPresets.User.enabledLayers)
      ? value.layerPresets.User.enabledLayers
      : []
    value.layerPresets.Agent.enabledLayers = Array.isArray(value.layerPresets.Agent.enabledLayers)
      ? value.layerPresets.Agent.enabledLayers
      : []
  }

  if (group === 'agent') {
    const claude = isPlainObject(value.claude) ? value.claude : {}
    const openai = isPlainObject(value.openai) ? value.openai : {}

    return {
      ...value,
      runtimeProvider: normalizeProvider(value.runtimeProvider),
      claude: {
        ...createClaudeDefaults(),
        ...claude,
        modelMapping: normalizeClaudeModelMapping(claude.modelMapping),
        env: normalizeClaudeEnv(claude.env)
      },
      openai: {
        ...createOpenAiDefaults(),
        ...openai,
        apiMode: normalizeOpenAiApiMode(openai.apiMode),
        disableTracing: normalizeDisableTracing(openai.disableTracing),
        modelMapping: normalizeOpenAiModelMapping(openai.modelMapping)
      }
    }
  }

  if (group === 'ccr') {
    value.HOST ??= '127.0.0.1'
    value.PORT ??= 3456
    value.LOG ??= true
    value.LOG_LEVEL ??= 'info'
    value.API_TIMEOUT_MS ??= 300000
    value.Router ??= {}
    value.Router.default ??= ''
    value.Router.think ??= ''
    value.Router.background ??= ''
    value.Router.longContext ??= ''
    value.Router.longContextThreshold ??= 60000
    value.Providers = Array.isArray(value.Providers) ? value.Providers : []
  }

  return value
}

const createDraft = (key: SettingsGroupKey): GroupDraft => {
  const values = normalize(key, {})
  return {
    title: '',
    sourceFile: '',
    values,
    jsonText: formatJson(values),
    jsonError: null
  }
}

const drafts = reactive<Record<SettingsGroupKey, GroupDraft>>({
  server: createDraft('server'),
  web: createDraft('web'),
  agent: createDraft('agent'),
  ccr: createDraft('ccr')
})

for (const key of groupKeys) {
  watch(() => drafts[key].values, value => {
    if (!drafts[key].jsonError) {
      drafts[key].jsonText = formatJson(value)
    }
  }, { deep: true })
}

const agentRuntimeProvider = computed<ProviderKey>({
  get: () => normalizeProvider(drafts.agent.values.runtimeProvider),
  set: (value) => {
    drafts.agent.values.runtimeProvider = normalizeProvider(value)
    ensureDefaultModelForCurrentProvider()
  }
})

const isOpenAiProvider = computed(() => agentRuntimeProvider.value === 'openai')
const openAiModelEntries = computed<[string, ModelMappingEntry][]>(() => (
  Object.entries(drafts.agent.values.openai?.modelMapping ?? {}) as [string, ModelMappingEntry][]
))

// Claude SDK 子进程环境变量 (ClaudeAgentOptions.env) —— 仿 ~/.claude/settings.json env 段
const claudeEnvEntries = computed<[string, string][]>(() => (
  Object.entries((drafts.agent.values.claude?.env ?? {}) as Record<string, string>)
))

const modelOptions = computed(() => {
  const modelMapping = isOpenAiProvider.value
    ? drafts.agent.values.openai?.modelMapping ?? {}
    : drafts.agent.values.claude?.modelMapping ?? {}

  return Object.entries(modelMapping).map(([key, entry]) => {
    const record = isPlainObject(entry) ? entry : {}
    return {
      value: key,
      label: typeof record.label === 'string' && record.label.trim() ? record.label : capitalize(key),
      helper: isOpenAiProvider.value ? '' : typeof record.id === 'string' ? record.id : ''
    }
  })
})

function currentDefaultModelPath(provider: ProviderKey = agentRuntimeProvider.value) {
  return provider === 'openai' ? 'agent.openai.defaultModel' : 'agent.claude.defaultModel'
}

function readDefaultModel(provider: ProviderKey = agentRuntimeProvider.value) {
  return provider === 'openai'
    ? drafts.agent.values.openai?.defaultModel ?? ''
    : drafts.agent.values.claude?.defaultModel ?? ''
}

function writeDefaultModel(value: string, provider: ProviderKey = agentRuntimeProvider.value) {
  if (provider === 'openai') {
    drafts.agent.values.openai.defaultModel = value
  } else {
    drafts.agent.values.claude.defaultModel = value
  }
  runtime.value.effectiveDefaultModelPath = currentDefaultModelPath(provider)
  runtime.value.effectiveDefaultModelValue = value
}

const effectiveDefaultModel = computed({
  get: () => readDefaultModel(),
  set: (value: string) => {
    writeDefaultModel(value)
  }
})

const openAiTracingMode = computed({
  get: () => {
    const value = drafts.agent.values.openai?.disableTracing
    if (value === true) {
      return 'disabled'
    }
    if (value === false) {
      return 'enabled'
    }
    return 'auto'
  },
  set: (value: string) => {
    if (value === 'disabled') {
      drafts.agent.values.openai.disableTracing = true
      return
    }
    if (value === 'enabled') {
      drafts.agent.values.openai.disableTracing = false
      return
    }
    drafts.agent.values.openai.disableTracing = null
  }
})

function applyGroup(key: SettingsGroupKey, group: SettingsGroup) {
  drafts[key].title = group.title
  drafts[key].sourceFile = group.sourceFile
  drafts[key].values = normalize(key, group.values)
  drafts[key].jsonError = null
  drafts[key].jsonText = formatJson(drafts[key].values)
}

function ensureDefaultModelForCurrentProvider() {
  const provider = agentRuntimeProvider.value
  const options = provider === 'openai'
    ? Object.keys(drafts.agent.values.openai?.modelMapping ?? {})
    : Object.keys(drafts.agent.values.claude?.modelMapping ?? {})
  const current = String(readDefaultModel(provider)).trim()
  if (options.length === 0) {
    runtime.value.effectiveDefaultModelPath = currentDefaultModelPath(provider)
    runtime.value.effectiveDefaultModelValue = current
    return
  }

  if (!options.includes(current)) {
    writeDefaultModel(options[0] ?? '', provider)
    return
  }

  runtime.value.effectiveDefaultModelPath = currentDefaultModelPath(provider)
  runtime.value.effectiveDefaultModelValue = current
}

function applySnapshot(snapshot: SettingsSnapshot) {
  applyGroup('server', snapshot.server)
  applyGroup('web', snapshot.web)
  applyGroup('agent', snapshot.agent)
  applyGroup('ccr', snapshot.ccr)
  runtime.value = {
    ...defaultRuntime,
    ...(snapshot.runtime ?? {})
  }
  ensureDefaultModelForCurrentProvider()

  // 刷新统一快照基线;源模式下同步源文本
  lastSavedUnified.value = buildUnified()
  if (editorMode.value === 'source') {
    sourceText.value = formatJson(lastSavedUnified.value)
    sourceError.value = null
  }
}

// ── 双模式：统一文档构建 / 解析 / 往返 ──

function buildUnified(): Record<string, any> {
  return {
    ...clone(unifiedExtra.value),
    server: clone(drafts.server.values),
    web: clone(drafts.web.values),
    agent: clone(drafts.agent.values),
    ccr: clone(drafts.ccr.values)
  }
}

function tryParseUnified(): { ok: true; value: Record<string, any> } | { ok: false; error: string } {
  try {
    const parsed = JSON.parse(sourceText.value)
    if (!isPlainObject(parsed)) {
      return { ok: false, error: '顶层必须是 JSON 对象' }
    }
    for (const key of groupKeys) {
      if (parsed[key] !== undefined && !isPlainObject(parsed[key])) {
        return { ok: false, error: `段 "${key}" 必须是 JSON 对象` }
      }
    }
    return { ok: true, value: parsed }
  } catch (error: any) {
    return { ok: false, error: error?.message || 'JSON 解析失败' }
  }
}

function applyUnifiedToGroups(parsed: Record<string, any>) {
  // 无损往返:非分组 key 原样保留;已知分组经 normalize 后写入(normalize 会保留段内未知字段)
  const extra: Record<string, any> = {}
  for (const [key, value] of Object.entries(parsed)) {
    if (!(groupKeys as string[]).includes(key)) {
      extra[key] = value
    }
  }
  unifiedExtra.value = extra
  for (const key of groupKeys) {
    if (parsed[key] !== undefined) {
      drafts[key].values = normalize(key, parsed[key])
      drafts[key].jsonError = null
    }
  }
  ensureDefaultModelForCurrentProvider()
}

function switchEditorMode(mode: 'visual' | 'source') {
  if (mode === editorMode.value) return
  if (mode === 'source') {
    sourceText.value = formatJson(buildUnified())
    sourceError.value = null
  } else {
    const parsed = tryParseUnified()
    if (!parsed.ok) {
      sourceError.value = parsed.error
      return
    }
    applyUnifiedToGroups(parsed.value)
    sourceError.value = null
  }
  editorMode.value = mode
  try { localStorage.setItem(SETTINGS_EDITOR_MODE_KEY, mode) } catch { /* ignore */ }
}

const diffSections = computed(() => {
  const next = pendingPayload.value
  const base = lastSavedUnified.value || {}
  return groupKeys.map(key => {
    const before = formatJson((base as any)[key] ?? {})
    const after = formatJson((next as any)?.[key] ?? {})
    return { key, title: drafts[key].title || key, changed: before !== after, after }
  })
})
const hasDiffChanges = computed(() => diffSections.value.some(section => section.changed))

// 源文件 textarea 高度自适应内容,去掉内部滚动条(只保留外层页面滚动)
function autoResizeSource() {
  const el = sourceTextareaRef.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = `${el.scrollHeight}px`
}
watch(sourceText, () => { nextTick(autoResizeSource) })
watch(editorMode, (mode) => { if (mode === 'source') nextTick(autoResizeSource) })

async function loadSettings() {
  isLoading.value = true
  loadError.value = null
  saveError.value = null

  try {
    applySnapshot(await SettingsService.getSettings())
  } catch (error: any) {
    loadError.value = error.response?.data?.message || error.message || '加载设置失败'
  } finally {
    isLoading.value = false
  }
}

function parseJson(group: SettingsGroupKey) {
  try {
    const parsed = JSON.parse(drafts[group].jsonText)
    if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') {
      drafts[group].jsonError = '顶层必须是 JSON 对象'
      return false
    }

    drafts[group].values = normalize(group, parsed)
    drafts[group].jsonError = null
    drafts[group].jsonText = formatJson(drafts[group].values)

    if (group === 'agent') {
      ensureDefaultModelForCurrentProvider()
    }

    return true
  } catch (error: any) {
    drafts[group].jsonError = error.message || 'JSON 解析失败'
    return false
  }
}

function claudeModelMappingValue(alias: ClaudeAlias, field: 'id' | 'label' = 'id') {
  return drafts.agent.values.claude?.modelMapping?.[alias]?.[field] ?? ''
}

function setClaudeModelMappingValue(alias: ClaudeAlias, field: 'id' | 'label', value: string) {
  drafts.agent.values.claude.modelMapping ??= clone(CLAUDE_MODEL_DEFAULTS)
  drafts.agent.values.claude.modelMapping[alias] ??= clone(CLAUDE_MODEL_DEFAULTS[alias])
  drafts.agent.values.claude.modelMapping[alias][field] = value
}

function addOpenAiModelMappingEntry() {
  drafts.agent.values.openai.modelMapping ??= clone(OPENAI_MODEL_DEFAULTS)
  let index = Object.keys(drafts.agent.values.openai.modelMapping).length + 1
  let nextId = 'gpt-5'
  while (drafts.agent.values.openai.modelMapping[nextId]) {
    nextId = `model-${index}`
    index += 1
  }

  drafts.agent.values.openai.modelMapping[nextId] = {
    id: nextId,
    label: nextId.toUpperCase()
  }
}

function renameOpenAiModelId(previousId: string, nextValue: string) {
  const nextId = nextValue.trim()
  if (!nextId || nextId === previousId || drafts.agent.values.openai.modelMapping[nextId]) {
    return
  }

  const entry = clone(drafts.agent.values.openai.modelMapping[previousId] ?? { id: previousId, label: previousId })
  delete drafts.agent.values.openai.modelMapping[previousId]
  drafts.agent.values.openai.modelMapping[nextId] = {
    ...entry,
    id: nextId
  }

  if (drafts.agent.values.openai.defaultModel === previousId) {
    writeDefaultModel(nextId, 'openai')
  }
}

function setOpenAiModelLabel(modelId: string, value: string) {
  drafts.agent.values.openai.modelMapping[modelId] ??= { id: modelId, label: modelId }
  drafts.agent.values.openai.modelMapping[modelId].label = value
}

function removeOpenAiModelMappingEntry(modelId: string) {
  delete drafts.agent.values.openai.modelMapping[modelId]
  ensureDefaultModelForCurrentProvider()
}

function addClaudeEnvEntry() {
  drafts.agent.values.claude.env ??= {}
  let index = Object.keys(drafts.agent.values.claude.env).length + 1
  let nextKey = `ENV_VAR_${index}`
  while (drafts.agent.values.claude.env[nextKey] !== undefined) {
    index += 1
    nextKey = `ENV_VAR_${index}`
  }
  drafts.agent.values.claude.env[nextKey] = ''
}

function renameClaudeEnvKey(previousKey: string, nextValue: string) {
  const nextKey = nextValue.trim()
  if (!nextKey || nextKey === previousKey) {
    return
  }
  drafts.agent.values.claude.env ??= {}
  if (drafts.agent.values.claude.env[nextKey] !== undefined) {
    // 目标 key 已存在,拒绝改名以避免静默覆盖
    return
  }
  const value = drafts.agent.values.claude.env[previousKey] ?? ''
  delete drafts.agent.values.claude.env[previousKey]
  drafts.agent.values.claude.env[nextKey] = value
}

function setClaudeEnvValue(key: string, value: string) {
  drafts.agent.values.claude.env ??= {}
  drafts.agent.values.claude.env[key] = value
}

function removeClaudeEnvEntry(key: string) {
  if (!drafts.agent.values.claude?.env) {
    return
  }
  delete drafts.agent.values.claude.env[key]
}

function textToLines(text: string) {
  return text.split(/\r?\n/).map(item => item.trim()).filter(Boolean)
}

function handleLayerPresetInput(target: string[], event: Event) {
  target.splice(0, target.length, ...textToLines((event.target as HTMLTextAreaElement).value))
}

function providerModels(provider: any) {
  return Array.isArray(provider.models) ? provider.models.join(', ') : ''
}

function updateProviderModels(provider: any, event: Event) {
  provider.models = (event.target as HTMLInputElement).value
    .split(',')
    .map(item => item.trim())
    .filter(Boolean)
}

// 保存前:校验当前编辑内容,准备 payload,弹出 Diff 确认。
function requestSave() {
  saveError.value = null
  saveMessage.value = null

  if (editorMode.value === 'source') {
    const parsed = tryParseUnified()
    if (!parsed.ok) {
      sourceError.value = parsed.error
      saveError.value = `源文件 JSON 有误：${parsed.error}`
      return
    }
    applyUnifiedToGroups(parsed.value)
    sourceError.value = null
  } else {
    for (const key of groupKeys) {
      if (!parseJson(key)) {
        saveError.value = `${drafts[key].title || key} 格式有误`
        return
      }
    }
  }

  pendingPayload.value = {
    server: clone(drafts.server.values),
    web: clone(drafts.web.values),
    agent: clone(drafts.agent.values),
    ccr: clone(drafts.ccr.values)
  }
  showDiffModal.value = true
}

function cancelSave() {
  showDiffModal.value = false
  pendingPayload.value = null
}

// 用户在 Diff 弹窗确认后:整份提交。
async function confirmSave() {
  if (!pendingPayload.value) return
  const payload = pendingPayload.value
  showDiffModal.value = false
  isSaving.value = true
  try {
    const result = await SettingsService.saveSettings(payload)

    applySnapshot(result.settings)
    saveMessage.value = '保存成功。'

    if (result.restartRequiredGroups.length > 0) {
      // 统一通过 systemStore 管理:每个变更 group 单独 mark,触发顶栏 [需要重启] 按钮 +
      // 一条左下 toast 告知。不再弹模态对话框(原对话框已删,与"插件管理"统一)。
      for (const group of result.restartRequiredGroups) {
        systemStore.markRestartRequired(`settings:${group}`)
      }
      systemStore.pushToast({
        title: '需要重启',
        message: `配置已保存（${result.restartRequiredGroups.join(', ')}），点击顶栏 [需要重启] 生效。`,
        type: 'warning',
      })
    }

    window.dispatchEvent(new CustomEvent('bimcanvas:web-config-updated', {
      detail: clone(result.settings.web.values)
    }))
  } catch (error: any) {
    saveError.value = error.response?.data?.message || error.message || '保存配置失败'
  } finally {
    isSaving.value = false
    pendingPayload.value = null
  }
}

function resolveModelId(provider: ProviderKey, alias: string) {
  const mapping = provider === 'openai'
    ? drafts.agent.values.openai?.modelMapping
    : drafts.agent.values.claude?.modelMapping
  const entry = isPlainObject(mapping?.[alias]) ? mapping[alias] : null
  const id = entry && typeof entry.id === 'string' ? entry.id.trim() : ''
  return id || alias
}

async function handleConnectionTest(provider: ProviderKey) {
  const isClaude = provider === 'claude'
  const baseUrl = isClaude
    ? String(drafts.agent.values.claude?.baseUrl ?? '').trim()
    : String(drafts.agent.values.openai?.baseUrl ?? '').trim()
  const apiKey = isClaude
    ? String(drafts.agent.values.claude?.apiKey ?? '')
    : String(drafts.agent.values.openai?.apiKey ?? '')
  const alias = readDefaultModel(provider)
  const model = resolveModelId(provider, alias)
  const apiMode = isClaude ? null : drafts.agent.values.openai?.apiMode ?? null

  const flag = isClaude ? isClaudeTesting : isOpenAiTesting
  const slot = isClaude ? claudeTestResult : openAiTestResult

  if (flag.value) {
    return
  }
  if (!baseUrl || !apiKey) {
    return
  }

  flag.value = true
  slot.value = null
  try {
    slot.value = await SettingsService.testLlmEndpoint({
      runtimeProvider: provider,
      baseUrl,
      apiKey,
      model,
      apiMode
    })
  } catch (err: any) {
    slot.value = {
      success: false,
      latencyMs: 0,
      statusCode: null,
      errorType: 'unknown',
      errorMessage: err?.message ?? '请求失败',
      sampleResponseSnippet: '',
      requestUrl: baseUrl
    }
  } finally {
    flag.value = false
  }
}

function describeTestResult(result: LlmEndpointTestResult | null) {
  if (!result) return ''
  if (result.success) {
    const latency = result.latencyMs > 0 ? ` (${result.latencyMs} ms)` : ''
    const snippet = result.sampleResponseSnippet
      ? `，回复：「${result.sampleResponseSnippet}」`
      : ''
    return `✓ 端点正常${latency}${snippet}`
  }
  const code = result.statusCode ? ` ${result.statusCode}` : ''
  switch (result.errorType) {
    case 'auth_failed':
      return `✗ 认证失败${code}，检查 apiKey`
    case 'timeout':
      return '✗ 端点 15 秒未响应（服务端排队/吊死，与认证无关）'
    case 'network_unreachable':
      return `✗ 无法连接 ${result.requestUrl || ''}（DNS / 网络问题）`
    case 'rate_limited':
      return `✗ 触发限流${code}`
    case 'server_error':
      return `✗ 服务端错误${code}`
    case 'bad_request':
      return `✗ 请求被拒${code}：${result.errorMessage || '未知原因'}`
    default:
      return `✗ ${result.errorType}：${result.errorMessage || '未知错误'}`
  }
}

function testResultDetail(result: LlmEndpointTestResult | null) {
  if (!result) return ''
  return JSON.stringify(result, null, 2)
}

onMounted(() => {
  isMounted.value = true
  loadSettings()
})
</script>

<template>
  <div class="settings-page">
    <Teleport to="#settings-header-actions" v-if="isMounted">
      <div class="teleported-actions">
        <GlassButton variant="ghost" :disabled="isLoading" @click="loadSettings">取消</GlassButton>
        <GlassButton variant="primary" :disabled="isSaving || isLoading" @click="requestSave" style="display: flex; align-items: center; justify-content: center; gap: 6px;">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="width: 14px; height: 14px;"><polyline points="20 6 9 17 4 12"></polyline></svg>
          {{ isSaving ? '保存中...' : '保存' }}
        </GlassButton>
      </div>
    </Teleport>

    <div class="settings-main">
      <div class="layout-bound wrapper-pad">
        <div v-if="loadError || saveError || saveMessage" class="alerts mb-md">
          <div v-if="loadError" class="alert alert-error">{{ loadError }}</div>
          <div v-if="saveError" class="alert alert-error">{{ saveError }}</div>
          <div v-if="saveMessage" class="alert alert-success">{{ saveMessage }}</div>
        </div>

        <div v-if="isLoading" class="loading-state">加载配置...</div>

        <template v-else>
          <!-- 编辑模式切换：可视化 / 源文件 -->
          <div class="editor-mode-tabs">
            <button type="button" class="mode-tab" :class="{ active: editorMode === 'visual' }" @click="switchEditorMode('visual')">可视化编辑</button>
            <button type="button" class="mode-tab" :class="{ active: editorMode === 'source' }" @click="switchEditorMode('source')">源文件编辑</button>
          </div>

          <div v-show="editorMode === 'visual'">
          <!-- Card 1: Agent -->
          <article class="config-card">
            <header class="card-header">
              <div class="heading-left">
                <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M12 2l8 4v6c0 5.25-3.438 9.938-8 11-4.562-1.062-8-5.75-8-11V6l8-4z"/><path d="M9 12l2 2 4-4"/></svg>
                <div class="heading-text">
                  <h3>Agent</h3>
                  <p>Runtime 选择与当前 provider 配置。表单只显示当前 provider，另一套配置会保留并随保存一起回写。</p>
                </div>
              </div>
              <div class="heading-right heading-right-wide">
                <div class="field compact-field">
                  <label>Runtime Provider</label>
                  <GlassSelect
                    v-model="agentRuntimeProvider"
                    class="settings-select"
                    width="220px"
                    :options="runtimeProviderOptions"
                    placeholder="选择 Runtime"
                  />
                </div>
              </div>
            </header>

            <div class="card-body">
              <div v-if="!isOpenAiProvider" class="config-section-stack">
                <section class="inner-subcard">
                  <div class="section-header">
                    <div>
                      <h4>Claude 连接</h4>
                      <p>填写 Claude 的接口地址与密钥。启用 CCR 网关时，实际请求由网关接管。</p>
                    </div>
                  </div>

                  <div class="form-grid">
                    <div class="field">
                      <label>Base URL</label>
                      <input v-model="drafts.agent.values.claude.baseUrl" type="text" placeholder="https://api.anthropic.com">
                    </div>
                    <div class="field">
                      <label>API Key</label>
                      <div class="input-wrapper">
                        <input v-model="drafts.agent.values.claude.apiKey" :type="showSecrets ? 'text' : 'password'" placeholder="空" class="pr-icon">
                        <button type="button" class="eye-btn" @click="showSecrets = !showSecrets" title="切换密码可视化">
                          <svg v-if="!showSecrets" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path><circle cx="12" cy="12" r="3"></circle></svg>
                          <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24M1 1l22 22"></path></svg>
                        </button>
                      </div>
                    </div>
                  </div>

                  <div class="diag-link-row">
                    <a
                      href="javascript:void(0)"
                      class="diag-link"
                      :class="{ disabled: isClaudeTesting || !drafts.agent.values.claude?.baseUrl || !drafts.agent.values.claude?.apiKey }"
                      @click="handleConnectionTest('claude')"
                    >· {{ isClaudeTesting ? '检测中…' : '检测当前连接' }}</a>
                  </div>
                  <div
                    v-if="claudeTestResult"
                    class="alert"
                    :class="claudeTestResult.success ? 'alert-success' : 'alert-error'"
                  >
                    {{ describeTestResult(claudeTestResult) }}
                    <details class="diag-detail">
                      <summary>详情</summary>
                      <pre>{{ testResultDetail(claudeTestResult) }}</pre>
                    </details>
                  </div>
                </section>

                <section class="inner-subcard">
                  <div class="section-header">
                    <div>
                      <h4>Claude 推理参数</h4>
                      <p>默认模型、推理深度与扩展思考预算。</p>
                    </div>
                  </div>

                  <div class="form-grid">
                    <div class="field">
                      <label>默认模型</label>
                      <GlassSelect
                        v-model="effectiveDefaultModel"
                        class="settings-select"
                        width="100%"
                        :options="modelOptions"
                        placeholder="选择默认模型"
                      />
                    </div>
                  </div>

                  <div class="form-grid mt-md">
                    <div class="field">
                      <label>Effort</label>
                      <GlassSelect
                        v-model="drafts.agent.values.claude.defaultEffort"
                        class="settings-select"
                        width="100%"
                        :options="effortOptions"
                        placeholder="选择 Effort"
                      />
                    </div>
                    <div class="field">
                      <label>Thinking</label>
                      <GlassSelect
                        v-model="drafts.agent.values.claude.defaultThinking"
                        class="settings-select"
                        width="100%"
                        :options="thinkingOptions"
                        placeholder="选择 Thinking"
                      />
                    </div>
                  </div>

                  <div class="form-grid mt-md">
                    <div class="field">
                      <label>最大 Thinking Tokens</label>
                      <input v-model.number="drafts.agent.values.claude.maxThinkingTokens" type="number">
                    </div>
                  </div>

                  <div class="divider mt-xl mb-md"><span>模型 ID 映射</span></div>
                  <div class="form-grid">
                    <div class="field" v-for="alias in CLAUDE_ALIAS_ORDER" :key="alias">
                      <label>{{ capitalize(alias) }}</label>
                      <input :value="claudeModelMappingValue(alias)" type="text" :placeholder="CLAUDE_MODEL_DEFAULTS[alias].id" @input="setClaudeModelMappingValue(alias, 'id', ($event.target as HTMLInputElement).value)">
                    </div>
                  </div>
                </section>

                <section class="inner-subcard">
                  <div class="section-header">
                    <div>
                      <h4>Claude 子进程环境变量</h4>
                      <p>传给 Claude Code CLI 子进程的环境变量，仿 <code>~/.claude/settings.json</code> 的 <code>env</code> 段。Key 和 Value 必须都是字符串（布尔/数字请填 <code>"1"</code>、<code>"0"</code> 等）。</p>
                    </div>
                  </div>

                  <div class="mapping-list">
                    <div v-for="[envKey, envValue] in claudeEnvEntries" :key="envKey" class="mapping-row">
                      <div class="field">
                        <label>Key</label>
                        <input :value="envKey" type="text" placeholder="CLAUDE_CODE_WORKFLOWS" @change="renameClaudeEnvKey(envKey, ($event.target as HTMLInputElement).value)">
                      </div>
                      <div class="field">
                        <label>Value</label>
                        <input :value="envValue" type="text" placeholder="1" @input="setClaudeEnvValue(envKey, ($event.target as HTMLInputElement).value)">
                      </div>
                      <button type="button" class="btn btn-danger mapping-remove" @click="removeClaudeEnvEntry(envKey)">移除</button>
                    </div>
                  </div>
                  <div class="mapping-toolbar mt-md">
                    <button type="button" class="btn btn-ghost" @click="addClaudeEnvEntry">添加环境变量</button>
                  </div>
                </section>
              </div>

              <div v-else class="config-section-stack">
                <section class="inner-subcard">
                  <div class="section-header">
                    <div>
                      <h4>OpenAI 连接</h4>
                      <p>填写 OpenAI 的接口地址与密钥。</p>
                    </div>
                  </div>

                  <div class="form-grid">
                    <div class="field">
                      <label>Base URL</label>
                      <input v-model="drafts.agent.values.openai.baseUrl" type="text" placeholder="https://api.openai.com/v1">
                    </div>
                    <div class="field">
                      <label>API Key</label>
                      <div class="input-wrapper">
                        <input v-model="drafts.agent.values.openai.apiKey" :type="showSecrets ? 'text' : 'password'" placeholder="空" class="pr-icon">
                        <button type="button" class="eye-btn" @click="showSecrets = !showSecrets" title="切换密码可视化">
                          <svg v-if="!showSecrets" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path><circle cx="12" cy="12" r="3"></circle></svg>
                          <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24M1 1l22 22"></path></svg>
                        </button>
                      </div>
                    </div>
                  </div>

                  <div class="diag-link-row">
                    <a
                      href="javascript:void(0)"
                      class="diag-link"
                      :class="{ disabled: isOpenAiTesting || !drafts.agent.values.openai?.baseUrl || !drafts.agent.values.openai?.apiKey }"
                      @click="handleConnectionTest('openai')"
                    >· {{ isOpenAiTesting ? '检测中…' : '检测当前连接' }}</a>
                  </div>
                  <div
                    v-if="openAiTestResult"
                    class="alert"
                    :class="openAiTestResult.success ? 'alert-success' : 'alert-error'"
                  >
                    {{ describeTestResult(openAiTestResult) }}
                    <details class="diag-detail">
                      <summary>详情</summary>
                      <pre>{{ testResultDetail(openAiTestResult) }}</pre>
                    </details>
                  </div>

                  <div class="form-grid mt-md">
                    <div class="field">
                      <label>API Mode</label>
                      <GlassSelect
                        v-model="drafts.agent.values.openai.apiMode"
                        class="settings-select"
                        width="100%"
                        :options="openAiApiModeOptions"
                        placeholder="选择 API 模式"
                      />
                    </div>
                    <div class="field">
                      <label>Tracing</label>
                      <GlassSelect
                        v-model="openAiTracingMode"
                        class="settings-select"
                        width="100%"
                        :options="openAiTracingOptions"
                        placeholder="选择 Tracing 策略"
                      />
                    </div>
                  </div>

                  <div class="form-grid mt-md">
                    <div class="field">
                      <label>默认模型</label>
                      <GlassSelect
                        v-model="effectiveDefaultModel"
                        class="settings-select"
                        width="100%"
                        :options="modelOptions"
                        placeholder="选择默认模型"
                      />
                    </div>
                  </div>

                  <div class="divider mt-xl mb-md"><span>模型 ID 映射</span></div>
                  <div class="mapping-list">
                    <div v-for="[modelId, entry] in openAiModelEntries" :key="modelId" class="mapping-row">
                      <div class="field">
                        <label>Model ID</label>
                        <input :value="modelId" type="text" placeholder="gpt-5" @change="renameOpenAiModelId(modelId, ($event.target as HTMLInputElement).value)">
                      </div>
                      <div class="field">
                        <label>Label</label>
                        <input :value="entry.label" type="text" placeholder="GPT-5" @input="setOpenAiModelLabel(modelId, ($event.target as HTMLInputElement).value)">
                      </div>
                      <button type="button" class="btn btn-danger mapping-remove" @click="removeOpenAiModelMappingEntry(modelId)">移除</button>
                    </div>
                  </div>
                  <div class="mapping-toolbar mt-md">
                    <button type="button" class="btn btn-ghost" @click="addOpenAiModelMappingEntry">添加模型</button>
                  </div>
                </section>
              </div>

            </div>
          </article>

          <!-- Card 2: 服务与连接 -->
          <article class="config-card">
            <header class="card-header">
              <div class="heading-left">
                <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8m-4-4v4"/></svg>
                <div class="heading-text">
                  <h3>服务与连接</h3>
                  <p>Agent 连接方式与启动选项。端口、CCR 主机等高级项请用「源文件编辑」。</p>
                </div>
              </div>
              <div class="heading-right">
                <div class="segment-group">
                  <label class="segment" :class="{ 'segment-active': !drafts.server.values.ccr.enabled }">
                    <input type="radio" :value="false" v-model="drafts.server.values.ccr.enabled"> 直连 (Direct)
                  </label>
                  <label class="segment" :class="{ 'segment-active': drafts.server.values.ccr.enabled }">
                    <input type="radio" :value="true" v-model="drafts.server.values.ccr.enabled"> CCR 网关
                  </label>
                </div>
              </div>
            </header>

            <div class="card-body">
              <div class="inline-alert warm mb-lg">
                <svg class="alert-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                <span>{{ drafts.server.values.ccr.enabled ? '所有模型请求经由 CCR 网关转发，支持多 Provider 负载均衡。' : 'Agent 直接调用模型 API，适合开发测试环境。' }}</span>
              </div>

              <div class="form-grid">
                <div class="field">
                  <label>Agent 基址</label>
                  <input v-model="drafts.server.values.agent.baseUrl" type="text" placeholder="留空时回退到本地托管 Agent；填外部地址则代理到该 Agent">
                </div>
              </div>

              <label class="checkbox-label mt-md">
                <input type="checkbox" v-model="drafts.server.values.agent.autoStart" class="checkbox-input">
                <span class="custom-checkbox"></span>
                <div class="checkbox-texts">
                  <span class="primary">自动启动内置 Agent</span>
                  <span class="secondary">关闭后，Server 将通过 Agent 基址连接外部 Agent 服务</span>
                </div>
              </label>

              <div class="divider mt-xl mb-md"><span>启动选项</span></div>

              <label class="checkbox-label">
                <input type="checkbox" v-model="drafts.server.values.startup.openBrowser" class="checkbox-input">
                <span class="custom-checkbox"></span>
                <div class="checkbox-texts">
                  <span class="primary">启动时打开浏览器</span>
                  <span class="secondary">实例启动后自动打开浏览器访问 Web 前端</span>
                </div>
              </label>

              <div v-if="drafts.server.values.startup.openBrowser" class="form-grid mt-md">
                <div class="field">
                  <label>浏览器路径</label>
                  <input v-model="drafts.server.values.startup.browserPath" type="text" placeholder="留空使用系统默认">
                </div>
              </div>
            </div>
          </article>

          <!-- Card 3: CCR -->
          <article class="config-card">
            <header class="card-header">
              <div class="heading-left">
                <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                <div class="heading-text">
                  <h3>CCR 网关</h3>
                  <p>Claude Code Router 网关配置（仅当「服务与连接」选择 CCR 网关时生效）。</p>
                </div>
              </div>
            </header>

            <div class="card-body">
              <div class="form-grid mb-md">
                <div class="field">
                  <label>CCR 监听 Host</label>
                  <input v-model="drafts.ccr.values.HOST" type="text">
                </div>
                <div class="field">
                  <label>CCR 监听 Port</label>
                  <input v-model.number="drafts.ccr.values.PORT" type="number">
                </div>
              </div>

              <div class="divider mt-xl mb-md"><span>Provider 列表</span></div>
              <div class="cluster-pool">
                <div v-for="(provider, index) in drafts.ccr.values.Providers" :key="index" class="inner-subcard cluster-card">
                  <div class="cluster-header">
                    <span class="cluster-title">{{ provider.name || `Provider Nodes [${index}]` }}</span>
                    <span class="badge badge-normal badge-mono">{{ provider.models?.length || 0 }} Models Listed</span>
                  </div>
                  <div class="form-grid mt-sm">
                    <div class="field">
                      <label>Base URL</label>
                      <input v-model="provider.api_base_url" type="text">
                    </div>
                    <div class="field">
                      <label>API Key</label>
                      <div class="input-wrapper">
                        <input v-model="provider.api_key" :type="showSecrets ? 'text' : 'password'" class="pr-icon">
                        <button type="button" class="eye-btn" @click="showSecrets = !showSecrets" title="切换密码可视化">
                          <svg v-if="!showSecrets" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path><circle cx="12" cy="12" r="3"></circle></svg>
                          <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24M1 1l22 22"></path></svg>
                        </button>
                      </div>
                    </div>
                  </div>
                  <div class="field mt-sm">
                    <label>可用模型 (逗号分隔)</label>
                    <input :value="providerModels(provider)" type="text" class="mono-font" @input="updateProviderModels(provider, $event)">
                  </div>
                </div>
              </div>

              <div class="divider mt-xl mb-md"><span>路由规则</span></div>
              <div class="form-grid">
                <div class="field">
                  <label>Default (兜底)</label>
                  <input v-model="drafts.ccr.values.Router.default" type="text" class="mono-font">
                </div>
                <div class="field">
                  <label>Think (深度思考)</label>
                  <input v-model="drafts.ccr.values.Router.think" type="text" class="mono-font">
                </div>
              </div>
              <div class="form-grid mt-md">
                <div class="field">
                  <label>Background (后台任务)</label>
                  <input v-model="drafts.ccr.values.Router.background" type="text" class="mono-font">
                </div>
                <div class="field">
                  <label>Long Context (长上下文)</label>
                  <input v-model="drafts.ccr.values.Router.longContext" type="text" class="mono-font">
                </div>
              </div>
              <div class="form-grid mt-md">
                <div class="field">
                  <label>长上下文阈值 (tokens)</label>
                  <input v-model.number="drafts.ccr.values.Router.longContextThreshold" type="number">
                </div>
              </div>
              <div class="form-grid form-grid-bottom mt-md">
                <div class="field">
                  <label>API 超时 (ms)</label>
                  <input v-model.number="drafts.ccr.values.API_TIMEOUT_MS" type="number">
                </div>
                <div class="field field-checkbox">
                  <label class="checkbox-label">
                    <input type="checkbox" v-model="drafts.ccr.values.LOG" class="checkbox-input">
                    <span class="custom-checkbox"></span>
                    <div class="checkbox-texts">
                      <span class="primary">启用请求日志</span>
                      <span class="secondary">在控制台输出请求和响应信息</span>
                    </div>
                  </label>
                </div>
              </div>
              <div class="form-grid mt-md" v-if="drafts.ccr.values.LOG">
                <div class="field">
                  <label>日志级别</label>
                  <GlassSelect
                    v-model="drafts.ccr.values.LOG_LEVEL"
                    class="settings-select"
                    width="100%"
                    :options="logLevelOptions"
                    placeholder="选择日志级别"
                  />
                </div>
              </div>

            </div>
          </article>

          <!-- Card 4: Web 展现预设 -->
          <article class="config-card">
            <header class="card-header">
              <div class="heading-left">
                <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
                <div class="heading-text">
                  <h3>Web 前端</h3>
                  <p>前端显示配置，当前仅管理图层预设；修改后立即生效，无需重启。</p>
                </div>
              </div>
            </header>
            
            <div class="card-body">
              <div class="divider mt-xl mb-md"><span>图层预设</span></div>
              <div class="form-grid">
                <div class="field">
                  <label>用户默认图层</label>
                  <textarea :value="drafts.web.values.layerPresets.User.enabledLayers.join('\n')" rows="5" class="mono-font" @input="handleLayerPresetInput(drafts.web.values.layerPresets.User.enabledLayers, $event)" />
                </div>
                <div class="field">
                  <label>Agent 默认图层</label>
                  <textarea :value="drafts.web.values.layerPresets.Agent.enabledLayers.join('\n')" rows="5" class="mono-font" @input="handleLayerPresetInput(drafts.web.values.layerPresets.Agent.enabledLayers, $event)" />
                </div>
              </div>

            </div>
          </article>
          </div>

          <!-- 源文件模式：编辑整份统一配置 JSON -->
          <div v-show="editorMode === 'source'" class="source-editor-wrap">
            <article class="config-card">
              <header class="card-header">
                <div class="heading-left">
                  <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                  <div class="heading-text">
                    <h3>源文件编辑 — {{ unifiedFileName }}</h3>
                    <p>直接编辑统一运行时配置（server / web / agent / ccr 四段）。切回可视化或保存时会校验 JSON；未识别字段会原样保留。</p>
                  </div>
                </div>
              </header>
              <div class="card-body">
                <textarea ref="sourceTextareaRef" class="editor-textarea source-textarea" v-model="sourceText" rows="20" spellcheck="false" @input="autoResizeSource"></textarea>
                <div v-if="sourceError" class="code-error">{{ sourceError }}</div>
              </div>
            </article>
          </div>

        </template>
      </div>
    </div>

    <!-- 保存确认（Diff）弹窗 -->
    <div v-if="showDiffModal" class="diff-modal-overlay" @click.self="cancelSave">
      <div class="diff-modal">
        <div class="diff-modal-header">
          <h3>确认保存</h3>
          <p>{{ hasDiffChanges ? '以下分段将写入统一配置文件：' : '未检测到与上次保存的差异。' }}</p>
        </div>
        <div class="diff-modal-body">
          <template v-for="section in diffSections" :key="section.key">
            <div v-if="section.changed" class="diff-section">
              <div class="diff-section-title">{{ section.title }} <span class="diff-badge">已修改</span></div>
              <pre class="diff-pre">{{ section.after }}</pre>
            </div>
          </template>
          <div v-if="!hasDiffChanges" class="diff-empty">无改动；仍可点击「确认保存」覆盖写入。</div>
        </div>
        <div class="diff-modal-footer">
          <GlassButton variant="ghost" @click="cancelSave">取消</GlassButton>
          <GlassButton variant="primary" :disabled="isSaving" @click="confirmSave">{{ isSaving ? '保存中...' : '确认保存' }}</GlassButton>
        </div>
      </div>
    </div>

  </div>
</template>

<style scoped>
/* Card-Based Moduler Dark Theme (Zinc-driven) */
.settings-page {
  --zinc-50:  #fafafa;
  --zinc-100: #f4f4f5;
  --zinc-200: #e4e4e7;
  --zinc-300: #d4d4d8;
  --zinc-400: #a1a1aa;
  --zinc-500: #71717a;
  --zinc-600: #52525b;
  --zinc-700: #3f3f46;
  --zinc-800: #27272a;
  --zinc-900: #18181b;
  --zinc-950: #0a0a0a;
  
  --bg-app: transparent;
  --bg-card: var(--glass-bg);
  --bg-input: rgba(0, 0, 0, 0.35);
  --bg-subcard: rgba(0, 0, 0, 0.2);
  
  --border-muted: rgba(255,255,255, 0.06);
  --border-card: rgba(255, 255, 255, 0.08);
  --border-focus: var(--accent-blue);
  
  --text-main: var(--zinc-50);
  --text-muted: var(--zinc-400);

  --radius-xs: 4px;
  --radius-sm: 6px;
  --radius-md: 8px;
  --radius-lg: 12px;
  
  display: flex;
  flex-direction: column;
  height: 100%;
  background-color: var(--bg-app);
  color: var(--text-main);
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Arial, sans-serif;
  overflow: hidden;
  font-size: 14px;
  color-scheme: dark;
}

hr { border: none; }
.layout-bound { max-width: 860px; margin: 0 auto; width: 100%; }

/* Page Intro */
.page-intro { margin-bottom: 24px; padding-left: 8px; }
.page-title {
  font-size: 1.6rem; font-weight: 600; color: var(--zinc-50);
  margin: 0 0 8px 0; letter-spacing: -0.02em;
}
.page-desc {
  font-size: 0.95rem; color: var(--zinc-400);
  margin: 0; line-height: 1.5;
}

/* Actions Teleported to Header */
.teleported-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.settings-main { flex: 1; overflow-y: auto; overflow-x: hidden; }
.wrapper-pad { padding: 32px 24px 80px; }
.settings-main::-webkit-scrollbar { width: 10px; height: 10px; }
.settings-main::-webkit-scrollbar-track { background: transparent; }
.settings-main::-webkit-scrollbar-thumb { background: var(--zinc-800); border-radius: 5px; border: 2px solid var(--bg-app); }
.settings-main::-webkit-scrollbar-thumb:hover { background: var(--zinc-600); }

/* Buttons */
.btn {
  display: inline-flex; align-items: center; justify-content: center;
  height: 32px; padding: 0 14px; border-radius: var(--radius-sm); font-size: 13px; font-weight: 500;
  cursor: pointer; transition: 0.15s; outline: none; border: 1px solid transparent; font-family: inherit;
}
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-primary { background: var(--zinc-50); color: black; }
.btn-primary:hover:not(:disabled) { background: var(--zinc-200); }
.btn-accent { 
  background: rgba(59, 130, 246, 0.15); 
  color: var(--accent-blue, #60a5fa); 
  border-color: rgba(59, 130, 246, 0.4); 
  box-shadow: inset 0 1px 1px rgba(255, 255, 255, 0.05), 0 0 12px rgba(59, 130, 246, 0.15);
}
.btn-accent:hover:not(:disabled) {
  background: rgba(59, 130, 246, 0.25);
  border-color: rgba(59, 130, 246, 0.6);
  box-shadow: inset 0 1px 1px rgba(255, 255, 255, 0.1), 0 0 16px rgba(59, 130, 246, 0.25);
}
.btn-ghost { background: transparent; color: var(--text-muted); }
.btn-ghost:hover:not(:disabled) { background: var(--zinc-800); color: var(--text-main); }
.btn-danger { background: transparent; color: #ef4444; border-color: var(--border-card); }
.btn-danger:hover:not(:disabled) { background: rgba(239, 68, 68, 0.1); border-color: #ef4444; }

/* Status Badges */
.badge { display: inline-flex; height: 22px; padding: 0 8px; border-radius: var(--radius-xs); font-size: 12px; font-weight: 500; align-items: center; gap: 6px; }
.badge-mono { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
.badge-normal { background: var(--zinc-800); color: var(--zinc-300); }
.subtle-badge { font-size: 11px; color: var(--zinc-500); background: transparent; border: 1px solid var(--border-muted); }

/* Inline Alerts & Blocks */
.alerts { display: flex; flex-direction: column; gap: 12px; }
.alert { padding: 12px 16px; border-radius: var(--radius-md); font-size: 13px; border: 1px solid transparent; line-height: 1.5; }
.alert-error { background: rgba(239, 68, 68, 0.1); border-color: rgba(239, 68, 68, 0.2); color: #ef4444; }
.alert-warning { background: rgba(234, 179, 8, 0.1); border-color: rgba(234, 179, 8, 0.2); color: #fde047; }
.alert-success { background: rgba(34, 197, 94, 0.1); border-color: rgba(34, 197, 94, 0.2); color: #4ade80; }

.diag-link-row { text-align: center; margin: 8px 0 4px; }
.diag-link { font-size: 12px; color: var(--zinc-500); text-decoration: none; opacity: 0.7; cursor: pointer; letter-spacing: 0.02em; }
.diag-link:hover { opacity: 1; text-decoration: underline; }
.diag-link.disabled { pointer-events: none; opacity: 0.35; }
.diag-detail { margin-top: 6px; font-size: 11px; }
.diag-detail summary { cursor: pointer; opacity: 0.7; user-select: none; }
.diag-detail pre { font-size: 11px; max-height: 240px; overflow: auto; margin: 6px 0 0; padding: 8px; background: rgba(255, 255, 255, 0.04); border-radius: 4px; white-space: pre-wrap; word-break: break-all; }

.inline-alert { display: flex; gap: 12px; padding: 12px 16px; border-radius: var(--radius-md); font-size: 13px; line-height: 1.5; }
.inline-alert svg { width: 18px; height: 18px; flex-shrink: 0; margin-top: 1px; }
.inline-alert.warm { background: rgba(234, 179, 8, 0.06); border: 1px solid rgba(234, 179, 8, 0.15); color: #fde047; }

/* 去掉 number 输入框原生上下箭头 spinner（与暗色玻璃风格统一） */
.settings-page input[type="number"] { -moz-appearance: textfield; appearance: textfield; }
.settings-page input[type="number"]::-webkit-outer-spin-button,
.settings-page input[type="number"]::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
.text-white { color: var(--text-main); }
.loading-state { text-align: center; color: var(--zinc-500); padding: 80px 0; }

/* === Card Framework (Core Enhancement) === */
.config-card {
  background-color: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(255,255,255,0.06);
  border-radius: var(--radius-lg, 12px);
  margin-bottom: 24px;
  box-shadow: inset 0 1px 1px rgba(255, 255, 255, 0.08), 0 8px 32px rgba(0, 0, 0, 0.4);
  overflow: hidden;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 24px 32px 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.04);
  background-color: rgba(255, 255, 255, 0.02);
}

.heading-left { display: flex; align-items: flex-start; gap: 12px; }
.heading-icon { width: 22px; height: 22px; color: var(--text-muted); padding-top: 2px; }
.heading-text h3 { margin: 0 0 4px 0; font-size: 15px; font-weight: 500; color: var(--text-main); }
.heading-text p { margin: 0; font-size: 13px; color: var(--text-muted); }
.heading-right { display: flex; align-items: center; }
.heading-right-wide { min-width: 220px; justify-content: flex-end; }

.card-body { padding: 24px 32px 32px; flex: 1; }

.inner-subcard {
  background: var(--bg-subcard); border: 1px solid var(--border-card);
  border-radius: var(--radius-md); padding: 24px;
}
.config-section-stack {
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.config-section {
  transition: border-color 0.2s ease, background-color 0.2s ease, opacity 0.2s ease;
}
.config-section-inactive {
  opacity: 0.72;
  border-color: rgba(255, 255, 255, 0.05);
}
.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 20px;
}
.section-header h4 {
  margin: 0 0 6px 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-main);
}
.section-header p {
  margin: 0;
  font-size: 12px;
  line-height: 1.5;
  color: var(--text-muted);
}
.cluster-card { padding: 20px; }
.cluster-header { display: flex; justify-content: space-between; padding-bottom: 12px; margin-bottom: 16px; border-bottom: 1px solid var(--border-muted); }
.cluster-title { font-weight: 500; color: var(--zinc-300); }

/* Forms & Grids (Breathing Space Added) */
.form-grid { display: flex; gap: 20px; margin-bottom: 20px; }
.form-grid > .field { flex: 1; min-width: 0; }
.form-grid-bottom { margin-bottom: 0; align-items: flex-end; }
.field { display: flex; flex-direction: column; gap: 8px; }
.compact-field { min-width: 220px; }
.field.opacity-muted { opacity: 0.5; transition: opacity 0.2s; }
.field label { font-size: 13px; font-weight: 500; color: var(--zinc-300); }
.runtime-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}
.runtime-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px;
  border-radius: var(--radius-md);
  border: 1px solid rgba(255, 255, 255, 0.06);
  background: rgba(255, 255, 255, 0.03);
}
.runtime-card-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-main);
}
.runtime-card-url {
  font-size: 13px;
  color: #93c5fd;
  word-break: break-all;
}
.runtime-card-meta {
  font-size: 12px;
  line-height: 1.5;
  color: var(--text-muted);
}

.settings-select {
  width: 100%;
}

.settings-select :deep(.glass-select-container) {
  width: 100%;
  display: block;
}

.settings-select :deep(.select-trigger) {
  width: 100%;
  height: 36px;
  padding: 0 12px;
  font-size: 13px;
  box-sizing: border-box;
  background-color: rgba(0, 0, 0, 0.45);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: var(--radius-sm);
  color: var(--text-main);
  box-shadow: inset 0 2px 4px rgba(0,0,0,0.5), 0 1px 0 rgba(255,255,255,0.03);
}

.settings-select :deep(.select-trigger:hover:not(.disabled)) {
  background-color: rgba(0, 0, 0, 0.55);
  border-color: rgba(255, 255, 255, 0.1);
}

.settings-select :deep(.select-trigger.active) {
  background-color: rgba(0, 0, 0, 0.6);
  border-color: rgba(59, 130, 246, 0.5);
  box-shadow: 0 0 0 1px rgba(59, 130, 246, 0.5), inset 0 2px 4px rgba(0,0,0,0.6);
}

.settings-select :deep(.select-trigger.disabled) {
  opacity: 0.5;
  cursor: not-allowed;
}

.settings-select :deep(.selected-text) {
  color: var(--text-main);
}

.settings-select :deep(.selected-text.placeholder),
.settings-select :deep(.chevron) {
  color: var(--text-muted);
}

.settings-select :deep(.select-dropdown) {
  top: calc(100% + 6px);
  box-sizing: border-box;
  width: 100%;
  min-width: 100%;
  max-width: none;
  background: var(--zinc-900);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: var(--radius-sm);
  padding: 6px;
  box-shadow: 0 16px 32px rgba(0, 0, 0, 0.45);
  z-index: 220;
}

.settings-select :deep(.select-option) {
  padding: 8px 12px;
  font-size: 13px;
  color: var(--text-muted);
}

.settings-select :deep(.select-option:hover) {
  background: rgba(255, 255, 255, 0.05);
  color: var(--text-main);
}

.settings-select :deep(.select-option.selected) {
  background: rgba(59, 130, 246, 0.18);
  color: #93c5fd;
}

input[type="text"], input[type="number"], input[type="password"], textarea {
  width: 100%; height: 36px; padding: 0 12px; font-size: 13px; box-sizing: border-box;
  background-color: rgba(0, 0, 0, 0.45); border: 1px solid rgba(255, 255, 255, 0.06); border-radius: var(--radius-sm);
  color: var(--text-main); font-family: inherit; outline: none; transition: 0.15s;
  box-shadow: inset 0 2px 4px rgba(0,0,0,0.5), 0 1px 0 rgba(255,255,255,0.03);
  color-scheme: dark;
}
textarea { height: auto; padding: 8px 12px; line-height: 1.5; resize: vertical; }
input:focus, textarea:focus { border-color: rgba(59, 130, 246, 0.5); box-shadow: 0 0 0 1px rgba(59, 130, 246, 0.5), inset 0 2px 4px rgba(0,0,0,0.6); background-color: rgba(0,0,0,0.6); }
input:disabled { opacity: 0.5; cursor: not-allowed; }
.mono-font { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
.static-note {
  min-height: 36px;
  display: flex;
  align-items: center;
  padding: 0 12px;
  border-radius: var(--radius-sm);
  border: 1px solid rgba(255, 255, 255, 0.06);
  background-color: rgba(255, 255, 255, 0.04);
  color: var(--text-muted);
  font-size: 12px;
  line-height: 1.5;
}

.mapping-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.mapping-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto;
  gap: 16px;
  align-items: end;
}

.mapping-remove {
  height: 36px;
}

.mapping-toolbar {
  display: flex;
  justify-content: flex-start;
}

.input-wrapper { position: relative; display: flex; align-items: center; width: 100%; }
.pr-icon { padding-right: 36px !important; }
.eye-btn {
  position: absolute; right: 4px; top: 50%; transform: translateY(-50%);
  background: transparent; border: none; padding: 4px; border-radius: var(--radius-xs);
  color: var(--zinc-500); cursor: pointer; display: flex; align-items: center; justify-content: center; outline: none;
}
.eye-btn svg { width: 16px; height: 16px; }
.eye-btn:hover { color: var(--text-main); background: var(--zinc-800); }

/* Custom Checkbox */
.field-checkbox { display: flex; align-items: center; height: 36px; }
.checkbox-label { display: flex; align-items: flex-start; gap: 12px; cursor: pointer; user-select: none; }
.checkbox-input { opacity: 0; position: absolute; }
.custom-checkbox { width: 16px; height: 16px; border-radius: 4px; border: 1px solid var(--border-card); background: var(--bg-input); display: inline-flex; justify-content: center; align-items: center; margin-top: 1px;}
.custom-checkbox::after { content: ""; width: 4px; height: 8px; border: solid black; border-width: 0 2px 2px 0; transform: rotate(45deg); opacity: 0; margin-bottom: 2px;}
.checkbox-input:checked + .custom-checkbox { background: var(--zinc-50); border-color: var(--zinc-50); }
.checkbox-input:checked + .custom-checkbox::after { opacity: 1; }
.checkbox-texts { display: flex; flex-direction: column; }
.checkbox-texts .primary { font-size: 13px; color: var(--text-main); font-weight: 500;}
.checkbox-texts .secondary { font-size: 12px; color: var(--zinc-500); }

/* Segment Control (Nested inside Header) */
.segment-group { display: inline-flex; background: rgba(0,0,0,0.5); border: 1px solid var(--border-card); border-radius: var(--radius-md); padding: 4px; gap: 4px; }
.segment { position: relative; padding: 6px 14px; cursor: pointer; border-radius: var(--radius-xs); font-size: 12px; font-weight: 500; color: var(--zinc-400); transition: 0.15s; border: 1px solid transparent;}
.segment input[type="radio"] { opacity: 0; position: absolute; }
.segment:hover { color: var(--text-main); }
.segment-active { background: var(--zinc-800); color: var(--text-main); border-color: var(--border-muted); }

/* Section Dividers */
.divider { display: flex; align-items: center; text-align: center; }
.divider::before, .divider::after { content: ''; flex: 1; border-bottom: 1px solid var(--border-card); }
.divider span { padding: 0 16px; color: var(--zinc-500); font-size: 12px; font-weight: 500; letter-spacing: 0.05em; text-transform: uppercase; }

/* === Editor Toolbar / Visual Upgrade === */
.code-editor-block { border: 1px solid rgba(255,255,255,0.08); border-radius: var(--radius-md); overflow: hidden; background: rgba(0, 0, 0, 0.25); }
.editor-summary { padding: 12px 16px; background: rgba(255, 255, 255, 0.03); font-size: 13px; font-weight: 500; cursor: pointer; list-style: none; user-select: none; color: var(--zinc-300); transition: 0.15s; }
.editor-summary:hover { color: var(--text-main); background: rgba(255, 255, 255, 0.08); }
.editor-summary::-webkit-details-marker { display: none; }
.editor-summary::before { content: '›'; display: inline-block; margin-right: 8px; font-family: monospace; transition: transform 0.2s; }
.code-editor-block[open] .editor-summary::before { transform: rotate(90deg); }
.code-editor-block[open] .editor-summary { border-bottom: 1px solid rgba(255,255,255,0.08); }

.editor-toolbar { display: flex; justify-content: space-between; align-items: center; padding: 8px 12px; background: rgba(0, 0, 0, 0.4); border-bottom: 1px solid rgba(255,255,255,0.05); }
.toolbar-left { display: flex; align-items: center; gap: 8px; color: rgba(255,255,255,0.5); }
.toolbar-left .file-icon { width: 14px; height: 14px; }
.toolbar-left .file-name { font-size: 12px; font-family: ui-monospace, SFMono-Regular, monospace; }
.toolbar-right .btn-tool {
  background: transparent; border: 1px solid rgba(255,255,255,0.1); padding: 4px 10px; border-radius: var(--radius-xs);
  font-size: 12px; color: rgba(255,255,255,0.6); cursor: pointer; display: flex; align-items: center; gap: 6px; transition: 0.15s;
}
.toolbar-right .btn-tool svg { width: 12px; height: 12px; }
.toolbar-right .btn-tool:hover { background: rgba(255,255,255,0.1); color: var(--text-main); }

.editor-textarea {
  width: 100%; border: none; border-radius: 0; background: transparent; padding: 16px;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: 12px; line-height: 1.5; color: var(--zinc-300);
}
.editor-textarea:focus { box-shadow: none; border: none; outline: none; background: rgba(0,0,0,0.5); }
.code-error { padding: 8px 16px; font-size: 12px; color: #ef4444; background: rgba(239, 68, 68, 0.1); border-top: 1px solid rgba(239, 68, 68, 0.2); }

/* 编辑模式切换 Tabs */
.editor-mode-tabs {
  display: inline-flex; gap: 4px; padding: 4px; margin-bottom: 20px;
  background: var(--bg-subcard); border: 1px solid var(--border-card); border-radius: var(--radius-md);
}
.mode-tab {
  appearance: none; border: none; cursor: pointer; font-family: inherit;
  padding: 7px 18px; border-radius: var(--radius-sm); font-size: 13px; font-weight: 500;
  background: transparent; color: var(--text-muted); transition: 0.15s;
}
.mode-tab:hover { color: var(--text-main); }
.mode-tab.active { background: var(--zinc-50); color: #000; }

/* 源文件编辑器 */
.source-editor-wrap .editor-textarea.source-textarea {
  min-height: 320px; background: rgba(0,0,0,0.35); border-radius: var(--radius-sm);
  /* 高度由 JS 自适应内容,关闭内部纵向滚动,只保留外层页面滚动条 */
  resize: none; overflow-y: hidden; overflow-x: auto; white-space: pre; tab-size: 2;
}
.source-textarea:focus { background: rgba(0,0,0,0.5); }

/* 保存确认（Diff）弹窗 */
.diff-modal-overlay {
  position: fixed; inset: 0; z-index: 1000;
  background: rgba(0,0,0,0.55); backdrop-filter: blur(2px);
  display: flex; align-items: center; justify-content: center; padding: 24px;
}
.diff-modal {
  width: min(720px, 100%); max-height: 80vh; display: flex; flex-direction: column;
  background: var(--zinc-900); border: 1px solid var(--border-card); border-radius: var(--radius-lg);
  box-shadow: 0 24px 64px rgba(0,0,0,0.5); overflow: hidden;
}
.diff-modal-header { padding: 20px 24px 12px; border-bottom: 1px solid var(--border-card); }
.diff-modal-header h3 { margin: 0 0 4px; font-size: 1.05rem; color: var(--zinc-50); }
.diff-modal-header p { margin: 0; font-size: 0.85rem; color: var(--text-muted); }
.diff-modal-body { padding: 16px 24px; overflow-y: auto; }
.diff-section { margin-bottom: 16px; }
.diff-section-title { font-size: 13px; font-weight: 600; color: var(--zinc-200); margin-bottom: 6px; }
.diff-badge {
  display: inline-block; margin-left: 6px; padding: 1px 8px; font-size: 11px; font-weight: 500;
  color: var(--accent-blue, #60a5fa); background: rgba(59,130,246,0.15);
  border: 1px solid rgba(59,130,246,0.4); border-radius: 999px;
}
.diff-pre {
  margin: 0; padding: 12px 14px; background: rgba(0,0,0,0.4); border: 1px solid var(--border-muted);
  border-radius: var(--radius-sm); font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 12px; line-height: 1.5; color: var(--zinc-300); white-space: pre-wrap; word-break: break-word;
  max-height: 280px; overflow: auto;
}
.diff-empty { font-size: 13px; color: var(--text-muted); padding: 8px 0; }
.diff-modal-footer {
  display: flex; justify-content: flex-end; gap: 12px;
  padding: 14px 24px; border-top: 1px solid var(--border-card);
}

/* Spacing Helpers */
.mt-sm { margin-top: 12px; }
.mt-md { margin-top: 20px; }
.mt-lg { margin-top: 24px; }
.mt-xl { margin-top: 32px; }
.mb-md { margin-bottom: 20px; }
.mb-lg { margin-bottom: 24px; }

/* 重启确认弹窗 */
.dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2000;
}

.dialog-card {
  background: #18181b;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 12px;
  width: 380px;
  box-shadow:
    0 8px 32px rgba(0, 0, 0, 0.4),
    0 0 0 1px rgba(0, 0, 0, 0.2),
    0 0 0 1px rgba(255, 255, 255, 0.05) inset;
  display: flex;
  flex-direction: column;
}

.dialog-header {
  padding: 16px 20px 12px;
  display: flex;
  align-items: center;
  gap: 10px;
}

.dialog-header .header-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 8px;
  flex-shrink: 0;
}

.dialog-header .header-icon.restart {
  background: rgba(234, 179, 8, 0.15);
  color: #fde047;
}

.dialog-header h3 {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
  flex: 1;
}

.dialog-header .close-btn {
  background: none;
  border: none;
  color: var(--text-secondary);
  cursor: pointer;
  padding: 6px;
  border-radius: 50%;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
}

.dialog-header .close-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--text-primary);
}

.dialog-body {
  padding: 0 20px 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.dialog-body .message {
  margin: 0;
  font-size: 0.9rem;
  color: var(--text-secondary);
  line-height: 1.5;
}

.restart-groups {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.restart-group-tag {
  display: inline-flex;
  height: 22px;
  padding: 0 8px;
  border-radius: var(--radius-xs, 4px);
  font-size: 12px;
  font-weight: 500;
  align-items: center;
  background: rgba(234, 179, 8, 0.08);
  color: #fde047;
  border: 1px solid rgba(234, 179, 8, 0.15);
}

.dialog-footer {
  padding: 16px 20px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  background: rgba(0, 0, 0, 0.1);
  border-radius: 0 0 12px 12px;
}

.dialog-footer :deep(button) {
  min-width: 88px;
  height: 32px;
  border-radius: 6px;
  justify-content: center;
  font-size: 0.85rem;
  padding: 0 12px;
  box-shadow: 0 0 0 1px rgba(255, 255, 255, 0.05) inset;
}

/* 弹窗动画 */
.dialog-enter-active,
.dialog-leave-active {
  transition: opacity 0.3s ease;
}

.dialog-enter-active .dialog-card,
.dialog-leave-active .dialog-card {
  transition: transform 0.3s cubic-bezier(0.16, 1, 0.3, 1);
}

.dialog-enter-from,
.dialog-leave-to {
  opacity: 0;
}

.dialog-enter-from .dialog-card,
.dialog-leave-to .dialog-card {
  transform: scale(0.9) translateY(20px);
}

@media (max-width: 840px) {
  .card-header {
    flex-direction: column;
    gap: 16px;
  }

  .heading-right,
  .heading-right-wide,
  .compact-field {
    width: 100%;
  }

  .runtime-grid,
  .mapping-row {
    grid-template-columns: 1fr;
  }

  .form-grid {
    flex-direction: column;
  }
}
</style>
