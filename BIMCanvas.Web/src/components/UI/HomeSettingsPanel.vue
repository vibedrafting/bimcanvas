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
    <div class="settings-shell">
      <header class="hero-card">
        <button class="back-chip" type="button" @click="emit('close')">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M15 18l-6-6 6-6" />
          </svg>
          返回项目列表
        </button>

        <div class="hero-main">
          <div class="hero-avatar">{{ isCcrMode ? 'C' : 'D' }}</div>
          <div class="hero-copy">
            <div class="hero-topline">
              <span class="mode-pill">{{ modeLabel }}</span>
              <span class="mode-note">{{ runtime.restartHint }}</span>
            </div>
            <h2>{{ pageTitle }}</h2>
            <p>{{ effectiveModelDescription }}</p>
          </div>
        </div>

        <div class="hero-metrics">
          <article class="metric-card">
            <span class="metric-label">当前默认</span>
            <div class="metric-value">{{ effectiveDefaultModel || '未设置' }}</div>
          </article>
          <article class="metric-card">
            <span class="metric-label">真源字段</span>
            <div class="metric-value"><code>{{ displayEffectiveModelPath }}</code></div>
          </article>
          <article class="metric-card">
            <span class="metric-label">重启方式</span>
            <div class="metric-value">{{ runtime.restartBehavior === 'docker-auto' ? 'Docker 自动拉起' : '手动重启服务' }}</div>
          </article>
          <article class="metric-card" :class="restartPendingGroups.length > 0 ? 'status-pending' : 'status-ready'">
            <span class="metric-label">当前状态</span>
            <div class="metric-value">
              {{ restartPendingGroups.length > 0 ? `待重启 ${restartPendingGroups.length} 组配置` : '已同步到当前实例' }}
            </div>
          </article>
        </div>
      </header>

      <div
        v-if="loadError || saveError || saveMessage || restartPendingGroups.length > 0"
        class="notice-stack"
      >
        <div v-if="loadError" class="notice error">{{ loadError }}</div>
        <div v-if="saveError" class="notice error">{{ saveError }}</div>
        <div v-if="saveMessage" class="notice info">{{ saveMessage }}</div>
        <div v-if="restartPendingGroups.length > 0" class="notice warn">
          已修改高影响配置：{{ restartPendingGroups.join(' / ') }}。保存后请完成一次实例重启。
        </div>
      </div>

      <div v-if="isLoading" class="loading-state">正在加载实例配置...</div>

      <template v-else>
        <section class="form-card">
          <div class="section-head">
            <div>
              <h3>运行方式</h3>
              <p>决定默认模型由哪一组配置控制，并影响重启后的真实行为。</p>
            </div>
            <label class="toggle-line" :class="{ enabled: drafts.server.values.ccr.enabled }">
              <span>启用 CCR</span>
              <input v-model="drafts.server.values.ccr.enabled" type="checkbox">
            </label>
          </div>

          <div class="form-grid">
            <label class="field">
              <span>Agent 端口</span>
              <input v-model.number="drafts.server.values.server.port" type="number">
            </label>
            <label class="field">
              <span>Python 命令</span>
              <input v-model="drafts.server.values.server.pythonCommand" type="text">
            </label>
            <label class="field">
              <span>CCR Host</span>
              <input v-model="drafts.server.values.ccr.host" type="text" :disabled="!isCcrMode">
            </label>
            <label class="field">
              <span>CCR Port</span>
              <input v-model.number="drafts.server.values.ccr.port" type="number" :disabled="!isCcrMode">
            </label>
          </div>
        </section>

        <section class="form-card">
          <div class="section-head">
            <div>
              <h3>默认模型与推理行为</h3>
              <p>{{ effectiveModelDescription }}</p>
            </div>
            <span class="effective-path">{{ displayEffectiveModelPath }}</span>
          </div>

          <div class="form-grid">
            <label class="field">
              <span>默认模型</span>
              <select v-model="effectiveDefaultModel">
                <option v-for="option in modelOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </option>
              </select>
            </label>
            <label class="field">
              <span>默认 Effort</span>
              <select v-model="drafts.agent.values.defaultEffort">
                <option v-for="option in effortOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </option>
              </select>
            </label>
            <label class="field">
              <span>默认 Thinking</span>
              <select v-model="drafts.agent.values.defaultThinking">
                <option v-for="option in thinkingOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </option>
              </select>
            </label>
            <label class="field">
              <span>最大 Thinking Tokens</span>
              <input v-model.number="drafts.agent.values.maxThinkingTokens" type="number">
            </label>
          </div>

          <div class="mapping-grid">
            <label class="field">
              <span>Opus 映射</span>
              <input
                :value="modelMappingValue('opus')"
                type="text"
                placeholder="claude-opus-4-6"
                @input="setModelMappingValue('opus', ($event.target as HTMLInputElement).value)"
              >
            </label>
            <label class="field">
              <span>Sonnet 映射</span>
              <input
                :value="modelMappingValue('sonnet')"
                type="text"
                placeholder="claude-sonnet-4-20250514"
                @input="setModelMappingValue('sonnet', ($event.target as HTMLInputElement).value)"
              >
            </label>
            <label class="field">
              <span>Haiku 映射</span>
              <input
                :value="modelMappingValue('haiku')"
                type="text"
                placeholder="claude-haiku-4-5-20251001"
                @input="setModelMappingValue('haiku', ($event.target as HTMLInputElement).value)"
              >
            </label>
          </div>
        </section>

        <section class="form-card">
          <div class="section-head">
            <div>
              <h3>连接与鉴权</h3>
              <p>根据当前运行模式展示真正参与运行的连接配置。</p>
            </div>
            <button class="text-button" type="button" @click="showSecrets = !showSecrets">
              {{ showSecrets ? '隐藏密钥' : '显示密钥' }}
            </button>
          </div>

          <template v-if="!isCcrMode">
            <div class="form-grid">
              <label class="field field-span-2">
                <span>请求地址</span>
                <input v-model="drafts.agent.values.baseUrl" type="text">
              </label>
              <label class="field field-span-2">
                <span>API Key</span>
                <input
                  v-model="drafts.agent.values.apiKey"
                  :type="showSecrets ? 'text' : 'password'"
                  placeholder="未设置"
                >
                <small>{{ showSecrets ? '当前显示明文' : maskSecret(drafts.agent.values.apiKey) }}</small>
              </label>
            </div>
          </template>

          <template v-else>
            <div class="form-grid">
              <label class="field">
                <span>本地 CCR Host</span>
                <input v-model="drafts.ccr.values.HOST" type="text">
              </label>
              <label class="field">
                <span>本地 CCR Port</span>
                <input v-model.number="drafts.ccr.values.PORT" type="number">
              </label>
            </div>

            <div class="providers-shell">
              <article
                v-for="(provider, index) in drafts.ccr.values.Providers"
                :key="provider.name || index"
                class="provider-card"
              >
                <div class="provider-head">
                  <div>
                    <h4>{{ provider.name || `Provider ${index + 1}` }}</h4>
                    <p>{{ Array.isArray(provider.transformer?.use) ? provider.transformer.use.join(', ') : '未配置 transformer' }}</p>
                  </div>
                  <span class="provider-badge">{{ provider.models?.length || 0 }} 个模型</span>
                </div>

                <label class="field field-span-2">
                  <span>请求地址</span>
                  <input v-model="provider.api_base_url" type="text">
                </label>
                <label class="field field-span-2">
                  <span>API Key</span>
                  <input v-model="provider.api_key" :type="showSecrets ? 'text' : 'password'">
                  <small>{{ showSecrets ? '当前显示明文' : maskSecret(provider.api_key || '') }}</small>
                </label>
                <label class="field field-span-2">
                  <span>模型列表</span>
                  <input :value="providerModels(provider)" type="text" @input="updateProviderModels(provider, $event)">
                </label>
              </article>
            </div>
          </template>

          <div v-if="primaryProvider && isCcrMode" class="tip-strip">
            当前主要 Provider：{{ primaryProvider.name }}，默认路由：{{ drafts.ccr.values.Router.default || '未设置' }}
          </div>
        </section>

        <section v-if="isCcrMode" class="form-card">
          <div class="section-head">
            <div>
              <h3>CCR 路由</h3>
              <p>这些字段仅在 CCR 模式下参与默认模型的最终路由。</p>
            </div>
          </div>

          <div class="form-grid">
            <label class="field">
              <span>Router.default</span>
              <input v-model="drafts.ccr.values.Router.default" type="text">
            </label>
            <label class="field">
              <span>Router.think</span>
              <input v-model="drafts.ccr.values.Router.think" type="text">
            </label>
            <label class="field">
              <span>Router.background</span>
              <input v-model="drafts.ccr.values.Router.background" type="text">
            </label>
            <label class="field">
              <span>Router.longContext</span>
              <input v-model="drafts.ccr.values.Router.longContext" type="text">
            </label>
            <label class="field">
              <span>API_TIMEOUT_MS</span>
              <input v-model.number="drafts.ccr.values.API_TIMEOUT_MS" type="number">
            </label>
            <label class="field toggle-line field-inline-toggle" :class="{ enabled: drafts.ccr.values.LOG }">
              <span>LOG</span>
              <input v-model="drafts.ccr.values.LOG" type="checkbox">
            </label>
          </div>
        </section>

        <section class="form-card">
          <div class="section-head">
            <div>
              <h3>Web 展示配置</h3>
              <p>这些配置保存后可直接热更新，无需重启实例。</p>
            </div>
          </div>

          <div class="form-grid">
            <label class="field field-span-2">
              <span>自定义模型列表</span>
              <textarea :value="modelLines()" rows="4" @input="handleModelLinesInput" />
            </label>
            <label class="field">
              <span>User 图层预设</span>
              <textarea
                :value="drafts.web.values.layerPresets.User.enabledLayers.join('\n')"
                rows="4"
                @input="handleLayerPresetInput(drafts.web.values.layerPresets.User.enabledLayers, $event)"
              />
            </label>
            <label class="field">
              <span>Agent 图层预设</span>
              <textarea
                :value="drafts.web.values.layerPresets.Agent.enabledLayers.join('\n')"
                rows="4"
                @input="handleLayerPresetInput(drafts.web.values.layerPresets.Agent.enabledLayers, $event)"
              />
            </label>
          </div>
        </section>

        <section class="form-card">
          <div class="section-head">
            <div>
              <h3>高级 JSON</h3>
              <p>保留原始配置编辑能力，便于核对与迁移。</p>
            </div>
          </div>

          <div class="json-list">
            <details v-for="key in groupKeys" :key="key" class="json-item">
              <summary>
                <span>{{ drafts[key].title }}</span>
                <small>{{ drafts[key].sourceFile }}</small>
              </summary>
              <div class="json-body">
                <textarea v-model="drafts[key].jsonText" rows="12" spellcheck="false" @blur="parseJson(key)" />
                <p v-if="drafts[key].jsonError" class="json-error">{{ drafts[key].jsonError }}</p>
              </div>
            </details>
          </div>
        </section>
      </template>
    </div>

    <footer class="sticky-footer">
      <div class="footer-copy">
        <strong>{{ modeLabel }}</strong>
        <span>{{ runtime.restartHint }}</span>
      </div>
      <div class="footer-actions">
        <button class="secondary-button" type="button" :disabled="isLoading" @click="loadSettings">刷新</button>
        <button
          v-if="restartPendingGroups.length > 0"
          class="danger-button"
          type="button"
          :disabled="isRestarting"
          @click="handleRestart"
        >
          {{ isRestarting ? '重启中...' : '重启实例' }}
        </button>
        <button class="primary-button" type="button" :disabled="isSaving || isLoading" @click="handleSave">
          {{ isSaving ? '保存中...' : '保存配置' }}
        </button>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.settings-page {
  --page-surface: rgba(17, 20, 31, 0.78);
  --page-surface-strong: rgba(11, 14, 22, 0.92);
  --page-surface-soft: rgba(255, 255, 255, 0.03);
  --page-surface-soft-hover: rgba(255, 255, 255, 0.05);
  --page-border: rgba(255, 255, 255, 0.1);
  --page-border-strong: rgba(255, 255, 255, 0.16);
  --page-text: var(--text-primary);
  --page-text-secondary: var(--text-secondary);
  --page-text-tertiary: var(--text-tertiary);
  --page-accent: var(--accent-blue);
  --page-accent-soft: rgba(59, 130, 246, 0.14);
  --page-accent-strong: rgba(59, 130, 246, 0.26);
  --page-success: var(--accent-green);
  --page-warn: var(--accent-yellow);
  --page-danger: var(--accent-danger);
  position: relative;
  min-height: 100%;
  padding: 8px 0 132px;
  color: var(--page-text);
}

.settings-page::before,
.settings-page::after {
  content: '';
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.settings-page::before {
  background:
    radial-gradient(circle at 14% 0%, rgba(59, 130, 246, 0.22), transparent 30%),
    radial-gradient(circle at 86% 6%, rgba(52, 199, 89, 0.12), transparent 24%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.04), transparent 24%);
  opacity: 0.95;
}

.settings-page::after {
  background-image:
    linear-gradient(rgba(255, 255, 255, 0.024) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.024) 1px, transparent 1px);
  background-size: 48px 48px;
  mask-image: linear-gradient(180deg, rgba(255, 255, 255, 0.18), transparent 55%);
}

.settings-shell {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  gap: 18px;
  max-width: 1380px;
  margin: 0 auto;
}

.hero-card,
.form-card {
  position: relative;
  overflow: hidden;
  background:
    linear-gradient(180deg, rgba(22, 26, 38, 0.9), rgba(12, 14, 22, 0.82)),
    var(--page-surface);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid var(--page-border);
  border-radius: 24px;
  box-shadow:
    var(--shadow-panel),
    inset 0 1px 0 rgba(255, 255, 255, 0.08);
}

.hero-card::before,
.form-card::before {
  content: '';
  position: absolute;
  inset: 0;
  pointer-events: none;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.08), transparent 26%);
}

.hero-card > *,
.form-card > * {
  position: relative;
  z-index: 1;
}

.hero-card {
  padding: 18px 22px 22px;
  box-shadow:
    0 22px 52px rgba(0, 0, 0, 0.34),
    inset 0 1px 0 rgba(255, 255, 255, 0.1),
    0 0 0 1px rgba(59, 130, 246, 0.08);
}

.back-chip {
  border: 1px solid var(--page-border);
  background: rgba(255, 255, 255, 0.04);
  color: var(--page-text);
  height: 44px;
  padding: 0 18px;
  border-radius: 999px;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  transition: background-color 0.18s ease, border-color 0.18s ease, color 0.18s ease;
}

.back-chip:hover {
  background: rgba(255, 255, 255, 0.08);
  border-color: var(--page-border-strong);
}

.back-chip svg {
  width: 16px;
  height: 16px;
}

.hero-main {
  display: grid;
  grid-template-columns: 108px minmax(0, 1fr);
  gap: 22px;
  align-items: start;
  margin-top: 18px;
}

.hero-avatar {
  width: 108px;
  height: 108px;
  border-radius: 28px;
  display: grid;
  place-items: center;
  background:
    radial-gradient(circle at 28% 24%, rgba(59, 130, 246, 0.28), transparent 42%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.08), rgba(255, 255, 255, 0.01)),
    rgba(7, 10, 16, 0.84);
  border: 1px solid rgba(59, 130, 246, 0.24);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.12),
    0 18px 28px rgba(59, 130, 246, 0.08);
  font-size: 2.4rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  color: #dbe9ff;
}

.hero-topline {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.mode-pill,
.provider-badge,
.effective-path {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  padding: 6px 10px;
  font-size: 0.78rem;
}

.mode-pill {
  background: var(--page-accent-soft);
  border: 1px solid var(--page-accent-strong);
  color: #d7e7ff;
}

.provider-badge,
.effective-path {
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.08);
  color: var(--page-text-secondary);
}

.mode-note,
.section-head p,
.hero-copy p,
.field small,
.json-item summary small,
.provider-head p,
.footer-copy span,
.loading-state {
  color: var(--page-text-secondary);
}

.hero-copy h2 {
  margin: 10px 0 8px;
  font-size: clamp(2rem, 4vw, 2.75rem);
  line-height: 1;
  letter-spacing: -0.03em;
}

.hero-copy p {
  margin: 0;
  max-width: 880px;
  font-size: 1rem;
}

.hero-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  margin-top: 22px;
}

.metric-card {
  border-radius: 18px;
  padding: 14px 16px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04);
}

.metric-label {
  display: block;
  margin-bottom: 8px;
  font-size: 0.74rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--page-text-tertiary);
}

.metric-value {
  font-size: 1rem;
  font-weight: 600;
  line-height: 1.45;
  word-break: break-word;
}

.metric-value code {
  display: inline-block;
  padding: 4px 9px;
  border-radius: 999px;
  background: rgba(59, 130, 246, 0.12);
  color: #d7e7ff;
  font-family: var(--font-mono);
  font-size: 0.86rem;
}

.metric-card.status-ready .metric-value {
  color: #bce8ca;
}

.metric-card.status-pending .metric-value {
  color: #ffe29f;
}

.notice-stack {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.notice {
  border-radius: 16px;
  padding: 14px 16px;
  border: 1px solid var(--page-border);
  background: rgba(255, 255, 255, 0.04);
}

.notice.error {
  color: #ffc0c0;
  border-color: rgba(255, 107, 107, 0.24);
  background: rgba(255, 107, 107, 0.1);
}

.notice.info {
  color: #cfe1ff;
  border-color: rgba(59, 130, 246, 0.24);
  background: rgba(59, 130, 246, 0.1);
}

.notice.warn {
  color: #ffe29f;
  border-color: rgba(255, 204, 0, 0.22);
  background: rgba(255, 204, 0, 0.08);
}

.loading-state {
  padding: 84px 24px;
  text-align: center;
  border-radius: 24px;
  border: 1px dashed rgba(255, 255, 255, 0.12);
  background: rgba(255, 255, 255, 0.03);
}

.form-card {
  padding: 22px 22px 24px;
}

.section-head,
.toggle-line,
.provider-head,
.sticky-footer,
.footer-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
}

.section-head {
  align-items: flex-start;
  margin-bottom: 18px;
}

.section-head h3,
.provider-head h4 {
  margin: 0;
  letter-spacing: -0.02em;
}

.section-head p,
.provider-head p {
  margin: 8px 0 0;
}

.form-grid,
.mapping-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.mapping-grid {
  margin-top: 16px;
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.field {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.toggle-line {
  display: flex;
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  min-height: 56px;
  padding: 12px 14px;
  border-radius: 18px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.03);
  transition: background-color 0.18s ease, border-color 0.18s ease, box-shadow 0.18s ease;
}

.toggle-line.enabled {
  background: rgba(59, 130, 246, 0.1);
  border-color: rgba(59, 130, 246, 0.26);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.05),
    0 0 0 1px rgba(59, 130, 246, 0.08);
}

.field-span-2 {
  grid-column: 1 / -1;
}

.field-inline-toggle {
  align-self: stretch;
}

.field span,
.toggle-line span {
  font-size: 0.92rem;
  font-weight: 600;
}

input,
select,
textarea {
  width: 100%;
  box-sizing: border-box;
  border: 1px solid var(--page-border);
  border-radius: 14px;
  background: rgba(9, 11, 18, 0.74);
  color: var(--page-text);
  padding: 14px 16px;
  font-size: 0.95rem;
  outline: none;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.03);
  transition: border-color 0.18s ease, box-shadow 0.18s ease, background-color 0.18s ease;
}

input::placeholder,
textarea::placeholder {
  color: var(--page-text-tertiary);
}

textarea {
  resize: vertical;
  min-height: 112px;
  font-family: var(--font-mono);
}

input:focus,
select:focus,
textarea:focus {
  border-color: rgba(59, 130, 246, 0.42);
  box-shadow:
    0 0 0 3px rgba(59, 130, 246, 0.16),
    inset 0 1px 0 rgba(255, 255, 255, 0.04);
}

input:disabled,
select:disabled,
textarea:disabled {
  opacity: 0.48;
  cursor: not-allowed;
  background: rgba(255, 255, 255, 0.02);
  color: var(--page-text-tertiary);
}

input[type='checkbox'] {
  appearance: none;
  -webkit-appearance: none;
  width: 52px;
  height: 30px;
  padding: 0;
  margin: 0;
  border-radius: 999px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  background: rgba(255, 255, 255, 0.12);
  position: relative;
  cursor: pointer;
  transition: background-color 0.18s ease, border-color 0.18s ease;
}

input[type='checkbox']::before {
  content: '';
  position: absolute;
  top: 3px;
  left: 3px;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: #f3f8ff;
  box-shadow: 0 3px 10px rgba(0, 0, 0, 0.28);
  transition: transform 0.18s ease, background-color 0.18s ease;
}

input[type='checkbox']:checked {
  background: rgba(59, 130, 246, 0.4);
  border-color: rgba(59, 130, 246, 0.5);
}

input[type='checkbox']:checked::before {
  transform: translateX(22px);
  background: #dcedff;
}

.text-button,
.primary-button,
.secondary-button,
.danger-button {
  border: 1px solid transparent;
  border-radius: 999px;
  padding: 12px 18px;
  cursor: pointer;
  font-weight: 600;
  transition:
    background-color 0.18s ease,
    border-color 0.18s ease,
    color 0.18s ease,
    box-shadow 0.18s ease;
}

.text-button {
  background: rgba(255, 255, 255, 0.04);
  border-color: rgba(255, 255, 255, 0.08);
  color: var(--page-text);
}

.primary-button {
  background: linear-gradient(135deg, rgba(59, 130, 246, 0.96), rgba(37, 99, 235, 0.76));
  border-color: rgba(147, 197, 253, 0.24);
  box-shadow: 0 10px 22px rgba(59, 130, 246, 0.22);
  color: #fff;
}

.secondary-button {
  background: rgba(255, 255, 255, 0.04);
  border-color: rgba(255, 255, 255, 0.08);
  color: var(--page-text);
}

.danger-button {
  background: rgba(255, 107, 107, 0.12);
  border-color: rgba(255, 107, 107, 0.24);
  color: #ffc6c6;
}

.text-button:hover,
.secondary-button:hover {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.12);
}

.primary-button:hover {
  box-shadow: 0 14px 28px rgba(59, 130, 246, 0.28);
}

.danger-button:hover {
  background: rgba(255, 107, 107, 0.18);
}

.primary-button:disabled,
.secondary-button:disabled,
.danger-button:disabled,
.text-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.providers-shell {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.provider-card {
  border: 1px solid var(--page-border);
  border-radius: 20px;
  background: rgba(255, 255, 255, 0.03);
  padding: 18px;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px 16px;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.03);
}

.provider-head {
  grid-column: 1 / -1;
}

.tip-strip {
  margin-top: 16px;
  border-radius: 14px;
  padding: 12px 14px;
  background: rgba(255, 204, 0, 0.08);
  border: 1px solid rgba(255, 204, 0, 0.12);
  color: #ffe29f;
}

.json-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.json-item {
  border: 1px solid var(--page-border);
  border-radius: 18px;
  background: rgba(255, 255, 255, 0.03);
  overflow: hidden;
}

.json-item summary {
  list-style: none;
  cursor: pointer;
  padding: 16px 18px;
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: center;
  transition: background-color 0.18s ease;
}

.json-item summary:hover {
  background: rgba(255, 255, 255, 0.03);
}

.json-item summary::-webkit-details-marker {
  display: none;
}

.json-body {
  padding: 0 18px 18px;
}

.json-error {
  margin: 8px 0 0;
  color: #ffc0c0;
}

.sticky-footer {
  position: fixed;
  left: 50%;
  bottom: 18px;
  transform: translateX(-50%);
  width: min(1320px, calc(100vw - 48px));
  padding: 16px 20px;
  background: rgba(10, 12, 20, 0.86);
  border: 1px solid var(--page-border-strong);
  border-radius: 22px;
  backdrop-filter: blur(18px) saturate(160%);
  -webkit-backdrop-filter: blur(18px) saturate(160%);
  box-shadow:
    0 20px 40px rgba(0, 0, 0, 0.38),
    inset 0 1px 0 rgba(255, 255, 255, 0.08);
  z-index: 30;
}

.footer-copy {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.footer-copy strong {
  font-size: 0.98rem;
}

@media (max-width: 1100px) {
  .hero-main,
  .hero-metrics,
  .form-grid,
  .mapping-grid,
  .provider-card {
    grid-template-columns: 1fr;
  }

  .sticky-footer,
  .section-head,
  .provider-head {
    flex-direction: column;
    align-items: stretch;
  }

  .hero-main {
    grid-template-columns: 1fr;
  }

  .hero-avatar {
    width: 92px;
    height: 92px;
  }

  .footer-actions {
    width: 100%;
    justify-content: stretch;
    flex-wrap: wrap;
  }

  .footer-actions button {
    flex: 1;
  }
}

@media (max-width: 720px) {
  .settings-page {
    padding-bottom: 152px;
  }

  .hero-card,
  .form-card {
    border-radius: 20px;
  }

  .hero-card,
  .form-card,
  .sticky-footer {
    width: 100%;
  }

  .sticky-footer {
    width: calc(100vw - 24px);
    bottom: 12px;
    padding: 14px 16px;
  }
}
</style>
