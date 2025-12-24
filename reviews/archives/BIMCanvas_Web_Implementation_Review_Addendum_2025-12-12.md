# BIMCanvas.Web 实现评审补充报告（基于 Gemini3Pro 代码）

> **版本**：v1.1（补充版）  
> **评审时间**：2025-12-12  
> **评审对象**：`BIMCanvas.Web/`（Gemini3Pro 生成的前端代码）  
> **参照文档**：`plans/Web_Implementation_Plan.md`、`docs/Architecture.md`、`BIMCanvas.Server/`、`docs/Schema-JSON.md`、Core 数据模型

---

## 0. 结论先行

Phase 1 的 Three.js 赛博朋克渲染与基础交互**整体达标**，代码结构清晰，视觉参数与计划一致；但当前 Web 与“真实 JSON 契约/后端实现”之间存在**若干关键不一致**（枚举序列化、Polygon2D 格式、API 基址/端点、SignalR Hub 缺失），会直接阻塞 Phase 2 的联调与真实数据接入。

---

## 1. 代码结构概览（现状）

实际目录与计划保持一致，核心文件如下：

- 渲染核心：`src/services/ThreeSceneService.ts`  
- 场景构建：`src/services/SceneBuilder.ts`  
- 交互拾取/拖拽：`src/services/InteractionService.ts`  
- 动态网格：`src/services/GridSystem.ts`  
- REST/SignalR 客户端：`src/services/ApiService.ts`、`src/services/SignalRService.ts`  
- Pinia 状态：`src/stores/canvasStore.ts`  
- UI：`src/components/UI/*`、渲染容器 `src/components/Canvas/ThreeCanvas.vue`

与计划的轻微偏离点：
- 计划中 `WallBuilder/ZoneBuilder/ModuleBuilder` 拆分为独立类；现代码使用单一 `SceneBuilder` 聚合实现（功能等价，但后续扩展时建议按职责拆分）。

---

## 2. 对照 `Web_Implementation_Plan.md` 的落实情况

### 2.1 Phase 1（核心重构）

| 计划项 | 代码现状 | 备注 |
|---|---|---|
| Vue3 + Vite + TS + Pinia 基础设施 | 已完成 | `package.json`、`main.ts`、`canvasStore.ts` |
| ThreeSceneService + OrthographicCamera | 已完成 | 相机/背景/MapControls 逻辑完整 |
| UnrealBloomPass 霓虹泛光 | 已完成 | 参数与计划完全一致 |
| 墙/柱/区/模块渲染 | 已完成 | `SceneBuilder` 对应构建 |
| Raycaster 拾取与选择 | 已完成 | `InteractionService` |
| CAD-style Pan/Zoom + Zoom Extents | 已完成 | 自定义“光标中心缩放” + `zoomExtents` |
| Toolbar/PropertyPanel/StatusBar | 已完成 | 赛博面板风格匹配 |

**Phase 1 完成度：≈100%（按计划口径）**

### 2.2 Phase 2（功能完善）

| 计划项 | 代码现状 | 备注 |
|---|---|---|
| 网格“仅拖拽时显示” | **已实现** | `InteractionService` 在拖拽时 `gridSystem.setVisible(true/false)` |
| 模块旋转 | 未实现 | 交互层无旋转输入/数据回写 |
| Ambient Particles 粒子 | 未实现 | ThreeSceneService 未引入粒子系统 |
| WallFinish 显示暂缓 | 符合 | SceneBuilder 未处理 wallFinishes（计划允许） |
| SignalR 实时同步 | 前端骨架已建，未联调 | 见 §4.2 |

### 2.3 Phase 3（高级特性）

均未开始（符合计划）。

---

## 3. 与 `Architecture.md` 的差异

### 3.1 有意架构变更（合理）

| 领域 | Architecture.md | 现实现 | 结论 |
|---|---|---|---|
| 渲染技术 | JSON → SVG | JSON → Three.js(WebGL) | **计划 v2.0 的有意变更** |
| 交互实现 | Konva/Fabric 备选 | Three.js + Raycaster | 合理且与视觉目标一致 |

**建议**：在 `docs/Architecture.md` 的 §6.3 Web 小节补记“SVG → Three.js”改动，以免后续多人协作出现认知偏差。

### 3.2 架构文档中仍需同步的点

- Web 章节示例 Store/渲染器仍按 SVG 设计（`SvgRenderer`、`selectedElementIds[]`、`commitChanges()` 等），与当前 Three.js 版本不一致。  
- 0.2“只有 zones 无 rooms”的早期决策表述，已与 Core v2.5+ 的 `Rooms` 列表不一致；Web/types 已包含 `rooms`。  

---

## 4. 与 `BIMCanvas.Server/` 的匹配检查

### 4.1 REST API

**匹配：**
- Web 已实现 `GET /api/canvas/{id}` 与 `POST /api/canvas`（`ApiService.ts`），Server `CanvasController.cs` 已提供。

**不匹配/阻塞点：**
1. **开发环境基址问题**  
   - Web `ApiService` 的 `baseURL: '/api'` 默认指向 `http://localhost:3000/api`。  
   - Server 实际运行在 `http://localhost:5000/api/canvas`，Vite 配置无 proxy。  
   - 结果：本地 dev 环境下 REST 调用会 404/跨域失败。  
   - 处理方向：Vite 增加 proxy 或改为可配置的绝对基址。

2. **提交端点缺失**  
   - Web 预留 `POST /api/canvas/{id}/commit`（Phase 2 的 ChangeSet 提交）。  
   - Server 尚无该端点与变更应用逻辑。  

### 4.2 SignalR

- Web `SignalRService` 期望 Hub：  
  - 事件：`DocumentUpdated`  
  - 方法：`JoinCanvas(canvasId)`  
  - Hub URL 约定：通常为 `/hubs/canvas`  
- Server 当前无 SignalR 注册与 Hub 实现（`Program.cs` 未 `AddSignalR/MapHub`）。  

**影响**：实时同步完全不可用，是 Phase 2 联调的首要阻塞项。

### 4.3 JSON 数据契约（最关键的不一致）

1. **枚举序列化格式**
   - Web `canvas.d.ts` 期望 `RoomType/ZoneTag/OpeningType/...` 为 **snake_case 字符串**。  
   - 实际 demo JSON 与 Server 当前 System.Text.Json 默认行为均为 **数字枚举**（例如 `room.type: 3`）。  
   - 根因：Server 使用 System.Text.Json 且未配置 `JsonStringEnumConverter`；Core 的枚举无字符串/命名策略属性。
   - 需要统一“字符串 vs 数字”以及命名策略（camelCase/snake_case）。

2. **Polygon2D 的 JSON 形态**
   - Web 类型定义：`type Polygon2D = Point2D[]`，仅支持简单数组。  
   - Core `Polygon2D`（`Models/Primitives/Polygon2D.cs`）支持两种 JSON：  
     - 简单：`[[x,y], ...]`  
     - 含洞：`{ "shell": [[x,y],...], "holes": [[[x,y],...],...] }`  
   - demo 中 `rooms[].boundary` 已出现 `{shell, holes}` 形式。  
   - 结果：Web 当前无法正确类型化/渲染带洞多边形（至少 rooms、未来 zones 也可能有洞）。  

3. **字段可选性与缺省**
   - demo 的 `modules[]` 缺少 `moduleId/zoneId` 等字段；Web 类型将其设为必填（`zoneId`、`moduleId`）。  
   - 需明确：真实数据是否必然具备这些字段；否则 Web 需放宽可选性或在 Normalizer 中补齐。

---

## 5. Web 代码层面值得尽快处理的问题

1. **`ThreeCanvas.vue` 缺少 `THREE` import**  
   - 文件内使用 `THREE.Object3D`，但未 `import * as THREE from 'three'`。  
   - 预计会导致 `vue-tsc` 报错/构建失败。

2. **事件监听移除方式错误（潜在泄漏）**  
   - `ThreeSceneService.dispose()`、`InteractionService.dispose()` 使用 `removeEventListener(..., this.xxx.bind(this))`，bind 每次返回新函数，无法移除原监听。  

3. **Vite 模板残留样式影响布局**  
   - `src/style.css` 仍是默认模板（`body display:flex`、`#app max-width` 等），与全屏画布目标冲突。  

4. **UI 中存在乱码字符**  
   - `PropertyPanel.vue` 关闭按钮与 `StatusBar.vue` 状态指示符出现 `¡Á`、`¡ñ`。  
   - 属于编码污染，应替换为正常符号（如 `×`、`●`）。

5. **模块拖拽对象拆分问题**  
   - 当前模块由 line + mesh 两个对象组成，拖拽时需要同步移动同 id 的兄弟对象；已有临时实现，但更稳妥方式是 SceneBuilder 使用 `THREE.Group` 聚合模块。  

---

## 6. 下一步开发建议（按优先级）

### 6.1 高优先级（阻塞性）

1. **统一 JSON 契约（Core/Server/Web 三方）**
   - 明确枚举序列化格式与命名策略。  
   - 明确 Polygon2D 统一输出（简单数组 / shell+holes / vertices+holes），并在 Web 侧做 Normalizer 兼容。  
2. **Server 实现 SignalR Hub + 路由**
   - `CanvasHub` 支持 `JoinCanvas`、`DocumentUpdated` 广播。  
3. **Server 实现 ChangeSet Commit 端点**
   - `POST /api/canvas/{id}/commit` 应用变更、版本递增、触发 Hub 通知。  
4. **Web 修复构建/显示基础问题**
   - 补 `THREE` import、修复乱码、清理模板样式、修正事件解绑。  

### 6.2 中优先级（Phase 2 功能）

5. **模块旋转交互**  
   - 交互输入（键盘 R / Gizmo）+ Three 对象旋转 + 写回 modules[].facing/rotation。  
6. **SceneBuilder 拆分职责/引入 Group**
   - 为后续 wallFinish/zone/exclusion 渲染留扩展点。  
7. **SignalRService 接入 Store**
   - 自动 join canvas、收到 DocumentUpdated 后合并/替换 document。  

### 6.3 低优先级（体验/性能）

8. **Ambient Particles**  
9. **节流/性能优化（pointermove throttle、InstancedMesh）**  
10. **多选/框选/Gizmo**（Phase 3）

---

## 7. 备注

`reviews/BIMCanvas_Web_Implementation_Review.md` 已覆盖 Phase 1 完成度与主要阻塞项，本补充报告重点补齐了**数据契约、开发基址与代码质量层面**的额外风险点，建议与原评审合并阅读，用于 Phase 2 迭代排期。

