<script setup lang="ts">
/**
 * Plugin 管理面板顶层入口
 *
 * 已安装列表 + [+ 安装新插件] 按钮 + 行内按钮状态机驱动 + 重启 banner。
 *
 * 状态机(主真理源 v1.1 §4.3 / R1 R2 R3 R5):
 *   - trustState=untrusted          → 显示 [信任并激活] (触发 TrustAndActivateDialog)
 *   - trustState=trusted + 非 active → 显示 [设为激活] (直接调,已 trusted)
 *   - trustState=trusted + active   → 显示 [已激活] 徽章,无操作
 *   - 所有状态                       → 显示 [卸载] 按钮 (二次确认)
 */
import { onMounted, ref, computed, nextTick } from 'vue';
import GlassButton from './base/GlassButton.vue';
import InstallPluginDialog from './dialogs/InstallPluginDialog.vue';
import TrustAndActivateDialog from './dialogs/TrustAndActivateDialog.vue';
import PluginConfigDialog from './dialogs/PluginConfigDialog.vue';
import { usePluginStore } from '../../stores/pluginStore';
import type { PluginListItem } from '../../types/plugin';

const emit = defineEmits<{
  (e: 'close'): void;
}>();

const store = usePluginStore();

// Teleport 目标 (#plugins-header-actions) 在 HomePage 模板内, 必须等其挂载完成才可注入
const isMounted = ref(false);

// ─── Dialog 状态 ────────────────────────────────────────────────────

const showInstallDialog = ref(false);
const trustTarget = ref<PluginListItem | null>(null);
const uninstallTarget = ref<PluginListItem | null>(null);
const configTarget = ref<PluginListItem | null>(null);

// ─── 工具函数 ───────────────────────────────────────────────────────

const formatTime = (iso: string | null | undefined) => {
  if (!iso) return '--';
  try {
    const d = new Date(iso);
    return d.toLocaleString('zh-CN', {
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit',
    });
  } catch {
    return iso;
  }
};

const trustLabel = (state: string) => state === 'trusted' ? '已信任' : '未信任';
const sourceKindLabel = (k: string) => {
  if (k === 'github') return 'GitHub';
  if (k === 'local') return '本地';
  if (k === 'zip') return 'ZIP';
  return k;
};

const sortedPlugins = computed(() => {
  // active 在最前;其他按 installedAt desc
  return [...store.installedPlugins].sort((a, b) => {
    if (a.isActive && !b.isActive) return -1;
    if (!a.isActive && b.isActive) return 1;
    return new Date(b.installedAt).getTime() - new Date(a.installedAt).getTime();
  });
});

// ─── 操作 handlers ──────────────────────────────────────────────────

const onInstallClick = () => {
  showInstallDialog.value = true;
};

const onInstallConfirm = async (
  payload:
    | { source: 'github'; repoUrl: string; ref?: string | null }
    | { source: 'local'; path: string; link: boolean }
) => {
  showInstallDialog.value = false;
  if (payload.source === 'github') {
    await store.install({ sourceKind: 'github', repoUrl: payload.repoUrl, ref: payload.ref });
  } else {
    await store.install({ sourceKind: 'local', path: payload.path, link: payload.link });
  }
};

const onTrustAndActivateClick = (plugin: PluginListItem) => {
  trustTarget.value = plugin;
};

const onTrustConfirm = async () => {
  if (!trustTarget.value) return;
  const id = trustTarget.value.pluginId;
  trustTarget.value = null;
  await store.trustAndActivate(id);
};

const onSetActiveClick = async (plugin: PluginListItem) => {
  await store.setActive(plugin.pluginId);
};

const onUninstallClick = (plugin: PluginListItem) => {
  uninstallTarget.value = plugin;
};

const onUninstallConfirm = async () => {
  if (!uninstallTarget.value) return;
  const id = uninstallTarget.value.pluginId;
  uninstallTarget.value = null;
  await store.uninstall(id);
};

onMounted(async () => {
  await nextTick();
  isMounted.value = true;
  store.fetchAll();
});
</script>

<template>
  <div class="plugins-page">
    <!-- 操作按钮 Teleport 到 HomePage 顶栏右侧 (与 HomeSettingsPanel 一致) -->
    <Teleport to="#plugins-header-actions" v-if="isMounted">
      <div class="teleported-actions">
        <GlassButton variant="ghost" @click="store.fetchAll()" :disabled="store.loading" title="刷新列表">
          <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" :class="{ spinning: store.loading }">
            <polyline points="23 4 23 10 17 10" />
            <polyline points="1 20 1 14 7 14" />
            <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
          </svg>
        </GlassButton>
        <GlassButton variant="primary" @click="onInstallClick" style="display: flex; align-items: center; justify-content: center; gap: 6px;">
          <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 5v14" />
            <path d="M5 12h14" />
          </svg>
          安装新插件
        </GlassButton>
      </div>
    </Teleport>

    <div class="plugins-main">
      <div class="layout-bound wrapper-pad">
        <div class="page-intro mb-lg">
          <h1 class="page-title">插件管理</h1>
          <p class="page-desc">安装、信任、激活与卸载 BIMCanvas plugin。激活变更需重启实例后 Agent 才会加载新 plugin。</p>
        </div>

        <article class="config-card">
          <header class="card-header">
            <div class="heading-left">
              <svg class="heading-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="3" width="7" height="7" rx="1" />
                <rect x="14" y="3" width="7" height="7" rx="1" />
                <rect x="3" y="14" width="7" height="7" rx="1" />
                <rect x="14" y="14" width="7" height="7" rx="1" />
              </svg>
              <div class="heading-text">
                <h3>
                  已安装插件
                  <span v-if="store.hasPlugins" class="count-badge">{{ store.installedPlugins.length }}</span>
                </h3>
                <p>列出本机所有 plugin；行内按钮按 trust / active 状态自动切换。</p>
              </div>
            </div>
          </header>

          <div class="card-body">
            <!-- Loading -->
            <div v-if="store.loading && !store.hasPlugins" class="loading-state">加载插件列表...</div>

            <!-- Empty -->
            <div v-else-if="!store.hasPlugins" class="empty-state">
              <svg viewBox="0 0 24 24" width="44" height="44" fill="none" stroke="currentColor" stroke-width="1.5" opacity="0.3">
                <rect x="3" y="3" width="7" height="7" rx="1" />
                <rect x="14" y="3" width="7" height="7" rx="1" />
                <rect x="3" y="14" width="7" height="7" rx="1" />
                <rect x="14" y="14" width="7" height="7" rx="1" />
              </svg>
              <p class="empty-title">尚未安装任何 plugin</p>
              <p class="empty-hint">点击右上 [+ 安装新插件] 从 GitHub 或本地目录安装。</p>
            </div>

            <!-- Plugin 列表 (subcard 风格,与 runtime-card 视觉一致) -->
            <ul v-else class="plugin-list">
              <li
                v-for="p in sortedPlugins"
                :key="p.pluginId"
                class="plugin-card"
                :class="{ 'plugin-card-active': p.isActive, 'plugin-card-untrusted': p.trustState === 'untrusted' }"
              >
                <div class="card-main">
                  <div class="card-title">
                    <span class="display-name">{{ p.displayName }}</span>
                    <span class="plugin-id mono-font">({{ p.pluginId }})</span>
                    <span v-if="p.isActive" class="badge badge-active">已激活</span>
                    <span :class="['badge', p.trustState === 'trusted' ? 'badge-trusted' : 'badge-untrusted']">
                      {{ trustLabel(p.trustState) }}
                    </span>
                    <span v-if="p.sourceKind === 'local'" class="badge badge-warn" title="本地 plugin, 复现性较弱">
                      本地
                    </span>
                  </div>

                  <p v-if="p.description" class="card-desc">{{ p.description }}</p>

                  <div class="card-meta">
                    <span>v{{ p.version }}</span>
                    <span v-if="p.mcpNamespace" class="meta-dot">
                      ns:&nbsp;<code>{{ p.mcpNamespace }}</code>
                    </span>
                    <span class="meta-dot">{{ sourceKindLabel(p.sourceKind) }}</span>
                    <span v-if="p.sourceUrl" class="meta-dot">
                      <a :href="p.sourceUrl" target="_blank" rel="noopener noreferrer" class="link">仓库</a>
                    </span>
                    <span v-if="p.resolvedCommit" class="meta-dot mono-font">
                      {{ p.resolvedCommit.slice(0, 7) }}
                    </span>
                    <span class="meta-dot">装于 {{ formatTime(p.installedAt) }}</span>
                    <span v-if="p.trustedAt" class="meta-dot">信于 {{ formatTime(p.trustedAt) }}</span>
                  </div>
                </div>

                <div class="card-actions">
                  <GlassButton
                    v-if="p.trustState === 'untrusted'"
                    variant="primary"
                    :disabled="store.isBusy(p.pluginId)"
                    @click="onTrustAndActivateClick(p)"
                  >
                    信任并激活
                  </GlassButton>
                  <GlassButton
                    v-else-if="!p.isActive"
                    variant="primary"
                    :disabled="store.isBusy(p.pluginId)"
                    @click="onSetActiveClick(p)"
                  >
                    设为激活
                  </GlassButton>
                  <span v-else class="active-indicator">
                    <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5">
                      <path d="M5 13l4 4L19 7" />
                    </svg>
                    已激活
                  </span>

                  <GlassButton
                    v-if="p.hasConfigSchema"
                    variant="secondary"
                    :disabled="store.isBusy(p.pluginId)"
                    @click="configTarget = p"
                  >
                    配置
                  </GlassButton>

                  <GlassButton
                    variant="danger"
                    :disabled="store.isBusy(p.pluginId)"
                    @click="onUninstallClick(p)"
                  >
                    卸载
                  </GlassButton>
                </div>
              </li>
            </ul>
          </div>
        </article>
      </div>
    </div>

    <!-- ─── 各 Dialog ─── -->
    <InstallPluginDialog
      :visible="showInstallDialog"
      @confirm="onInstallConfirm"
      @cancel="showInstallDialog = false"
    />

    <PluginConfigDialog
      v-if="configTarget"
      :visible="!!configTarget"
      :plugin-id="configTarget.pluginId"
      :display-name="configTarget.displayName"
      @close="configTarget = null"
      @saved="configTarget = null"
    />

    <TrustAndActivateDialog
      :visible="!!trustTarget"
      :plugin="trustTarget"
      @confirm="onTrustConfirm"
      @cancel="trustTarget = null"
    />

    <!-- 卸载二次确认 -->
    <Teleport to="body">
      <Transition name="dialog">
        <div v-if="uninstallTarget" class="dialog-overlay" @click.self="uninstallTarget = null">
          <div class="uninstall-dialog">
            <div class="dialog-header">
              <svg class="warn-icon" viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 9v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <h3>确认卸载</h3>
            </div>
            <div class="dialog-content">
              <p>
                确定卸载 plugin <strong>{{ uninstallTarget.pluginId }}</strong> 吗？
              </p>
              <p class="warn">
                此操作会从 <code>BIMCANVAS_HOME/plugins/</code> 删除该 plugin 目录,
                并清除其 trust / install 元数据。
              </p>
              <p v-if="uninstallTarget.isActive" class="warn">
                该 plugin 当前处于 active 状态;卸载后 Agent 将失去其能力，可能需要重启。
              </p>
            </div>
            <div class="dialog-actions">
              <GlassButton variant="danger" @click="onUninstallConfirm">卸载</GlassButton>
              <GlassButton variant="ghost" @click="uninstallTarget = null">取消</GlassButton>
            </div>
          </div>
        </div>
      </Transition>
    </Teleport>
  </div>
</template>

<style scoped>
/* ─── 与 HomeSettingsPanel 对齐的 Zinc 调色板 ──────────────────── */
.plugins-page {
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

  --border-muted: rgba(255, 255, 255, 0.06);
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

/* Teleported actions */
.teleported-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.spinning {
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* ─── Layout ──────────────────────────────────────────────────────── */
.plugins-main { flex: 1; overflow-y: auto; overflow-x: hidden; }
.layout-bound { max-width: 860px; margin: 0 auto; width: 100%; box-sizing: border-box; }
.wrapper-pad { padding: 32px 24px 80px; }
.plugins-main::-webkit-scrollbar { width: 10px; height: 10px; }
.plugins-main::-webkit-scrollbar-track { background: transparent; }
.plugins-main::-webkit-scrollbar-thumb { background: var(--zinc-800); border-radius: 5px; border: 2px solid var(--bg-app); }
.plugins-main::-webkit-scrollbar-thumb:hover { background: var(--zinc-600); }

/* ─── Page intro ──────────────────────────────────────────────────── */
.page-intro { margin-bottom: 24px; padding-left: 8px; }
.page-title {
  font-size: 1.6rem; font-weight: 600; color: var(--zinc-50);
  margin: 0 0 8px 0; letter-spacing: -0.02em;
}
.page-desc {
  font-size: 0.95rem; color: var(--zinc-400);
  margin: 0; line-height: 1.5;
}

/* ─── Alerts (与 HomeSettingsPanel 同) ──────────────────────────── */
.alerts { display: flex; flex-direction: column; gap: 12px; }
.alert {
  padding: 12px 16px;
  border-radius: var(--radius-md);
  font-size: 13px;
  border: 1px solid transparent;
  line-height: 1.5;
  cursor: pointer;
}
.alert-error { background: rgba(239, 68, 68, 0.1); border-color: rgba(239, 68, 68, 0.2); color: #ef4444; }
.alert-success { background: rgba(34, 197, 94, 0.1); border-color: rgba(34, 197, 94, 0.2); color: #4ade80; }
.alert-title strong { font-weight: 600; }
.alert-details {
  margin: 6px 0 0;
  padding-left: 18px;
  font-size: 11.5px;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  opacity: 0.85;
}

.inline-alert {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  border-radius: var(--radius-md);
  font-size: 13px;
  line-height: 1.5;
}
.inline-alert svg { width: 18px; height: 18px; flex-shrink: 0; }
.inline-alert.warm { background: rgba(234, 179, 8, 0.06); border: 1px solid rgba(234, 179, 8, 0.15); color: #fde047; }
.restart-row .restart-text { flex: 1; }

/* ─── Card framework (与 HomeSettingsPanel 同) ──────────────────── */
.config-card {
  background-color: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: var(--radius-lg);
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
.heading-icon { width: 22px; height: 22px; color: var(--text-muted); padding-top: 2px; flex-shrink: 0; }
.heading-text h3 {
  margin: 0 0 4px 0;
  font-size: 15px;
  font-weight: 500;
  color: var(--text-main);
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.heading-text p { margin: 0; font-size: 13px; color: var(--text-muted); }
.count-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 22px;
  height: 20px;
  padding: 0 7px;
  font-size: 11.5px;
  font-weight: 500;
  border-radius: 10px;
  background: var(--zinc-800);
  color: var(--zinc-300);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  letter-spacing: 0.02em;
}
.card-body { padding: 24px 32px 28px; flex: 1; }

/* ─── Loading / Empty ─────────────────────────────────────────────── */
.loading-state { text-align: center; color: var(--zinc-500); padding: 60px 0; font-size: 13px; }

.empty-state {
  text-align: center;
  padding: 56px 0 40px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  color: var(--text-muted);
}
.empty-title { font-size: 14px; color: var(--zinc-300); margin: 6px 0 0; }
.empty-hint { font-size: 12.5px; color: var(--zinc-500); margin: 0; }

/* ─── Plugin list (subcard 风格,对齐 runtime-card) ──────────────── */
.plugin-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.plugin-card {
  display: flex;
  align-items: flex-start;
  gap: 20px;
  padding: 16px 18px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: var(--radius-md);
  transition: border-color 0.15s, background-color 0.15s;
}
.plugin-card:hover { border-color: rgba(255, 255, 255, 0.1); }
.plugin-card-active {
  background: rgba(59, 130, 246, 0.06);
  border-color: rgba(59, 130, 246, 0.28);
}
.plugin-card-active:hover { border-color: rgba(59, 130, 246, 0.4); }
.plugin-card-untrusted { border-style: dashed; }

.card-main { flex: 1; min-width: 0; }
.card-title {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 6px;
}
.display-name { font-size: 14px; font-weight: 600; color: var(--text-main); }
.plugin-id { font-size: 12px; color: var(--zinc-500); }

.badge {
  padding: 2px 8px;
  border-radius: 10px;
  font-size: 10.5px;
  letter-spacing: 0.02em;
  font-weight: 500;
}
.badge-active { background: rgba(59, 130, 246, 0.2); color: #93c5fd; }
.badge-trusted { background: rgba(34, 197, 94, 0.18); color: #86efac; }
.badge-untrusted { background: rgba(234, 179, 8, 0.18); color: #fde047; }
.badge-warn { background: rgba(234, 179, 8, 0.12); color: #fde047; }

.card-desc {
  margin: 4px 0 8px;
  font-size: 12.5px;
  color: var(--zinc-300);
  line-height: 1.5;
}

.card-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0;
  font-size: 11.5px;
  color: var(--zinc-500);
}
.card-meta code {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  color: var(--zinc-300);
}
.mono-font { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
.card-meta .link { color: var(--accent-blue, #60a5fa); text-decoration: none; }
.card-meta .link:hover { text-decoration: underline; }
.meta-dot::before {
  content: '·';
  margin: 0 6px;
  color: var(--zinc-600);
}

.card-actions {
  display: flex;
  flex-direction: row;
  gap: 8px;
  align-items: center;
  flex-shrink: 0;
}

.active-indicator {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 6px 12px;
  background: rgba(34, 197, 94, 0.1);
  border: 1px solid rgba(34, 197, 94, 0.3);
  border-radius: var(--radius-sm);
  color: #86efac;
  font-size: 12.5px;
  font-weight: 500;
}

/* ─── Spacing helpers (对齐 HomeSettingsPanel) ───────────────────── */
.mb-md { margin-bottom: 20px; }
.mb-lg { margin-bottom: 24px; }

/* ─── 卸载对话框 ─────────────────────────────────────────────────── */
.dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.uninstall-dialog {
  background: var(--bg-surface, #1a1d24);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: var(--radius-lg);
  padding: 24px;
  min-width: 440px;
  max-width: 540px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.35), 0 0 0 1px rgba(255, 255, 255, 0.05) inset;
}

.dialog-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 14px;
}

.warn-icon { color: var(--accent-danger, #ef4444); }

.dialog-header h3 {
  margin: 0;
  font-size: 17px;
  font-weight: 600;
}

.dialog-content { margin-bottom: 18px; }
.dialog-content p {
  margin: 0 0 8px;
  color: var(--zinc-300);
  font-size: 13px;
  line-height: 1.55;
}
.dialog-content strong {
  color: var(--text-main);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
}
.dialog-content code {
  background: rgba(0, 0, 0, 0.4);
  padding: 1px 5px;
  border-radius: 3px;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 11.5px;
}
.dialog-content .warn {
  color: var(--accent-danger, #fca5a5);
  font-size: 12.5px;
}

.dialog-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

.dialog-enter-active,
.dialog-leave-active { transition: all 0.2s ease; }
.dialog-enter-from,
.dialog-leave-to { opacity: 0; }
.dialog-enter-from .uninstall-dialog,
.dialog-leave-to .uninstall-dialog {
  transform: scale(0.95) translateY(-10px);
  opacity: 0;
}
.dialog-enter-active .uninstall-dialog,
.dialog-leave-active .uninstall-dialog {
  transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
