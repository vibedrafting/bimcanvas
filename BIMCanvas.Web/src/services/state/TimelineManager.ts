/**
 * 时间线管理器 - 增强版
 *
 * 职责：
 * 1. 管理历史快照栈（支持 Undo/Redo）
 * 2. 根据变更来源智能决策历史策略
 * 3. 提供历史查询和过滤功能
 * 4. 支持操作回放和协作冲突解决（未来）
 *
 * 设计原则：
 * - 职责分离：历史管理与数据加载解耦
 * - 策略驱动：集中配置历史策略（清空 vs 保留）
 * - 类型安全：完整的 TypeScript 类型支持
 * - 易于扩展：新增场景只需修改策略配置
 */

import type { ProjectData } from '../../types/canvas';
import { ChangeSource, ChangeType, type HistorySnapshot, type HistoryStats } from '../../types/history';

export class TimelineManager {
  // ========== 私有状态 ==========

  private snapshots: HistorySnapshot[] = [];
  private currentIndex: number = -1;
  private maxHistory: number = 50;

  // ========== 策略配置 ==========

  /**
   * 清空历史策略
   *
   * 这些来源会清空历史栈，只保留新快照：
   * - 用户上传新项目：加载全新的项目文件
   * - Git 初始化：新建 Git 仓库
   * - 系统初始化：应用启动时首次加载
   */
  private readonly CLEAR_HISTORY_SOURCES = new Set<ChangeSource>([
    ChangeSource.UserUpload,
    ChangeSource.GitInit,
    ChangeSource.SystemInit,
  ]);

  /**
   * 保留历史策略
   *
   * 这些来源会保留历史栈，追加新快照：
   * - Agent 修改：AI 自动布置家具
   * - Server 同步：远程文件变化推送
   * - 协作同步：其他用户的修改（未来）
   * - 用户编辑：手动拖动、旋转等操作
   */
  private readonly PRESERVE_HISTORY_SOURCES = new Set<ChangeSource>([
    ChangeSource.AgentModify,
    ChangeSource.ServerSync,
    ChangeSource.CollabSync,
    ChangeSource.UserEdit,
  ]);

  /**
   * 保持视图策略
   *
   * 这些来源会保持当前视图（不重置缩放/平移）：
   * - Git 操作：切换分支、放弃更改
   * - 远程同步：Agent 修改、Server 推送
   * - 撤销/重做：用户历史导航
   */
  private readonly PRESERVE_VIEW_SOURCES = new Set<ChangeSource>([
    ChangeSource.GitCheckout,
    ChangeSource.GitDiscard,
    ChangeSource.AgentModify,
    ChangeSource.ServerSync,
    ChangeSource.UserUndo,
    ChangeSource.UserRedo,
  ]);

  // ========== 核心 API ==========

  /**
   * 推入新快照
   *
   * @param state 项目数据
   * @param source 变更来源
   * @param options 可选配置
   * @param options.description 人类可读描述
   * @param options.changeType 变更类型
   * @param options.affectedIds 受影响的对象ID列表
   * @param options.metadata 扩展元数据
   */
  public push(
    state: ProjectData,
    source: ChangeSource,
    options?: {
      description?: string;
      changeType?: ChangeType;
      affectedIds?: string[];
      metadata?: Record<string, any>;
    }
  ): void {
    // 如果不在历史末尾，丢弃未来状态（分支清理）
    if (this.currentIndex < this.snapshots.length - 1) {
      this.snapshots = this.snapshots.slice(0, this.currentIndex + 1);
    }

    // 创建快照
    const snapshot: HistorySnapshot = {
      id: this.generateSnapshotId(),
      timestamp: Date.now(),
      state: JSON.stringify(state),
      source,
      description: options?.description,
      changeType: options?.changeType,
      affectedIds: options?.affectedIds,
      metadata: options?.metadata,
    };

    this.snapshots.push(snapshot);
    this.currentIndex++;

    // 限制历史大小（FIFO）
    if (this.snapshots.length > this.maxHistory) {
      this.snapshots.shift();
      this.currentIndex--;
    }

    console.log(`[Timeline] Pushed snapshot: ${source}`, {
      index: this.currentIndex,
      total: this.snapshots.length,
      description: options?.description || '(no description)',
    });
  }

  /**
   * 撤销（返回上一个快照）
   *
   * @returns 上一个快照，如果无法撤销则返回 null
   */
  public undo(): HistorySnapshot | null {
    if (!this.canUndo) {
      console.warn('[Timeline] Cannot undo: already at the beginning');
      return null;
    }

    this.currentIndex--;
    const snapshot = this.snapshots[this.currentIndex];

    console.log(`[Timeline] Undo to snapshot: ${snapshot.source}`, {
      index: this.currentIndex,
      timestamp: new Date(snapshot.timestamp).toLocaleString(),
      description: snapshot.description || '(no description)',
    });

    return snapshot;
  }

  /**
   * 重做（返回下一个快照）
   *
   * @returns 下一个快照，如果无法重做则返回 null
   */
  public redo(): HistorySnapshot | null {
    if (!this.canRedo) {
      console.warn('[Timeline] Cannot redo: already at the end');
      return null;
    }

    this.currentIndex++;
    const snapshot = this.snapshots[this.currentIndex];

    console.log(`[Timeline] Redo to snapshot: ${snapshot.source}`, {
      index: this.currentIndex,
      timestamp: new Date(snapshot.timestamp).toLocaleString(),
      description: snapshot.description || '(no description)',
    });

    return snapshot;
  }

  /**
   * 清空历史
   */
  public clear(): void {
    this.snapshots = [];
    this.currentIndex = -1;
    console.log('[Timeline] History cleared');
  }

  // ========== 策略决策 API ==========

  /**
   * 判断指定来源是否应该清空历史
   *
   * @param source 变更来源
   * @returns 是否清空历史
   */
  public shouldClearHistory(source: ChangeSource): boolean {
    return this.CLEAR_HISTORY_SOURCES.has(source);
  }

  /**
   * 判断指定来源是否应该保留历史
   *
   * @param source 变更来源
   * @returns 是否保留历史
   */
  public shouldPreserveHistory(source: ChangeSource): boolean {
    return this.PRESERVE_HISTORY_SOURCES.has(source);
  }

  /**
   * 判断指定来源是否应该保持视图
   *
   * @param source 变更来源
   * @returns 是否保持视图
   */
  public shouldPreserveView(source: ChangeSource): boolean {
    return this.PRESERVE_VIEW_SOURCES.has(source);
  }

  // ========== 查询 API（用于调试/可视化）==========

  /**
   * 获取当前快照
   *
   * @returns 当前快照，如果历史为空则返回 null
   */
  public getCurrentSnapshot(): HistorySnapshot | null {
    return this.snapshots[this.currentIndex] || null;
  }

  /**
   * 获取所有快照（只读）
   *
   * @returns 所有快照的只读数组
   */
  public getAllSnapshots(): ReadonlyArray<HistorySnapshot> {
    return [...this.snapshots];
  }

  /**
   * 按来源过滤快照
   *
   * @param source 变更来源
   * @returns 匹配的快照列表
   */
  public getSnapshotsBySource(source: ChangeSource): HistorySnapshot[] {
    return this.snapshots.filter((s) => s.source === source);
  }

  /**
   * 按时间范围过滤快照
   *
   * @param start 开始时间戳（毫秒）
   * @param end 结束时间戳（毫秒）
   * @returns 匹配的快照列表
   */
  public getSnapshotsByTimeRange(start: number, end: number): HistorySnapshot[] {
    return this.snapshots.filter((s) => s.timestamp >= start && s.timestamp <= end);
  }

  /**
   * 获取历史统计信息
   *
   * @returns 统计信息对象
   */
  public getStats(): HistoryStats {
    // 按来源分组统计
    const bySource = this.snapshots.reduce((acc, snapshot) => {
      acc[snapshot.source] = (acc[snapshot.source] || 0) + 1;
      return acc;
    }, {} as Record<ChangeSource, number>);

    // 计算总内存占用（粗略估算）
    const memoryUsage = this.snapshots.reduce((total, snapshot) => {
      return total + snapshot.state.length * 2; // 字符串每字符约2字节
    }, 0);

    return {
      total: this.snapshots.length,
      currentIndex: this.currentIndex,
      undoCount: this.currentIndex,
      redoCount: this.snapshots.length - this.currentIndex - 1,
      bySource,
      memoryUsage,
    };
  }

  // ========== 状态 Getters ==========

  /**
   * 是否可以撤销
   */
  public get canUndo(): boolean {
    return this.currentIndex > 0;
  }

  /**
   * 是否可以重做
   */
  public get canRedo(): boolean {
    return this.currentIndex < this.snapshots.length - 1;
  }

  /**
   * 历史长度
   */
  public get historyLength(): number {
    return this.snapshots.length;
  }

  // ========== 私有方法 ==========

  /**
   * 生成唯一快照ID
   *
   * 格式：snapshot_{timestamp}_{random}
   */
  private generateSnapshotId(): string {
    return `snapshot_${Date.now()}_${Math.random().toString(36).substring(7)}`;
  }
}
