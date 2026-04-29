/**
 * 历史管理相关类型定义
 *
 * 职责：
 * - 定义变更来源（ChangeSource）
 * - 定义历史快照（HistorySnapshot）
 * - 定义变更类型（ChangeType）
 * - 定义加载选项（LoadOptions）
 */

// ========== 变更源枚举 ==========

/**
 * 变更来源
 *
 * 用于标识状态变更的触发源，TimelineManager 根据此枚举
 * 智能决策是否清空历史、保留视图等策略。
 */
export const ChangeSource = {
  // ========== 用户主动操作 ==========

  /** 用户手动编辑（拖动、旋转、修改属性等） */
  UserEdit: 'user_edit',

  /** 用户上传新项目文件 */
  UserUpload: 'user_upload',

  /** 用户撤销操作 */
  UserUndo: 'user_undo',

  /** 用户重做操作 */
  UserRedo: 'user_redo',

  // ========== Git 操作 ==========

  /** Git 分支切换 */
  GitCheckout: 'git_checkout',

  /** Git 放弃更改 */
  GitDiscard: 'git_discard',

  /** Git 初始化项目 */
  GitInit: 'git_init',

  // ========== 远程同步 ==========

  /** AI Agent 修改 */
  AgentModify: 'agent_modify',

  /** Server 端文件变化推送 */
  ServerSync: 'server_sync',

  /** 协作者修改（未来扩展） */
  CollabSync: 'collab_sync',

  // ========== 系统操作 ==========

  /** 系统初始化（应用启动时加载） */
  SystemInit: 'system_init',

  /** 系统恢复（异常处理、冲突解决） */
  SystemRestore: 'system_restore',
} as const

export type ChangeSource = typeof ChangeSource[keyof typeof ChangeSource]

// ========== 变更类型枚举 ==========

/**
 * 变更类型
 *
 * 用于描述本次变更的具体操作类型，便于历史面板
 * 展示和未来的操作回放功能。
 */
export const ChangeType = {
  /** 创建新对象 */
  Create: 'create',

  /** 更新对象属性 */
  Update: 'update',

  /** 删除对象 */
  Delete: 'delete',

  /** 移动对象 */
  Move: 'move',

  /** 旋转对象 */
  Rotate: 'rotate',

  /** 批量操作 */
  Batch: 'batch',
} as const

export type ChangeType = typeof ChangeType[keyof typeof ChangeType]

// ========== 历史快照接口 ==========

/**
 * 历史快照
 *
 * 完整记录某个时间点的项目状态，包含状态数据、
 * 来源信息、变更描述和元数据。
 */
export interface HistorySnapshot {
  // ========== 基础信息 ==========

  /** 快照唯一ID */
  id: string;

  /** 时间戳（毫秒） */
  timestamp: number;

  /** 序列化的 ProjectData JSON 字符串 */
  state: string;

  // ========== 来源追踪 ==========

  /** 变更来源 */
  source: ChangeSource;

  /** 来源标识（如 agentId、userId） */
  sourceId?: string;

  // ========== 变更描述 ==========

  /** 人类可读描述 */
  description?: string;

  /** 变更类型 */
  changeType?: ChangeType;

  /** 受影响的对象ID列表 */
  affectedIds?: string[];

  // ========== 元数据 ==========

  /** 扩展元数据 */
  metadata?: {
    /** Git分支名 */
    branchName?: string;

    /** Git提交哈希 */
    commitHash?: string;

    /** Agent对话ID */
    agentConversationId?: string;

    /** 用户会话ID */
    userSessionId?: string;

    /** 其他扩展字段 */
    [key: string]: unknown;
  };
}

// ========== 加载选项接口 ==========

/**
 * 加载选项
 *
 * 传递给 loadInitialProject() 的配置项，用于控制
 * 历史管理和视图行为。
 */
export interface LoadOptions {
  /** 加载来源（必填） */
  source: ChangeSource;

  /**
   * 保持视图
   *
   * - undefined: 根据 source 自动决策
   * - true: 保持当前视图（缩放、平移）
   * - false: 重置视图（适配屏幕）
   */
  preserveView?: boolean;

  /**
   * 保留历史
   *
   * - undefined: 根据 source 自动决策
   * - true: 保留历史栈，追加新快照
   * - false: 清空历史栈，只保留新快照
   */
  preserveHistory?: boolean;

  /** 自定义描述 */
  description?: string;

  /** 元数据 */
  metadata?: Record<string, unknown>;
}

// ========== 历史统计接口（可选，用于调试） ==========

/**
 * 历史统计信息
 *
 * 用于历史可视化面板展示。
 */
export interface HistoryStats {
  /** 总快照数 */
  total: number;

  /** 当前索引 */
  currentIndex: number;

  /** 可撤销数 */
  undoCount: number;

  /** 可重做数 */
  redoCount: number;

  /** 按来源分组的快照数 */
  bySource: Record<ChangeSource, number>;

  /** 总内存占用（字节） */
  memoryUsage: number;
}
