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
const pageTitle = computed(() => isCcrMode.value ? 'CCR 实例设置' : '直连实例设置')
const modeLabel = computed(() => isCcrMode.value ? 'CCR 网关模式' : '直连模式')
const displayEffectiveModelPath = computed(() => isCcrMode.value
  ? 'server.ccr.defaultModelFamily'
  : 'agent.model')
const effectiveModelDescription = computed(() => isCcrMode.value
  ? '当前运行模式下，真正生效的默认模型来自 Server > ccr.defaultModelFamily。'
  : '当前运行模式下，真正生效的默认模型来自 Agent > model。')

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
  if (!value) return '未设置'
  if (value.length <= 8) return '********'
  return `${value.slice(0, 3)}••••${value.slice(-3)}`
}

async function handleSave() {
  saveError.value = null
  saveMessage.value = null

  for (const key of groupKeys) {
    if (!parseJson(key)) {
      saveError.value = `${drafts[key].title} 的高级 JSON 格式有误`
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
      ? `已保存。${result.restartRequiredGroups.join(' / ')} 仍需重启后生效。`
      : '已保存，当前更改已即时生效。'

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
    
    <!-- 问题5和问题1改进：独立的顶部吸顶操作栏，不再提供冗余返回按钮，只承载核心交互 -->
    <header class="sticky-top-bar">
      <div class="header-left">
        <h2>{{ pageTitle }}</h2>
        <div class="header-badges">
          <span class="mode-pill">{{ modeLabel }}</span>
          <span class="status-pill" :class="restartPendingGroups.length > 0 ? 'status-pending' : 'status-ready'">
            <i class="dot"></i>
            {{ restartPendingGroups.length > 0 ? `待重启 ${restartPendingGroups.length} 组配置` : '已同步当前实例' }}
          </span>
        </div>
      </div>
      
      <div class="header-actions">
        <button class="text-button" type="button" :disabled="isLoading" @click="loadSettings">取消并重置</button>
        <button
          v-if="restartPendingGroups.length > 0"
          class="danger-button outlined"
          type="button"
          :disabled="isRestarting"
          @click="handleRestart"
        >
          {{ isRestarting ? '重启中...' : '重启实例生效' }}
        </button>
        <button class="primary-button glass-btn" type="button" :disabled="isSaving || isLoading" @click="handleSave">
          {{ isSaving ? '保存中...' : '保存更改' }}
        </button>
      </div>
    </header>

    <div class="settings-content-scroll">
      <div class="settings-shell">
        
        <div v-if="loadError || saveError || saveMessage || restartPendingGroups.length > 0" class="notice-stack">
          <div v-if="loadError" class="notice error">{{ loadError }}</div>
          <div v-if="saveError" class="notice error">{{ saveError }}</div>
          <div v-if="saveMessage" class="notice info">{{ saveMessage }}</div>
          <div v-if="restartPendingGroups.length > 0" class="notice warn">
            已修改高影响配置：{{ restartPendingGroups.join(' / ') }}。保存后请完成一次实例重启。
            ({{ runtime.restartBehavior === 'docker-auto' ? 'Docker 将自动拉起' : '需要手动重启服务' }})
          </div>
        </div>

        <div v-if="isLoading" class="loading-state">正在拉取实例配置及节点状态...</div>

        <template v-else>
          <div class="main-settings-card">
            
            <!-- 问题2改进：运行模式切换改为视觉冲击力更强的大卡片 -->
            <section class="config-section mode-section">
              <div class="section-title">
                <h3>运行架构模式</h3>
                <p>决定 Agent 对接哪个链路。不同的模式下，网络和默认推理策略将完全分离。</p>
              </div>
              
              <div class="mode-cards">
                <!-- 直连模式卡片 -->
                <button
                  type="button"
                  class="mode-card"
                  :class="{ active: !drafts.server.values.ccr.enabled }"
                  @click="drafts.server.values.ccr.enabled = false"
                >
                  <div class="mode-icon">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 12H3m0 0l6-6m-6 6l6 6"/></svg>
                  </div>
                  <div class="mode-info">
                    <h4>本地直连模式</h4>
                    <p>Agent 直接连接各个提供商。适合单机部署、轻量化任务开发与直接调用云端大模型 API。</p>
                  </div>
                  <div class="mode-radio">
                    <div class="radio-inner" v-if="!drafts.server.values.ccr.enabled"></div>
                  </div>
                </button>

                <!-- CCR 网关模式卡片 -->
                <button
                  type="button"
                  class="mode-card"
                  :class="{ active: drafts.server.values.ccr.enabled }"
                  @click="drafts.server.values.ccr.enabled = true"
                >
                  <div class="mode-icon">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
                  </div>
                  <div class="mode-info">
                    <h4>挂载大模型路由网关 (CCR)</h4>
                    <p>将 Agent 流量定向至中央网关，支持复杂的路由控制、长上下文无缝切换与日志拦截溯源。</p>
                  </div>
                  <div class="mode-radio">
                    <div class="radio-inner" v-if="drafts.server.values.ccr.enabled"></div>
                  </div>
                </button>
              </div>

              <!-- 基础服务配置 (不受模式影响) -->
              <div class="form-row col-2 mt-4">
                <label class="field highlight-box">
                  <span>Agent 服务监听端口</span>
                  <input v-model.number="drafts.server.values.server.port" type="number">
                </label>
                <label class="field highlight-box">
                  <span>Python 启动命令环境</span>
                  <input v-model="drafts.server.values.server.pythonCommand" type="text" placeholder="python 或 python3">
                </label>
              </div>
              <div class="form-row col-2">
                <label class="field highlight-box" :class="{ disabled: !isCcrMode }">
                  <span>向代理注册的独立 CCR Host</span>
                  <input v-model="drafts.server.values.ccr.host" type="text" :disabled="!isCcrMode">
                </label>
                <label class="field highlight-box" :class="{ disabled: !isCcrMode }">
                  <span>向代理注册的独立 CCR Port</span>
                  <input v-model.number="drafts.server.values.ccr.port" type="number" :disabled="!isCcrMode">
                </label>
              </div>
            </section>

            <div class="section-divider"></div>

            <!-- 问题3改进：每个分类专属的高级 JSON 查询挂载在其区块最下方 -->
            
            <section class="config-section">
              <div class="section-title">
                <h3>默认模型族与推理行为策略</h3>
                <p>
                  {{ effectiveModelDescription }}
                  <span class="path-badge">{{ displayEffectiveModelPath }}</span>
                </p>
              </div>
              
              <div class="section-content">
                <div class="form-row col-2">
                  <label class="field">
                    <span>基准默认模型</span>
                    <select v-model="effectiveDefaultModel">
                      <option v-for="option in modelOptions" :key="option.value" :value="option.value">
                        {{ option.label }}
                      </option>
                    </select>
                  </label>
                  <label class="field">
                    <span>全局保底 Effort (投入资源)</span>
                    <select v-model="drafts.agent.values.defaultEffort" :disabled="isCcrMode && !drafts.ccr.values.Router?.default">
                      <option v-for="option in effortOptions" :key="option.value" :value="option.value">
                        {{ option.label }}
                      </option>
                    </select>
                  </label>
                </div>
                <div class="form-row col-2">
                  <label class="field">
                    <span>扩展思考模式 (Extended Thinking)</span>
                    <select v-model="drafts.agent.values.defaultThinking">
                      <option v-for="option in thinkingOptions" :key="option.value" :value="option.value">
                        {{ option.label }}
                      </option>
                    </select>
                  </label>
                  <label class="field">
                    <span>最大 Thinking Tokens 容量</span>
                    <input v-model.number="drafts.agent.values.maxThinkingTokens" type="number">
                  </label>
                </div>

                <div class="sub-section-title">基座模型硬链接映射规则 (Model Mapping)</div>
                <div class="form-row col-3">
                  <label class="field">
                    <span>全能型推演映射 (Opus)</span>
                    <input
                      :value="modelMappingValue('opus')"
                      type="text"
                      placeholder="claude-opus-4"
                      @input="setModelMappingValue('opus', ($event.target as HTMLInputElement).value)"
                    >
                  </label>
                  <label class="field">
                    <span>高效均衡型映射 (Sonnet)</span>
                    <input
                      :value="modelMappingValue('sonnet')"
                      type="text"
                      placeholder="claude-sonnet-4-20250514"
                      @input="setModelMappingValue('sonnet', ($event.target as HTMLInputElement).value)"
                    >
                  </label>
                  <label class="field">
                    <span>高速轻量型映射 (Haiku)</span>
                    <input
                      :value="modelMappingValue('haiku')"
                      type="text"
                      placeholder="claude-haiku-4-5-20251001"
                      @input="setModelMappingValue('haiku', ($event.target as HTMLInputElement).value)"
                    >
                  </label>
                </div>
              </div>
              
              <!-- 属于这里的独立高级 JSON 视图 -->
              <details class="local-json-accordion">
                <summary>
                  <div class="json-acc-title">
                    <svg viewBox="0 0 24 24" fill="none" class="code-icon" stroke="currentColor" stroke-width="2"><path d="M16 18l6-6-6-6M8 6l-6 6 6 6"/></svg>
                    <span>排查底层 Agent 原始配置源</span>
                  </div>
                  <span class="path-badge">{{ drafts.agent.sourceFile }}</span>
                </summary>
                <div class="json-acc-body">
                  <textarea v-model="drafts.agent.jsonText" rows="8" spellcheck="false" @blur="parseJson('agent')" />
                  <p v-if="drafts.agent.jsonError" class="json-error">{{ drafts.agent.jsonError }}</p>
                </div>
              </details>
            </section>

            <div class="section-divider"></div>

            <!-- 连接与鉴权区块 -->
            <section class="config-section">
              <div class="section-title split-title">
                <div class="title-left">
                  <h3>链路连接与鉴权信道</h3>
                  <p>根据当前左侧选择的运行模式，这里将动态激活对应的下沉参数。</p>
                </div>
                <button class="text-button small-btn" type="button" @click="showSecrets = !showSecrets">
                  {{ showSecrets ? '隐藏明文密钥保护' : '暴露敏感密钥字段' }}
                </button>
              </div>
              
              <div class="section-content">
                <!-- 直连模式选项 -->
                <transition name="fade-slide" mode="out-in">
                  <div v-if="!isCcrMode" class="config-branch" key="direct">
                    <div class="form-row col-2">
                      <label class="field">
                        <span>提供商通信地址 (Base URL)</span>
                        <input v-model="drafts.agent.values.baseUrl" type="text" placeholder="https://api.anthropic.com">
                      </label>
                      <label class="field">
                        <span>顶级访问令牌 (API Key)</span>
                        <input
                          v-model="drafts.agent.values.apiKey"
                          :type="showSecrets ? 'text' : 'password'"
                          placeholder="未配置或留空"
                        >
                        <small class="helper-text highlight-text">{{ showSecrets ? '当前已解除马赛克，防止围观' : maskSecret(drafts.agent.values.apiKey) }}</small>
                      </label>
                    </div>
                  </div>

                  <!-- CCR模式选项 -->
                  <div v-else class="config-branch ccr-branch" key="ccr">
                    <div class="form-row col-2">
                      <label class="field">
                        <span>本地 CCR 网关暴露 Host</span>
                        <input v-model="drafts.ccr.values.HOST" type="text">
                      </label>
                      <label class="field">
                        <span>本地 CCR 网关核心 Port</span>
                        <input v-model.number="drafts.ccr.values.PORT" type="number">
                      </label>
                    </div>

                    <div class="sub-section-title">Providers 集群负载及路由控制组</div>
                    <div class="providers-list">
                      <article
                        v-for="(provider, index) in drafts.ccr.values.Providers"
                        :key="provider.name || index"
                        class="provider-box"
                      >
                        <div class="provider-header">
                          <div class="provider-name">{{ provider.name || `节点集群 ${index + 1}` }}</div>
                          <span class="provider-badge pulse-badge">{{ provider.models?.length || 0 }} 个注册模型</span>
                        </div>
                        
                        <div class="form-row col-2">
                          <label class="field">
                            <span>该层级请求转发地址</span>
                            <input v-model="provider.api_base_url" type="text">
                          </label>
                          <label class="field">
                            <span>该层级通道秘钥 (Key)</span>
                            <input v-model="provider.api_key" :type="showSecrets ? 'text' : 'password'">
                            <small class="helper-text highlight-text">{{ showSecrets ? '已解除显示限制' : maskSecret(provider.api_key || '') }}</small>
                          </label>
                        </div>
                        <div class="form-row">
                          <label class="field">
                            <span>承接模型白名单列表 (使用英文逗号隔离)</span>
                            <input :value="providerModels(provider)" type="text" @input="updateProviderModels(provider, $event)">
                          </label>
                        </div>
                      </article>
                    </div>
                    
                    <div v-if="primaryProvider" class="info-strip outline-glass highlight">
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4m0-4h.01"/></svg>
                      <div>
                        发现可用首选集群边界：<strong>{{ primaryProvider.name }}</strong><br/>
                        目前网关兜底备用路由规则为：<strong>{{ drafts.ccr.values.Router.default || '未识别配置' }}</strong>
                      </div>
                    </div>

                    <!-- CCR 特有的路由子参数 -->
                    <div class="accordion-embedded mt-4">
                      <div class="form-row col-2">
                        <label class="field">
                          <span>精确拦截规则: Default 分支</span>
                          <input v-model="drafts.ccr.values.Router.default" type="text">
                        </label>
                        <label class="field">
                          <span>精确拦截规则: Think 定向分支</span>
                          <input v-model="drafts.ccr.values.Router.think" type="text">
                        </label>
                      </div>
                      <div class="form-row col-2">
                        <label class="field">
                          <span>精确拦截规则: 纯后台队列</span>
                          <input v-model="drafts.ccr.values.Router.background" type="text">
                        </label>
                        <label class="field">
                          <span>精确拦截规则: 超长文脉路段</span>
                          <input v-model="drafts.ccr.values.Router.longContext" type="text">
                        </label>
                      </div>
                      <div class="form-row col-2 align-end">
                        <label class="field">
                          <span>全局路由中断超时 (Timeout Ms)</span>
                          <input v-model.number="drafts.ccr.values.API_TIMEOUT_MS" type="number">
                        </label>
                        <label class="field switch-field glass-switch">
                          <div class="switch-meta">
                            <span class="switch-label">挂载级联 Debug 日志</span>
                            <span class="switch-desc">追踪每一笔进入网关的流量和延迟响应面</span>
                          </div>
                          <div class="switch">
                            <input v-model="drafts.ccr.values.LOG" type="checkbox">
                            <span class="slider"></span>
                          </div>
                        </label>
                      </div>
                    </div>
                  </div>
                </transition>
              </div>

              <!-- 属于这里的独立高级 JSON 视图 -->
              <div class="json-acc-group">
                <details class="local-json-accordion">
                  <summary>
                    <div class="json-acc-title">
                      <svg viewBox="0 0 24 24" fill="none" class="code-icon" stroke="currentColor" stroke-width="2"><path d="M16 18l6-6-6-6M8 6l-6 6 6 6"/></svg>
                      <span>排查底层 Server 桥接配置源</span>
                    </div>
                    <span class="path-badge">{{ drafts.server.sourceFile }}</span>
                  </summary>
                  <div class="json-acc-body">
                    <textarea v-model="drafts.server.jsonText" rows="6" spellcheck="false" @blur="parseJson('server')" />
                    <p v-if="drafts.server.jsonError" class="json-error">{{ drafts.server.jsonError }}</p>
                  </div>
                </details>
                
                <details v-if="isCcrMode" class="local-json-accordion">
                  <summary>
                    <div class="json-acc-title">
                      <svg viewBox="0 0 24 24" fill="none" class="code-icon" stroke="currentColor" stroke-width="2"><path d="M16 18l6-6-6-6M8 6l-6 6 6 6"/></svg>
                      <span>排查 CCR 网关核心路由与集群配置源</span>
                    </div>
                    <span class="path-badge">{{ drafts.ccr.sourceFile }}</span>
                  </summary>
                  <div class="json-acc-body">
                    <textarea v-model="drafts.ccr.jsonText" rows="10" spellcheck="false" @blur="parseJson('ccr')" />
                    <p v-if="drafts.ccr.jsonError" class="json-error">{{ drafts.ccr.jsonError }}</p>
                  </div>
                </details>
              </div>
            </section>

            <div class="section-divider"></div>

            <!-- 可折叠区块：Web 展示配置 -->
            <section class="config-section">
              <div class="section-title">
                <h3>视图化 Web 客户端渲染预设</h3>
                <p>此类配置修改直接作用于内存刷新周期，无需依赖服务级硬重启。</p>
              </div>
              
              <div class="section-content">
                <div class="form-row">
                  <label class="field">
                    <span>外部强制模型列表重写 (每行一条将压制系统枚举)</span>
                    <textarea :value="modelLines()" rows="3" class="mono-font" @input="handleModelLinesInput" />
                  </label>
                </div>
                <div class="form-row col-2">
                  <label class="field">
                    <span>User 图层硬性绑定组 (每行定义一套预设组)</span>
                    <textarea
                      :value="drafts.web.values.layerPresets.User.enabledLayers.join('\n')"
                      rows="4" class="mono-font"
                      @input="handleLayerPresetInput(drafts.web.values.layerPresets.User.enabledLayers, $event)"
                    />
                  </label>
                  <label class="field">
                    <span>Agent 动态映射层级预设组 (按行隔离)</span>
                    <textarea
                      :value="drafts.web.values.layerPresets.Agent.enabledLayers.join('\n')"
                      rows="4" class="mono-font"
                      @input="handleLayerPresetInput(drafts.web.values.layerPresets.Agent.enabledLayers, $event)"
                    />
                  </label>
                </div>
              </div>

              <!-- 属于这里的独立高级 JSON 视图 -->
              <details class="local-json-accordion mt-4">
                <summary>
                  <div class="json-acc-title">
                    <svg viewBox="0 0 24 24" fill="none" class="code-icon" stroke="currentColor" stroke-width="2"><path d="M16 18l6-6-6-6M8 6l-6 6 6 6"/></svg>
                    <span>排查 Web 渲染系统原始配置边界</span>
                  </div>
                  <span class="path-badge">{{ drafts.web.sourceFile }}</span>
                </summary>
                <div class="json-acc-body">
                  <textarea v-model="drafts.web.jsonText" rows="6" spellcheck="false" @blur="parseJson('web')" />
                  <p v-if="drafts.web.jsonError" class="json-error">{{ drafts.web.jsonError }}</p>
                </div>
              </details>
            </section>

          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* 核心架构样式 */
.settings-page {
  --page-surface: #0a0c10;
  --glass-bg: rgba(22, 26, 35, 0.7);
  --glass-solid: #151821;
  --glass-border: rgba(255, 255, 255, 0.08);
  --glass-border-strong: rgba(255, 255, 255, 0.16);
  --text-main: rgba(255, 255, 255, 0.95);
  --text-sub: rgba(255, 255, 255, 0.65);
  --text-mute: rgba(255, 255, 255, 0.4);
  --accent-base: #3b82f6;
  --accent-hover: #4f91fb;
  --accent-soft: rgba(59, 130, 246, 0.15);
  --danger-color: #fca5a5;
  --danger-soft: rgba(248, 113, 113, 0.12);
  
  display: flex;
  flex-direction: column;
  height: 100%;
  color: var(--text-main);
  background: var(--page-surface);
  position: relative;
  font-family: 'Inter', system-ui, -apple-system, sans-serif;
  overflow: hidden;
}

/* 环境光晕 */
.settings-page::before {
  content: '';
  position: absolute;
  top: -100px;
  left: 20%;
  width: 60%;
  height: 400px;
  background: radial-gradient(ellipse at top, rgba(59, 130, 246, 0.15) 0%, transparent 70%);
  pointer-events: none;
  z-index: 0;
}

/* 独立且吸顶的头部操作栏 */
.sticky-top-bar {
  position: sticky;
  top: 0;
  z-index: 50;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 18px 32px;
  background: rgba(14, 16, 22, 0.85);
  backdrop-filter: blur(24px) saturate(180%);
  border-bottom: 1px solid var(--glass-border);
  box-shadow: 0 4px 24px rgba(0, 0, 0, 0.2);
}

.header-left {
  display: flex;
  align-items: center;
  gap: 16px;
}

.header-left h2 {
  margin: 0;
  font-size: 1.35rem;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.header-badges {
  display: flex;
  gap: 10px;
}

.mode-pill, .status-pill, .path-badge, .provider-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 12px;
  border-radius: 999px;
  font-size: 0.82rem;
  font-weight: 500;
  border: 1px solid var(--glass-border);
}

.mode-pill { background: var(--accent-soft); color: #93c5fd; border-color: rgba(59, 130, 246, 0.3); }
.status-pill { background: rgba(255, 255, 255, 0.04); }
.status-ready { color: #86efac; }
.status-ready .dot { width: 6px; height: 6px; border-radius: 50%; background: #86efac; }
.status-pending { color: #fdf08a; background: rgba(253, 224, 71, 0.08); border-color: rgba(253, 224, 71, 0.2); }
.status-pending .dot { width: 6px; height: 6px; border-radius: 50%; background: #fdf08a; box-shadow: 0 0 6px #fdf08a; animation: pulse 2s infinite; }
.path-badge { background: rgba(0, 0, 0, 0.4); font-family: monospace; letter-spacing: 0.05em; color: var(--text-sub); }

.header-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.glass-btn, .text-button, .danger-button.outlined {
  padding: 8px 18px;
  border-radius: 12px;
  font-size: 0.95rem;
  font-weight: 500;
  cursor: pointer;
  outline: none;
  font-family: inherit;
  transition: all 0.2s ease;
}

.glass-btn.primary-button {
  background: var(--accent-base);
  color: #fff;
  border: 1px solid rgba(255, 255, 255, 0.1);
  box-shadow: 0 0 12px rgba(59, 130, 246, 0.4), inset 0 1px 0 rgba(255, 255, 255, 0.2);
}
.glass-btn.primary-button:hover:not(:disabled) {
  background: var(--accent-hover);
  transform: translateY(-1px);
}

.text-button {
  background: transparent;
  color: var(--text-sub);
  border: 1px solid transparent;
}
.text-button:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.05);
  color: var(--text-main);
}
.text-button.small-btn {
  padding: 6px 12px;
  font-size: 0.85rem;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 999px;
}

.danger-button.outlined {
  background: transparent;
  color: var(--danger-color);
  border: 1px solid var(--danger-soft);
}
.danger-button.outlined:hover:not(:disabled) {
  background: var(--danger-soft);
}

button:disabled { opacity: 0.5; cursor: not-allowed; transform: none !important; }

/* 页面滚动区域 */
.settings-content-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 32px 20px 80px;
}

.settings-shell {
  max-width: 1024px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.main-settings-card {
  background: var(--glass-bg);
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(16px);
  border: 1px solid var(--glass-border);
  border-radius: 24px;
  box-shadow: 0 20px 48px rgba(0, 0, 0, 0.4), inset 0 1px 0 rgba(255, 255, 255, 0.04);
}

.config-section {
  padding: 36px 44px;
}

.section-divider {
  border-top: 1px solid rgba(255, 255, 255, 0.06);
  margin: 0 44px;
}

.split-title { display: flex; justify-content: space-between; align-items: flex-start; }
.section-title h3 { margin: 0 0 8px; font-size: 1.15rem; font-weight: 600; color: var(--text-main); }
.section-title p { margin: 0; font-size: 0.9rem; color: var(--text-sub); line-height: 1.5; }

/* 独特的模式选择巨无霸卡片 */
.mode-cards {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 20px;
  margin-top: 24px;
}

.mode-card {
  position: relative;
  display: flex;
  align-items: flex-start;
  gap: 16px;
  background: var(--glass-solid);
  border: 1px solid var(--glass-border-strong);
  border-radius: 20px;
  padding: 24px;
  cursor: pointer;
  text-align: left;
  transition: all 0.25s cubic-bezier(0.2, 0.8, 0.2, 1);
  overflow: hidden;
}

.mode-card::before {
  content: '';
  position: absolute;
  inset: 0;
  opacity: 0;
  background: linear-gradient(135deg, rgba(59, 130, 246, 0.1), transparent);
  transition: opacity 0.25s ease;
}

.mode-card:hover {  border-color: rgba(59, 130, 246, 0.4); transform: translateY(-2px); box-shadow: 0 8px 24px rgba(0, 0, 0, 0.2); }
.mode-card.active { border-color: var(--accent-base); background: rgba(59, 130, 246, 0.05); box-shadow: 0 0 0 1px var(--accent-base), 0 12px 32px rgba(59, 130, 246, 0.15); }
.mode-card.active::before { opacity: 1; }

.mode-icon {
  width: 48px;
  height: 48px;
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.05);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-main);
  flex-shrink: 0;
  transition: all 0.25s ease;
}

.mode-card.active .mode-icon {
  background: var(--accent-base);
  color: #fff;
  box-shadow: 0 4px 16px rgba(59, 130, 246, 0.4);
}

.mode-icon svg { width: 24px; height: 24px; }
.mode-info h4 { margin: 0 0 6px; font-size: 1.05rem; font-weight: 600; }
.mode-info p { margin: 0; font-size: 0.85rem; color: var(--text-sub); line-height: 1.5; }
.mode-radio {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  border: 2px solid rgba(255, 255, 255, 0.2);
  margin-left: auto;
  position: relative;
  flex-shrink: 0;
}
.mode-card.active .mode-radio { border-color: var(--accent-base); }
.radio-inner {
  position: absolute;
  top: 50%; left: 50%;
  transform: translate(-50%, -50%);
  width: 10px; height: 10px;
  border-radius: 50%;
  background: var(--accent-base);
}

.mt-4 { margin-top: 1.5rem; }

/* 基础表单组件 */
.form-row { display: flex; gap: 24px; margin-bottom: 20px; }
.form-row.col-2 > .field { flex: 1; }
.form-row.col-3 > .field { flex: 1; }
.form-row:last-child { margin-bottom: 0; }
.align-end { align-items: flex-end; }

.sub-section-title {
  margin: 32px 0 16px;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-main);
  padding-bottom: 8px;
  border-bottom: 1px dashed rgba(255, 255, 255, 0.1);
}

.field { display: flex; flex-direction: column; justify-content: center; gap: 8px; }
.field.disabled { opacity: 0.5; filter: grayscale(1); }
.field > span { font-size: 0.9rem; font-weight: 500; color: var(--text-sub); }

.highlight-box {
  background: rgba(255, 255, 255, 0.02);
  padding: 16px;
  border-radius: 16px;
  border: 1px solid rgba(255, 255, 255, 0.04);
}

input, select, textarea {
  width: 100%;
  background: rgba(0, 0, 0, 0.2);
  border: 1px solid rgba(255, 255, 255, 0.1);
  color: var(--text-main);
  border-radius: 12px;
  padding: 12px 16px;
  font-size: 0.95rem;
  font-family: inherit;
  outline: none;
  transition: all 0.2s;
  box-shadow: inset 0 2px 4px rgba(0,0,0,0.1);
}
textarea { resize: vertical; min-height: 80px; }
.mono-font { font-family: monospace; font-size: 0.88rem; }
input:focus, select:focus, textarea:focus { border-color: var(--accent-hover); box-shadow: 0 0 0 3px var(--accent-soft), inset 0 2px 4px rgba(0,0,0,0.1); }
input:disabled, select:disabled { opacity: 0.5; background: rgba(0, 0, 0, 0.3); cursor: not-allowed; }
.helper-text { font-size: 0.8rem; color: var(--text-mute); margin-top: 4px; }
.highlight-text { color: #fdf08a; }

/* 细化的 Switch 开关 */
.switch-field.glass-switch {
  display: flex;
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  background: rgba(255, 255, 255, 0.03);
  padding: 12px 16px;
  border-radius: 16px;
  border: 1px solid rgba(255, 255, 255, 0.06);
}
.switch-meta { display: flex; flex-direction: column; gap: 4px; }
.switch-label { font-size: 0.95rem; font-weight: 500; color: var(--text-main); }
.switch-desc { font-size: 0.8rem; color: var(--text-sub); }

.switch { position: relative; width: 48px; height: 26px; flex-shrink: 0; }
.switch input { opacity: 0; width: 0; height: 0; }
.slider { position: absolute; cursor: pointer; inset: 0; background: rgba(255, 255, 255, 0.15); border-radius: 34px; transition: 0.3s; box-shadow: inset 0 2px 4px rgba(0,0,0,0.2); }
.slider:before { content: ""; position: absolute; height: 20px; width: 20px; left: 3px; bottom: 3px; background-color: #fff; border-radius: 50%; box-shadow: 0 2px 4px rgba(0,0,0,0.3); transition: 0.3s cubic-bezier(0.2, 0.8, 0.2, 1); }
input:checked + .slider { background-color: var(--accent-base); }
input:checked + .slider:before { transform: translateX(22px); }

/* Providers 列表 */
.providers-list { display: flex; flex-direction: column; gap: 16px; margin-bottom: 20px; }
.provider-box { background: rgba(0, 0, 0, 0.15); border: 1px solid var(--glass-border); border-radius: 16px; padding: 20px 24px; position: relative; }
.provider-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }
.provider-name { font-size: 1.05rem; font-weight: 600; color: #93c5fd; }
.provider-badge { background: rgba(59, 130, 246, 0.1); border-color: rgba(59, 130, 246, 0.3); color: #93c5fd; }
.pulse-badge { position: relative; }
.pulse-badge::before { content:''; position: absolute; left: 8px; width: 6px; height: 6px; border-radius: 50%; background: #60a5fa; box-shadow: 0 0 6px #60a5fa; }
.provider-badge.pulse-badge { padding-left: 20px; }

.info-strip { display: flex; align-items: flex-start; gap: 12px; border-radius: 14px; padding: 14px 18px; line-height: 1.6; }
.info-strip svg { width: 20px; height: 20px; flex-shrink: 0; color: var(--accent-base); margin-top: 2px;}
.info-strip.highlight { background: var(--accent-soft); border: 1px solid rgba(59, 130, 246, 0.2); }

/* 就近分布的局部 JSON Accordion */
.json-acc-group { display: flex; flex-direction: column; gap: 12px; margin-top: 24px; }
.local-json-accordion {
  background: transparent;
  border-top: 1px dashed rgba(255, 255, 255, 0.1);
  margin-top: 24px;
}
.json-acc-group .local-json-accordion { border-top: none; background: rgba(0, 0, 0, 0.2); border: 1px solid rgba(255, 255, 255, 0.06); border-radius: 12px; margin-top: 0; }

.local-json-accordion summary {
  list-style: none;
  cursor: pointer;
  padding: 16px 0;
  display: flex;
  justify-content: space-between;
  align-items: center;
  user-select: none;
}
.json-acc-group .local-json-accordion summary { padding: 16px; }
.local-json-accordion summary::-webkit-details-marker { display: none; }
.local-json-accordion summary:hover { opacity: 0.8; }

.json-acc-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.9rem;
  color: var(--text-sub);
}
.json-acc-title .code-icon { width: 16px; height: 16px; opacity: 0.7; }

.local-json-accordion[open] .json-acc-title { color: var(--accent-base); }

.json-acc-body {
  padding: 0 0 16px;
  animation: slideFadeIn 0.3s ease-out forwards;
}
.json-acc-group .json-acc-body { padding: 0 16px 16px; }

.json-acc-body textarea {
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.82rem;
  line-height: 1.5;
  background: #000;
  border: 1px solid rgba(255, 255, 255, 0.1);
  box-shadow: inset 0 4px 12px rgba(0, 0, 0, 0.5);
  border-radius: 12px;
}
.json-error { padding: 12px; background: rgba(239, 68, 68, 0.15); color: #fca5a5; font-size: 0.85rem; border-radius: 8px; margin-top: 12px; }

@keyframes slideFadeIn {
  from { opacity: 0; transform: translateY(-4px); }
  to { opacity: 1; transform: translateY(0); }
}

/* 通知条 */
.notice-stack { display: flex; flex-direction: column; gap: 12px; margin-bottom: 20px; }
.notice { padding: 14px 20px; border-radius: 14px; font-size: 0.9rem; line-height: 1.5; border: 1px solid transparent; }
.notice.error { background: var(--danger-soft); border-color: rgba(239, 68, 68, 0.2); color: #fca5a5; }
.notice.warn { background: rgba(234, 179, 8, 0.1); border-color: rgba(234, 179, 8, 0.2); color: #fde047; }
.notice.info { background: var(--accent-soft); border-color: rgba(59, 130, 246, 0.2); color: #93c5fd; }
.loading-state { padding: 60px 0; text-align: center; color: var(--text-sub); }

/* 过渡动画 */
.fade-slide-enter-active, .fade-slide-leave-active { transition: all 0.3s ease; }
.fade-slide-enter-from { opacity: 0; transform: translateY(-10px); }
.fade-slide-leave-to { opacity: 0; transform: translateY(10px); }

@keyframes pulse {
  0% { box-shadow: 0 0 0 0 rgba(253, 224, 71, 0.4); }
  70% { box-shadow: 0 0 0 6px rgba(253, 224, 71, 0); }
  100% { box-shadow: 0 0 0 0 rgba(253, 224, 71, 0); }
}
</style>
