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
const pageTitle = computed(() => isCcrMode.value ? 'CCR 实例配置' : '直连实例配置')
const modeLabel = computed(() => isCcrMode.value ? 'CCR Gate' : 'Direct')
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
          <span class="badge badge-normal badge-mono">{{ modeLabel }}</span>
          <span class="badge" :class="restartPendingGroups.length > 0 ? 'badge-warning' : 'badge-success'">
            {{ restartPendingGroups.length > 0 ? `待重启 (${restartPendingGroups.length})` : '已同步' }}
          </span>
        </div>
        
        <div class="header-actions">
          <button class="btn btn-ghost" type="button" :disabled="isLoading" @click="loadSettings">取消</button>
          <button v-if="restartPendingGroups.length > 0" class="btn btn-danger" type="button" :disabled="isRestarting" @click="handleRestart">
            {{ isRestarting ? '重启中...' : '重启生效' }}
          </button>
          <button class="btn btn-primary" type="button" :disabled="isSaving || isLoading" @click="handleSave">
            {{ isSaving ? '保存中...' : '保存更改' }}
          </button>
        </div>
      </div>
    </header>

    <div class="settings-main layout-bound">
      <div v-if="loadError || saveError || saveMessage || restartPendingGroups.length > 0" class="alerts">
        <div v-if="loadError" class="alert alert-error">{{ loadError }}</div>
        <div v-if="saveError" class="alert alert-error">{{ saveError }}</div>
        <div v-if="saveMessage" class="alert alert-success">{{ saveMessage }}</div>
        <div v-if="restartPendingGroups.length > 0" class="alert alert-warning">
          待更新配置层：{{ restartPendingGroups.join(', ') }}。配置已落盘，需重启服务挂载使其生效。
          ({{ runtime.restartBehavior === 'docker-auto' ? '由 Docker 管理' : '手动干预' }})
        </div>
      </div>

      <div v-if="isLoading" class="loading-state">载入系统级配置实例...</div>

      <template v-else>
        <!-- Section: Architecture -->
        <section class="config-section">
          <div class="section-heading">
            <h3>运行架构选项 (Architecture)</h3>
            <p>选择流量调度与底层服务暴露模式。</p>
          </div>
          
          <div class="segment-group">
            <label class="segment" :class="{ 'segment-active': !drafts.server.values.ccr.enabled }">
              <input type="radio" :value="false" v-model="drafts.server.values.ccr.enabled">
              本地直连调用 (Direct API)
            </label>
            <label class="segment" :class="{ 'segment-active': drafts.server.values.ccr.enabled }">
              <input type="radio" :value="true" v-model="drafts.server.values.ccr.enabled">
              CCR 路由网关管控 (Gateway Router)
            </label>
          </div>
          <p class="helper-text mt-sm">
            {{ drafts.server.values.ccr.enabled ? '将所有 Agent 流量上报至内部配置的中央并发网关层，适用于复杂编排和负载平衡。' : '剥离中间链路，由 Agent 持有顶级 Key，直接对接公网级 API 源，极度轻量且易于测试调试。' }}
          </p>

          <div class="form-grid top-margin">
            <div class="field">
              <label>Agent 侦听端口</label>
              <input v-model.number="drafts.server.values.server.port" type="number">
            </div>
            <div class="field">
              <label>Python 执行环境</label>
              <input v-model="drafts.server.values.server.pythonCommand" type="text" placeholder="python 或 python3">
            </div>
          </div>
          
          <div class="form-grid">
            <div class="field" :class="{ 'opacity-muted': !isCcrMode }">
              <label>CCR 网关 Host</label>
              <input v-model="drafts.server.values.ccr.host" type="text" :disabled="!isCcrMode">
            </div>
            <div class="field" :class="{ 'opacity-muted': !isCcrMode }">
              <label>CCR 网关 Port</label>
              <input v-model.number="drafts.server.values.ccr.port" type="number" :disabled="!isCcrMode">
            </div>
          </div>
        </section>

        <hr class="section-divider">

        <!-- Section: Models -->
        <section class="config-section">
          <div class="section-heading">
            <div class="heading-row">
              <h3>默认推理调度群 (Inference Models)</h3>
              <span class="badge badge-mono">{{ displayEffectiveModelPath }}</span>
            </div>
            <p>{{ effectiveModelDescription }}</p>
          </div>
          
          <div class="form-grid">
            <div class="field">
              <label>主线默认模型基座 (Default Baseline)</label>
              <select v-model="effectiveDefaultModel">
                <option v-for="option in modelOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </option>
              </select>
            </div>
            <div class="field">
              <label>任务深度边界 (Effort Cap)</label>
              <select v-model="drafts.agent.values.defaultEffort" :disabled="isCcrMode && !drafts.ccr.values.Router?.default">
                <option v-for="option in effortOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </option>
              </select>
            </div>
          </div>
          
          <div class="form-grid">
            <div class="field">
              <label>链式推演预判 (Extended Thinking)</label>
              <select v-model="drafts.agent.values.defaultThinking">
                <option v-for="option in thinkingOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </option>
              </select>
            </div>
            <div class="field">
              <label>单次调度 Thinking Tokens 配额</label>
              <input v-model.number="drafts.agent.values.maxThinkingTokens" type="number">
            </div>
          </div>

          <div class="subsection-heading">基座模型名称显式绑定 (Family Mappings)</div>
          <div class="form-grid">
            <div class="field">
              <label>Opus (全能专家)</label>
              <input :value="modelMappingValue('opus')" type="text" placeholder="claude-opus-4" @input="setModelMappingValue('opus', ($event.target as HTMLInputElement).value)">
            </div>
            <div class="field">
              <label>Sonnet (主力均衡)</label>
              <input :value="modelMappingValue('sonnet')" type="text" placeholder="claude-sonnet-3-5" @input="setModelMappingValue('sonnet', ($event.target as HTMLInputElement).value)">
            </div>
            <div class="field">
              <label>Haiku (极速响应)</label>
              <input :value="modelMappingValue('haiku')" type="text" placeholder="claude-haiku-4" @input="setModelMappingValue('haiku', ($event.target as HTMLInputElement).value)">
            </div>
          </div>
          
          <!-- 专属 JSON Viewer -->
          <details class="code-viewer mt-md">
            <summary class="code-summary">
              <span class="label-text">Agent Configurations (JSON)</span>
              <span class="symbol-expand">›</span>
            </summary>
            <div class="code-content">
              <textarea v-model="drafts.agent.jsonText" rows="6" spellcheck="false" @blur="parseJson('agent')" />
              <div v-if="drafts.agent.jsonError" class="code-error">{{ drafts.agent.jsonError }}</div>
            </div>
          </details>
        </section>

        <hr class="section-divider">

        <!-- Section: Authorization / Providers -->
        <section class="config-section">
          <div class="section-heading split-heading">
            <div class="heading-left">
              <h3>路由节点与身份信道 (Auth & Connections)</h3>
              <p>对应不同架构的底层认证参数载体。</p>
            </div>
            <button class="btn btn-ghost btn-sm" @click="showSecrets = !showSecrets">
              {{ showSecrets ? '隐藏敏感值' : '暴露出站密钥' }}
            </button>
          </div>
          
          <div class="sub-surface" v-if="!isCcrMode">
            <div class="form-grid">
              <div class="field">
                <label>出站 Base URL</label>
                <input v-model="drafts.agent.values.baseUrl" type="text" placeholder="https://api.anthropic.com">
              </div>
              <div class="field">
                <label>服务提供商 API Key</label>
                <input v-model="drafts.agent.values.apiKey" :type="showSecrets ? 'text' : 'password'" placeholder="空">
                <small class="helper-text">{{ showSecrets ? '明文暴露中，请注意安全' : maskSecret(drafts.agent.values.apiKey) }}</small>
              </div>
            </div>
          </div>

          <div v-else class="ccr-branch">
            <div class="form-grid">
              <div class="field">
                <label>中央网关 Host</label>
                <input v-model="drafts.ccr.values.HOST" type="text">
              </div>
              <div class="field">
                <label>中央网关 Port</label>
                <input v-model.number="drafts.ccr.values.PORT" type="number">
              </div>
            </div>

            <div class="subsection-heading">注册池 (CCR Providers Pool)</div>
            <div class="list-container">
              <div v-for="(provider, index) in drafts.ccr.values.Providers" :key="index" class="list-item">
                <div class="item-header">
                  <span class="item-title">{{ provider.name || `Provider Nodes [${index}]` }}</span>
                  <span class="badge badge-normal badge-mono">{{ provider.models?.length || 0 }} Models</span>
                </div>
                <div class="form-grid mt-sm">
                  <div class="field">
                    <label>Proxy 转发域名端口</label>
                    <input v-model="provider.api_base_url" type="text">
                  </div>
                  <div class="field">
                    <label>Provider 集群 Key</label>
                    <input v-model="provider.api_key" :type="showSecrets ? 'text' : 'password'">
                    <small class="helper-text">{{ showSecrets ? '明文暴露中' : maskSecret(provider.api_key) }}</small>
                  </div>
                </div>
                <div class="field mt-sm">
                  <label>过滤池白名单 (英文逗号分割)</label>
                  <input :value="providerModels(provider)" type="text" class="mono-font" @input="updateProviderModels(provider, $event)">
                </div>
              </div>
            </div>
            
            <div class="alert alert-normal mt-md">
              <div class="alert-content">
                <strong>Main Preferred:</strong> {{ primaryProvider?.name || 'Null' }} 
                <span style="opacity:0.4; margin:0 8px;">|</span> 
                <strong>Default Fallback:</strong> {{ drafts.ccr.values.Router.default || 'Null' }}
              </div>
            </div>

            <div class="subsection-heading">底层路由转发面配置 (Router Parameters)</div>
            <div class="form-grid">
              <div class="field">
                <label>Default Fallback</label>
                <input v-model="drafts.ccr.values.Router.default" type="text" class="mono-font">
              </div>
              <div class="field">
                <label>Think Traffic</label>
                <input v-model="drafts.ccr.values.Router.think" type="text" class="mono-font">
              </div>
            </div>
            <div class="form-grid">
              <div class="field">
                <label>Background Queue</label>
                <input v-model="drafts.ccr.values.Router.background" type="text" class="mono-font">
              </div>
              <div class="field">
                <label>Long Context Branch</label>
                <input v-model="drafts.ccr.values.Router.longContext" type="text" class="mono-font">
              </div>
            </div>
            <div class="form-grid form-grid-bottom">
              <div class="field">
                <label>全局抛流超时 (Timeout ms)</label>
                <input v-model.number="drafts.ccr.values.API_TIMEOUT_MS" type="number">
              </div>
              <div class="field field-checkbox">
                <label class="checkbox-label">
                  <input type="checkbox" v-model="drafts.ccr.values.LOG" class="checkbox-input">
                  <span class="custom-checkbox"></span>
                  <div class="checkbox-texts">
                    <span class="primary">网关全埋点日志记录 (LOG)</span>
                    <span class="secondary">于服务端输出底层 TCP/HTTP 流量</span>
                  </div>
                </label>
              </div>
            </div>
          </div>
          
          <div class="json-group">
            <details class="code-viewer mt-md">
              <summary class="code-summary">
                <span class="label-text">Server Bridge Configurations (JSON)</span>
                <span class="symbol-expand">›</span>
              </summary>
              <div class="code-content">
                <textarea v-model="drafts.server.jsonText" rows="6" spellcheck="false" @blur="parseJson('server')" />
                <div v-if="drafts.server.jsonError" class="code-error">{{ drafts.server.jsonError }}</div>
              </div>
            </details>
            <details class="code-viewer mt-sm" v-if="isCcrMode">
              <summary class="code-summary">
                <span class="label-text">CCR Gateway Configurations (JSON)</span>
                <span class="symbol-expand">›</span>
              </summary>
              <div class="code-content">
                <textarea v-model="drafts.ccr.jsonText" rows="8" spellcheck="false" @blur="parseJson('ccr')" />
                <div v-if="drafts.ccr.jsonError" class="code-error">{{ drafts.ccr.jsonError }}</div>
              </div>
            </details>
          </div>
        </section>

        <hr class="section-divider">

        <!-- Section: Web Presentation -->
        <section class="config-section">
          <div class="section-heading">
            <h3>控制台 Web 呈现预设 (Presentation)</h3>
            <p>基于本地运行内存周期存活的客户端数据绑定，提交即刻完成同步。</p>
          </div>
          
          <div class="field mb-md">
            <label>强枚举覆盖集 (每排一项，此项具备全局置高覆盖率)</label>
            <textarea :value="modelLines()" rows="3" class="mono-font" @input="handleModelLinesInput" />
          </div>
          <div class="form-grid">
            <div class="field">
              <label>User 空间强制过滤层 (按行断代)</label>
              <textarea :value="drafts.web.values.layerPresets.User.enabledLayers.join('\n')" rows="5" class="mono-font" @input="handleLayerPresetInput(drafts.web.values.layerPresets.User.enabledLayers, $event)" />
            </div>
            <div class="field">
              <label>Agent 推演可写覆盖层 (按行断代)</label>
              <textarea :value="drafts.web.values.layerPresets.Agent.enabledLayers.join('\n')" rows="5" class="mono-font" @input="handleLayerPresetInput(drafts.web.values.layerPresets.Agent.enabledLayers, $event)" />
            </div>
          </div>

          <details class="code-viewer mt-md">
            <summary class="code-summary">
              <span class="label-text">Web App Configurations (JSON)</span>
              <span class="symbol-expand">›</span>
            </summary>
            <div class="code-content">
              <textarea v-model="drafts.web.jsonText" rows="6" spellcheck="false" @blur="parseJson('web')" />
              <div v-if="drafts.web.jsonError" class="code-error">{{ drafts.web.jsonError }}</div>
            </div>
          </details>

        </section>

      </template>
    </div>
  </div>
</template>

<style scoped>
/* Zinc 调色盘与极简工业风定义 (Vercel/Linear Style) */
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
  
  --bg-page: var(--zinc-950);
  --bg-surface: #111111;
  --bg-subsurface: var(--zinc-900);
  
  --border-light: rgba(255, 255, 255, 0.08);
  --border-medium: var(--zinc-800);
  
  --text-primary: var(--zinc-50);
  --text-secondary: var(--zinc-400);
  
  --accent-blue: #0066ff;
  --radius-sm: 4px;
  --radius-md: 6px;
  --radius-lg: 8px;
  
  display: flex;
  flex-direction: column;
  height: 100%;
  background-color: var(--bg-page);
  color: var(--text-primary);
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif, "Apple Color Emoji", "Segoe UI Emoji", "Segoe UI Symbol";
  overflow: hidden;
  font-size: 14px;
}

hr { border: none; }
.layout-bound { max-width: 860px; margin: 0 auto; width: 100%; }

/* --- Header --- */
.settings-header {
  position: sticky;
  top: 0;
  z-index: 50;
  background: rgba(10, 10, 10, 0.85); /* 真 · 透明黑 */
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border-bottom: 1px solid var(--border-medium);
  padding: 16px 24px;
}

.header-content {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.header-title { display: flex; align-items: center; gap: 12px; }
.header-title .title { margin: 0; font-size: 1.15rem; font-weight: 600; color: var(--text-primary); letter-spacing: -0.01em; }

/* Status Badges */
.badge { display: inline-flex; padding: 2px 8px; border-radius: var(--radius-sm); font-size: 12px; font-weight: 500; height: 22px; align-items: center; }
.badge-mono { font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace; }
.badge-normal { background: var(--zinc-800); color: var(--zinc-300); }
.badge-success { background: rgba(34, 197, 94, 0.1); color: #4ade80; border: 1px solid rgba(34, 197, 94, 0.2); }
.badge-warning { background: rgba(234, 179, 8, 0.1); color: #fde047; border: 1px solid rgba(234, 179, 8, 0.2); }

/* Actions */
.header-actions { display: flex; gap: 10px; align-items: center; }

/* Clean Buttons (Vercel Style) */
.btn {
  display: inline-flex; align-items: center; justify-content: center;
  height: 32px; padding: 0 14px;
  border-radius: var(--radius-md); font-size: 13px; font-weight: 500;
  cursor: pointer; transition: background-color 0.15s, color 0.15s, border-color 0.15s; outline: none; border: 1px solid transparent; font-family: inherit;
}
.btn:disabled { opacity: 0.5; cursor: not-allowed; }

.btn-primary { background: var(--zinc-50); color: black; border-color: var(--zinc-50); }
.btn-primary:hover:not(:disabled) { background: var(--zinc-200); border-color: var(--zinc-200); }

.btn-ghost { background: transparent; color: var(--text-secondary); }
.btn-ghost:hover:not(:disabled) { background: var(--zinc-800); color: var(--text-primary); }

.btn-sm { height: 26px; padding: 0 10px; font-size: 12px; border-radius: var(--radius-sm); }

.btn-danger { background: transparent; color: #ef4444; border-color: var(--border-medium); }
.btn-danger:hover:not(:disabled) { background: rgba(239, 68, 68, 0.1); border-color: #ef4444; }

/* --- Scroll Container --- */
.settings-main { flex: 1; overflow-y: auto; overflow-x: hidden; padding: 32px 24px 80px; }

/* --- Alert Messages --- */
.alerts { display: flex; flex-direction: column; gap: 12px; margin-bottom: 24px; }
.alert { padding: 12px 16px; border-radius: var(--radius-md); font-size: 13px; border: 1px solid transparent; }
.alert-error { background: rgba(239, 68, 68, 0.1); border-color: rgba(239, 68, 68, 0.2); color: #ef4444; }
.alert-warning { background: rgba(234, 179, 8, 0.1); border-color: rgba(234, 179, 8, 0.2); color: #fde047; }
.alert-success { background: rgba(34, 197, 94, 0.1); border-color: rgba(34, 197, 94, 0.2); color: #4ade80; }
.alert-normal { background: var(--zinc-900); border-color: var(--zinc-800); color: var(--zinc-300); padding: 10px 14px; font-size: 13px; display: inline-block; width: 100%; font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;}
.alert-content { font-size: 12px;}
.loading-state { text-align: center; color: var(--zinc-500); padding: 80px 0; }

/* --- Layout Defaults --- */
.config-section { margin-bottom: 12px; }
.section-divider { border-bottom: 1px solid var(--border-medium); margin: 36px 0; height: 0; }

.section-heading { margin-bottom: 20px; }
.section-heading h3 { margin: 0 0 6px; font-size: 1.1rem; font-weight: 500; letter-spacing: -0.01em; color: var(--text-primary); }
.section-heading p { margin: 0; padding: 0; font-size: 13px; color: var(--text-secondary); line-height: 1.5; }

.heading-row { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
.heading-row h3 { margin-bottom: 0; }

.split-heading { display: flex; justify-content: space-between; align-items: flex-start; }

.subsection-heading { margin: 32px 0 16px; font-size: 14px; font-weight: 500; color: var(--zinc-300); }

/* --- Form Grids & Fields --- */
.form-grid { display: flex; gap: 16px; margin-bottom: 16px; }
.form-grid > .field { flex: 1; min-width: 0; }
.form-grid-bottom { margin-bottom: 0; align-items: flex-end; }
.top-margin { margin-top: 24px; }
.mt-sm { margin-top: 12px; }
.mt-md { margin-top: 20px; }
.mb-md { margin-bottom: 20px; }

.field { display: flex; flex-direction: column; gap: 6px; }
.field.opacity-muted { opacity: 0.5; transition: opacity 0.2s; }
.field label { font-size: 13px; font-weight: 500; color: var(--zinc-300); }

.mono-font { font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace; }
.helper-text { font-size: 12px; color: var(--zinc-500); margin: 2px 0 0 2px;}

/* Pure Forms */
input[type="text"], input[type="number"], input[type="password"], select, textarea {
  width: 100%;
  background-color: var(--bg-page);
  border: 1px solid var(--border-medium);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-family: inherit;
  font-size: 13px;
  padding: 0 12px;
  height: 36px;
  box-sizing: border-box;
  outline: none;
  transition: border-color 0.15s, box-shadow 0.15s;
}
textarea { height: auto; padding: 8px 12px; resize: vertical; line-height: 1.5; }
input:focus, select:focus, textarea:focus { border-color: var(--zinc-500); box-shadow: 0 0 0 1px var(--zinc-500); }
input:disabled, select:disabled { opacity: 0.5; background: var(--zinc-900); cursor: not-allowed; }
input::placeholder { color: var(--zinc-600); }

/* --- Segmented Control (Mode Switch) --- */
.segment-group {
  display: inline-flex;
  background: var(--bg-subsurface);
  border: 1px solid var(--border-medium);
  border-radius: var(--radius-lg);
  padding: 4px;
  gap: 4px;
}
.segment {
  position: relative;
  display: flex; align-items: center; justify-content: center;
  padding: 6px 16px; cursor: pointer;
  border-radius: var(--radius-md);
  font-size: 13px; font-weight: 500; color: var(--zinc-400);
  transition: all 0.15s ease;
  user-select: none;
}
.segment input[type="radio"] { opacity: 0; position: absolute; width: 0; height: 0; }
.segment:hover { color: var(--text-primary); }
.segment-active { background: var(--bg-page); color: var(--text-primary); box-shadow: 0 1px 2px rgba(0,0,0,0.5); border: 1px solid var(--border-medium); }

/* --- Custom Checkbox (Toggle Alternative) --- */
.field-checkbox { display: flex; flex-direction: row; align-items: center; height: 36px; padding-left: 4px;}
.checkbox-label { display: flex; align-items: flex-start; gap: 12px; cursor: pointer; user-select: none;}
.checkbox-input { opacity: 0; position: absolute; width: 0; height: 0; }
.custom-checkbox { width: 16px; height: 16px; border-radius: 4px; border: 1px solid var(--border-medium); background: var(--bg-page); display: inline-flex; align-items: center; justify-content: center; transition: 0.1s; margin-top: 1px;}
.custom-checkbox::after { content: ""; width: 4px; height: 8px; border: solid black; border-width: 0 2px 2px 0; transform: rotate(45deg); opacity: 0; margin-bottom: 2px;}
.checkbox-input:checked + .custom-checkbox { background: var(--zinc-50); border-color: var(--zinc-50); }
.checkbox-input:checked + .custom-checkbox::after { opacity: 1; }
.checkbox-texts { display: flex; flex-direction: column; }
.checkbox-texts .primary { font-size: 13px; color: var(--text-primary); font-weight: 500;}
.checkbox-texts .secondary { font-size: 12px; color: var(--zinc-500); }

/* --- Sub-surfaces & Providers List --- */
.sub-surface { background: var(--bg-surface); border: 1px solid var(--border-medium); border-radius: var(--radius-lg); padding: 20px; }
.list-container { display: flex; flex-direction: column; gap: 16px; }
.list-item { background: var(--bg-surface); border: 1px solid var(--border-medium); border-radius: var(--radius-lg); padding: 16px; }
.item-header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--zinc-800); padding-bottom: 12px; margin-bottom: 12px; }
.item-title { font-size: 13px; font-weight: 600; color: var(--zinc-300); }

/* --- JSON Expandable Viewers (Ultra Clean) --- */
.code-viewer { border-top: 1px solid var(--border-medium); }
.code-summary { 
  display: flex; align-items: center; justify-content: space-between; 
  padding: 12px 0; cursor: pointer; list-style: none; user-select: none;
}
.code-summary::-webkit-details-marker { display: none; }
.label-text { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: 12px; color: var(--zinc-400); }
.symbol-expand { display: inline-block; transition: transform 0.2s; color: var(--zinc-600); }
.code-viewer[open] .symbol-expand { transform: rotate(90deg); color: var(--text-primary); }
.code-viewer[open] .label-text { color: var(--text-primary); }

.code-content { padding: 8px 0 16px; }
.code-content textarea {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 12px;
  line-height: 1.5;
  background: black;
  border-radius: var(--radius-md);
  padding: 12px;
}
.code-error { font-size: 12px; color: #ef4444; margin-top: 8px; }

/* Desktop Tweaks */
@media (min-width: 768px) {
  .code-content textarea { height: auto; }
}
</style>
