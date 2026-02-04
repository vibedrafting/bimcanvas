# AI设计规则体系架构 (AI Design Rule System Architecture)

> **版本**：v1.0 (Based on Review 2026-02-04)
> **状态**：已冻结 (Consensus Reached)
> **核心原则**：强外壳 + 弱内核 (Hybrid Schema)

---

## 1. 架构意图 (Architectural Intent)

在构建 BIMCanvas 的 "AI 设计师" 时，我们面临一个核心矛盾：
**Server 需要严谨性** vs **AI 需要理解力**。

*   **Server/Web** 并不关心"私密性(Privacy)"或"对齐电视(Facings)"，它们只关心"几何尺寸(Size)"和"是否重叠(Collision)"，以确保渲染合法性。
*   **Agent (LLM)** 则需要理解"为什么这个床要放在这里"（意图），而不仅仅是坐标。

因此，我们设计了 **混合架构 (Hybrid Schema)**，明确划分了 **契约层 (Contract)** 与 **意图层 (Intent)**。

## 2. 核心架构：强外壳 + 弱内核 (Strong Shell + Weak Core)

### 2.1 数据结构概览

```json
{
  // --- [L1] 契约层 (Contract Layer) ---
  // 服务对象：Server (校验), Web (渲染), Revit (导出)
  // 特征：强结构，强校验，不含模糊语义
  "id": "mod_bed_002",
  "tags": ["sleep"],         // 严格对齐 Core.ZoneTag
  "size": { "width": 1800, "depth": 2100 },
  "svgPath": "assets/mod_bed_002.svg",

  // --- [L2] 意图层 (Intent Layer) ---
  // 服务对象：Agent (决策), Planner (算法)
  // 特征：混合结构，允许自然语言与逻辑共存
  "agent_config": {
    
    // -> 支柱 A: 形态学 (Morphology)
    // 决定"物体长什么样/能不能变" (物理属性)
    // 需要 Server 参与部分校验 (如 Limit Check)
    "morphology": {
      "strategy": "fixed", // enum: fixed | horizontal_fill | parametric
      "limits": { ... }    // 仅当非 fixed 时存在
    },

    // -> 支柱 B: 拓扑规则 (Topology Rules)
    // 决定"物体放在哪里" (Object-to-Space)
    // 纯语义，Server 不校验
    "topology_rules": [
      "Snap to wall (Headboard side)",
      "Strictly avoid door swing area"
    ],

    // -> 支柱 C: 关系规则 (Relation Rules)
    // 决定"物体怎么组合" (Object-to-Object)
    // 纯语义，Server 不校验
    "relation_rules": [
      "Avoid facing window directly",
      "Prefer symmetry"
    ]
  }
}
```

---

## 3. 四大支柱详解 (The Four Pillars)

### 3.1 契约层 (Contract Layer)

*   **Tags (标签)**：必须是功能的**唯一真理**。
    *   *规则*：严禁使用 `furniture` 等元数据标签。必须存在于 `ZoneTag` 枚举中（如 `sleep`, `study`）。
    *   *价值*：保证 Agent 选品时不会选错类别，Server 校验时有据可依。
*   **Size (基准尺寸)**：web 渲染的默认值。

### 3.2 形态学 (Morphology)

描述物体在几何层面的**可变性**。

| 策略 (`strategy`) | 含义 | 典型案例 | Server 行为 |
| :--- | :--- | :--- | :--- |
| **`fixed`** | **刚体**。不可改变尺寸。 | 标准床、成品柜、马桶 | 忽略 limits，强制使用 size |
| **`horizontal_fill`** (1D) | **横向填充**。在宽度方向寻找最近边界（墙/柜）并填满。 | 定制衣柜、窗帘、一字淋浴屏 | 校验 width 在 limits 范围内 |
| **`parametric`** (2D) | **参数化寻优**。在二维范围内自主寻找最佳长宽比。 | L型沙发、转角淋浴房 | 校验 width/depth 均在 limits 内 |
| **`pad_to_fit`** | **适配填充**。物体不变，增加填充条(Padding)适配空间。 | 嵌入式冰箱、洗衣机 | 核心尺寸不变，计算 Padding |

### 3.3 拓扑规则 (Topology Rules)

描述物体与 **空间容器 (Room/Zone/Wall)** 的关系。

*   **格式**：自然语言字符串数组。
*   **设计哲学**：使用 Key (`topology_rules`) 作为对 LLM 的**High-level Hint**。
*   *示例*：
    *   `"Snap to wall"` (靠墙)
    *   `"Center in room"` (居中)
    *   `"Avoid circulation path"` (避让动线)
    *   `"Placed in inner corner"` (深处角落)

### 3.4 关系规则 (Relation Rules)

描述物体与 **其他物体 (Object/Feature)** 的关系。

*   **格式**：自然语言字符串数组。
*   **范围**：包含组合设计 (Composition) 和 对位关系 (Alignment)。
*   *示例*：
    *   `"Face towards TV"` (功能朝向)
    *   `"Align with bed center"` (对齐)
    *   `"Symmetry required"` (对称组合)
    *   `"Side-by-side with vanity"` (并排)

---

## 4. 架构价值 (Value Proposition)

1.  **解耦 (Decoupling)**：
    *   Server 不需要升级代码就能支持新的设计规则（因为它是自然语言，由 Agent 解析）。
    *   Agent 不需要理解复杂的几何算法，只需要理解 "Alignment" 的语义。

2.  **鲁棒性 (Robustness)**：
    *   通过强类型的 `limits`，Server 可以物理拦截 Agent 产生的"幻觉尺寸"（比如生成一个 5米的马桶）。

3.  **可维护性 (Maintainability)**：
    *   分类清晰。当发现 AI 总是把马桶放在路中间时，我们知道去修 `topology_rules`；当发现 AI 把柜子拉得太长时，我们去修 `morphology.limits`。

4.  **表达力 (Expressiveness)**：
    *   支持 `parametric` 策略，允许 AI 进行真正的"设计"（寻找最优比例），而不仅仅是"放置"（摆放图块）。

---

## 5. 迁移指南 (Migration Guide)

针对旧版数据的迁移策略：

*   `size: -1` -> 转换为 `morphology.strategy: "horizontal_fill"` + 补充 `limits`。
*   `placement_role` -> 归入 `topology_rules`。
*   `rules` / `hints` -> 根据语义拆分入 `topology` 或 `relation`。
*   `tags` -> 清洗非 `ZoneTag` 的标签。
