<script setup lang="ts">
import { ref, computed } from 'vue'
import { SERVER_API } from '../../config/api'
import { ScreenshotService } from '../../services/ScreenshotService'
import ImageLightbox from './ImageLightbox.vue'

const screenshotService = new ScreenshotService()

// Panel visibility is controlled by parent via event; internal state for slide animation
const props = defineProps<{ visible: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const style = ref('')
const aspectRatio = ref('16:9')
const isGenerating = ref(false)
const resultImageSrc = ref<string | null>(null)
const errorMessage = ref<string | null>(null)
const lightboxSrc = ref<string | null>(null)

const aspectRatioOptions = ['16:9', '4:3', '1:1', '3:4', '9:16']

const canGenerate = computed(() => !isGenerating.value)

async function generate() {
  if (!canGenerate.value) return

  errorMessage.value = null
  resultImageSrc.value = null
  isGenerating.value = true

  try {
    // 1. 截取当前画布视图
    const dataUri = await screenshotService.captureCanvas()
    // captureCanvas 返回 data:image/png;base64,xxx，去掉前缀
    const screenshotBase64 = dataUri.includes(',') ? dataUri.split(',')[1] : dataUri

    // 2. 调用 envision plugin web_action
    const resp = await fetch(`${SERVER_API}/plugin-actions/envision/generate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        screenshotBase64,
        style: style.value.trim(),
        aspectRatio: aspectRatio.value,
      }),
    })

    if (!resp.ok) {
      const err = await resp.json().catch(() => ({}))
      throw new Error(err.error ?? `HTTP ${resp.status}`)
    }

    const data = await resp.json()
    if (data.error) throw new Error(data.error)

    resultImageSrc.value = `data:${data.mimeType ?? 'image/jpeg'};base64,${data.imageData}`
  } catch (err: any) {
    errorMessage.value = err?.message ?? '生成失败，请重试'
  } finally {
    isGenerating.value = false
  }
}

function openLightbox() {
  if (resultImageSrc.value) {
    lightboxSrc.value = resultImageSrc.value
  }
}

function closeLightbox() {
  lightboxSrc.value = null
}
</script>

<template>
  <Teleport to="body">
    <Transition name="panel-fade">
      <div v-if="props.visible" class="envision-panel" @mousedown.stop>
        <!-- Header -->
        <div class="panel-header">
          <button class="btn-close" @click="emit('close')" title="关闭">
            <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
              <path d="M1 1l12 12M13 1L1 13" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
            </svg>
          </button>
          <span class="panel-title">Envision</span>
          <span class="panel-subtitle">AI 效果图</span>
        </div>

        <!-- Controls -->
        <div class="panel-body">
          <div class="field">
            <label class="field-label">风格描述</label>
            <input
              v-model="style"
              class="field-input"
              type="text"
              placeholder="例：现代简约、北欧风、日式..."
              :disabled="isGenerating"
              @keydown.enter="generate"
            />
          </div>

          <div class="field">
            <label class="field-label">画面比例</label>
            <div class="ratio-group">
              <button
                v-for="r in aspectRatioOptions"
                :key="r"
                class="ratio-btn"
                :class="{ active: aspectRatio === r }"
                :disabled="isGenerating"
                @click="aspectRatio = r"
              >{{ r }}</button>
            </div>
          </div>

          <button
            class="btn-generate"
            :disabled="!canGenerate"
            @click="generate"
          >
            <span v-if="isGenerating" class="spinner" />
            <span>{{ isGenerating ? '生成中…' : '生成效果图' }}</span>
          </button>

          <!-- Error -->
          <p v-if="errorMessage" class="error-msg">{{ errorMessage }}</p>

          <!-- Result -->
          <div v-if="resultImageSrc" class="result-area">
            <img
              :src="resultImageSrc"
              class="result-img"
              alt="AI 效果图"
              @click="openLightbox"
              title="点击查看大图"
            />
          </div>
        </div>
      </div>
    </Transition>

    <ImageLightbox
      v-if="lightboxSrc"
      :src="lightboxSrc"
      @close="closeLightbox"
    />
  </Teleport>
</template>

<style scoped lang="scss">
.envision-panel {
  position: fixed;
  top: 80px;
  right: 320px;
  width: 280px;
  z-index: 300;
  border-radius: 12px;
  overflow: hidden;

  background: var(--glass-bg, rgba(20, 20, 24, 0.88));
  backdrop-filter: var(--glass-blur, blur(16px));
  -webkit-backdrop-filter: var(--glass-blur, blur(16px));
  border: var(--glass-border, 1px solid rgba(255,255,255,0.08));
  box-shadow: 0 8px 32px rgba(0,0,0,0.4);
}

.panel-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 14px 10px;
  border-bottom: 1px solid rgba(255,255,255,0.06);
}

.btn-close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border: none;
  background: transparent;
  color: var(--text-muted, rgba(255,255,255,0.4));
  border-radius: 6px;
  cursor: pointer;
  flex-shrink: 0;
  transition: background 0.15s, color 0.15s;

  &:hover {
    background: rgba(255,255,255,0.08);
    color: var(--text-primary, #fff);
  }
}

.panel-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary, #fff);
  letter-spacing: 0.02em;
}

.panel-subtitle {
  font-size: 11px;
  color: var(--text-muted, rgba(255,255,255,0.4));
  margin-left: auto;
}

.panel-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.field-label {
  font-size: 11px;
  color: var(--text-muted, rgba(255,255,255,0.5));
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.field-input {
  width: 100%;
  padding: 8px 10px;
  border-radius: 8px;
  border: 1px solid rgba(255,255,255,0.1);
  background: rgba(255,255,255,0.05);
  color: var(--text-primary, #fff);
  font-size: 13px;
  outline: none;
  transition: border-color 0.15s;
  box-sizing: border-box;

  &::placeholder {
    color: rgba(255,255,255,0.25);
  }

  &:focus {
    border-color: rgba(255,255,255,0.25);
  }

  &:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
}

.ratio-group {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.ratio-btn {
  padding: 4px 10px;
  border-radius: 6px;
  border: 1px solid rgba(255,255,255,0.12);
  background: transparent;
  color: var(--text-muted, rgba(255,255,255,0.5));
  font-size: 11px;
  cursor: pointer;
  transition: all 0.15s;

  &:hover:not(:disabled) {
    border-color: rgba(255,255,255,0.25);
    color: var(--text-primary, #fff);
  }

  &.active {
    border-color: rgba(255,255,255,0.4);
    background: rgba(255,255,255,0.08);
    color: var(--text-primary, #fff);
  }

  &:disabled {
    opacity: 0.4;
    cursor: not-allowed;
  }
}

.btn-generate {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  width: 100%;
  padding: 10px;
  border-radius: 8px;
  border: none;
  background: rgba(255,255,255,0.12);
  color: var(--text-primary, #fff);
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.15s;

  &:hover:not(:disabled) {
    background: rgba(255,255,255,0.18);
  }

  &:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
}

.spinner {
  width: 14px;
  height: 14px;
  border: 2px solid rgba(255,255,255,0.25);
  border-top-color: #fff;
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
  flex-shrink: 0;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.error-msg {
  font-size: 12px;
  color: #ff6b6b;
  margin: 0;
  padding: 8px 10px;
  background: rgba(255, 80, 80, 0.1);
  border-radius: 6px;
  border: 1px solid rgba(255,80,80,0.2);
}

.result-area {
  border-radius: 8px;
  overflow: hidden;
  border: 1px solid rgba(255,255,255,0.08);
}

.result-img {
  display: block;
  width: 100%;
  cursor: zoom-in;
  transition: opacity 0.2s;

  &:hover {
    opacity: 0.9;
  }
}

// Mount/unmount animation
.panel-fade-enter-active,
.panel-fade-leave-active {
  transition: opacity 0.2s ease, transform 0.2s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.panel-fade-enter-from,
.panel-fade-leave-to {
  opacity: 0;
  transform: translateY(-8px) scale(0.97);
}
</style>
