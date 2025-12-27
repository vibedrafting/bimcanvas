# Ribbon UI 实施计划 (Revit 风格)

## 目标
重构 Web 端顶部导航，实现类似 Revit 的 **双层界面布局**，以容纳 v3.0 架构的复杂功能。

## 界面架构

### 第一层：顶栏 (Top Bar / Quick Access)
*   **高度**：32px
*   **背景**：深色/主色调
*   **内容**：
    *   **左侧**：品牌标识 (BIMCanvas Icon)。
    *   **左中**：快速访问工具 (Quick Access Toolbar)
        *   Undo (撤销)
        *   Redo (重做)
        *   Save (保存/Commit)
    *   **右侧**：用户头像/登录状态 (预留)。

### 第二层：功能区 (Ribbon Toolbar)
*   **高度**：自适应 (展开/折叠)
*   **交互**：
    *   **Revit 风格折叠面板**：摒弃平铺式排列，采用独立的**面板窗口**封装每个分组。
    *   **默认折叠**：平时仅显示分组标题（Tab），点击后展开详细按钮面板。
    *   **视觉风格**：面板应具有窗口感，悬浮或嵌入于主界面上方，不占用过多画布空间。

> **重要约束**：
> *   本次重构**严禁修改或影响**现有的“灵动岛控件” (Dynamic Island)。
> *   灵动岛应继续作为独立组件悬浮于画布上方，与 Ribbon UI 互不干扰。

#### 详细分组与按钮定义 (基于 Step 0 研究)（已定稿）

| 分组 (Group) | 按钮/组件 | 说明 | 对应文档/功能 |
| :--- | :--- | :--- | :--- |
| **文件 (File)** | **打开项目** | 打开文件夹/.bcp | FileDrivenArchitecture |
| | **保存** | Git Commit | FileDrivenArchitecture |
| | **导入/导出** | .bcp 格式 | Architecture Phase 2/6 |
| **项目 (Project)** | **项目信息** | Metadata 查看 | Architecture §3.1 |
| | **配置** | 全局设置 | - |
| **策略 (Strategy)** | **策略选择器** | **Combo Dropdown**：显示当前策略，下拉列表包含策略选项及“新建/管理”入口 | FileDrivenArchitecture §1.1 |
| **变体 (Variant)** | **变体选择器** | **Combo Dropdown**：显示当前变体，下拉列表包含分支选项及“新建/管理/对比”入口 | FileDrivenArchitecture §3.1 |
| **AI 协作 (AI)** | **AI 对话** | Toggle 按钮 | Workflows §7.1 |
| | **Agent 设置** | 按钮 | - |
| **分区 (Zone)** | **区域编辑** | Toggle 按钮 | Workflows Phase 3 |
| **模块库 (Library)**| **本地库** | 按钮 | - |
| | **云端库** | 按钮 (MCP) | Architecture §2.1 |
| **编辑 (Edit)** | **选择** | 指针图标 | 现有功能 |
| | **移动** | 按钮 | 现有功能 |
| | **旋转** | 按钮 | 现有功能 |
| | **复制** | 按钮 | 现有功能 |
| | **删除** | 按钮 | 现有功能 |
| **视图 (View)** | **图层管理** | Dropdown | 现有功能 |
| | **主题切换** | Toggle | 现有功能 |

## 实施步骤

### Step 1: 实现 UI 框架与占位按钮
1.  创建 `AppHeader.vue` (Top Bar)。
2.  创建 `RibbonToolbar.vue` (Ribbon 容器)。
3.  创建所有分组组件 (`FileGroup`, `StrategyGroup`, `VariantGroup`, etc.)，内部放置**占位按钮**（仅 UI，无逻辑）。
4.  在 `MainLayout.vue` 中替换旧的 `CanvasToolbar`。

### Step 2: 绑定现有功能
1.  将 `canvasStore` 中的 Undo/Redo 绑定到 Top Bar。
2.  将 `EditGroup` 中的按钮绑定到 `dispatchAction`。
3.  将 `ViewGroup` 中的按钮绑定到图层和主题逻辑。
4.  其他新功能（策略、变体、AI）保持为 Mock/占位状态，等待后续后端 API 就绪。

## 验证计划
*   **Step 1 验收**：界面布局符合 Revit 风格，所有按钮可见且有 Tooltip，布局响应式正常。
*   **Step 2 验收**：旧有的绘图、编辑、视图功能在点击新按钮时正常工作。
