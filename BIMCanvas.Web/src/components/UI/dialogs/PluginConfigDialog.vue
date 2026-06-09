<script setup lang="ts">
import { ref, watch } from 'vue'
import GlassButton from '../base/GlassButton.vue'
import { SERVER_API } from '../../../config/api'

interface ConfigSchemaItem {
  key: string
  label: string
  description?: string
  secret?: boolean
  required?: boolean
}

const props = defineProps<{
  visible: boolean
  pluginId: string
  displayName: string
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'saved'): void
}>()

const schema = ref<ConfigSchemaItem[]>([])
const values = ref<Record<string, string>>({})
const loading = ref(false)
const saving = ref(false)
const errorMsg = ref<string | null>(null)
const savedOk = ref(false)

// 密码显示切换
const showSecret = ref<Record<string, boolean>>({})

async function load() {
  if (!props.pluginId) return
  loading.value = true
  errorMsg.value = null
  try {
    const resp = await fetch(`${SERVER_API}/plugins/${props.pluginId}/config`)
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`)
    const data = await resp.json()
    schema.value = data.schema ?? []
    // 初始化表单值：*** 替换为空（让用户重新输入 secret 字段）
    const loaded: Record<string, string> = {}
    for (const item of schema.value) {
      const raw = data.values?.[item.key]
      loaded[item.key] = (raw === '***' || raw == null) ? '' : raw
    }
    values.value = loaded
  } catch (err: any) {
    errorMsg.value = err?.message ?? '加载配置失败'
  } finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  errorMsg.value = null
  savedOk.value = false
  try {
    // 只提交非空值（避免用 "" 覆盖已有 secret）
    const payload: Record<string, string> = {}
    for (const item of schema.value) {
      const v = values.value[item.key] ?? ''
      if (v !== '') payload[item.key] = v
    }
    const resp = await fetch(`${SERVER_API}/plugins/${props.pluginId}/config`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
    if (!resp.ok) {
      const err = await resp.json().catch(() => ({}))
      throw new Error(err.message ?? `HTTP ${resp.status}`)
    }
    savedOk.value = true
    setTimeout(() => { savedOk.value = false }, 2000)
    emit('saved')
  } catch (err: any) {
    errorMsg.value = err?.message ?? '保存失败'
  } finally {
    saving.value = false
  }
}

watch(() => props.visible, (v) => {
  if (v) load()
}, { immediate: true })
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog-fade">
      <div v-if="props.visible" class="dialog-overlay" @mousedown.self="emit('close')">
        <div class="dialog-box" @mousedown.stop>
          <div class="dialog-header">
            <span class="dialog-title">配置 · {{ props.displayName }}</span>
            <button class="btn-close" @click="emit('close')">
              <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
                <path d="M1 1l12 12M13 1L1 13" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
              </svg>
            </button>
          </div>

          <div class="dialog-body">
            <div v-if="loading" class="state-msg">加载中…</div>
            <div v-else-if="schema.length === 0" class="state-msg muted">该插件暂无可配置项</div>

            <div v-else class="fields">
              <div v-for="item in schema" :key="item.key" class="field">
                <label class="field-label">
                  {{ item.label }}
                  <span v-if="item.required" class="required-mark">*</span>
                </label>
                <p v-if="item.description" class="field-desc">{{ item.description }}</p>
                <div class="input-wrap">
                  <input
                    v-model="values[item.key]"
                    class="field-input"
                    :type="item.secret && !showSecret[item.key] ? 'password' : 'text'"
                    :placeholder="item.secret ? '留空保留现有值' : ''"
                    :disabled="saving"
                  />
                  <button
                    v-if="item.secret"
                    class="btn-toggle-secret"
                    @click="showSecret[item.key] = !showSecret[item.key]"
                    :title="showSecret[item.key] ? '隐藏' : '显示'"
                  >
                    <svg v-if="!showSecret[item.key]" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                    </svg>
                    <svg v-else width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                      <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/>
                    </svg>
                  </button>
                </div>
              </div>
            </div>

            <p v-if="errorMsg" class="error-msg">{{ errorMsg }}</p>
            <p v-if="savedOk" class="success-msg">已保存，下次调用时自动生效</p>
          </div>

          <div class="dialog-footer">
            <GlassButton variant="secondary" @click="emit('close')">取消</GlassButton>
            <GlassButton
              variant="primary"
              :disabled="loading || saving || schema.length === 0"
              @click="save"
            >
              {{ saving ? '保存中…' : '保存' }}
            </GlassButton>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped lang="scss">
.dialog-overlay {
  position: fixed;
  inset: 0;
  z-index: 500;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
}

.dialog-box {
  width: 420px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  border-radius: 14px;
  overflow: hidden;

  background: var(--glass-bg, rgba(20, 20, 24, 0.95));
  backdrop-filter: var(--glass-blur, blur(20px));
  -webkit-backdrop-filter: var(--glass-blur, blur(20px));
  border: var(--glass-border, 1px solid rgba(255, 255, 255, 0.1));
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
}

.dialog-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 18px 14px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  flex-shrink: 0;
}

.dialog-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary, #fff);
}

.btn-close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border: none;
  background: transparent;
  color: rgba(255, 255, 255, 0.4);
  border-radius: 6px;
  cursor: pointer;
  transition: background 0.15s, color 0.15s;

  &:hover {
    background: rgba(255, 255, 255, 0.08);
    color: #fff;
  }
}

.dialog-body {
  flex: 1;
  overflow-y: auto;
  padding: 18px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.state-msg {
  font-size: 13px;
  color: var(--text-primary, #fff);
  text-align: center;
  padding: 20px 0;

  &.muted {
    color: rgba(255, 255, 255, 0.4);
  }
}

.fields {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.field-label {
  font-size: 12px;
  font-weight: 500;
  color: rgba(255, 255, 255, 0.7);
  letter-spacing: 0.02em;
}

.required-mark {
  color: #ff6b6b;
  margin-left: 2px;
}

.field-desc {
  font-size: 11px;
  color: rgba(255, 255, 255, 0.35);
  margin: 0;
  line-height: 1.4;
}

.input-wrap {
  position: relative;
  display: flex;
  align-items: center;
}

.field-input {
  width: 100%;
  padding: 8px 36px 8px 10px;
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  background: rgba(255, 255, 255, 0.05);
  color: var(--text-primary, #fff);
  font-size: 13px;
  font-family: monospace;
  outline: none;
  box-sizing: border-box;
  transition: border-color 0.15s;

  &::placeholder {
    color: rgba(255, 255, 255, 0.2);
    font-family: inherit;
  }

  &:focus {
    border-color: rgba(255, 255, 255, 0.25);
  }

  &:disabled {
    opacity: 0.5;
  }
}

.btn-toggle-secret {
  position: absolute;
  right: 8px;
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.35);
  cursor: pointer;
  padding: 2px;
  display: flex;
  align-items: center;
  transition: color 0.15s;

  &:hover {
    color: rgba(255, 255, 255, 0.7);
  }
}

.error-msg {
  font-size: 12px;
  color: #ff6b6b;
  margin: 0;
  padding: 8px 10px;
  background: rgba(255, 80, 80, 0.1);
  border-radius: 6px;
  border: 1px solid rgba(255, 80, 80, 0.2);
}

.success-msg {
  font-size: 12px;
  color: #6bffb8;
  margin: 0;
  padding: 8px 10px;
  background: rgba(80, 255, 160, 0.08);
  border-radius: 6px;
  border: 1px solid rgba(80, 255, 160, 0.15);
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 14px 18px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
  flex-shrink: 0;
}

.dialog-fade-enter-active,
.dialog-fade-leave-active {
  transition: opacity 0.2s ease;
  .dialog-box {
    transition: transform 0.2s cubic-bezier(0.34, 1.56, 0.64, 1), opacity 0.2s ease;
  }
}

.dialog-fade-enter-from,
.dialog-fade-leave-to {
  opacity: 0;
  .dialog-box {
    transform: scale(0.95) translateY(-10px);
    opacity: 0;
  }
}
</style>
