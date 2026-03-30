<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { SettingsService } from '../../services/SettingsService'
import { SERVER_BASE } from '../../config/api'
import GlassButton from './base/GlassButton.vue'
import GlassSelect from './base/GlassSelect.vue'
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
const isMounted = ref(false)

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

const logLevelOptions = [
  { value: 'debug', label: 'Debug' },
  { value: 'info', label: 'Info' },
  { value: 'warn', label: 'Warn' },
  { value: 'error', label: 'Error' }
]

for (const key of groupKeys) {
  watch(() => drafts[key].values, value => {
    if (!drafts[key].jsonError) {
      drafts[key].jsonText = formatJson(value)
    }
  }, { deep: true })
}

const isCcrMode = computed(() => Boolean(drafts.server.values.ccr?.enabled))
const displayEffectiveModelPath = computed(() => isCcrMode.value
  ? 'server.ccr.defaultModelFamily'
  : 'agent.model')

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
    await SettingsService.restartInstance()
    saveMessage.value = '服务正在重启，请稍候...'
    restartPendingGroups.value = []

    // 等待旧服务关闭（800ms 延迟 + 余量）
    await new Promise(r => setTimeout(r, 2000))

    // 轮询健康检查，等待新实例就绪
    const maxRetries = 20
    const retryInterval = 1500
    for (let i = 0; i < maxRetries; i++) {
      try {
        const resp = await fetch(`${SERVER_BASE}/health`, { cache: 'no-store' })
        if (resp.ok) {
          saveMessage.value = '服务已恢复，正在刷新页面...'
          await new Promise(r => setTimeout(r, 500))
          window.location.reload()
          return
        }
      } catch {
        // 服务还未就绪，继续轮询
      }
      await new Promise(r => setTimeout(r, retryInterval))
    }

    // 超时提示
    saveError.value = '服务重启超时（30秒），请手动刷新页面或检查服务状态。'
  } catch (error: any) {
    // 请求发出后连接断开是正常的（服务正在关闭）
    if (error.code === 'ECONNABORTED' || error.message?.includes('Network Error')) {
      saveMessage.value = '服务正在重启，请稍候...'
      // 同样开始轮询
      await new Promise(r => setTimeout(r, 2000))
      const maxRetries = 20
      for (let i = 0; i < maxRetries; i++) {
        try {
          const resp = await fetch(`${SERVER_BASE}/health`, { cache: 'no-store' })
          if (resp.ok) {
            window.location.reload()
            return
          }
        } catch { /* continue */ }
        await new Promise(r => setTimeout(r, 1500))
      }
      saveError.value = '服务重启超时，请手动刷新页面。'
    } else {
      saveError.value = error.response?.data?.message || error.message || '触发重启失败'
    }
  } finally {
    isRestarting.value = false
  }
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
        <span class="badge" :class="restartPendingGroups.length > 0 ? 'badge-warning' : 'badge-success'">
          <span class="dot"></span>
          {{ restartPendingGroups.length > 0 ? `待重启 (${restartPendingGroups.length})` : '已同步' }}
        </span>
        <GlassButton variant="ghost" :disabled="isLoading" @click="loadSettings">取消并重置</GlassButton>
        <GlassButton v-if="restartPendingGroups.length > 0" variant="danger" :disabled="isRestarting" @click="handleRestart">
          {{ isRestarting ? '重启中...' : '重启服务生效' }}
        </GlassButton>
        <GlassButton variant="primary" :disabled="isSaving || isLoading" @click="handleSave" style="display: flex; align-items: center; justify-content: center; gap: 6px;">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="width: 14px; height: 14px;"><polyline points="20 6 9 17 4 12"></polyline></svg>
          {{ isSaving ? '保存中...' : '提交更改' }}
        </GlassButton>
      </div>
    </Teleport>

    <div class="settings-main">
      <div class="layout-bound wrapper-pad">
        <div class="page-intro mb-lg">
          <h1 class="page-title">全局配置</h1>
          <p class="page-desc">配置下一次应用启动或重载时的环境变量与底层偏好。修改涉及重型组态时需重启实例。</p>
        </div>

        <div v-if="loadError || saveError || saveMessage || restartPendingGroups.length > 0" class="alerts mb-md">
          <div v-if="loadError" class="alert alert-error">{{ loadError }}</div>
          <div v-if="saveError" class="alert alert-error">{{ saveError }}</div>
          <div v-if="saveMessage" class="alert alert-success">{{ saveMessage }}</div>
          <div v-if="restartPendingGroups.length > 0" class="alert alert-warning">
            您修改了以下配置（{{ restartPendingGroups.join(', ') }}），已保存到磁盘，需重启服务生效。
            ({{ runtime.restartBehavior === 'docker-auto' ? 'Docker 管理自动重启' : '需手动重启' }})
          </div>
        </div>

        <div v-if="isLoading" class="loading-state">加载配置...</div>

        <template v-else>
          
          <!-- Card 1: 运行架构 -->
          <article class="config-card">
            <header class="card-header">
              <div class="heading-left">
                <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8m-4-4v4"/></svg>
                <div class="heading-text">
                  <h3>服务与连接</h3>
                  <p>Server 端口、Python 环境及 API 请求路由模式。</p>
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
                  <label>Server 端口</label>
                  <input v-model.number="drafts.server.values.server.port" type="number">
                </div>
                <div class="field">
                  <label>Python 命令</label>
                  <input v-model="drafts.server.values.server.pythonCommand" type="text" placeholder="python 或 python3">
                </div>
              </div>
              
              <div class="form-grid mt-md">
                <div class="field" :class="{ 'opacity-muted': !isCcrMode }">
                  <label>CCR Host</label>
                  <input v-model="drafts.server.values.ccr.host" type="text" :disabled="!isCcrMode">
                </div>
                <div class="field" :class="{ 'opacity-muted': !isCcrMode }">
                  <label>CCR Port</label>
                  <input v-model.number="drafts.server.values.ccr.port" type="number" :disabled="!isCcrMode">
                </div>
              </div>

              <div class="form-grid mt-md">
                <div class="field field-checkbox" :class="{ 'opacity-muted': !isCcrMode }">
                  <label class="checkbox-label" :style="!isCcrMode ? 'cursor: not-allowed;' : ''">
                    <input type="checkbox" v-model="drafts.server.values.ccr.autoStart" class="checkbox-input" :disabled="!isCcrMode">
                    <span class="custom-checkbox"></span>
                    <div class="checkbox-texts">
                      <span class="primary">自动启动 CCR</span>
                      <span class="secondary">Server 启动时自动拉起 CCR 网关进程</span>
                    </div>
                  </label>
                </div>
              </div>

              <div class="divider mt-xl mb-md"><span>Agent 回连地址</span></div>
              <div class="form-grid">
                <div class="field">
                  <label>Agent → Server Host</label>
                  <input v-model="drafts.agent.values.server.host" type="text" placeholder="127.0.0.1">
                </div>
                <div class="field">
                  <label>Agent → Server Port</label>
                  <input v-model.number="drafts.agent.values.server.port" type="number">
                </div>
              </div>

              <div class="divider mt-xl mb-md"><span>启动选项</span></div>
              <div class="form-grid">
                <div class="field field-checkbox">
                  <label class="checkbox-label">
                    <input type="checkbox" v-model="drafts.server.values.startup.openBrowser" class="checkbox-input">
                    <span class="custom-checkbox"></span>
                    <div class="checkbox-texts">
                      <span class="primary">启动时打开浏览器</span>
                    </div>
                  </label>
                </div>
                <div class="field" :class="{ 'opacity-muted': !drafts.server.values.startup.openBrowser }">
                  <label>浏览器路径</label>
                  <input v-model="drafts.server.values.startup.browserPath" type="text" placeholder="留空使用系统默认" :disabled="!drafts.server.values.startup.openBrowser">
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
                  <h3>模型与推理</h3>
                  <p>默认模型选择、推理参数及模型 ID 映射。</p>
                </div>
              </div>
              <div class="heading-right">
                <span class="badge badge-mono subtle-badge">{{ displayEffectiveModelPath }}</span>
              </div>
            </header>
            
            <div class="card-body">
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
                <div class="field">
                  <label>Effort</label>
                  <GlassSelect
                    v-model="drafts.agent.values.defaultEffort"
                    class="settings-select"
                    width="100%"
                    :options="effortOptions"
                    :disabled="isCcrMode && !drafts.ccr.values.Router?.default"
                    placeholder="选择 Effort"
                  />
                </div>
              </div>
              
              <div class="form-grid mt-md">
                <div class="field">
                  <label>Extended Thinking</label>
                  <GlassSelect
                    v-model="drafts.agent.values.defaultThinking"
                    class="settings-select"
                    width="100%"
                    :options="thinkingOptions"
                    placeholder="选择 Thinking"
                  />
                </div>
                <div class="field">
                  <label>最大 Thinking Tokens</label>
                  <input v-model.number="drafts.agent.values.maxThinkingTokens" type="number">
                </div>
              </div>

              <div class="divider mt-xl mb-md">
                <span>模型 ID 映射</span>
              </div>
              <div class="form-grid">
                <div class="field">
                  <label>Opus</label>
                  <input :value="modelMappingValue('opus')" type="text" placeholder="claude-opus-4" @input="setModelMappingValue('opus', ($event.target as HTMLInputElement).value)">
                </div>
                <div class="field">
                  <label>Sonnet</label>
                  <input :value="modelMappingValue('sonnet')" type="text" placeholder="claude-sonnet-3-5" @input="setModelMappingValue('sonnet', ($event.target as HTMLInputElement).value)">
                </div>
                <div class="field">
                  <label>Haiku</label>
                  <input :value="modelMappingValue('haiku')" type="text" placeholder="claude-haiku-4" @input="setModelMappingValue('haiku', ($event.target as HTMLInputElement).value)">
                </div>
              </div>
              
              <!-- Editor block -->
              <details class="code-editor-block mt-xl">
                <summary class="editor-summary">
                  JSON 编辑器 — agent config
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
                  <h3>API 密钥与 Provider</h3>
                  <p>模型 API 的连接地址和鉴权配置。</p>
                </div>
              </div>
            </header>
            
            <div class="card-body">
              <div v-if="!isCcrMode" class="inner-subcard">
                <div class="form-grid">
                  <div class="field">
                    <label>Base URL</label>
                    <input v-model="drafts.agent.values.baseUrl" type="text" placeholder="https://api.anthropic.com">
                  </div>
                  <div class="field">
                    <label>API Key</label>
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

                <div class="inline-alert warm mt-md mb-lg">
                  <svg class="alert-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
                  <span>首选 Provider: <strong class="text-white">{{ primaryProvider?.name || 'Null' }}</strong> | 无模型匹配时兜底: <strong class="text-white">{{ drafts.ccr.values.Router.default || 'Null' }}</strong></span>
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
              
              <div class="json-group mt-xl">
                <details class="code-editor-block">
                  <summary class="editor-summary">JSON 编辑器 — server_config</summary>
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
                  <summary class="editor-summary">JSON 编辑器 — ccr_config</summary>
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
                  <h3>Web 前端</h3>
                  <p>前端显示配置，修改后立即生效，无需重启。</p>
                </div>
              </div>
            </header>
            
            <div class="card-body">
              <div class="field mb-lg">
                <label>自定义模型列表 (每行一个)</label>
                <textarea :value="modelLines()" rows="3" class="mono-font" @input="handleModelLinesInput" placeholder="留下空白即使用自动检测..." />
              </div>

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

              <details class="code-editor-block mt-xl">
                <summary class="editor-summary">JSON 编辑器 — web_config</summary>
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
