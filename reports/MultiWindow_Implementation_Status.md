# Web 端多窗口功能实现现状汇报

> **日期**：2026-01-16
> **状态**：✅ **已完成后端对接**
> **目标**：梳理 `AICommandCenter.vue` 中关于多窗口（Multi-Window）的实现逻辑，对比 `Flow_Git_Operations.md` 标准文档。

---

## 一、 实现概述

Web 端的多窗口功能已完成**后端 Git Worktree API 对接**：
- **UI 交互**：窗口切换、新建窗口、关闭窗口、分支独占过滤
- **后端对接**：虚拟窗口创建/关闭现已调用 `GitWorktreeService` API
- **状态展示**：加载中、错误状态的 UI 反馈

---

## 二、 已完成的改造

### 2.1 新增文件

| 文件 | 说明 |
|------|------|
| `src/types/worktree.ts` | Worktree 类型定义（与后端 DTO 对应） |
| `src/services/GitWorktreeService.ts` | Worktree API 服务封装 |

### 2.2 数据模型扩展

```typescript
// AICommandCenter.vue 行 100-110
interface ChatWindow {
  id: string;
  name: string;
  branchId: string;
  messages: ChatMessage[];
  isPrimary: boolean;
  // === 新增后端关联字段 ===
  worktreeName?: string;  // 后端 Worktree 名称
  isLoading?: boolean;    // 加载状态
  error?: string | null;  // 错误信息
}
```

### 2.3 核心方法改造

#### addWindow（行 217-291）

```typescript
const addWindow = async (branchName: string) => {
  // 1. 生成 worktreeName
  const worktreeName = `window-${Date.now()}`;

  // 2. 先在 UI 显示加载状态
  const newWindow: ChatWindow = {
    // ...
    worktreeName,
    isLoading: true,
    error: null
  };
  windows.value.push(newWindow);

  // 3. 调用后端 API
  try {
    await GitWorktreeService.createWorktree({
      name: worktreeName,
      branch: branchName
    });
    // 成功更新状态
  } catch (error) {
    // 显示错误，3秒后自动移除
  }
};
```

#### closeWindow（行 173-216）

```typescript
const closeWindow = async (id: string) => {
  // 1. 检查非主窗口、非加载中
  // 2. 设置加载状态
  win.isLoading = true;

  // 3. 调用后端 API
  try {
    await GitWorktreeService.deleteWorktree(win.worktreeName, false);
  } catch (error) {
    // 即使失败也关闭窗口（后端可能已不存在）
  }

  // 4. 从 UI 移除
  windows.value.splice(index, 1);
};
```

### 2.4 UI 状态展示

**模板改动**（行 1392-1438）：
- 添加 `.loading` / `.error` class 绑定
- 添加加载指示器 `⏳` 和错误指示器 `⚠️`
- 加载中时隐藏关闭按钮

**样式改动**（行 2230-2279）：
- `.window-tab.loading` - 半透明、等待光标
- `.window-tab.error` - 红色边框、红色背景
- `.tab-status` - 状态指示器动画

---

## 三、 与标准架构的对比

| 功能点 | 目标架构 | 实现状态 |
|--------|----------|----------|
| 虚拟窗口创建 | 调用 `POST /api/git/worktrees` | ✅ 已实现 |
| 虚拟窗口关闭 | 调用 `DELETE /api/git/worktrees` | ✅ 已实现 |
| 分支标识符 | 统一使用 Branch Name | ✅ 已实现 |
| 加载状态展示 | UI 反馈 | ✅ 已实现 |
| 错误处理 | 显示错误、自动恢复 | ✅ 已实现 |
| 真窗口分支同步 | 用户主动切换 | ✅ 现有逻辑 |

---

## 四、 测试验证

### 手动测试步骤

```bash
# 1. 启动 Server
cd BIMCanvas.Server && dotnet run

# 2. 启动 Web
cd BIMCanvas.Web && npm run dev

# 3. 在浏览器中测试
# - 点击 "+" 创建新窗口，选择一个分支
# - 观察加载状态（⏳ 图标）
# - 验证后端创建 Worktree: python test_worktree.py --list
# - 关闭窗口，验证后端删除 Worktree

# 4. 清理测试残留
cd BIMCanvas.Server/tests && python test_worktree.py --clean
```

### 验证点

- [x] 创建窗口时显示加载状态
- [x] 创建成功后移除加载状态
- [x] 创建失败时显示错误并自动移除
- [x] 关闭窗口时调用后端 API
- [x] 主窗口不可关闭
- [x] 加载中的窗口不可关闭

---

## 五、 API 服务封装

### GitWorktreeService.ts

```typescript
export class GitWorktreeService {
  static async getWorktrees(): Promise<WorktreeInfo[]>;
  static async createWorktree(request: CreateWorktreeRequest): Promise<CreateWorktreeResponse>;
  static async deleteWorktree(name: string, deleteBranch?: boolean): Promise<DeleteWorktreeResponse>;
}
```

### 类型定义 worktree.ts

```typescript
interface WorktreeInfo {
  name: string;
  path: string;
  branch: string | null;
  commitHash: string | null;
  isMain: boolean;
}

interface CreateWorktreeRequest {
  name: string;
  branch: string;
  baseBranch?: string;
}
```

---

## 六、 后续优化建议

1. **Worktree 状态同步**：页面刷新时从后端同步已存在的 Worktree
2. **关闭时确认**：关闭窗口前询问是否同时删除分支
3. **批量清理**：提供清理所有虚拟窗口的功能
4. **持久化**：将窗口状态保存到 localStorage，重启后恢复
