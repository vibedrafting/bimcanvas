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

const createDraft = (): GroupDraft => ({
  title: '',
  sourceFile: '',
  values: {},
  jsonText: '{}',
  jsonError: null
})

const drafts = reactive<Record<SettingsGroupKey, GroupDraft>>({
  server: createDraft(),
  web: createDraft(),
  agent: createDraft(),
  ccr: createDraft()
})

const runtime = ref<SettingsRuntime>({ ...defaultRuntime })
const isLoading = ref(false)
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
  runtime.value = snapshot.runtime ?? { ...defaultRuntime }
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
            <p>
              当前生效默认模型：<strong>{{ effectiveDefaultModel || '未设置' }}</strong>
              <span class="path-hint">真源字段：{{ displayEffectiveModelPath }}</span>
            </p>
          </div>
        </div>
      </header>

      <div v-if="loadError" class="notice error">{{ loadError }}</div>
      <div v-if="saveError" class="notice error">{{ saveError }}</div>
      <div v-if="saveMessage" class="notice info">{{ saveMessage }}</div>
      <div v-if="restartPendingGroups.length > 0" class="notice warn">
        已修改高影响配置：{{ restartPendingGroups.join(' / ') }}。保存后请完成一次实例重启。
      </div>

      <div v-if="isLoading" class="loading-state">正在加载实例配置...</div>

      <template v-else>
        <section class="form-card">
          <div class="section-head">
            <div>
              <h3>运行方式</h3>
              <p>决定默认模型由哪一组配置控制，并影响重启后的真实行为。</p>
            </div>
            <label class="toggle-line">
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
                    <p>{{ provider.transformer?.use?.join(', ') || '未配置 transformer' }}</p>
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
            <label class="field toggle-line field-inline-toggle">
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
  --page-bg: #f5f1e8;
  --page-surface: #ffffff;
  --page-surface-soft: #f8f6f1;
  --page-border: #e7dfd2;
  --page-text: #23262e;
  --page-text-secondary: #7a746a;
  --page-accent: #2f6df6;
  --page-accent-soft: rgba(47, 109, 246, 0.08);
  --page-success: #2b8a5a;
  --page-warn: #c68a1d;
  --page-danger: #c45f43;
  color: var(--page-text);
  min-height: 100%;
  padding: 12px 0 96px;
}

.settings-shell {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.hero-card,
.form-card {
  background: var(--page-surface);
  border: 1px solid var(--page-border);
  border-radius: 28px;
  box-shadow: 0 18px 50px rgba(52, 42, 25, 0.06);
}

.hero-card {
  padding: 18px 22px 24px;
}

.back-chip {
  border: none;
  background: var(--page-surface-soft);
  color: var(--page-text);
  height: 42px;
  padding: 0 16px;
  border-radius: 999px;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}

.back-chip svg {
  width: 16px;
  height: 16px;
}

.hero-main {
  display: grid;
  grid-template-columns: 92px 1fr;
  gap: 20px;
  align-items: center;
  margin-top: 18px;
}

.hero-avatar {
  width: 92px;
  height: 92px;
  border-radius: 28px;
  display: grid;
  place-items: center;
  background: linear-gradient(180deg, #fbfaf6, #f1ece2);
  border: 1px solid var(--page-border);
  font-size: 2rem;
  font-weight: 700;
  color: #6d6b64;
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
  color: var(--page-accent);
}

.provider-badge,
.effective-path {
  background: #f2eee5;
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
  font-size: 2rem;
  line-height: 1.1;
}

.path-hint {
  margin-left: 12px;
}

.notice {
  border-radius: 18px;
  padding: 14px 16px;
  border: 1px solid var(--page-border);
  background: var(--page-surface);
}

.notice.error {
  color: var(--page-danger);
  border-color: rgba(196, 95, 67, 0.22);
  background: rgba(196, 95, 67, 0.08);
}

.notice.info {
  color: var(--page-accent);
  border-color: rgba(47, 109, 246, 0.22);
  background: rgba(47, 109, 246, 0.08);
}

.notice.warn {
  color: var(--page-warn);
  border-color: rgba(198, 138, 29, 0.22);
  background: rgba(198, 138, 29, 0.08);
}

.loading-state {
  padding: 72px 0;
  text-align: center;
}

.form-card {
  padding: 22px;
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

.field,
.toggle-line {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.field-span-2 {
  grid-column: 1 / -1;
}

.field-inline-toggle {
  justify-content: flex-end;
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
  background: #fff;
  color: var(--page-text);
  padding: 14px 16px;
  font-size: 0.95rem;
  outline: none;
  transition: border-color 0.18s ease, box-shadow 0.18s ease;
}

textarea {
  resize: vertical;
  min-height: 112px;
  font-family: var(--font-mono);
}

input:focus,
select:focus,
textarea:focus {
  border-color: rgba(47, 109, 246, 0.4);
  box-shadow: 0 0 0 3px rgba(47, 109, 246, 0.12);
}

input[type='checkbox'] {
  width: 44px;
  height: 24px;
  padding: 0;
  accent-color: var(--page-accent);
}

.text-button,
.primary-button,
.secondary-button,
.danger-button {
  border: none;
  border-radius: 999px;
  padding: 12px 18px;
  cursor: pointer;
  font-weight: 600;
}

.text-button {
  background: #f2eee5;
  color: var(--page-text);
}

.primary-button {
  background: linear-gradient(135deg, #2f6df6, #4a8bff);
  color: #fff;
}

.secondary-button {
  background: #ece7db;
  color: var(--page-text);
}

.danger-button {
  background: #ffe5df;
  color: var(--page-danger);
}

.primary-button:disabled,
.secondary-button:disabled,
.danger-button:disabled {
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
  background: var(--page-surface-soft);
  padding: 18px;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px 16px;
}

.provider-head {
  grid-column: 1 / -1;
}

.tip-strip {
  margin-top: 16px;
  border-radius: 14px;
  padding: 12px 14px;
  background: rgba(198, 138, 29, 0.08);
  color: var(--page-warn);
}

.json-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.json-item {
  border: 1px solid var(--page-border);
  border-radius: 18px;
  background: var(--page-surface-soft);
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
}

.json-item summary::-webkit-details-marker {
  display: none;
}

.json-body {
  padding: 0 18px 18px;
}

.json-error {
  margin: 8px 0 0;
  color: var(--page-danger);
}

.sticky-footer {
  position: fixed;
  left: 0;
  right: 0;
  bottom: 0;
  padding: 14px 24px;
  background: rgba(247, 243, 235, 0.94);
  border-top: 1px solid rgba(220, 212, 199, 0.95);
  backdrop-filter: blur(14px);
  z-index: 30;
}

.footer-copy {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

@media (max-width: 1100px) {
  .hero-main,
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

  .footer-actions {
    width: 100%;
    justify-content: stretch;
  }

  .footer-actions button {
    flex: 1;
  }
}
</style>
