/**
 * Plugin 服务 - 封装与 Server /api/plugins/* 与 /api/project/scenes 的 REST 调用
 *
 * 与组 2 BIMCanvas.Server/Controllers/PluginsController.cs (382 行) 严格对齐。
 * 错误处理统一从 PluginErrorResponse {code, message, details?} 提取。
 *
 * 主真理源 v1.1 §4.3 + 组 4 任务模板 §4.A
 */

import axios from 'axios';
import { SERVER_API } from '../config/api';
import type {
  PluginListResponse,
  InstallPluginRequest,
  InstallPluginResponse,
  TrustAndActivateResponse,
  SetActiveRequest,
  SetActiveResponse,
  PluginErrorResponse,
} from '../types/plugin';

const PLUGINS_BASE = `${SERVER_API}/plugins`;

/**
 * 统一错误返回结构,供 Pinia store 透传给 UI 显示
 */
export interface PluginServiceError {
  ok: false;
  code: string;
  message: string;
  details?: unknown[] | null;
  httpStatus?: number;
}

export interface PluginServiceSuccess<T> {
  ok: true;
  data: T;
}

export type PluginServiceResult<T> = PluginServiceSuccess<T> | PluginServiceError;

function toError(error: any, fallbackCode = 'internal_error'): PluginServiceError {
  const data = error?.response?.data as Partial<PluginErrorResponse> | undefined;
  return {
    ok: false,
    code: data?.code || fallbackCode,
    message: data?.message || error?.message || '操作失败',
    details: data?.details ?? null,
    httpStatus: error?.response?.status,
  };
}

export class PluginService {
  /** GET /api/plugins */
  static async list(): Promise<PluginServiceResult<PluginListResponse>> {
    try {
      const response = await axios.get<PluginListResponse>(PLUGINS_BASE);
      return { ok: true, data: response.data };
    } catch (error: any) {
      return toError(error);
    }
  }

  /** POST /api/plugins/install */
  static async install(
    request: InstallPluginRequest
  ): Promise<PluginServiceResult<InstallPluginResponse>> {
    try {
      const response = await axios.post<InstallPluginResponse>(
        `${PLUGINS_BASE}/install`,
        request
      );
      return { ok: true, data: response.data };
    } catch (error: any) {
      return toError(error);
    }
  }

  /**
   * POST /api/plugins/{id}/trust-and-activate
   *
   * 首次激活专用 (主真理源 §2.1 步骤 7)。
   * 连续调 Trust(id) → Activate(id);响应含 restartRequired: true。
   * 调用前必须经过 TrustAndActivateDialog 二次确认 (R9 RCE 防御)。
   */
  static async trustAndActivate(
    pluginId: string
  ): Promise<PluginServiceResult<TrustAndActivateResponse>> {
    try {
      const response = await axios.post<TrustAndActivateResponse>(
        `${PLUGINS_BASE}/${encodeURIComponent(pluginId)}/trust-and-activate`
      );
      return { ok: true, data: response.data };
    } catch (error: any) {
      return toError(error);
    }
  }

  /**
   * POST /api/plugins/active
   *
   * 后续切换 active plugin (plugin 已 trusted)。
   * 对 untrusted plugin Server 返回 403 + code=plugin_not_trusted。
   */
  static async setActive(
    pluginId: string
  ): Promise<PluginServiceResult<SetActiveResponse>> {
    try {
      const request: SetActiveRequest = { pluginId };
      const response = await axios.post<SetActiveResponse>(
        `${PLUGINS_BASE}/active`,
        request
      );
      return { ok: true, data: response.data };
    } catch (error: any) {
      return toError(error);
    }
  }

  /** DELETE /api/plugins/{id} */
  static async uninstall(
    pluginId: string
  ): Promise<PluginServiceResult<{ pluginId: string; uninstalled: boolean }>> {
    try {
      const response = await axios.delete<{ pluginId: string; uninstalled: boolean }>(
        `${PLUGINS_BASE}/${encodeURIComponent(pluginId)}`
      );
      return { ok: true, data: response.data };
    } catch (error: any) {
      return toError(error);
    }
  }

  /** POST /api/plugins/{id}/validate (不修改 trustState, 不执行代码) */
  static async validate(
    pluginId: string
  ): Promise<PluginServiceResult<{ pluginId: string; valid: boolean; code?: string; errors: string[] }>> {
    try {
      const response = await axios.post(
        `${PLUGINS_BASE}/${encodeURIComponent(pluginId)}/validate`
      );
      return { ok: true, data: response.data };
    } catch (error: any) {
      return toError(error);
    }
  }

}
