/**
 * 变体历史管理器 —— 撤销/重做与「纯指针式平级 + Zone 递归嵌套」模型对齐。
 *
 * 设计要点（见 docs 讨论）：
 * - 撤销文档单元 = 编辑目标 `(designZoneId, variantSlug)`，不是整工程投影。
 * - 每个目标一条独立栈：互不污染由结构保证（而非事后守卫）。
 * - 快照只存「该目标的模块」（扁平，zoneId=叶子）；落盘时由 Server 按该变体子分区重分叶子。
 * - 远程同步使受影响目标历史失效（invalidate），防陈旧本地 undo 覆盖远程改动。
 *
 * 与旧 TimelineManager 的区别：后者对整个 ProjectData 做全局快照、按实时选择回写，
 * 在多变体模型下会跨变体写错文件、复活脏指针（见 variant-edit-canonical-leak）。
 */

import type { Module } from '../../types/canvas';
import type { ChangeType } from '../../types/history';

/** 编辑目标：设计区 + 变体（null = canonical / adopted 当前生效方案）。 */
export interface EditTarget {
  designZoneId: string;
  variantSlug: string | null;
}

export type TargetKey = string;

/** 目标键：`${designZoneId}::${slug | '@canonical'}`。 */
export function targetKey(target: EditTarget): TargetKey {
  return `${target.designZoneId}::${target.variantSlug ?? '@canonical'}`;
}

interface EditEntry {
  modules: Module[];
  description?: string;
  changeType?: ChangeType;
  timestamp: number;
}

interface ScopedStack {
  entries: EditEntry[];
  index: number;
}

function cloneModules(modules: Module[]): Module[] {
  return JSON.parse(JSON.stringify(modules)) as Module[];
}

export class VariantHistory {
  private stacks = new Map<TargetKey, ScopedStack>();
  private readonly maxPerTarget: number;

  constructor(maxPerTarget = 50) {
    this.maxPerTarget = maxPerTarget;
  }

  /**
   * 首次投影某目标时播种 baseline（index 0 = 该目标加载/切换时的初态）。
   * 栈非空则不动——避免远程刷新/重复投影覆盖已有历史。
   */
  seedBaseline(target: EditTarget, modules: Module[]): void {
    const key = targetKey(target);
    const existing = this.stacks.get(key);
    if (existing && existing.entries.length > 0) return;
    this.stacks.set(key, {
      entries: [{ modules: cloneModules(modules), timestamp: Date.now() }],
      index: 0,
    });
  }

  /** 编辑后推入该目标的「之后态」快照（snapshot-after）。丢弃 redo 分支。 */
  push(
    target: EditTarget,
    modules: Module[],
    meta?: { description?: string; changeType?: ChangeType }
  ): void {
    const key = targetKey(target);
    let stack = this.stacks.get(key);
    if (!stack) {
      stack = { entries: [], index: -1 };
      this.stacks.set(key, stack);
    }
    if (stack.index < stack.entries.length - 1) {
      stack.entries = stack.entries.slice(0, stack.index + 1);
    }
    stack.entries.push({
      modules: cloneModules(modules),
      description: meta?.description,
      changeType: meta?.changeType,
      timestamp: Date.now(),
    });
    stack.index++;
    if (stack.entries.length > this.maxPerTarget) {
      stack.entries.shift();
      stack.index--;
    }
  }

  /** 撤销：返回该目标上一条快照的模块（已 clone）；无可撤销则 null。 */
  undo(target: EditTarget): Module[] | null {
    const stack = this.stacks.get(targetKey(target));
    if (!stack || stack.index <= 0) return null;
    stack.index--;
    return cloneModules(stack.entries[stack.index]!.modules);
  }

  /** 重做：返回该目标下一条快照的模块（已 clone）；无可重做则 null。 */
  redo(target: EditTarget): Module[] | null {
    const stack = this.stacks.get(targetKey(target));
    if (!stack || stack.index >= stack.entries.length - 1) return null;
    stack.index++;
    return cloneModules(stack.entries[stack.index]!.modules);
  }

  /** 当前 index 处该目标的模块（未 clone，仅供「是否变化」比较，勿改）。无栈/无条目则 null。 */
  peek(target: EditTarget): Module[] | null {
    const stack = this.stacks.get(targetKey(target));
    if (!stack || stack.index < 0) return null;
    return stack.entries[stack.index]?.modules ?? null;
  }

  canUndo(target: EditTarget | null): boolean {
    if (!target) return false;
    const stack = this.stacks.get(targetKey(target));
    return !!stack && stack.index > 0;
  }

  canRedo(target: EditTarget | null): boolean {
    if (!target) return false;
    const stack = this.stacks.get(targetKey(target));
    return !!stack && stack.index < stack.entries.length - 1;
  }

  /**
   * 使历史失效。
   * - 给 designZoneId：清该设计区下所有变体的栈（远程改动了该区）。
   * - 不给：全清（整工程远程刷新）。
   */
  invalidate(designZoneId?: string): void {
    if (!designZoneId) {
      this.stacks.clear();
      return;
    }
    const prefix = `${designZoneId}::`;
    for (const key of Array.from(this.stacks.keys())) {
      if (key.startsWith(prefix)) this.stacks.delete(key);
    }
  }

  clear(): void {
    this.stacks.clear();
  }
}
