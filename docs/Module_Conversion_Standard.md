# 凤栖湖模块标准化转换规范 (S.O.P.)

本文档定义了将《凤栖湖模块统计》中的家具清单转换为系统可用 SVG 及 JSON 数据的标准作业流程。

## 1. 视觉转换标准 (The "Clean CAD" Standard)

### 原则
*   **提取符号，而非描摹素描**：参考图仅作为几何特征的来源，不可照搬其手绘/素描风格。
*   **去噪**：忽略纹理线、阴影线、虚线（除非转换为实线表达结构）。
*   **特征保留**：只保留最核心的识别特征（如床的斜向被单线、床头柜的中心装饰点），用最简练的实线表达。

### 风格指南
| 元素 | 处理方式 |
| :--- | :--- |
| **外轮廓** | 严格的几何形状（矩形、圆角矩形、圆）。禁止随意的波浪线。 |
| **软包/枕头** | 使用圆角矩形 + 内缩线表达。避免复杂的“捏角”或拟真曲线。 |
| **织物/被单** | 使用简单流畅的贝塞尔曲线表达走向。禁止密集的纹理排线。 |
| **虚线** | **严禁使用虚线**。原图的虚线结构应转换为细实线 (`fine-lines`) 或忽略。 |

---

## 2. SVG 技术规范 (Tech Specs)

### 视口与尺寸
*   **ViewBox**：`viewBox="0 0 dx dy"` (必须严格等于表格中的尺寸)。
*   **防裁剪内缩 (Inset Rule)**：
    *   由于 SVG 描边居中渲染，25px 的粗轮廓边缘会被视口裁切。
    *   **规定**：所有贴近边缘的外轮廓矩形，坐标必须内缩 **13px**。
    *   *示例*：对于 450x400 的物体，绘制矩形为 `x="13" y="13" width="424" height="374"`。

### 线型系统 (CSS Classes)
直接复用 `furniture-svg` 技能定义的样式：
```css
.main-lines { stroke-width: 25; } /* 外轮廓 */
.detail-lines { stroke-width: 20; } /* 内部主要部件 (扶手/枕头) */
.fine-lines { stroke-width: 15; } /* 内部装饰/纹理 */
```

---

## 3. 数据录入规范 (Data Entry)

### module_library.json 字段

1.  **ID**: `mod_{类别}_{标识}` (如 `mod_cabinet_bedside_001`)。
2.  **Name**: 简洁中文名称 (如 "凤栖湖床头柜")。
3.  **Tags**: **严格校验**。必须存在于 `BIMCanvas.Core/Models/Shared/ZoneTag.cs` 中。
    *   卧室家具 → `["sleep"]` (因为 `Bedroom` 只支持 `Sleep` 和 `WardrobeStorage`)
    *   一般储物 → `["generalStorage"]`
    *   *注意：代码中未定义的标签(如 lighting)禁止使用。*
4.  **Size**:
    *   表格为固定值 → 填入 `width/depth`。
    *   表格为 `*` (可定制) → JSON 中填 `-1`。即 `{"width": -1, "depth": 600}`。
    *   **SVG 处理**：**必须选取一个具象的尺寸生成**。
        *   优先参考表格中的备注（如 "<1500" 则取 1500 或 1200）。
        *   若无备注，取该类家具的常规尺寸（如柜体宽度取 800mm）。
        *   *SVG ViewBox 必须是正整数，不能为 -1。*
6.  **AI Configuration (agent_config) [v3.1]**:
    *   **定位**：替代传统的硬约束，用于向 Agent 传递"设计意图"和"柔性规则"。
    *   **结构**：开放式字典，但推荐遵守以下标准字段以保持一致性。
    *   **标准字段**：
        *   `resize_strategy` (Enum): `horizontal_fill` (横向填充) / `vertical_stretch` (纵向拉伸) / `scale` (等比缩放)。
        *   `width_range` / `depth_range` (Array): `[min, max]` (物理极限)。
        *   `placement_rules` (Array): **核心设计原则**。包含多条件判断、硬性避让等复杂逻辑 (如 "Snap to window")。
        *   `placement_hint` (String): **弱约束/微调建议**。用于简单的对齐或亲和性提示 (如 "Align with wardrobe", "Near door")，优先级低于 rules。
        *   `description` (String): 对 AI 的补充说明。
    *   **示例 (Bed Rules)**：
        ```json
        "placement_rules": [
             "Avoid facing window directly (privacy). Prefer side-to-window.",
             "Snap entire sleep group (bed+nightstands) to window wall, leaving 200mm gap for curtains."
        ]
        ```
    *   **扩展性**：允许新增自定义字段，要求使用 snake_case 命名。
5.  **SvgPath**:
    *   统一使用相对路径：`"assets/{filename}.svg"`。

---

## 4. 作业流程 checklist

- [ ] **分析参考图**：识别 1-2 个核心几何特征（如螺旋图案、特定倒角）。
- [ ] **生成 SVG**：应用 "Inset 13px" 规则绘制初稿。
- [ ] **风格审查**：去除了所有杂乱线条吗？是闭合的几何体吗？
- [ ] **代码比对**：Tag 是否在后端 C# 枚举中存在？
- [ ] **录入数据**：更新 JSON，确保路径正确。
