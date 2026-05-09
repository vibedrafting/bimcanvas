<script setup lang="ts">
import GlassButton from './base/GlassButton.vue';

defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  (e: 'select', format: 'snapshot' | 'bcp'): void;
  (e: 'cancel'): void;
}>();
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog">
      <div v-if="visible" class="dialog-overlay" @click.self="emit('cancel')">
        <div class="dialog-card">
          <div class="dialog-header">
            <h3>选择导出格式</h3>
            <button class="close-btn" type="button" @click="emit('cancel')">
              <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>

          <div class="format-list">
            <button class="format-card" type="button" @click="emit('select', 'snapshot')">
              <div class="format-icon">
                <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="1.8">
                  <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                  <polyline points="17 8 12 3 7 8"></polyline>
                  <line x1="12" y1="3" x2="12" y2="15"></line>
                </svg>
              </div>
              <div class="format-copy">
                <span class="format-title">Snapshot JSON</span>
                <span class="format-desc">用于 Standalone 导入，包含当前 Web 视图数据。</span>
              </div>
            </button>

            <button class="format-card" type="button" @click="emit('select', 'bcp')">
              <div class="format-icon">
                <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="1.8">
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                  <polyline points="14 2 14 8 20 8"></polyline>
                  <path d="M12 18v-6"></path>
                  <path d="M9 15l3 3 3-3"></path>
                </svg>
              </div>
              <div class="format-copy">
                <span class="format-title">BCP 项目文件</span>
                <span class="format-desc">用于 Connected 项目归档，保持 Server 项目格式。</span>
              </div>
            </button>
          </div>

          <div class="dialog-footer">
            <GlassButton variant="ghost" @click="emit('cancel')">取消</GlassButton>
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
  background: rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2600;
}

.dialog-card {
  width: 420px;
  background: var(--glass-bg-solid, #18181b);
  border: 1px solid var(--border-subtle);
  border-radius: 10px;
  box-shadow: 0 16px 42px rgba(0, 0, 0, 0.42);
  overflow: hidden;
}

.dialog-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px 18px 12px;

  h3 {
    margin: 0;
    flex: 1;
    color: var(--text-primary);
    font-size: 1rem;
    font-weight: 600;
  }
}

.close-btn {
  width: 30px;
  height: 30px;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;

  &:hover {
    background: rgba(255, 255, 255, 0.08);
    color: var(--text-primary);
  }
}

.format-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 4px 18px 18px;
}

.format-card {
  width: 100%;
  min-height: 72px;
  border: 1px solid var(--border-subtle);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  color: var(--text-primary);
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px;
  text-align: left;
  cursor: pointer;

  &:hover {
    border-color: var(--accent-blue);
    background: rgba(59, 130, 246, 0.12);
  }
}

.format-icon {
  width: 38px;
  height: 38px;
  border-radius: 8px;
  background: rgba(59, 130, 246, 0.14);
  color: var(--accent-blue);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.format-copy {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.format-title {
  font-size: 0.92rem;
  font-weight: 600;
}

.format-desc {
  color: var(--text-secondary);
  font-size: 0.8rem;
  line-height: 1.35;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  padding: 14px 18px;
  border-top: 1px solid var(--border-subtle);
  background: rgba(0, 0, 0, 0.08);
}

.dialog-enter-active,
.dialog-leave-active {
  transition: opacity 0.18s ease;

  .dialog-card {
    transition: transform 0.18s ease, opacity 0.18s ease;
  }
}

.dialog-enter-from,
.dialog-leave-to {
  opacity: 0;

  .dialog-card {
    transform: translateY(8px) scale(0.98);
    opacity: 0;
  }
}
</style>
