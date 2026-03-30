<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { SettingsService } from '../../services/SettingsService'
import type {
  SettingsGroup,
  SettingsGroupKey,
  SettingsRuntime,
  SettingsSnapshot
} from '../../types/settings'

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

const groupKeys: SettingsGroupKey[] = ['server', 'web', 'agent', 'ccr']
const defaultRuntime: SettingsRuntime = {
  mode: 'direct',
  effectiveDefaultModelPath: 'agent.model',
  effectiveDefaultModelValue: '',
  dockerManagedRestart: false,
  restartBehavior: 'manual',
  restartHint: '当前环境未检测到 Docker 自动重启，点击重启后需要手动重新启动服务。'
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

const runtime = ref<SettingsRuntime>({ ...defaultRuntime })
const isLoading = ref(true)
const isSaving = ref(false)
const isRestarting = ref(false)
const showSecrets = ref(false)
const saveMessage = ref<string | null>(null)
const saveError = ref<string | null>(null)
const loadError = ref<string | null>(null)
const restartPendingGroups = ref<string[]>([])

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

for (const key of groupKeys) {
  watch(() => drafts[key].values, value => {
    if (!drafts[key].jsonError) {
      drafts[key].jsonText = formatJson(value)
    }
  }, { deep: true })
}

const isCcrMode = computed(() => Boolean(drafts.server.values.ccr?.enabled))
const pageTitle = '系统全局实例设置'
const displayEffectiveModelPath = computed(() => isCcrMode.value
  ? 'server.ccr.defaultModelFamily'
  : 'agent.model')
const effectiveModelDescription = computed(() => isCcrMode.value
  ? '当前生效底层参数源: server > ccr.defaultModelFamily'
  : '当前生效底层参数源: agent > model')

const modelOptions = computed(() => {
  const modelMapping = drafts.agent.values.modelMapping ?? {}
  const mapped = Object.entries(modelMapping)
    .map(([family, entry]) => {
      const record = entry as Record<string, unknown> | null
      return {
        value: family,
        label: record?.label ? String(record.label) : capitalize(family),
        helper: record?.id ? String(record.id) : ''
      }
    })

  if (mapped.length > 0) {
    return mapped
  }

  const customModels = Array.isArray(drafts.web.values.customModels)
    ? drafts.web.values.customModels
    : []

  return customModels.map((item: any) => ({
    value: item.id,
    label: item.label || item.id,
    helper: ''
  }))
})

const effectiveDefaultModel = computed({
  get: () => isCcrMode.value
    ? drafts.server.values.ccr?.defaultModelFamily ?? ''
    : drafts.agent.values.model ?? '',
  set: (value: string) => {
    if (isCcrMode.value) {
      drafts.server.values.ccr.defaultModelFamily = value
      runtime.value.effectiveDefaultModelPath = 'server.ccr.defaultModelFamily'
    } else {
      drafts.agent.values.model = value
      runtime.value.effectiveDefaultModelPath = 'agent.model'
    }
    runtime.value.effectiveDefaultModelValue = value
  }
})

const primaryProvider = computed(() => {
  const providers = Array.isArray(drafts.ccr.values.Providers) ? drafts.ccr.values.Providers : []
  return providers[0] ?? null
})

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value))
}

function formatJson(value: unknown) {
  return JSON.stringify(value, null, 2)
}

function formatJsonString(group: SettingsGroupKey) {
  try {
    const parsed = JSON.parse(drafts[group].jsonText);
    drafts[group].jsonText = JSON.stringify(parsed, null, 2);
    parseJson(group);
  } catch(e) { /* ignore invalid JSON formats, user must fix manually */ }
}

function capitalize(value: string) {
  return value.charAt(0).toUpperCase() + value.slice(1)
}

function normalize(group: SettingsGroupKey, raw: Record<string, any>) {
  const value = clone(raw ?? {})

  if (group === 'server') {
    value.server ??= {}
    value.startup ??= {}
    value.ccr ??= {}
    value.server.port ??= 8865
    value.server.pythonCommand ??= 'python'
    value.startup.openBrowser ??= false
    value.startup.browserPath ??= ''
    value.ccr.enabled ??= false
    value.ccr.autoStart ??= true
    value.ccr.host ??= '127.0.0.1'
    value.ccr.port ??= 3456
    value.ccr.defaultModelFamily ??= 'sonnet'
  }

  if (group === 'web') {
    value.customModels = Array.isArray(value.customModels) ? value.customModels : []
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
    value.baseUrl ??= ''
    value.apiKey ??= ''
    value.model ??= 'sonnet'
    value.defaultEffort ??= 'medium'
    value.defaultThinking ??= 'adaptive'
    value.maxThinkingTokens ??= 8000
    value.modelMapping ??= {}
    value.permissions ??= { allow: [], deny: [] }
    value.server ??= {}
    value.server.host ??= '127.0.0.1'
    value.server.port ??= 8865
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

function applyGroup(key: SettingsGroupKey, group: SettingsGroup) {
  drafts[key].title = group.title
  drafts[key].sourceFile = group.sourceFile
  drafts[key].values = normalize(key, group.values)
  drafts[key].jsonError = null
  drafts[key].jsonText = formatJson(drafts[key].values)
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
}

async function loadSettings() {
  isLoading.value = true
  loadError.value = null
  saveError.value = null

  try {
    applySnapshot(await SettingsService.getSettings())
    restartPendingGroups.value = []
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
    return true
  } catch (error: any) {
    drafts[group].jsonError = error.message || 'JSON 解析失败'
    return false
  }
}

function modelMappingValue(family: string) {
  return drafts.agent.values.modelMapping?.[family]?.id ?? ''
}

function setModelMappingValue(family: string, value: string) {
  drafts.agent.values.modelMapping ??= {}
  drafts.agent.values.modelMapping[family] ??= { id: '', label: capitalize(family) }
  drafts.agent.values.modelMapping[family].id = value
  drafts.agent.values.modelMapping[family].label ||= capitalize(family)
}

function modelLines() {
  const customModels = Array.isArray(drafts.web.values.customModels)
    ? drafts.web.values.customModels
    : []
  return customModels.map((item: { id?: string }) => item.id ?? '').join('\n')
}

function textToLines(text: string) {
  return text.split(/\r?\n/).map(item => item.trim()).filter(Boolean)
}

function handleModelLinesInput(event: Event) {
  drafts.web.values.customModels = textToLines((event.target as HTMLTextAreaElement).value)
    .map(id => ({ id, label: capitalize(id) }))
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

function maskSecret(value: string) {
  if (!value) return ''
  if (value.length <= 8) return '********'
  return `${value.slice(0, 3)}••••${value.slice(-3)}`
}

async function handleSave() {
  saveError.value = null
  saveMessage.value = null

  for (const key of groupKeys) {
    if (!parseJson(key)) {
      saveError.value = `${drafts[key].title} 格式有误`
      return
    }
  }

  isSaving.value = true
  try {
    const result = await SettingsService.saveSettings({
      server: drafts.server.values,
      web: drafts.web.values,
      agent: drafts.agent.values,
      ccr: drafts.ccr.values
    })

    applySnapshot(result.settings)
    restartPendingGroups.value = result.restartRequiredGroups
    saveMessage.value = result.restartRequiredGroups.length > 0
      ? `保存成功: ${result.restartRequiredGroups.join(', ')} 需重启生效。`
      : '保存成功。'

    window.dispatchEvent(new CustomEvent('bimcanvas:web-config-updated', {
      detail: clone(result.settings.web.values)
    }))
  } catch (error: any) {
    saveError.value = error.response?.data?.message || error.message || '保存配置失败'
  } finally {
    isSaving.value = false
  }
}

async function handleRestart() {
  isRestarting.value = true
  saveError.value = null

  try {
    const result = await SettingsService.restartInstance()
    saveMessage.value = result.message
  } catch (error: any) {
    saveError.value = error.response?.data?.message || error.message || '触发重启失败'
  } finally {
    isRestarting.value = false
  }
}

onMounted(() => {
  loadSettings()
})
</script>

<template>
  <div class="settings-page">
    <header class="settings-header">
      <div class="header-content layout-bound">
        <div class="header-title">
          <h2 class="title">{{ pageTitle }}</h2>
          <span class="badge" :class="restartPendingGroups.length > 0 ? 'badge-warning' : 'badge-success'">
            <span class="dot"></span>
            {{ restartPendingGroups.length > 0 ? `待重启 (${restartPendingGroups.length})` : '已同步' }}
          </span>
        </div>
        
        <div class="header-actions">
          <button class="btn btn-ghost" type="button" :disabled="isLoading" @click="loadSettings">取消并重置</button>
          <button v-if="restartPendingGroups.length > 0" class="btn btn-danger" type="button" :disabled="isRestarting" @click="handleRestart">
            {{ isRestarting ? '重启中...' : '重启服务生效' }}
          </button>
          <button class="btn btn-accent" type="button" :disabled="isSaving || isLoading" @click="handleSave">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="width: 14px; height: 14px; margin-right: 6px; margin-top: -1px; vertical-align: middle;"><polyline points="20 6 9 17 4 12"></polyline></svg>
            {{ isSaving ? '保存中...' : '提交更改' }}
          </button>
        </div>
      </div>
    </header>

    <div class="settings-main">
      <div class="layout-bound wrapper-pad">
        <div v-if="loadError || saveError || saveMessage || restartPendingGroups.length > 0" class="alerts mb-md">
          <div v-if="loadError" class="alert alert-error">{{ loadError }}</div>
          <div v-if="saveError" class="alert alert-error">{{ saveError }}</div>
          <div v-if="saveMessage" class="alert alert-success">{{ saveMessage }}</div>
          <div v-if="restartPendingGroups.length > 0" class="alert alert-warning">
            您修改了内核级参数（{{ restartPendingGroups.join(', ') }}），该修改已落盘，需重启服务句柄才能载入。
            ({{ runtime.restartBehavior === 'docker-auto' ? '由 Docker 管理自动重启' : '目前配置为手动维护重启' }})
          </div>
        </div>

        <div v-if="isLoading" class="loading-state">载入系统级配置实例...</div>

        <template v-else>
          
          <!-- Card 1: 运行架构 -->
          <article class="config-card">
            <header class="card-header">
              <div class="heading-left">
                <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8m-4-4v4"/></svg>
                <div class="heading-text">
                  <h3>运行架构与通道拓扑 (Architecture)</h3>
                  <p>在此直接决断所有 Agent 模型请求的出站路由隧道方案。</p>
                </div>
              </div>
              <div class="heading-right">
                <div class="segment-group">
                  <label class="segment" :class="{ 'segment-active': !drafts.server.values.ccr.enabled }">
                    <input type="radio" :value="false" v-model="drafts.server.values.ccr.enabled"> 直连通道 (Direct)
                  </label>
                  <label class="segment" :class="{ 'segment-active': drafts.server.values.ccr.enabled }">
                    <input type="radio" :value="true" v-model="drafts.server.values.ccr.enabled"> CCR 路由网关 (Gateway)
                  </label>
                </div>
              </div>
            </header>
            
            <div class="card-body">
              <div class="inline-alert warm mb-lg">
                <svg class="alert-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                <span>{{ drafts.server.values.ccr.enabled ? '所有出站基座 API 请求将被劫持并压入私有中央网关，享有集群负载转移与流量重放（Traffic Trace）能力' : '以最纯粹的形态进行请求出站，Agent 完全独立持有各家模型渠道的顶级访问 Key，无任何中间件阻塞风险（建议开发测试环境选用）。' }}</span>
              </div>

              <div class="form-grid">
                <div class="field">
                  <label>Agent 侦听端口 (Daemon Port)</label>
                  <input v-model.number="drafts.server.values.server.port" type="number">
                </div>
                <div class="field">
                  <label>Python 执行沙箱路径 / 指令 (Execution Command)</label>
                  <input v-model="drafts.server.values.server.pythonCommand" type="text" placeholder="python 或 python3">
                </div>
              </div>
              
              <div class="form-grid mt-md">
                <div class="field" :class="{ 'opacity-muted': !isCcrMode }">
                  <label>CCR 网关 Host 控制面定位点</label>
                  <input v-model="drafts.server.values.ccr.host" type="text" :disabled="!isCcrMode">
                </div>
                <div class="field" :class="{ 'opacity-muted': !isCcrMode }">
                  <label>CCR 网关 Port 控制面隧道</label>
                  <input v-model.number="drafts.server.values.ccr.port" type="number" :disabled="!isCcrMode">
                </div>
              </div>
            </div>
          </article>

          <!-- Card 2: 推理调度群 -->
          <article class="config-card">
            <header class="card-header">
              <div class="heading-left">
                <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><polyline points="3.27 6.96 12 12.01 20.73 6.96"/><line x1="12" y1="22.08" x2="12" y2="12"/></svg>
                <div class="heading-text">
                  <h3>推理调度集群选项 (Inference Pipeline)</h3>
                  <p>分配默认基座模型代系，并为特殊任务（思考或海量 Token）绑定覆盖簇。</p>
                </div>
              </div>
              <div class="heading-right">
                <span class="badge badge-mono subtle-badge">{{ displayEffectiveModelPath }}</span>
              </div>
            </header>
            
            <div class="card-body">
              <div class="form-grid">
                <div class="field">
                  <label>预设环境默认基座 (Base Pipeline Default)</label>
                  <select v-model="effectiveDefaultModel">
                    <option v-for="option in modelOptions" :key="option.value" :value="option.value">
                      {{ option.label }}
                    </option>
                  </select>
                </div>
                <div class="field">
                  <label>底层调度深度边界 (Global Effort Cap)</label>
                  <select v-model="drafts.agent.values.defaultEffort" :disabled="isCcrMode && !drafts.ccr.values.Router?.default">
                    <option v-for="option in effortOptions" :key="option.value" :value="option.value">
                      {{ option.label }}
                    </option>
                  </select>
                </div>
              </div>
              
              <div class="form-grid mt-md">
                <div class="field">
                  <label>链式思维强化覆盖策略 (Extended Thinking Override)</label>
                  <select v-model="drafts.agent.values.defaultThinking">
                    <option v-for="option in thinkingOptions" :key="option.value" :value="option.value">
                      {{ option.label }}
                    </option>
                  </select>
                </div>
                <div class="field">
                  <label>思考周期上限控制阀 (Thinking Tokens Maximum Limit)</label>
                  <input v-model.number="drafts.agent.values.maxThinkingTokens" type="number">
                </div>
              </div>

              <div class="divider mt-xl mb-md">
                <span>基座代号静态链接绑定库 (Family Static Mappings)</span>
              </div>
              <div class="form-grid">
                <div class="field">
                  <label>Opus 层级 (全能且厚重)</label>
                  <input :value="modelMappingValue('opus')" type="text" placeholder="claude-opus-4" @input="setModelMappingValue('opus', ($event.target as HTMLInputElement).value)">
                </div>
                <div class="field">
                  <label>Sonnet 层级 (万金油)</label>
                  <input :value="modelMappingValue('sonnet')" type="text" placeholder="claude-sonnet-3-5" @input="setModelMappingValue('sonnet', ($event.target as HTMLInputElement).value)">
                </div>
                <div class="field">
                  <label>Haiku 层级 (敏捷执行)</label>
                  <input :value="modelMappingValue('haiku')" type="text" placeholder="claude-haiku-4" @input="setModelMappingValue('haiku', ($event.target as HTMLInputElement).value)">
                </div>
              </div>
              
              <!-- Editor block -->
              <details class="code-editor-block mt-xl">
                <summary class="editor-summary">
                  配置源代码溯源 (Source Editor)
                </summary>
                <div class="editor-container">
                  <div class="editor-toolbar">
                    <div class="toolbar-left">
                      <svg class="file-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/></svg>
                      <span class="file-name">agent_config.json</span>
                    </div>
                    <div class="toolbar-right">
                      <button class="btn-tool" @click="formatJsonString('agent')" type="button">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>
                        Format
                      </button>
                    </div>
                  </div>
                  <textarea class="editor-textarea" v-model="drafts.agent.jsonText" rows="6" spellcheck="false" @blur="parseJson('agent')" />
                </div>
                <div v-if="drafts.agent.jsonError" class="code-error">{{ drafts.agent.jsonError }}</div>
              </details>
            </div>
          </article>

          <!-- Card 3: 授权信道 -->
          <article class="config-card">
            <header class="card-header">
              <div class="heading-left">
                <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                <div class="heading-text">
                  <h3>出网身份凭证及集群配给 (Authorization & Clusters)</h3>
                  <p>用于在跨边界握手时验证鉴身标识 (API-Keys)。</p>
                </div>
              </div>
            </header>
            
            <div class="card-body">
              <div v-if="!isCcrMode" class="inner-subcard">
                <div class="form-grid">
                  <div class="field">
                    <label>公网 Base HTTP URL</label>
                    <input v-model="drafts.agent.values.baseUrl" type="text" placeholder="https://api.anthropic.com">
                  </div>
                  <div class="field">
                    <label>核心权限出站秘钥 (Top API Key)</label>
                    <div class="input-wrapper">
                      <input v-model="drafts.agent.values.apiKey" :type="showSecrets ? 'text' : 'password'" placeholder="空" class="pr-icon">
                      <button type="button" class="eye-btn" @click="showSecrets = !showSecrets" title="切换密码可视化">
                        <svg v-if="!showSecrets" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path><circle cx="12" cy="12" r="3"></circle></svg>
                        <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24M1 1l22 22"></path></svg>
                      </button>
                    </div>
                  </div>
                </div>
              </div>

              <div v-else class="ccr-branch">
                <div class="form-grid mb-md">
                  <div class="field">
                    <label>自身暴露层 Host</label>
                    <input v-model="drafts.ccr.values.HOST" type="text">
                  </div>
                  <div class="field">
                    <label>自身暴露层 Port</label>
                    <input v-model.number="drafts.ccr.values.PORT" type="number">
                  </div>
                </div>

                <div class="divider mt-xl mb-md"><span>出海集群池列表 (CCR Clusters Pool)</span></div>
                <div class="cluster-pool">
                  <div v-for="(provider, index) in drafts.ccr.values.Providers" :key="index" class="inner-subcard cluster-card">
                    <div class="cluster-header">
                      <span class="cluster-title">{{ provider.name || `Provider Nodes [${index}]` }}</span>
                      <span class="badge badge-normal badge-mono">{{ provider.models?.length || 0 }} Models Listed</span>
                    </div>
                    <div class="form-grid mt-sm">
                      <div class="field">
                        <label>网关下游分发地址 (Proxy Gateway URL)</label>
                        <input v-model="provider.api_base_url" type="text">
                      </div>
                      <div class="field">
                        <label>分支隧道握手秘钥 (Provider API Key)</label>
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
                      <label>允许下沉分派的模型清单 (用半角逗号间隔白名单策略)</label>
                      <input :value="providerModels(provider)" type="text" class="mono-font" @input="updateProviderModels(provider, $event)">
                    </div>
                  </div>
                </div>

                <div class="inline-alert warm mt-md mb-lg">
                  <svg class="alert-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
                  <span>首选集群主导器 (Primary Resolver): <strong class="text-white">{{ primaryProvider?.name || 'Null' }}</strong> | 无模型匹配时强制转移至: <strong class="text-white">{{ drafts.ccr.values.Router.default || 'Null' }}</strong></span>
                </div>

                <div class="divider mt-xl mb-md"><span>异常/特权拦截网守 (Router Sub-Guards)</span></div>
                <div class="form-grid">
                  <div class="field">
                    <label>Default Failover (脱靶备选)</label>
                    <input v-model="drafts.ccr.values.Router.default" type="text" class="mono-font">
                  </div>
                  <div class="field">
                    <label>Thinking Block (烧卡拦截)</label>
                    <input v-model="drafts.ccr.values.Router.think" type="text" class="mono-font">
                  </div>
                </div>
                <div class="form-grid mt-md">
                  <div class="field">
                    <label>Background Sync (低优先级驻留队列)</label>
                    <input v-model="drafts.ccr.values.Router.background" type="text" class="mono-font">
                  </div>
                  <div class="field">
                    <label>Long Context Branch (长线文脉高额路由)</label>
                    <input v-model="drafts.ccr.values.Router.longContext" type="text" class="mono-font">
                  </div>
                </div>
                <div class="form-grid form-grid-bottom mt-md">
                  <div class="field">
                    <label>网关级 API 发送超限切断阀值 (Timeout ms)</label>
                    <input v-model.number="drafts.ccr.values.API_TIMEOUT_MS" type="number">
                  </div>
                  <div class="field field-checkbox">
                    <label class="checkbox-label">
                      <input type="checkbox" v-model="drafts.ccr.values.LOG" class="checkbox-input">
                      <span class="custom-checkbox"></span>
                      <div class="checkbox-texts">
                        <span class="primary">网关全埋点级深度日志脱壳记录 (LOG)</span>
                        <span class="secondary">于服务端底层终端控制台打出入站流向与报文</span>
                      </div>
                    </label>
                  </div>
                </div>
              </div>
              
              <div class="json-group mt-xl">
                <details class="code-editor-block">
                  <summary class="editor-summary">配置源代码溯源 (Source Editor) - server_config</summary>
                  <div class="editor-container">
                    <div class="editor-toolbar">
                      <div class="toolbar-left"><svg class="file-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg><span class="file-name">server_bridge_config.json</span></div>
                      <div class="toolbar-right"><button class="btn-tool" @click="formatJsonString('server')" type="button"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>Format</button></div>
                    </div>
                    <textarea class="editor-textarea" v-model="drafts.server.jsonText" rows="6" spellcheck="false" @blur="parseJson('server')" />
                  </div>
                  <div v-if="drafts.server.jsonError" class="code-error">{{ drafts.server.jsonError }}</div>
                </details>

                <details class="code-editor-block mt-md" v-if="isCcrMode">
                  <summary class="editor-summary">配置源代码溯源 (Source Editor) - ccr_config</summary>
                  <div class="editor-container">
                    <div class="editor-toolbar">
                      <div class="toolbar-left"><svg class="file-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg><span class="file-name">ccr_gateway_config.json</span></div>
                      <div class="toolbar-right"><button class="btn-tool" @click="formatJsonString('ccr')" type="button"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>Format</button></div>
                    </div>
                    <textarea class="editor-textarea" v-model="drafts.ccr.jsonText" rows="8" spellcheck="false" @blur="parseJson('ccr')" />
                  </div>
                  <div v-if="drafts.ccr.jsonError" class="code-error">{{ drafts.ccr.jsonError }}</div>
                </details>
              </div>
            </div>
          </article>

          <!-- Card 4: Web 展现预设 -->
          <article class="config-card">
            <header class="card-header">
              <div class="heading-left">
                <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
                <div class="heading-text">
                  <h3>全栈 Web 绘图界面呈现指令集 (Presentation UI)</h3>
                  <p>这些参数下发到前端组件的深层结构中（无需进程硬重启）。</p>
                </div>
              </div>
            </header>
            
            <div class="card-body">
              <div class="field mb-lg">
                <label>显式压制：强制枚举可用基座（每行截断作为一条）</label>
                <textarea :value="modelLines()" rows="3" class="mono-font" @input="handleModelLinesInput" placeholder="留下空白即使用自动检测..." />
              </div>

              <div class="divider mt-xl mb-md"><span>底层视图结构化渲染强制绑定簇 (Layer Rendering Enforcements)</span></div>
              <div class="form-grid">
                <div class="field">
                  <label>普通用户行为树可见性：锁定显示级 (User Space Sets)</label>
                  <textarea :value="drafts.web.values.layerPresets.User.enabledLayers.join('\n')" rows="5" class="mono-font" @input="handleLayerPresetInput(drafts.web.values.layerPresets.User.enabledLayers, $event)" />
                </div>
                <div class="field">
                  <label>人工智能规划干预树：权限赋予池 (Agent Overrides)</label>
                  <textarea :value="drafts.web.values.layerPresets.Agent.enabledLayers.join('\n')" rows="5" class="mono-font" @input="handleLayerPresetInput(drafts.web.values.layerPresets.Agent.enabledLayers, $event)" />
                </div>
              </div>

              <details class="code-editor-block mt-xl">
                <summary class="editor-summary">配置源代码溯源 (Source Editor) - web_config</summary>
                <div class="editor-container">
                  <div class="editor-toolbar">
                    <div class="toolbar-left"><svg class="file-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg><span class="file-name">web_presentation_config.json</span></div>
                    <div class="toolbar-right"><button class="btn-tool" @click="formatJsonString('web')" type="button"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>Format</button></div>
                  </div>
                  <textarea class="editor-textarea" v-model="drafts.web.jsonText" rows="6" spellcheck="false" @blur="parseJson('web')" />
                </div>
                <div v-if="drafts.web.jsonError" class="code-error">{{ drafts.web.jsonError }}</div>
              </details>
            </div>
          </article>

        </template>
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
}

hr { border: none; }
.layout-bound { max-width: 860px; margin: 0 auto; width: 100%; }

/* Header & Scaffolding */
.settings-header {
  position: sticky; top: 24px; z-index: 50;
  padding: 0 24px; margin-bottom: 32px;
}
.header-content { 
  display: flex; justify-content: space-between; align-items: center; 
  background-color: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(255,255,255,0.06);
  border-radius: var(--radius-lg, 12px);
  padding: 18px 32px;
  box-shadow: inset 0 1px 1px rgba(255, 255, 255, 0.08), 0 8px 32px rgba(0, 0, 0, 0.4);
}
.header-title { display: flex; align-items: center; gap: 12px; }
.header-title .title { margin: 0; font-size: 1.15rem; font-weight: 600; color: var(--text-main); letter-spacing: -0.01em; }

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
.badge-success { background: rgba(34, 197, 94, 0.08); color: #4ade80; border: 1px solid rgba(34, 197, 94, 0.15); }
.badge-warning { background: rgba(234, 179, 8, 0.08); color: #fde047; border: 1px solid rgba(234, 179, 8, 0.15); }
.subtle-badge { font-size: 11px; color: var(--zinc-500); background: transparent; border: 1px solid var(--border-muted); }
.dot { width: 6px; height: 6px; border-radius: 50%; background: currentColor; }

/* Inline Alerts & Blocks */
.alerts { display: flex; flex-direction: column; gap: 12px; }
.alert { padding: 12px 16px; border-radius: var(--radius-md); font-size: 13px; border: 1px solid transparent; line-height: 1.5; }
.alert-error { background: rgba(239, 68, 68, 0.1); border-color: rgba(239, 68, 68, 0.2); color: #ef4444; }
.alert-warning { background: rgba(234, 179, 8, 0.1); border-color: rgba(234, 179, 8, 0.2); color: #fde047; }
.alert-success { background: rgba(34, 197, 94, 0.1); border-color: rgba(34, 197, 94, 0.2); color: #4ade80; }

.inline-alert { display: flex; gap: 12px; padding: 12px 16px; border-radius: var(--radius-md); font-size: 13px; line-height: 1.5; }
.inline-alert svg { width: 18px; height: 18px; flex-shrink: 0; margin-top: 1px; }
.inline-alert.warm { background: rgba(234, 179, 8, 0.06); border: 1px solid rgba(234, 179, 8, 0.15); color: #fde047; }
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

.card-body { padding: 24px 32px 32px; flex: 1; }

.inner-subcard {
  background: var(--bg-subcard); border: 1px solid var(--border-card);
  border-radius: var(--radius-md); padding: 24px;
}
.cluster-card { padding: 20px; }
.cluster-header { display: flex; justify-content: space-between; padding-bottom: 12px; margin-bottom: 16px; border-bottom: 1px solid var(--border-muted); }
.cluster-title { font-weight: 500; color: var(--zinc-300); }

/* Forms & Grids (Breathing Space Added) */
.form-grid { display: flex; gap: 20px; margin-bottom: 20px; }
.form-grid > .field { flex: 1; min-width: 0; }
.form-grid-bottom { margin-bottom: 0; align-items: flex-end; }
.field { display: flex; flex-direction: column; gap: 8px; }
.field.opacity-muted { opacity: 0.5; transition: opacity 0.2s; }
.field label { font-size: 13px; font-weight: 500; color: var(--zinc-300); }

input[type="text"], input[type="number"], input[type="password"], select, textarea {
  width: 100%; height: 36px; padding: 0 12px; font-size: 13px; box-sizing: border-box;
  background-color: rgba(0, 0, 0, 0.45); border: 1px solid rgba(255, 255, 255, 0.06); border-radius: var(--radius-sm);
  color: var(--text-main); font-family: inherit; outline: none; transition: 0.15s;
  box-shadow: inset 0 2px 4px rgba(0,0,0,0.5), 0 1px 0 rgba(255,255,255,0.03);
}
textarea { height: auto; padding: 8px 12px; line-height: 1.5; resize: vertical; }
input:focus, select:focus, textarea:focus { border-color: rgba(59, 130, 246, 0.5); box-shadow: 0 0 0 1px rgba(59, 130, 246, 0.5), inset 0 2px 4px rgba(0,0,0,0.6); background-color: rgba(0,0,0,0.6); }
input:disabled, select:disabled { opacity: 0.5; cursor: not-allowed; }
.mono-font { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }

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

/* Spacing Helpers */
.mt-sm { margin-top: 12px; }
.mt-md { margin-top: 20px; }
.mt-lg { margin-top: 24px; }
.mt-xl { margin-top: 32px; }
.mb-md { margin-bottom: 20px; }
.mb-lg { margin-bottom: 24px; }
</style>
