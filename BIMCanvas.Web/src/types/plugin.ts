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

/** Scene 生命周期状态 (SceneStatus.cs) */
export type SceneStatus = 'active';

/** Agent 启动模式 (LaunchMode.cs) */
export type LaunchMode = 'projectless' | 'projectBound';

/** OpenProject 三态返回 (OpenStatus.cs) */
export type OpenStatus = 'bound' | 'sceneSelectRequired' | 'requiresSceneBinding';

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
}

/** GET /api/plugins 响应 */
export interface PluginListResponse {
  plugins: PluginListItem[];
  activePluginId: string | null;
}

/** POST /api/plugins/install 请求 */
export interface InstallPluginRequest {
  repoUrl: string;
  ref?: string | null;
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

// ─── Scene 相关 (project.json.scenes[]) ──────────────────────────────────

/** scenes[].plugin 子对象 (ScenePluginRef.cs) */
export interface ScenePluginRef {
  id: string;
  versionRange: string;
}

/** scenes[] 单项 (ProjectScene.cs) */
export interface ProjectScene {
  sceneId: string;
  scene: string;
  plugin: ScenePluginRef;
  status: SceneStatus;
  createdAt: string; // ISO 8601
}

// ─── OpenProject 扩展字段 (ProjectLoadResult.cs v1.1 字段) ───────────────

/**
 * OpenProject 响应扩展字段(与 ProjectService 现有 ProjectLoadResult 合并使用)。
 * 注意:这些字段是 ProjectLoadResult 在 v1.1 plugin 化后追加的可选字段。
 */
export interface ProjectLoadPluginExtension {
  openStatus?: OpenStatus | null;
  currentActivePlugin?: string | null;
  activeSceneId?: string | null;
  candidates?: ProjectScene[] | null; // sceneSelectRequired 时填充
  existingScenes?: ProjectScene[] | null; // requiresSceneBinding 时填充
}

// ─── POST /api/project/scenes 请求 / 响应 (BindSceneRequest.cs) ──────────

export interface BindSceneRequest {
  sceneId: string;
  scene?: string | null;
  plugin: {
    id: string;
    versionRange?: string | null;
  };
}

export interface BindSceneResult {
  success: boolean;
  sceneId?: string | null;
  pluginId?: string | null;
  projectPath?: string | null;
  message?: string | null;
}
