# BIMCanvas.Web 视觉与体验升级实施计划

> **目标**：将 BIMCanvas.Web 从“功能原型”升级为符合 "Calm Tech" 设计哲学的“专业设计工作台”。
> **策略**：分阶段迭代，优先建立系统，再打磨细节。

---

## Phase 1: 建立设计系统 (Design System Foundation)
**优先级**：🔥 最高 (Urgent & Important)
**目标**：消除硬编码样式，建立统一的视觉语言变量库。

### 1.1 定义全局 CSS 变量 (Design Tokens)
- [ ] 创建 `src/styles/variables.css` (或 `.scss`)。
- [ ] **Color Palette (色彩)**：
    -   `--bg-canvas`: `#0a0a0f` (深邃背景)
    -   `--surface-glass`: `rgba(10, 10, 15, 0.6)` (毛玻璃基底)
    -   `--surface-glass-hover`: `rgba(20, 20, 30, 0.7)`
    -   `--border-subtle`: `rgba(255, 255, 255, 0.08)` (微弱边框)
    -   `--text-primary`: `#e0e0e0`
    -   `--text-secondary`: `rgba(255, 255, 255, 0.5)`
    -   `--accent-blue`: `#3b82f6` (选中/高亮)
    -   `--accent-glow`: `rgba(59, 130, 246, 0.2)` (微光)
- [ ] **Typography (排版)**：
    -   `--font-sans`: `'Inter', system-ui, ...`
    -   `--font-mono`: `'JetBrains Mono', monospace` (用于 ID/坐标)
- [ ] **Spacing & Radius (间距与圆角)**：
    -   `--radius-md`: `8px`
    -   `--radius-sm`: `4px`

### 1.2 重构全局样式
- [ ] 在 `style.css` 中引入变量文件。
- [ ] 清理 `body` 和 `#app` 的硬编码样式，替换为变量引用。

---

## Phase 2: UI 质感升级 (UI Polish & Glassmorphism)
**优先级**：High (Important)
**目标**：实现“悬浮于画布之上”的高级感 UI。

### 2.1 封装基础 UI 组件
- [ ] **GlassButton** (`components/UI/base/GlassButton.vue`)：
    -   移除默认边框和背景。
    -   应用 `backdrop-filter: blur(10px)`。
    -   实现平滑的 Hover/Active 状态过渡 (transition: all 0.2s)。
    -   支持 `variant`: `primary` (带微蓝光), `ghost` (纯透明), `danger`。
- [ ] **IconBadge** (`components/UI/base/IconBadge.vue`)：
    -   用于显示 "Calm Tech" 等标签，统一圆角和内边距。

### 2.2 重构核心面板
- [ ] **CanvasToolbar**：
    -   使用 `GlassButton` 替换原生 `<button>`。
    -   优化布局间距，使用 Flex gap 代替 margin。
    -   优化 Logo 和标题排版，使用 `--font-sans`。
- [ ] **SideGallery**：
    -   实现真正的侧边栏容器样式。
    -   添加“展开/收起”的微交互动画。
    -   (暂不实现具体画廊内容，先确立容器质感)。

---

## Phase 3: 3D 场景氛围 (Calm Tech Atmosphere)
**优先级**：Medium (Important for Feel)
**目标**：消除“纸片感”，营造“微缩模型”的物理质感。

### 3.1 材质与光照优化
- [ ] **材质升级**：
    -   将 `MeshBasicMaterial` (无光影) 替换为 `MeshStandardMaterial` (受光照影响)。
    -   墙体：哑光深灰，粗糙度 (roughness) 0.8。
    -   地板：极深色，接受阴影。
- [ ] **光照微调**：
    -   `AmbientLight`: 降低强度，避免画面发白。
    -   `DirectionalLight`: 调整角度，产生柔和的投影。
    -   **关键**：尝试开启 `AmbientOcclusion` (AO) 后处理 (如果性能允许)，或者通过烘焙/贴图模拟接触阴影。

### 3.2 动效优化
- [ ] 确保所有物体移动（拖拽、生成）都有惯性或插值 (Lerp)，避免瞬间跳变。

---

## Phase 4: 双视图差异化 (Dual Render Mode)
**优先级**：Medium (Feature Differentiator)
**目标**：让 AI 视图真正“像机器的视角”。

### 4.1 Human View (默认)
- [ ] 隐藏所有非必要的辅助线、网格、ID 标签。
- [ ] 确保画面干净、静谧。

### 4.2 AI Vision View (调试/后台)
- [ ] **高对比度网格**：显示明亮的工程网格。
- [ ] **语义参考线**：绘制墙中线、门窗开启范围线 (使用虚线或特定颜色)。
- [ ] **对象 ID**：使用 CSS 2DObject 或 Sprite 在每个物体上方显示其 UUID 前4位。
- [ ] **包围盒**：显示物体的 AABB 包围盒。

---

## Phase 5: 细节打磨 (Final Polish)
**优先级**：Low (Nice to have)
**目标**：提升完整度。

- [ ] **Loading 状态**：优雅的骨架屏或微弱呼吸效果，替代生硬的 Loading 文字。
- [ ] **空状态**：Gallery 为空时的优雅提示。
- [ ] **光标样式**：根据操作（移动、旋转）切换合适的 CSS 光标。
