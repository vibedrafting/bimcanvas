# BIMCanvas 首页功能需求

> 本文档聚焦功能诉求与交互逻辑，供 UI 设计参考。不含实现细节。

---

## 1. 背景与目标

当前 BIMCanvas 启动后直接加载单一项目进入工作区，没有首页。用户无法浏览已有项目、无法切换项目、无法查看打开历史。

**目标**：增加首页作为应用入口，让用户能够管理和快速访问项目。

### 设计约束

| 约束项 | 说明 |
|--------|------|
| 不支持新建空白项目 | 项目必须从 Revit 导出 .bcp 开始，首页只有「打开」和「导入」 |
| 启动即首页，可返回 | 启动显示首页 → 选择项目进入工作区 → 可随时返回首页 |
| 仅管理默认目录 | 只扫描 `~/Documents/BIMCanvas/Projects/` 下的项目，不支持任意路径 |
| 单项目模式 | 一次只能打开一个项目，切换需先关闭当前项目 |

---

## 2. 功能总览

| 编号 | 功能 | 描述 | 优先级 |
|------|------|------|--------|
| F1 | 项目列表 | 扫描默认目录，展示所有可用项目 | P0 |
| F2 | 打开项目 | 从列表中选择项目文件夹，加载进入工作区 | P0 |
| F3 | 导入 .bcp | 选择 .bcp 文件，解压到默认目录，自动打开 | P0 |
| F4 | 最近打开记录 | 记录打开历史，按时间排序，支持快速重开 | P1 |
| F5 | 关闭项目 / 返回首页 | 从工作区卸载项目，回到首页 | P0 |
| F6 | 项目元数据展示 | 每个项目展示名称、日期、策略数等摘要信息 | P1 |
| F7 | 项目删除 | 从默认目录中删除项目文件夹 | P2 |

---

## 3. 视图状态机

应用有两个主视图：**首页** 和 **工作区**。

```
App 启动 → 【首页】

【首页】
  ├─ 点击项目条目 → 加载项目 → 【工作区】
  └─ 导入 .bcp → 解压 + 加载 → 【工作区】

【工作区】
  └─ 点击「返回首页」→ 关闭项目 → 【首页】
```

**要点**：
- 启动时始终进入首页（不自动加载上次项目）
- 首页 ↔ 工作区可双向切换
- 从工作区返回首页前，如有未保存变更需提示

---

## 4. 各功能详细需求

### F1 项目列表

**触发**：进入首页时自动扫描。

**项目合法性判断**：
- 是文件夹（非文件）
- 包含 `project.json` 且可正常解析

**每个项目展示的信息**：

| 字段 | 说明 |
|------|------|
| 项目名称 | `project.json` 中的 `name`，回退到文件夹名 |
| 最后修改时间 | `project.json` 中的 `updatedAt`，回退到文件系统时间 |
| 策略数量 | `project.json` 中 `schemes[]` 数组长度 |
| 当前策略名 | `activeSchemeId` 对应的策略名称 |

**排序**：
1. 最近打开过的靠前（结合 F4 最近记录）
2. 未打开过的按修改时间倒序

**异常状态**：
- 文件夹存在但 `project.json` 缺失/损坏 → 标记为「异常项目」，仍显示但置灰
- 默认目录下无项目 → 显示空状态 + 引导导入提示

**刷新时机**：
- 首页初次加载
- 从工作区返回首页
- 导入完成后

### F2 打开项目

**触发**：点击项目列表中的某个项目条目。

**流程**：
```
点击项目条目
  → 调用后端「打开文件夹」接口
  → 后端验证合法性 + 初始化缺失文件
  → 前端获取项目数据
  → 切换到工作区视图
  → 更新最近打开记录
```

**异常处理**：
- 文件夹已被删除 → 提示并从列表移除
- `project.json` 损坏 → 提示需修复
- `baseline/` 缺失 → 提示需重新从 Revit 导出

### F3 导入 .bcp

**触发**：首页上的「导入」按钮。

**流程**：
```
点击「导入」
  → 打开文件选择器（仅 .bcp 文件）
  → 上传到后端
  → 后端检测是否同名项目已存在
    → 不冲突：解压 + 初始化 → 成功
    → 冲突：弹出对话框
      → 覆盖：删除旧项目，重新解压
      → 使用已有：直接打开已存在的项目
      → 取消：回到首页
  → 导入成功后自动打开项目（进入工作区）
  → 新项目出现在列表中
```

**冲突对话框内容**：
- 标题：「项目已存在」
- 正文：「项目 '{name}' 已存在于本地，如何处理？」
- 选项：覆盖 / 使用已有 / 取消

### F4 最近打开记录

**功能**：记录用户打开项目的历史。

**行为**：
- 每次打开项目（F2）或导入成功（F3）时更新记录
- 记录内容：项目名、路径、最后打开时间、打开次数
- 最多保留 20 条
- 超出时移除最早的记录
- 影响项目列表排序（最近打开的靠前）
- 项目文件夹已不存在的记录标记为不可用（不自动删除）

### F5 关闭项目 / 返回首页

**触发**：工作区内的操作（如 AppHeader 中的返回按钮）。

**流程**：
```
点击「返回首页」
  → 检查是否有未保存变更
    → 有变更：弹出确认对话框
      → 保存并关闭：先保存，再关闭
      → 不保存关闭：直接关闭
      → 取消：取消返回
    → 无变更：直接关闭
  → 调用后端「关闭项目」接口
  → 清空前端状态
  → 切换到首页视图
  → 重新扫描项目列表
```

**未保存变更检测**：
- 后端检查 Git 是否有未提交的变更
- 对话框文案：「有未保存的设计变更，是否仍要关闭？」

### F6 项目元数据展示

项目列表中每个条目展示的摘要信息，来源于 `project.json`。

扫描时只读取 `project.json`（轻量），不加载 baseline、computed 等大文件。

**可展示字段**：

| 字段 | 来源 |
|------|------|
| 项目名称 | `project.json → name`（回退到文件夹名） |
| 最后修改时间 | `project.json → updatedAt`（回退到文件系统时间） |
| 创建时间 | `project.json → createdAt` |
| 策略数量 | `project.json → schemes.length` |
| 当前策略名 | `activeSchemeId` 对应的 scheme.name |
| 项目版本 | `project.json → version` |

### F7 项目删除

**触发**：项目条目上的删除操作（右键菜单或删除按钮）。

**流程**：
```
触发删除
  → 二次确认对话框：「确定删除项目 {name}？此操作不可恢复。」
  → 确认 → 调用后端删除接口
  → 后端删除文件夹
  → 刷新项目列表
  → 从最近打开记录中移除
```

**安全约束**：
- 禁止删除当前正在打开的项目（工作区中的）
- 必须二次确认

---

## 5. API 接口定义

### 5.1 获取项目列表

```
GET /api/project/list
```

**响应 200**：
```json
{
  "projects": [
    {
      "name": "demo_1",
      "folderPath": "C:/.../Projects/demo_1",
      "createdAt": "2026-01-15T10:00:00",
      "updatedAt": "2026-03-12T14:30:00",
      "schemeCount": 2,
      "activeScheme": "Default",
      "version": "3.0",
      "isValid": true,
      "errorMessage": null
    },
    {
      "name": "broken_project",
      "folderPath": "C:/.../Projects/broken_project",
      "createdAt": null,
      "updatedAt": null,
      "schemeCount": 0,
      "activeScheme": null,
      "version": null,
      "isValid": false,
      "errorMessage": "project.json 无法解析"
    }
  ],
  "projectsRoot": "C:/.../Documents/BIMCanvas/Projects"
}
```

### 5.2 打开项目文件夹

```
POST /api/project/open-folder
```

**请求**：
```json
{ "folderPath": "C:/.../Projects/demo_1" }
```

**响应 200（成功）**：
```json
{ "status": "Success", "projectPath": "C:/.../Projects/demo_1" }
```

**响应 400（无效项目）**：
```json
{ "status": "Error", "message": "project.json 不存在或无法解析" }
```

**响应 404（目录不存在）**：
```json
{ "status": "Error", "message": "项目目录不存在" }
```

### 5.3 关闭当前项目

```
POST /api/project/close
```

**响应 200（无未保存变更）**：
```json
{ "status": "Success", "hasUnsavedChanges": false }
```

**响应 200（有未保存变更，需确认）**：
```json
{ "status": "Warning", "hasUnsavedChanges": true, "message": "有未提交的设计变更" }
```

确认后强制关闭：
```
POST /api/project/close?force=true
```

### 5.4 删除项目

```
DELETE /api/project/{name}
```

**响应 200**：
```json
{ "status": "Success" }
```

**响应 400（当前正在使用）**：
```json
{ "status": "Error", "message": "无法删除当前正在打开的项目" }
```

### 5.5 获取最近打开记录

```
GET /api/project/recent
```

**响应 200**：
```json
{
  "projects": [
    {
      "name": "demo_1",
      "folderPath": "C:/.../Projects/demo_1",
      "lastOpenedAt": "2026-03-13T14:30:00",
      "openCount": 5,
      "exists": true
    },
    {
      "name": "old_project",
      "folderPath": "C:/.../Projects/old_project",
      "lastOpenedAt": "2026-02-10T09:15:00",
      "openCount": 1,
      "exists": false
    }
  ]
}
```

### 5.6 现有接口（导入 .bcp）

以下接口已存在，无需修改，首页直接调用：

| 接口 | 说明 |
|------|------|
| `POST /api/project/upload` | 上传 .bcp 文件（带冲突检测） |
| `POST /api/project/upload-resolve?resolution=Overwrite\|UseExisting` | 冲突解决 |
| `GET /api/project` | 获取当前项目完整数据（工作区使用） |
| `GET /api/project/status` | 获取当前项目状态 |
| `GET /api/project/export` | 导出当前项目为 .bcp |

---

## 6. 数据结构

### 最近打开记录（持久化到 Server 端）

```json
// ~/Documents/BIMCanvas/recent_projects.json
{
  "version": 1,
  "maxCount": 20,
  "projects": [
    {
      "name": "demo_1",
      "folderPath": "C:/Users/xxx/Documents/BIMCanvas/Projects/demo_1",
      "lastOpenedAt": "2026-03-13T14:30:00",
      "openCount": 5
    }
  ]
}
```

### 项目元数据（来自 project.json）

```json
// ~/Documents/BIMCanvas/Projects/{name}/project.json
{
  "id": "proj_demo_1",
  "name": "demo_1",
  "version": "3.0",
  "createdAt": "2025-12-25T14:30:25",
  "updatedAt": "2025-12-25T15:45:10",
  "activeSchemeId": "s1_Default",
  "schemes": [
    { "id": "s1_Default", "path": "./schemes", "name": "Default" }
  ]
}
```

---

## 7. 边界情况

| 场景 | 预期行为 |
|------|---------|
| 默认目录下无任何项目 | 显示空状态 + 引导用户导入 .bcp |
| 项目文件夹被外部删除 | 列表刷新时自动移除；最近记录标记 `exists: false` |
| 异常项目（project.json 损坏） | 列表中显示但置灰，tooltip 提示原因 |
| 正在打开的项目被外部删除 | 弹出错误提示，自动回到首页 |
| 导入后同名项目已存在 | 冲突对话框：覆盖 / 使用已有 / 取消 |
| 工作区有未保存变更时返回首页 | 确认对话框：保存并关闭 / 不保存关闭 / 取消 |
| 两个浏览器窗口同时访问 | 共享同一 Server 状态，看到同一项目（单项目模式） |
