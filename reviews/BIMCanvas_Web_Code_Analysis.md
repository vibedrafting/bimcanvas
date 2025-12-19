# BIMCanvas_Web_Code_Analysis_Review

<!-- 
文件命名规范：[TopicName]_Review.md
例如：GeometryType_Review.md, AuthFlow_Review.md
版本：v2.0 (Integrated Discussion Guidelines)
-->

> [!IMPORTANT]
> **协作规则**
> 1. **追加式讨论**：所有新意见请以 `### [时间戳] [专家名]: [观点]` 格式追加在 "深入讨论" 章节。
> 2. **严禁修改**：禁止修改其他专家的已存档观点。
> 3. **优先级标注**：明确区分 `[Blocker]` (阻碍性) 与 `[Suggestion]` (建议性)。
> 4. **文本规范**：不要使用Emoji。

> [!TIP]
> **讨论原则**
> - **建设性**：反对时请提供替代方案。
> - **聚焦核心**：优先解决架构风险与数据一致性。
> - **拥抱共识**：寻找折中方案或最优解，避免无休止的争论。

## 1. 议题概述

- **主题**：BIMCanvas.Web 项目代码深度分析与改进建议
- **发起时间**：2025-12-19
- **参与者**：Gemini (AI Expert)
- **背景信息**：
  <!-- 描述当前现状、遇到的问题、相关代码路径或文档链接 -->
  当前 BIMCanvas.Web 项目已完成基础架构搭建，包含核心的 Three.js 渲染、交互逻辑、状态管理及 UI 框架。为了确保后续功能的稳健扩展（如 Agent 集成、更复杂的编辑功能），需要对现有代码库进行一次全面的深度扫描，识别架构设计、性能瓶颈、代码规范及 UI/UX 方面的潜在问题，并提出改进建议。

---

## 2. 初始观点

> [!NOTE]
> **生成指南 (Phase 1)**
> 请各位专家阅读议题背景，在此处追加初始观点。
> - **基础性**：初始观点是后续讨论的基础，要足够详细。
> - **独特性**：基于议题方向，产生自己独特的见解。
> - **独立性**：初始观点不要受其他专家影响，更不要对其观点作出回应（独立思考）。

<!-- 每位专家必须在此处生成详细的初始观点 -->

### 专家：Gemini

经过对项目代码的全面扫描与核心模块的深入阅读，我对 `BIMCanvas.Web` 项目进行了多维度的分析。整体来看，这是一个架构清晰、分层合理且具备良好扩展性的现代化 Web 3D 应用。

以下是详细的分析报告与改进建议：

#### 1. 架构与设计模式 (Architecture & Design Patterns)

**现状分析：**

- **分层清晰**：项目采用了经典的 `Store` (状态) -> `Service` (业务逻辑) -> `Builder` (构建) -> `Component` (UI) 的分层架构。
  - `ThreeSceneService` 作为 3D 场景的总管，协调各个 Builder。
  - `SceneBuilder`, `GridBuilder` 等负责具体的 Three.js 对象构建，职责单一。
  - `InteractionService` 集中处理交互逻辑，并使用 `Tool` 模式（`MoveTool`, `RotateTool`）来隔离不同的操作行为，扩展性很好。
- **状态管理**：使用 Pinia (`canvasStore`) 管理全局状态，并配合 `TimelineManager` 实现了基于快照的撤销/重做机制。
- **单例模式**：`ThemeService` 和 `SignalRService` 采用单例模式，便于全局访问和状态共享。

**改进建议：**

- **依赖注入 (DI)**：目前 Service 之间存在一定的耦合（例如 `ThreeSceneService` 直接实例化 Builder）。随着项目复杂度增加，可以考虑引入轻量级的 DI 容器或更严格的依赖注入方式，方便单元测试。
- **事件总线**：目前大量使用了 `window.dispatchEvent` 进行跨组件/服务通信（如 `bimcanvas:action-rotate`）。虽然解耦了，但全局事件容易造成追踪困难。建议封装一个类型安全的 `EventBus` 服务，或更多地利用 Pinia 的 Action/State 进行通信。

#### 2. 渲染与性能 (Rendering & Performance)

**现状分析：**

- **响应式重建**：`ThreeSceneService` 监听 `store.document` 的变化（`deep: true`），一旦数据改变就调用 `SceneBuilder.buildFromDocument` 重建整个场景。
  - **优点**：逻辑简单，保证视图与数据绝对一致。
  - **缺点**：性能隐患。对于大型文档，移动一个家具导致整个场景（包括墙体、网格）全部销毁重建，开销巨大。
- **按需渲染**：使用了 `requestAnimationFrame` 循环，且包含 `CSS2DRenderer`。

**改进建议：**

- **增量更新**：建议在 `SceneBuilder` 中实现增量更新逻辑。例如，当 `store.updateModule` 被调用时，只更新对应的 Mesh 位置/旋转，而不是重建整个场景。
- **对象池**：对于频繁创建/销毁的对象（如临时的 Ghost 对象或大量标注），可以考虑使用对象池技术。

#### 3. 交互与体验 (Interaction & UX)

**现状分析：**

- **工具模式**：`InteractionService` 采用状态机模式管理当前工具（Move, Rotate），逻辑清晰，避免了状态冲突。
- **UI 设计**：`RibbonToolbar` 采用了类似 "Dynamic Island" (灵动岛) 的设计，结合 `GlassButton` 和精细的动画，视觉效果出色。
- **主题系统**：`ThemeService` 实现了完善的深色/浅色模式切换，不仅改变 UI 颜色，还同步更新 Three.js 场景内的材质颜色，细节处理到位。

**改进建议：**

- **选中反馈**：目前选中物体主要依赖包围盒 (`BoxHelper`)。可以增加更丰富的视觉反馈，如高亮发光 (OutlinePass) 或材质颜色变化。
- **手势支持**：目前主要针对鼠标操作，建议增加对触控板手势（缩放、平移）的优化支持。

#### 4. 代码质量与规范 (Code Quality)

**现状分析：**

- **TypeScript**：项目全量使用 TypeScript，定义了清晰的接口 (`CanvasDocument`, `Wall`, `Module` 等)。
- **类型安全**：大部分代码类型安全，但在 `canvasStore` 和部分交互逻辑中仍存在 `any` 类型（如 `selectedObject`），这降低了类型系统的保护作用。
- **硬编码**：`SceneBuilder` 中存在一些硬编码的尺寸（如墙高 2800，门宽等）。

**改进建议：**

- **消除 `any`**：逐步将 `selectedObject` 等关键状态的类型具体化，使用联合类型或泛型。
- **配置常量化**：将墙高、默认材质参数等提取到单独的 `Config` 文件中，便于统一管理和调整。
- **单元测试**：目前未发现明显的单元测试文件。建议对核心算法（如几何计算、坐标转换）添加 Jest/Vitest 测试。

#### 总结

BIMCanvas.Web 是一个高质量的工程，代码结构清晰，技术选型合理。当前的架构足以支撑中等规模的应用开发。为了应对未来的扩展，建议优先解决**增量渲染**带来的性能瓶颈，并加强**类型定义**以提升代码健壮性。


### 专家：Claude (BIMCanvas.Web 多维度深度分析)

**分析方法论**

本次分析采用系统性代码审查方法，覆盖以下 7 个维度：架构设计、类型安全、内存管理、代码质量、性能优化、安全稳定、可维护性。通过阅读核心源文件（ThreeSceneService.ts、canvasStore.ts、InteractionService.ts、SceneBuilder.ts、SignalRService.ts、canvas.ts）进行深度分析。

---

#### 一、问题总览

| 维度 | 严重问题 | 中等问题 | 轻微问题 |
|------|---------|---------|---------|
| 架构设计 | 3 | 4 | 2 |
| 类型安全 | 4 | 3 | 1 |
| 内存管理 | 3 | 2 | 1 |
| 代码质量 | 2 | 5 | 4 |
| 性能优化 | 2 | 3 | 2 |
| 安全稳定 | 2 | 3 | 2 |
| 可维护性 | 1 | 4 | 3 |

共识别 **41 个具体问题**，其中 17 个严重、24 个中等。

---

#### 二、架构设计问题

**[Blocker] P1: ThreeSceneService 职责过重 (God Object)**

文件 `src/services/three/ThreeSceneService.ts` 共 374 行，承担了过多职责：场景管理、相机控制、Builder 协调、事件监听、主题切换、视口适配等。该类管理了 11 个不同的对象实例，严重违反单一职责原则。

```typescript
// ThreeSceneService.ts 管理了过多对象
private scene, camera, renderer, labelRenderer, animationId
private sceneBuilder, gridBuilder, outlineBuilder, labelBuilder, zoneBuilder
private layerManager, interactionService, viewportService, selectionManager...
```

影响：难以测试、难以维护、修改风险高。

**[Blocker] P2: 全局事件总线滥用**

多个文件使用 `window.addEventListener('bimcanvas:*')` 作为事件通信机制。事件类型是魔法字符串，缺乏类型安全，无法静态分析。ThreeSceneService.ts 第 147-183 行注册了 7 个全局事件监听，缺乏统一管理。

**[Blocker] P3: 服务实例化无依赖注入**

服务直接使用 `new` 实例化其他服务（如 ThreeSceneService.ts 第 106-110 行），形成紧耦合，无法替换实现、无法 Mock 测试。

**[Suggestion] P4-P7: 其他架构问题**

- Store 与 Service 边界模糊（canvasStore 直接实例化 SignalRService）
- Builder 缺乏统一接口（5 个 Builder 没有共同基类）
- 事件监听器生命周期管理不统一
- Services 目录扁平，缺少分层架构

---

#### 三、类型安全问题

**[Blocker] P8: `any` 类型滥用**

```typescript
// canvasStore.ts:14
const selectedObject = ref<any | null>(null);  // 应该是 Module | Wall | Column | Opening

// canvasStore.ts:101
const updateModule = (moduleId: string, updates: Partial<any>) => { ... }

// canvas.ts:68
items: any[];  // Module.items 缺乏类型定义

// canvas.ts:76
metadata?: any;  // CanvasDocument.metadata 缺乏类型
```

**[Blocker] P9: 类型定义不完整**

- `Opening.type` 使用魔法数字 `0/1` 而非枚举
- `Module.facing` 的 `string | Point2D` 联合类型不够精确，缺少 FacingDirection 枚举

**[Blocker] P10: 事件类型未定义**

所有 `CustomEvent` 的 detail 类型都是隐式 any，没有统一的事件类型定义文件。

**[Suggestion] P11-P14: 其他类型问题**

- Store 返回值缺乏明确接口定义
- 配置常量未使用 `as const` 或 `readonly`
- fitToScreen 参数类型为 any 而非 CanvasDocument
- TimelineManager 可使用泛型约束

---

#### 四、内存管理问题

**[Blocker] P15: 事件监听器移除 Bug**

```typescript
// ThreeSceneService.ts:364
public dispose() {
    window.removeEventListener('resize', this.onWindowResize.bind(this));
    // 错误：bind() 每次创建新函数，无法移除原监听器
}
```

这是一个确定性的内存泄漏问题。每次 dispose 都无法正确清理事件监听器。

**[Blocker] P16: Builder 重建时材质未复用**

ThreeSceneService.ts 第 229-234 行 `rebuildWithNewTheme()` 每次主题切换都创建新 Builder，旧 Builder 的材质未显式 dispose。

**[Blocker] P17: Three.js 资源泄漏风险**

`SceneBuilder.clearScene()` 遍历清理场景，但未处理嵌套 Group 的所有子对象。门窗创建的 Group 可能未完全清理。

**[Suggestion] P18-P19: 其他内存问题**

- CSS2DObject 未清理 DOM 元素
- 动画循环每帧 bind() 创建新函数

---

#### 五、代码质量问题

**[Blocker] P20: 重复代码**

```typescript
// ThreeSceneService.ts:342-343
this.camera.updateProjectionMatrix();
this.camera.updateProjectionMatrix();  // 重复调用

// ThreeSceneService.ts:80-81
this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;  // 重复设置
```

**[Blocker] P21: 调试代码遗留**

canvasStore.ts、SceneBuilder.ts、ThreeSceneService.ts 中存在大量 `console.log` 调试语句未清理。

**[Suggestion] P22-P26: 其他质量问题**

- 硬编码魔法数字（门高 2100、窗台高 900、setTimeout 100ms）
- 注释掉的代码未删除（buildFloor 方法）
- 缺乏一致的错误处理机制
- 方法过长（constructor 184 行，createDoor 80+ 行）
- 缺乏多边形有效性验证

---

#### 六、性能优化问题

**[Blocker] P27: 深度 Watch 触发过度重建**

```typescript
// ThreeSceneService.ts:121
watch(() => this.store.document, (newDoc) => { ... }, { deep: true });
// 任何模块属性变化都会触发完整场景重建
// 移动一个家具 -> 重建所有墙体、门窗、网格
```

这是当前最严重的性能问题，会导致 UI 卡顿。

**[Blocker] P28: 缺乏 Debounce/Throttle**

resize 事件无节流，store 变化无防抖，高频操作可能导致性能问题。

**[Suggestion] P29-P31: 其他性能问题**

- 场景遍历效率低（每次清理都完整遍历）
- Raycaster 未优化（递归检测所有对象包括不可选择的）
- 未使用 InstancedMesh 批量渲染相同对象

---

#### 七、安全与稳定性问题

**[Blocker] P32: 配置硬编码**

```typescript
// SignalRService.ts:9
.withUrl("http://localhost:5000/hubs/canvas")  // 应从环境变量读取
```

**[Blocker] P33: 缺乏错误边界**

Three.js 渲染错误可能导致整个应用崩溃，缺少 Vue ErrorBoundary 或 try-catch 保护。

**[Suggestion] P34-P36: 其他安全问题**

- SignalR 重连策略未实现（注释说明 "Retry logic could go here"）
- 服务器数据未经验证、JSON 未校验 schema
- 标签内容可能存在 XSS 风险

---

#### 八、可维护性问题

**[Blocker] P37: 缺乏单元测试**

没有 `tests/` 目录，没有测试配置，核心逻辑无测试覆盖。

**[Suggestion] P38-P41: 其他可维护性问题**

- 文件组织不一致（SignalRService.ts 未放入子目录）
- 缺少 JSDoc 注释
- 配置分散在多个文件
- 命名不一致（createWallMesh vs createDoor）

---

#### 九、改进建议优先级

**P0 - 必须修复（影响稳定性）**

| 编号 | 问题 | 建议方案 |
|-----|------|---------|
| P15 | 事件监听器移除 Bug | 保存 bound 函数引用 |
| P20 | 重复代码 | 删除重复行 |
| P27 | 深度 Watch 过度重建 | 改为细粒度 Watch |
| P32 | 配置硬编码 | 使用 .env 环境变量 |

**P1 - 建议修复（提升质量）**

| 编号 | 问题 | 建议方案 |
|-----|------|---------|
| P1 | ThreeSceneService 过重 | 拆分为 SceneManager, EventCoordinator |
| P8 | any 类型滥用 | 定义 SelectableObject 联合类型 |
| P21 | 调试代码遗留 | 使用 debugStore 或删除 |
| P28 | 缺乏 Debounce | resize 添加防抖 |

**P2 - 可以优化（锦上添花）**

| 编号 | 问题 | 建议方案 |
|-----|------|---------|
| P2 | 全局事件总线 | 引入 mitt 或 Vue 事件总线 |
| P3 | 无依赖注入 | 考虑 Vue provide/inject |
| P37 | 缺乏测试 | 添加 Vitest + Vue Test Utils |

---

#### 十、推荐重构路径

**Phase 1: 稳定性修复（紧急）**
1. 修复事件监听器 dispose bug（P15）
2. 删除重复代码（P20）
3. 环境变量配置化（P32）

**Phase 2: 类型系统加固**
1. 定义 SelectableObject, EventPayload 类型
2. 消除关键 any 类型
3. 添加 Opening.type 枚举

**Phase 3: 架构优化**
1. 拆分 ThreeSceneService
2. 统一事件系统
3. Builder 抽象接口

**Phase 4: 性能优化**
1. 细粒度 Watch 策略
2. Debounce/Throttle 保护
3. Raycaster 层过滤

**Phase 5: 工程化**
1. 添加单元测试
2. 配置 ESLint 规则
3. 补充关键 JSDoc

---

#### 十一、关键修复代码示例

**修复 P15: 事件监听器**

```typescript
// Before
constructor() {
    window.addEventListener('resize', this.onWindowResize.bind(this));
}
dispose() {
    window.removeEventListener('resize', this.onWindowResize.bind(this)); // 错误
}

// After
private boundOnResize: () => void;

constructor() {
    this.boundOnResize = this.onWindowResize.bind(this);
    window.addEventListener('resize', this.boundOnResize);
}
dispose() {
    window.removeEventListener('resize', this.boundOnResize); // 正确
}
```

**修复 P27: 细粒度 Watch**

```typescript
// Before: 任何变化都重建整个场景
watch(() => this.store.document, () => { rebuildAll() }, { deep: true });

// After: 只监听特定变化
watch(() => this.store.document?.modules, (newModules, oldModules) => {
    this.updateModules(newModules, oldModules);  // 差量更新
}, { deep: true });

watch(() => this.store.document?.walls, () => {
    this.rebuildWalls();  // walls 很少变化
});
```

---

#### 十二、总结

BIMCanvas.Web 作为早期项目，核心功能已实现，但在代码质量和工程化方面存在较多技术债务。

**优点**：
- 模块化设计思路清晰
- TypeScript 基础类型使用正确
- Builder 模式应用得当
- 主题系统设计良好

**需要改进**：
- 类型安全需加强（any 过多）
- 内存管理需修复（事件监听器泄漏）
- 性能需优化（深度 Watch）
- 工程化需补充（测试、文档）

**建议优先级**：P15 > P27 > P32 > P8 > P1 > 其他



### 专家：Codex: 初始观点（以“可构建、可维护、可扩展”为第一性原则）

**核心判断**
BIMCanvas.Web 已经具备“渲染引擎雏形 + 交互雏形 + 主题/UI 雏形”的骨架，但当前最关键的短板不是功能缺失，而是**工程可交付性与架构可控性**不足：构建不通过、生命周期管理薄弱、状态/事件/渲染耦合偏紧。这会直接阻碍后续接入 Server/Agent、引入更复杂编辑能力（撤销/协作/约束）以及长期稳定演进。

---

#### 1) 现状亮点（建议保留并强化）
- **服务化拆分方向正确**：`ThreeSceneService` 统筹，Builders（`SceneBuilder/GridBuilder/OutlineBuilder/LabelBuilder/ZoneBuilder`）做具体构建，交互侧（`InteractionService + tools + SelectionManager`）也已成形。
- **主题系统有成为“统一设计 Token”中心的潜力**：`ThemeService` 同时服务 3D 材质与 CSS Variables，这是后续做 AI Vision/Human View 双模式一致性的关键抓手。
- **层系统（LayerManager）方向正确**：这对“人类视图 vs Agent 视图”的可观测性非常关键，值得继续做成强约束（哪些对象必须在哪些 layer）。

---

#### 2) [Blocker] 我认为必须优先解决的阻碍点（否则后续迭代会持续返工）
1) **工程不可构建（build gate 失效）**
   - 现状：`npm run build`（`vue-tsc -b`）不过，存在 TS 严格模式下的硬错误与大量 unused。
   - 典型症状：`src/components/UI/CanvasToolbar.vue` 含残留 `<content>`、引用不存在的 `LayerManager.LAYER_AXES`；`FloatingLayerManager.vue` 对 `layers` 的索引类型不成立；多处 unused import/变量触发 `noUnusedLocals`。
   - 影响：任何“接入 Server/Agent、加协作/撤销”的改动都无法用 build 作为质量门槛，风险会指数级累积。

2) **生命周期与事件监听普遍存在泄漏风险（越用越乱）**
   - 现状：多个类里 `addEventListener` 使用 `bind(this)`，而 `removeEventListener` 再次 `bind(this)` 会导致无法移除；或 add 使用 bind、remove 使用未 bind 的原函数引用。
   - 影响：热重载/重复挂载/重复创建服务后，事件触发次数叠加，表现为操作越来越“敏感”、性能下降、难以复现的幽灵 bug。
   - 这是“系统性问题”，不是某个点修修补补能解决：需要引入统一的 `Disposable`/`AbortController` 机制或统一的事件总线封装来彻底收口。

3) **状态模型（Pinia document）与 Three 对象（Object3D）混用，导致类型与行为不可控**
   - 现状：`selectedObject` 在不同位置被当作“数据对象”或“3D 对象/其 userData”使用；工具与选择逻辑里有大量“兼容式判断”。
   - 影响：类型系统无法真正保护你；后续接入 patch/协作时，很容易出现“UI 显示选中但场景未选中”“数据更新但 mesh 未更新”等双写问题。

---

#### 3) 多维度诊断（架构/渲染/交互/协作/可观测性）
**A. 架构边界与依赖流（建议形成强约束）**
- 目前的真实依赖关系更接近：UI -> window event -> ThreeSceneService -> store -> watch -> rebuild。
- 问题在于：window event 没有类型契约、没有生命周期、没有单一入口；store 内存在副作用（如连接/监听启动），降低可测试性。
- 建议的“强约束流向”（后续讨论的基线）：  
  UI（意图） -> Command（可序列化） -> Store（唯一真相） -> Renderer（增量同步） -> Network（转发/回放）

**B. 渲染与性能（从“全量重建”升级为“增量更新”）**
- 现状：`ThreeSceneService` 对 `store.document` 做 deep watch 并触发 `SceneBuilder.buildFromDocument()` 等全量重建。
- 风险：移动/旋转一个 module 也可能触发重建大量几何、重建 CSS2D 标签与 grid，性能不可控，且引入更多资源释放压力。
- 建议：建立 `RenderRegistry`（id -> Object3D）作为渲染层的索引；对 modules 做增量更新（位置/旋转/尺寸），静态层（walls/columns/openings）只在 doc 替换时重建。

**C. 坐标与几何一致性（建议把“坐标变换”做成唯一模块）**
- 现状：代码中分散出现 `2D (x,y) -> 3D (x,0,-y)`、`delta2D_Y = -delta.z`、旋转符号取反等逻辑，且对“Y-Up vs Y-Down”存在口径混杂。
- 影响：吸附、旋转方向、朝向 facing、以及未来的回写/对齐线都会在细节上持续出错。
- 建议：引入单点真相：
  - `toWorld(Point2D) => Vector3`
  - `toModel(Vector3) => Point2D`
  - `rotate2D(point, center, theta)` 的 theta 定义清晰（以数据模型为准：`cartesian_mm_yUp`）
  让 Move/Rotate/Drag/fitToScreen/Label/Zone 全部走同一套转换函数。

**D. 交互与工具系统（需要收敛成“一个入口”）**
- 当前存在 Tool 体系（`MoveTool/RotateTool`）与 DragManager 体系并存的迹象，且 GhostManager 被多处重复 new。
- 建议：明确一种主交互范式（我更倾向 Tool 模式，因为未来更容易表达为 Command 并支持撤销/回放），其余路径要么合并为 Tool，要么暂时下线入口，避免输入事件竞争。

**E. 协作与撤销（先定“协议形态”，再堆功能）**
- `TimelineManager` 已存在，但与 server patch/操作日志尚未形成闭环。
- 建议先讨论并固定三件事：
  1) 前端对外发送的是“命令”（Move/Rotate/Delete with payload），还是“文档 patch”（JSON Patch/自定义 patch）？
  2) 撤销/重做是在“命令层”做可逆，还是在“状态快照层”做回滚？
  3) 多端协作冲突的策略（至少先做到：同一模块同一时刻只允许一个操作者；或服务端裁决顺序）

**F. 可观测性（Debug 不应侵入业务逻辑）**
- 目前 debug 日志与 store/selection 强耦合（甚至把 store 暴露到 window），短期方便但会污染边界。
- 建议：将 DebugConsole/DebugStore 作为“旁路订阅者”，订阅 Command 执行与渲染事件，而不是在核心路径里拼接字符串。

---

#### 4) 我建议的落地路线图（可作为后续讨论的行动基线）
**P0（质量门槛）**
- [Blocker] 修到 `npm run build` 通过；把 TS 严格规则当成质量门槛而不是阻碍。
- [Blocker] 统一事件监听与销毁机制：所有服务/组件必须可 `dispose()`，且 dispose 后不会残留监听器与 DOM/材质。

**P1（架构收敛）**
- [Suggestion] 统一选择模型：store 只存 `selectedId`（或 `{id,type}`），渲染层通过 registry 找 Object3D；避免在 store 里塞 Object3D 或混合结构。
- [Suggestion] 建立 `RenderRegistry` + 增量更新 modules；静态层只在 doc 替换时重建。
- [Suggestion] 单例化 GhostManager，并明确 ghost 的职责：预览层不应通过“修改原材质”实现（或至少要严谨 dispose/restore）。

**P2（面向 Server/Agent）**
- [Suggestion] 固定“命令/patch 协议”与版本化 schema；建议考虑从 JSON Schema 生成 TS 类型，避免手写类型与实际数据漂移。
- [Suggestion] 打通 server-update -> store -> renderer 的闭环，并定义可验证的验收标准（回放一致性、撤销一致性、跨端一致性）。

---

#### 5) 建议尽早确认的关键问题（否则实现会反复推倒）
1) `CanvasDocument` 的权威 schema 版本与字段口径：前端以哪个版本为准？是否由 schema 自动生成 TS？
2) 坐标系与方向口径：数据模型 `cartesian_mm_yUp` 的朝向（north/east 等）与 Three 顶视相机的 up 向量如何严格对应？
3) “模块”的表示方式：继续用 `bounds: Polygon2D(4点)` 做真相，还是升级为更利于交互/回写的 `center + size + rotation`（OBB），由渲染/导出层互转？
4) 协作模式：先做单用户确定性编辑 + 可回放，再做多端并发；还是直接进入多端协作？

---

## 3. 深入讨论

> [!NOTE]
> **讨论指南 (Phase 2)**
> 阅读其他专家的初始观点，在此处追加讨论。
> - **重点关注**：用户提出的观点、冲突的看法、达成的共识、需要进一步推进的方向。
> - **互动要求**：
>   - 适当的坚持自己合理的观点。
>   - 需要得到其他人的回复时，请直接 @专家名。
>   - 得到其他人的 @ 时，要积极的作出回应。

> **追加讨论格式示例**：
>
> `### [YYYY-MM-DD HH:mm] [专家名]: [观点标题]`
>
> 内容详情（不要使用标题格式）...

<!-- 请在此分隔线下方追加新的讨论内容 -->



---

## 4. 共识总结

<!-- 讨论结束并且得到用户明确要求后填写，汇总达成的共识和结论 -->
