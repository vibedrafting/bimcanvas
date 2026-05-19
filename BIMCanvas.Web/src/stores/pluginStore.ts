/**
 * Plugin 管理 Pinia store
 *
 * 职责:
 *   1. 维护已安装 plugin 列表 + active plugin id (来源:GET /api/plugins)
 *   2. 暴露 install / trustAndActivate / setActive / uninstall actions
 *
 * 跨 store 状态:
 *   - "需要重启"由 systemStore.markRestartRequired(reason) 统一管理(顶栏 [需要重启] 按钮 + 左下 toast)
 *   - 操作错误 / 成功反馈走 systemStore.pushToast(),不再持本地 lastError/lastInfo
 *
 * 设计原则:
 *   - 所有写动作走 PluginService(R3 / R5: UI 不直接读写文件系统)
 *   - trustState 数据严格来自 GET /api/plugins(R2: 不假设 plugin 目录内文件)
 *   - install 与 activate 严格分离(R1: 安装阶段不自动激活)
 */

import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { PluginService, type PluginServiceError } from '../services/PluginService';
import type {
  PluginListItem,
  InstallPluginRequest,
} from '../types/plugin';
import { useSystemStore } from './systemStore';

export const usePluginStore = defineStore('plugin', () => {
  const systemStore = useSystemStore();

  // ─── 状态 ───────────────────────────────────────────────────────────

  const installedPlugins = ref<PluginListItem[]>([]);
  const activePluginId = ref<string | null>(null);

  const loading = ref(false);
  const loadError = ref<PluginServiceError | null>(null); // 仅供列表加载失败时显示

  // 按 pluginId 维度的 busy 状态(防止同 plugin 重复点击)
  const busyPluginIds = ref<Set<string>>(new Set());

  // ─── 计算属性 ───────────────────────────────────────────────────────

  /** 当前 active plugin 的完整记录(便于 UI 顶部展示) */
  const activePlugin = computed<PluginListItem | null>(() => {
    if (!activePluginId.value) return null;
    return installedPlugins.value.find(p => p.pluginId === activePluginId.value) ?? null;
  });

  const hasPlugins = computed(() => installedPlugins.value.length > 0);

  // ─── 辅助 ───────────────────────────────────────────────────────────

  const setBusy = (pluginId: string, busy: boolean) => {
    const next = new Set(busyPluginIds.value);
    if (busy) next.add(pluginId);
    else next.delete(pluginId);
    busyPluginIds.value = next;
  };

  const isBusy = (pluginId: string) => busyPluginIds.value.has(pluginId);

  const clearLoadError = () => {
    loadError.value = null;
  };

  const pushError = (err: PluginServiceError, title: string) => {
    systemStore.pushToast({
      title: `${title} [${err.code}]`,
      message: err.message,
      type: 'error',
    });
  };

  // ─── Actions ────────────────────────────────────────────────────────

  /** 拉取已安装 plugin 列表 + 当前 active id */
  const fetchAll = async () => {
    loading.value = true;
    loadError.value = null;
    try {
      const result = await PluginService.list();
      if (result.ok) {
        installedPlugins.value = result.data.plugins;
        activePluginId.value = result.data.activePluginId;
      } else {
        loadError.value = result;
        pushError(result, '加载 plugin 列表失败');
      }
    } finally {
      loading.value = false;
    }
  };

  /**
   * 安装 plugin (R1: 只 clone + 静态校验, 绝不自动激活)
   * @returns 成功时返回 pluginId 供 UI 后续展示提示
   */
  const install = async (request: InstallPluginRequest): Promise<string | null> => {
    loading.value = true;
    try {
      const result = await PluginService.install(request);
      if (result.ok) {
        systemStore.pushToast({
          title: '安装成功',
          message: `${result.data.pluginId} v${result.data.installedVersion}。${result.data.nextStep}`,
          type: 'success',
        });
        await fetchAll();
        return result.data.pluginId;
      } else {
        pushError(result, '安装失败');
        return null;
      }
    } finally {
      loading.value = false;
    }
  };

  /**
   * 信任并激活 (首次激活,触发 ExecutablePluginProbe + 设 active)
   * 调用前必须经过 TrustAndActivateDialog 二次确认 (R9 RCE 防御)
   */
  const trustAndActivate = async (pluginId: string): Promise<boolean> => {
    setBusy(pluginId, true);
    try {
      const result = await PluginService.trustAndActivate(pluginId);
      if (result.ok) {
        systemStore.pushToast({
          title: 'Plugin 已激活',
          message: result.data.message,
          type: 'success',
        });
        if (result.data.restartRequired) {
          systemStore.markRestartRequired(`plugin:${pluginId}`);
        }
        await fetchAll();
        return true;
      } else {
        pushError(result, '激活失败');
        return false;
      }
    } finally {
      setBusy(pluginId, false);
    }
  };

  /**
   * 后续切换 active plugin (plugin 已 trusted)
   * Server 对 untrusted plugin 返回 403 + code=plugin_not_trusted
   */
  const setActive = async (pluginId: string): Promise<boolean> => {
    setBusy(pluginId, true);
    try {
      const result = await PluginService.setActive(pluginId);
      if (result.ok) {
        systemStore.pushToast({
          title: 'Active plugin 已切换',
          message: `已切换 active = ${pluginId},点击顶栏 [需要重启] 让 Agent 重新加载。`,
          type: 'success',
        });
        if (result.data.restartRequired) {
          systemStore.markRestartRequired(`plugin:${pluginId}`);
        }
        await fetchAll();
        return true;
      } else {
        pushError(result, '切换 active 失败');
        return false;
      }
    } finally {
      setBusy(pluginId, false);
    }
  };

  /** 卸载 plugin */
  const uninstall = async (pluginId: string): Promise<boolean> => {
    setBusy(pluginId, true);
    try {
      const result = await PluginService.uninstall(pluginId);
      if (result.ok) {
        systemStore.pushToast({
          title: '已卸载',
          message: `${pluginId} 已删除`,
          type: 'success',
        });
        await fetchAll();
        return true;
      } else {
        pushError(result, '卸载失败');
        return false;
      }
    } finally {
      setBusy(pluginId, false);
    }
  };

  return {
    // state
    installedPlugins,
    activePluginId,
    loading,
    loadError,
    // computed
    activePlugin,
    hasPlugins,
    // queries
    isBusy,
    // actions
    fetchAll,
    install,
    trustAndActivate,
    setActive,
    uninstall,
    clearLoadError,
  };
});
