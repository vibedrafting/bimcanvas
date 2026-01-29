# 后台截图一致性评审说明

> 目标：针对“后台截图与 Web 端画布显示样式不一致”的问题，梳理理想架构、现状与偏差点，并给出方向性建议（不涉及修改计划或实现细节）。

## 1. 理想设计诉求（重述）

- 画布上所有可见元素必须归属某一图层，并由图层显隐控制。
- 画布元素的样式（颜色、线型、网格间距、透明度、标签样式等）应由图层统一管理。
- UI 主题（明/暗）只影响控件与 UI，不应改变画布元素显示效果。
- 画布渲染应当是“可复现”的：同一数据 + 同一图层配置 = 同一画面。

## 2. 现有实现概览（与理想设计的差距）

### 2.1 图层体系现状

- 图层仅控制“可见性开关”，并不管理样式配置。  
  见：`BIMCanvas.Web/src/services/three/LayerManager.ts`

### 2.2 样式由主题驱动（而非图层驱动）

- 画布材质颜色来自 ThemeService：墙/柱/模块/门窗/AI 视觉等均按主题生成材质。  
  见：`BIMCanvas.Web/src/services/theme/ThemeService.ts`  
  见：`BIMCanvas.Web/src/services/builders/SceneBuilder.ts`

- 网格颜色与网格标签颜色来自主题。  
  见：`BIMCanvas.Web/src/services/builders/GridBuilder.ts`

- Zone/Exclusion/Outline/Label 的颜色与透明度由主题决定。  
  见：`BIMCanvas.Web/src/services/builders/ZoneBuilder.ts`  
  见：`BIMCanvas.Web/src/services/builders/ExclusionBuilder.ts`  
  见：`BIMCanvas.Web/src/services/builders/OutlineBuilder.ts`  
  见：`BIMCanvas.Web/src/services/builders/LabelBuilder.ts`

### 2.3 光照与渲染环境影响可视结果

- 画布使用光照与雾效，且墙/柱/门窗/模块材质为 MeshStandardMaterial，颜色会受光照影响。  
  见：`BIMCanvas.Web/src/services/three/ThreeSceneService.ts`  
  见：`BIMCanvas.Web/src/services/builders/SceneBuilder.ts`

### 2.4 SVG 渲染颜色与图层无关

- SVG 颜色来自 SVG 文件本身，且对黑色填充/描边做了强制替换为白色的策略。  
  见：`BIMCanvas.Web/src/services/builders/SVGModuleRenderer.ts`

### 2.5 网格间距配置不是图层样式

- 600/1000mm 网格由事件驱动切换，不属于图层配置。  
  见：`BIMCanvas.Web/src/services/three/ThreeSceneService.ts`  
  见：`BIMCanvas.Web/src/services/builders/GridBuilder.ts`

### 2.6 标签显隐存在“跨图层依赖”

- Zone 标签显示依赖 Labels 与 Zones 双图层状态，而非单层独立控制。  
  见：`BIMCanvas.Web/src/services/builders/LabelBuilder.ts`  
  见：`BIMCanvas.Web/src/services/three/ThreeSceneService.ts`

## 3. 为什么会出现“后台截图 vs Web 画布不一致”

- 主题参与了画布颜色：只要后台主题与 Web 当前主题不同，画布颜色就会不同。
- 预设/图层配置差异会导致叠加层缺失或多出（AI 视觉、Outline、Zones、SVG 等）。
- 光照与材质类型导致相同颜色在不同渲染环境下产生轻微偏移。
- SVG 自带颜色 + 替换策略可能与前端视觉期望不一致。

## 4. 是否要实现“理想设计”

- **可实现**：需要把画布样式从 Theme 中彻底剥离，建立“图层样式体系”，并约束渲染材质与环境。
- **代价高**：牵动全部 Builder 与渲染流程，属于体系级重构。
- **对当前目标而言并非必要**：一致性问题更可能来自“主题/预设/图层配置状态不一致”，并非图层体系本身失效。

## 5. 建议（围绕一致性目标）

### 5.1 优先建议（低成本一致性）

- 明确后台截图的“渲染 Profile”，与前端展示保持一致：  
  - 主题（dark/light）  
  - 图层预设（User/Agent）  
  - layerEnable/layerDisable  
  - 网格间距（600/1000）
- 前端截图/后台截图共用同一 Profile 参数，减少“隐性状态差异”。

### 5.2 中期建议（降低主题干扰）

- 引入“CanvasStyle”（画布样式）与“UI Theme”解耦：  
  - Theme 只管 UI，CanvasStyle 管画布  
  - 画布颜色在 CanvasStyle 中固定，主题切换不影响画布

### 5.3 长期建议（理想设计演进方向）

- 建立“LayerStyleRegistry”（图层样式中心）：  
  - 每层的颜色/线型/透明度/标签样式/间距等可配置  
  - 所有 Builder 只读取 LayerStyleRegistry
- 统一渲染材质策略（例如 2D 视图改为不受光照影响的材质）。

---

**结论**：  
为了“后台截图与 Web 画布一致”，优先解决“状态一致（主题/图层/网格间距）”即可，不必立刻推进全面的图层样式体系重构。后续若确有产品需求，再规划向理想架构演进。
