<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import GlassButton from './base/GlassButton.vue'
import { SettingsService } from '../../services/SettingsService'
import type {
  SettingsApplyMode,
  SettingsFieldMeta,
  SettingsGroup,
  SettingsGroupKey,
  SettingsSnapshot
} from '../../types/settings'

interface GroupDraft {
  title: string
  sourceFile: string
  applyMode: SettingsApplyMode
  values: Record<string, any>
  fields: SettingsFieldMeta[]
  jsonText: string
  jsonError: string | null
  showAdvanced: boolean
  showSecrets: boolean
}

const props = defineProps<{ visible: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const keys: SettingsGroupKey[] = ['server', 'web', 'agent', 'ccr']
const emptyGroup = (): GroupDraft => ({
  title: '',
  sourceFile: '',
  applyMode: 'restart',
  values: {},
  fields: [],
  jsonText: '{}',
  jsonError: null,
  showAdvanced: false,
  showSecrets: false
})

const drafts = reactive<Record<SettingsGroupKey, GroupDraft>>({
  server: emptyGroup(),
  web: emptyGroup(),
  agent: emptyGroup(),
  ccr: emptyGroup()
})

const isLoading = ref(false)
const isSaving = ref(false)
const isRestarting = ref(false)
const loadError = ref<string | null>(null)
const saveError = ref<string | null>(null)
const saveMessage = ref<string | null>(null)
const restartPendingGroups = ref<string[]>([])

watch(() => props.visible, async (visible) => {
  if (visible) await loadSettings()
})

for (const key of keys) {
  watch(() => drafts[key].values, value => {
    if (!drafts[key].jsonError) drafts[key].jsonText = pretty(value)
  }, { deep: true })
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value))
}

function pretty(value: unknown) {
  return JSON.stringify(value, null, 2)
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
  }

  if (group === 'agent') {
    value.baseUrl ??= ''
    value.apiKey ??= ''
    value.model ??= ''
    value.defaultEffort ??= 'medium'
    value.defaultThinking ??= 'adaptive'
    value.maxThinkingTokens ??= 8000
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
    value.Providers = Array.isArray(value.Providers) ? value.Providers : []
  }

  return value
}

function applyGroup(key: SettingsGroupKey, group: SettingsGroup) {
  drafts[key].title = group.title
  drafts[key].sourceFile = group.sourceFile
  drafts[key].applyMode = group.applyMode
  drafts[key].fields = group.fields ?? []
  drafts[key].jsonError = null
  drafts[key].values = normalize(key, group.values)
  drafts[key].jsonText = pretty(drafts[key].values)
}

function applySnapshot(snapshot: SettingsSnapshot) {
  applyGroup('server', snapshot.server)
  applyGroup('web', snapshot.web)
  applyGroup('agent', snapshot.agent)
  applyGroup('ccr', snapshot.ccr)
}

async function loadSettings() {
  isLoading.value = true
  loadError.value = null
  try {
    applySnapshot(await SettingsService.getSettings())
    restartPendingGroups.value = []
  } catch (error: any) {
    loadError.value = error.response?.data?.message || error.message || '加载配置失败'
  } finally {
    isLoading.value = false
  }
}

function effectLabel(mode: SettingsApplyMode) {
  return mode === 'immediate' ? '即时生效' : '需重启'
}

function maskSecret(value: string) {
  if (!value) return '未设置'
  if (value.length <= 8) return '********'
  return `${value.slice(0, 3)}••••${value.slice(-3)}`
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
    drafts[group].jsonText = pretty(drafts[group].values)
    return true
  } catch (error: any) {
    drafts[group].jsonError = error.message || 'JSON 解析失败'
    return false
  }
}

function textToLines(text: string) {
  return text.split(/\r?\n/).map(item => item.trim()).filter(Boolean)
}

function setModelLines(text: string) {
  drafts.web.values.customModels = textToLines(text).map(id => ({ id, label: id }))
}

function modelLines() {
  return drafts.web.values.customModels.map((item: { id?: string }) => item.id ?? '').join('\n')
}

function setLayerLines(target: string[], text: string) {
  target.splice(0, target.length, ...textToLines(text))
}

function handleModelInput(event: Event) {
  setModelLines((event.target as HTMLTextAreaElement).value)
}

function handleUserLayersInput(event: Event) {
  setLayerLines(drafts.web.values.layerPresets.User.enabledLayers, (event.target as HTMLTextAreaElement).value)
}

function handleAgentLayersInput(event: Event) {
  setLayerLines(drafts.web.values.layerPresets.Agent.enabledLayers, (event.target as HTMLTextAreaElement).value)
}

function providerModels(provider: any) {
  return Array.isArray(provider.models) ? provider.models.join(', ') : ''
}

function setProviderModels(provider: any, text: string) {
  provider.models = text.split(',').map((item: string) => item.trim()).filter(Boolean)
}

function handleProviderModelsInput(provider: any, event: Event) {
  setProviderModels(provider, (event.target as HTMLInputElement).value)
}

async function handleSave() {
  saveError.value = null
  saveMessage.value = null

  for (const key of keys) {
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
      ? `已保存，${result.restartRequiredGroups.join(' / ')} 将在重启后生效。`
      : '已保存，Web 配置已即时生效。'

    window.dispatchEvent(new CustomEvent('bimcanvas:web-config-updated', {
      detail: clone(result.settings.web.values)
    }))
  } catch (error: any) {
    saveError.value = error.response?.data?.message || error.message || '保存失败'
  } finally {
    isSaving.value = false
  }
}

async function handleRestart() {
  isRestarting.value = true
  try {
    const result = await SettingsService.restartInstance()
    saveMessage.value = result.message
  } catch (error: any) {
    saveError.value = error.response?.data?.message || error.message || '重启失败'
  } finally {
    isRestarting.value = false
  }
}
</script>

<template>
  <Teleport to="body">
    <Transition name="settings-shell">
      <div v-if="visible" class="settings-shell" @click.self="emit('close')">
        <section class="settings-panel">
          <header class="panel-header">
            <div>
              <p class="kicker">Docker Instance Console</p>
              <h2>实例设置</h2>
              <p class="subtitle">首页内统一管理四份实例级 JSON 配置，离开首页后不再显示入口。</p>
            </div>
            <GlassButton variant="ghost" @click="emit('close')">关闭</GlassButton>
          </header>

          <div class="toolbar">
            <div class="status-pills">
              <span class="pill pill-hot">Web 即时生效</span>
              <span class="pill pill-restart">Server / Agent / CCR 需重启</span>
            </div>
            <div class="toolbar-actions">
              <GlassButton variant="ghost" :disabled="isLoading" @click="loadSettings">刷新</GlassButton>
              <GlassButton variant="primary" :disabled="isSaving || isLoading" @click="handleSave">
                {{ isSaving ? '保存中...' : '保存配置' }}
              </GlassButton>
            </div>
          </div>

          <div v-if="loadError" class="notice notice-error">{{ loadError }}</div>
          <div v-if="saveError" class="notice notice-error">{{ saveError }}</div>
          <div v-if="saveMessage" class="notice notice-info">{{ saveMessage }}</div>
          <div v-if="restartPendingGroups.length > 0" class="notice notice-warning">
            已修改高影响配置：{{ restartPendingGroups.join(' / ') }}。重启后当前连接可能中断。
            <GlassButton variant="danger" class="inline-btn" :disabled="isRestarting" @click="handleRestart">
              {{ isRestarting ? '重启中...' : '重启实例' }}
            </GlassButton>
          </div>

          <div v-if="isLoading" class="loading">正在读取实例配置...</div>

          <div v-else class="content">
            <section class="card">
              <div class="card-head">
                <div><h3>Server</h3><p>{{ drafts.server.sourceFile }}</p></div>
                <span class="pill pill-restart">{{ effectLabel(drafts.server.applyMode) }}</span>
              </div>
              <div class="grid">
                <label><span>Agent 端口</span><input v-model.number="drafts.server.values.server.port" type="number"></label>
                <label><span>Python 命令</span><input v-model="drafts.server.values.server.pythonCommand" type="text"></label>
                <label class="switch"><span>自动打开浏览器</span><input v-model="drafts.server.values.startup.openBrowser" type="checkbox"></label>
                <label><span>浏览器路径</span><input v-model="drafts.server.values.startup.browserPath" type="text"></label>
                <label class="switch"><span>启用 CCR</span><input v-model="drafts.server.values.ccr.enabled" type="checkbox"></label>
                <label class="switch"><span>自动启动 CCR</span><input v-model="drafts.server.values.ccr.autoStart" type="checkbox"></label>
                <label><span>CCR Host</span><input v-model="drafts.server.values.ccr.host" type="text"></label>
                <label><span>CCR Port</span><input v-model.number="drafts.server.values.ccr.port" type="number"></label>
                <label><span>默认模型家族</span><input v-model="drafts.server.values.ccr.defaultModelFamily" type="text"></label>
              </div>
            </section>

            <section class="card">
              <div class="card-head">
                <div><h3>Web</h3><p>{{ drafts.web.sourceFile }}</p></div>
                <span class="pill pill-hot">{{ effectLabel(drafts.web.applyMode) }}</span>
              </div>
              <div class="stack">
                <label>
                  <span>自定义模型列表</span>
                  <textarea :value="modelLines()" rows="5" @input="handleModelInput" />
                </label>
                <label>
                  <span>User 图层预设</span>
                  <textarea :value="drafts.web.values.layerPresets.User.enabledLayers.join('\n')" rows="4" @input="handleUserLayersInput" />
                </label>
                <label>
                  <span>Agent 图层预设</span>
                  <textarea :value="drafts.web.values.layerPresets.Agent.enabledLayers.join('\n')" rows="4" @input="handleAgentLayersInput" />
                </label>
              </div>
            </section>

            <section class="card">
              <div class="card-head">
                <div><h3>Agent</h3><p>{{ drafts.agent.sourceFile }}</p></div>
                <div class="head-actions">
                  <GlassButton variant="ghost" @click="drafts.agent.showSecrets = !drafts.agent.showSecrets">
                    {{ drafts.agent.showSecrets ? '隐藏密钥' : '显示密钥' }}
                  </GlassButton>
                  <span class="pill pill-restart">{{ effectLabel(drafts.agent.applyMode) }}</span>
                </div>
              </div>
              <div class="grid">
                <label><span>网关地址</span><input v-model="drafts.agent.values.baseUrl" type="text"></label>
                <label><span>默认模型</span><input v-model="drafts.agent.values.model" type="text"></label>
                <label><span>默认 Effort</span><input v-model="drafts.agent.values.defaultEffort" type="text"></label>
                <label><span>默认 Thinking</span><input v-model="drafts.agent.values.defaultThinking" type="text"></label>
                <label><span>最大 Thinking Tokens</span><input v-model.number="drafts.agent.values.maxThinkingTokens" type="number"></label>
                <label><span>监听 Host</span><input v-model="drafts.agent.values.server.host" type="text"></label>
                <label><span>监听 Port</span><input v-model.number="drafts.agent.values.server.port" type="number"></label>
                <label>
                  <span>API Key</span>
                  <input v-model="drafts.agent.values.apiKey" :type="drafts.agent.showSecrets ? 'text' : 'password'">
                  <small>{{ drafts.agent.showSecrets ? '当前显示明文' : maskSecret(drafts.agent.values.apiKey) }}</small>
                </label>
              </div>
            </section>

            <section class="card">
              <div class="card-head">
                <div><h3>CCR</h3><p>{{ drafts.ccr.sourceFile }}</p></div>
                <div class="head-actions">
                  <GlassButton variant="ghost" @click="drafts.ccr.showSecrets = !drafts.ccr.showSecrets">
                    {{ drafts.ccr.showSecrets ? '隐藏密钥' : '显示密钥' }}
                  </GlassButton>
                  <span class="pill pill-restart">{{ effectLabel(drafts.ccr.applyMode) }}</span>
                </div>
              </div>
              <div class="grid">
                <label><span>HOST</span><input v-model="drafts.ccr.values.HOST" type="text"></label>
                <label><span>PORT</span><input v-model.number="drafts.ccr.values.PORT" type="number"></label>
                <label class="switch"><span>LOG</span><input v-model="drafts.ccr.values.LOG" type="checkbox"></label>
                <label><span>LOG_LEVEL</span><input v-model="drafts.ccr.values.LOG_LEVEL" type="text"></label>
                <label><span>API_TIMEOUT_MS</span><input v-model.number="drafts.ccr.values.API_TIMEOUT_MS" type="number"></label>
                <label><span>Router.default</span><input v-model="drafts.ccr.values.Router.default" type="text"></label>
                <label><span>Router.think</span><input v-model="drafts.ccr.values.Router.think" type="text"></label>
                <label><span>Router.background</span><input v-model="drafts.ccr.values.Router.background" type="text"></label>
                <label><span>Router.longContext</span><input v-model="drafts.ccr.values.Router.longContext" type="text"></label>
              </div>
              <div v-if="drafts.ccr.values.Providers.length > 0" class="providers">
                <article v-for="(provider, index) in drafts.ccr.values.Providers" :key="provider.name || index" class="provider">
                  <strong>{{ provider.name || `Provider ${index + 1}` }}</strong>
                  <p>{{ provider.api_base_url || '未配置 base url' }}</p>
                  <p>{{ drafts.ccr.showSecrets ? (provider.api_key || '未设置') : maskSecret(provider.api_key || '') }}</p>
                  <input :value="providerModels(provider)" type="text" @input="handleProviderModelsInput(provider, $event)">
                </article>
              </div>
            </section>

            <section v-for="key in keys" :key="`${key}-json`" class="card">
              <div class="card-head">
                <div><h3>{{ drafts[key].title }} 高级 JSON</h3><p>{{ drafts[key].sourceFile }}</p></div>
                <GlassButton variant="ghost" @click="drafts[key].showAdvanced = !drafts[key].showAdvanced">
                  {{ drafts[key].showAdvanced ? '收起' : '展开' }}
                </GlassButton>
              </div>
              <div v-if="drafts[key].showAdvanced" class="stack">
                <textarea v-model="drafts[key].jsonText" rows="14" spellcheck="false" @blur="parseJson(key)" />
                <p v-if="drafts[key].jsonError" class="json-error">{{ drafts[key].jsonError }}</p>
              </div>
            </section>
          </div>
        </section>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.settings-shell { position: fixed; inset: 0; z-index: 3000; display: flex; justify-content: flex-end; background: radial-gradient(circle at top right, rgba(59,130,246,.18), transparent 30%), rgba(5,8,16,.72); backdrop-filter: blur(16px); }
.settings-panel { width: min(920px, 100vw); height: 100vh; overflow: auto; box-sizing: border-box; padding: 28px; background: linear-gradient(180deg, rgba(8,16,28,.96), rgba(10,12,18,.96)); border-left: 1px solid rgba(255,255,255,.08); box-shadow: -24px 0 80px rgba(0,0,0,.35); }
.panel-header, .toolbar, .card-head, .toolbar-actions, .head-actions { display: flex; justify-content: space-between; gap: 12px; }
.panel-header { align-items: flex-start; margin-bottom: 20px; }
.kicker, .subtitle, .card p, .provider p, small, .json-error { margin: 0; color: var(--text-secondary); }
.kicker { text-transform: uppercase; letter-spacing: .22em; font-size: .72rem; color: rgba(125,211,252,.8); }
.panel-header h2, .card h3 { margin: 6px 0 0; }
.toolbar, .card { padding: 16px; border-radius: 16px; border: 1px solid rgba(255,255,255,.08); background: rgba(255,255,255,.03); }
.toolbar { margin-bottom: 16px; }
.status-pills { display: flex; flex-wrap: wrap; gap: 8px; }
.pill { display: inline-flex; align-items: center; padding: 6px 10px; border-radius: 999px; font-size: .78rem; border: 1px solid rgba(255,255,255,.08); }
.pill-hot { background: rgba(34,197,94,.12); color: #9ff6bf; }
.pill-restart { background: rgba(245,158,11,.12); color: #ffd48d; }
.notice { margin-bottom: 12px; padding: 12px 14px; border-radius: 12px; line-height: 1.6; }
.notice-error { background: rgba(255,107,107,.12); border: 1px solid rgba(255,107,107,.25); color: #ff8f8f; }
.notice-info { background: rgba(59,130,246,.12); border: 1px solid rgba(59,130,246,.25); color: #9dc4ff; }
.notice-warning { background: rgba(245,158,11,.12); border: 1px solid rgba(245,158,11,.25); color: #ffd48d; }
.inline-btn { margin-left: 12px; }
.loading { padding: 72px 0; text-align: center; color: var(--text-secondary); }
.content { display: flex; flex-direction: column; gap: 16px; }
.grid { margin-top: 16px; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
.stack { margin-top: 16px; display: flex; flex-direction: column; gap: 12px; }
label, .provider { display: flex; flex-direction: column; gap: 8px; }
.switch { justify-content: space-between; }
label span { color: var(--text-primary); font-size: .88rem; }
input, textarea { width: 100%; box-sizing: border-box; border-radius: 12px; border: 1px solid rgba(255,255,255,.08); background: rgba(6,10,18,.72); color: var(--text-primary); padding: 12px 14px; outline: none; font-size: .92rem; font-family: var(--font-sans); }
textarea { resize: vertical; min-height: 120px; font-family: var(--font-mono); }
input:focus, textarea:focus { border-color: rgba(125,211,252,.42); box-shadow: 0 0 0 3px rgba(59,130,246,.14); }
.providers { margin-top: 16px; display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }
.provider { padding: 14px; border-radius: 14px; background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.06); }
.json-error { color: #ff8f8f; }
.settings-shell-enter-active, .settings-shell-leave-active { transition: opacity .2s ease; }
.settings-shell-enter-from, .settings-shell-leave-to { opacity: 0; }
.settings-shell-enter-from .settings-panel, .settings-shell-leave-to .settings-panel { transform: translateX(32px); }
.settings-shell-enter-active .settings-panel, .settings-shell-leave-active .settings-panel { transition: transform .24s ease; }
@media (max-width: 900px) { .grid { grid-template-columns: 1fr; } .panel-header, .toolbar { flex-direction: column; } }
</style>
