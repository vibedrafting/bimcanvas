/**
 * Plugin 管理 Pinia store
 *
 * 职责:
 *   1. 维护已安装 plugin 列表 + active plugin id (来源:GET /api/plugins)
 *   2. 暴露 install / trustAndActivate / setActive / uninstall actions
 *   3. 维护 "需要重启" banner 状态(任何激活/切换成功后置 true)
 *   4. 维护 UI 错误状态(供 PluginsPanel 顶部 alert 渲染)
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

export const usePluginStore = defineStore('plugin', () => {
  // ─── 状态 ───────────────────────────────────────────────────────────

  const installedPlugins = ref<PluginListItem[]>([]);
  const activePluginId = ref<string | null>(null);

  const loading = ref(false);
  const lastError = ref<PluginServiceError | null>(null);
  const lastInfo = ref<string | null>(null);
  const restartRequired = ref(false);

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

  const clearError = () => {
    lastError.value = null;
  };

  const clearInfo = () => {
    lastInfo.value = null;
  };

  const dismissRestart = () => {
    restartRequired.value = false;
  };

  // ─── Actions ────────────────────────────────────────────────────────

  /** 拉取已安装 plugin 列表 + 当前 active id */
  const fetchAll = async () => {
    loading.value = true;
    lastError.value = null;
    try {
      const result = await PluginService.list();
      if (result.ok) {
        installedPlugins.value = result.data.plugins;
        activePluginId.value = result.data.activePluginId;
      } else {
        lastError.value = result;
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
    clearError();
    clearInfo();
    loading.value = true;
    try {
      const result = await PluginService.install(request);
      if (result.ok) {
        lastInfo.value =
          `已安装 ${result.data.pluginId} v${result.data.installedVersion}。` +
          `${result.data.nextStep}`;
        await fetchAll();
        return result.data.pluginId;
      } else {
        lastError.value = result;
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
    clearError();
    clearInfo();
    setBusy(pluginId, true);
    try {
      const result = await PluginService.trustAndActivate(pluginId);
      if (result.ok) {
        lastInfo.value = result.data.message;
        restartRequired.value = result.data.restartRequired;
        await fetchAll();
        return true;
      } else {
        lastError.value = result;
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
    clearError();
    clearInfo();
    setBusy(pluginId, true);
    try {
      const result = await PluginService.setActive(pluginId);
      if (result.ok) {
        lastInfo.value = `已切换 active = ${pluginId},请重启 BIMCanvas 让 Agent 重新加载`;
        restartRequired.value = result.data.restartRequired;
        await fetchAll();
        return true;
      } else {
        lastError.value = result;
        return false;
      }
    } finally {
      setBusy(pluginId, false);
    }
  };

  /** 卸载 plugin */
  const uninstall = async (pluginId: string): Promise<boolean> => {
    clearError();
    clearInfo();
    setBusy(pluginId, true);
    try {
      const result = await PluginService.uninstall(pluginId);
      if (result.ok) {
        lastInfo.value = `已卸载 ${pluginId}`;
        await fetchAll();
        return true;
      } else {
        lastError.value = result;
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
    lastError,
    lastInfo,
    restartRequired,
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
    clearError,
    clearInfo,
    dismissRestart,
  };
});
