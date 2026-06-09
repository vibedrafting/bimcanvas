/**
 * Plugin 相关类型定义
 *
 * 与组 2 (BIMCanvas.Server/Models/Plugins/) 与组 1 (docs/plugin-manifest-schema.json)
 * 严格对齐。所有 enum 值走 camelCase (CamelCaseEnumConverter 输出格式)。
 *
 * 主真理源 v1.1 §4.3
 */

// ─── 枚举(JSON camelCase 字符串) ─────────────────────────────────────────

/** Plugin 信任状态 (BIMCanvas.Server/Models/Plugins/TrustState.cs) */
export type TrustState = 'untrusted' | 'trusted';

/** Plugin 来源类型 (SourceKind.cs) */
export type SourceKind = 'github' | 'local' | 'zip';

/** Agent 启动模式 (LaunchMode.cs) */
export type LaunchMode = 'projectless' | 'projectBound';

/** OpenProject 返回状态 (项目去插件态后只余 bound 单态) (OpenStatus.cs) */
export type OpenStatus = 'bound';

// ─── DTO ─────────────────────────────────────────────────────────────────

/** Plugin 列表项 (PluginsController.cs PluginListItem) */
export interface PluginListItem {
  pluginId: string;
  displayName: string;
  description?: string | null;
  version: string;
  mcpNamespace?: string | null;
  trustState: TrustState;
  sourceUrl?: string | null;
  resolvedCommit?: string | null;
  sourceKind: SourceKind;
  installedAt: string; // ISO 8601
  trustedAt?: string | null; // ISO 8601
  isActive: boolean;
  /** manifest 声明了 configSchema，UI 显示"配置"按钮 */
  hasConfigSchema?: boolean;
}

/** GET /api/plugins 响应 */
export interface PluginListResponse {
  plugins: PluginListItem[];
  activePluginId: string | null;
}

/** POST /api/plugins/install 请求 (与 PluginsController.InstallRequest 对齐) */
export interface InstallPluginRequest {
  /** source 类型:'github'(默认) | 'local'。缺省按 github 处理(向后兼容)。 */
  sourceKind?: SourceKind;

  // ─ github source ─
  repoUrl?: string;
  ref?: string | null;

  // ─ local source ─
  /** 本地 plugin 目录绝对路径(sourceKind=local 时必填)。 */
  path?: string;
  /** local 模式:true(默认)=junction 软链,改源码即时生效;false=复制快照。 */
  link?: boolean;
}

/** POST /api/plugins/install 响应 */
export interface InstallPluginResponse {
  pluginId: string;
  trustState: TrustState;
  installedVersion: string;
  sourceUrl?: string | null;
  resolvedCommit?: string | null;
  nextStep: string;
}

/** POST /api/plugins/{id}/trust-and-activate 响应 */
export interface TrustAndActivateResponse {
  pluginId: string;
  trustState: TrustState;
  activated: boolean;
  restartRequired: boolean;
  message: string;
}

/** POST /api/plugins/active 请求 / 响应 */
export interface SetActiveRequest {
  pluginId: string;
}

export interface SetActiveResponse {
  pluginId: string;
  activated: boolean;
  restartRequired: boolean;
}

/** Plugin 操作通用错误响应 (PluginsController ErrorResponse) */
export interface PluginErrorResponse {
  code: string;
  message: string;
  details?: unknown[] | null;
}

// ─── OpenProject 扩展字段 (ProjectLoadResult.cs) ───────────────

/**
 * OpenProject 响应扩展字段(与 ProjectService 现有 ProjectLoadResult 合并使用)。
 * 项目去插件态后只余 openStatus(恒 bound)+ currentActivePlugin。
 */
export interface ProjectLoadPluginExtension {
  openStatus?: OpenStatus | null;
  currentActivePlugin?: string | null;
}
