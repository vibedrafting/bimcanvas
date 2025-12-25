# BIMCanvas 理想项目结构示例

> Multi-Repo Collection 架构演示

## 架构概述

本示例展示 BIMCanvas v3.0 的多仓库集合（Multi-Repo Collection）存储架构。

### 文件驱动架构："三层汉堡"模型

> 核心理念：**文件系统是连接 AI、Web、Server 和用户的通用总线**

| 层级 | 文件夹路径 | 内容 | 读写属性 | 说明 |
|:-----|:-----------|:-----|:---------|:-----|
| **底层 (基准)** | `baseline/` | 墙、柱、门窗、房间 | **只读** | Revit 导出，Server 启动加载 |
| **中层 (计算)** | `schemes/{s}/zones.json` | 功能分区、完成面 | **混合** | AI/Server 计算 computedBoundary |
| **顶层 (布局)** | `schemes/{s}/modules.json` | 家具模块、位置 | **读写** | **双向同步**：文件变动 ↔ Web 渲染 |

**双向同步场景**：
- **场景 A（代码式设计）**：VS Code 编辑 JSON → FileWatcher 检测 → Server 解析 → SignalR 推送 → Web 渲染
- **场景 B（可视化设计）**：Web 拖拽 → Server 验证 → 覆写 JSON 文件 → 广播确认

详见 `docs/FileDrivenArchitecture.md`

---

### 核心设计原则

| 概念 | 物理载体 | 开发模式 | Git 角色 |
|------|----------|----------|----------|
| **策略 (Strategy)** | 独立文件夹 | 并行开发 | 独立仓库 |
| **变体 (Variant)** | Git 分支 | 线性回溯 | 分支 |

**设计原理**：
- 不同策略开发是**经常并行的**，互不影响 → 用独立文件夹隔离
- 不同变体的开发是**线性的**，变体产生于重大选择前的存档 → 用 Git 分支表达

---

## 目录结构

```
IdealProjectStructure_Demo/
├── README.md                    # 本文档
├── project.json                 # 项目入口（activeSchemeId, schemes 列表）
├── .gitignore                   # 项目级忽略规则
│
├── baseline/                    # 【基准层】只读，Revit 导出
│   ├── metadata.json            # 坐标转换参数
│   ├── architecture.json        # 墙、柱（物理构造）
│   ├── openings.json            # 门窗
│   ├── rooms.json               # 房间边界
│   └── location_lines.json      # 完成面定位线
│
├── context/                     # 【上下文层】设计知识
│   └── requirements.md          # 用户需求
│
├── schemes/                     # 【策略集合】每个策略是独立 Git 仓库
│   ├── s1_Flow/                 # 策略1：动线优先
│   │   ├── .gitignore
│   │   ├── strategy.json        # 策略元数据（含 origin, status）
│   │   ├── zones.json           # 分区数据
│   │   ├── finishes.json        # 完成面配置
│   │   └── modules.json         # 家具布置
│   │
│   └── s2_Derived/              # 策略2：衍生策略（origin 非空）
│       ├── .gitignore
│       ├── strategy.json
│       ├── zones.json
│       ├── finishes.json
│       └── modules.json
│
└── Assets/                      # 【资产层】截图等，不进 Git
    └── .gitkeep
```

---

## 关键字段说明

### strategy.json

```json
{
  "id": "s1_Flow",
  "name": "动线优先策略",
  "approach": "circulation_first",
  "origin": null,                              // 原创策略为 null
  "lastValidatedBaselineHash": "sha256:...",   // 底图哈希
  "status": "valid"                            // valid | dirty | invalid
}
```

**衍生策略的 origin 字段**：
```json
{
  "origin": {
    "sourceStrategyId": "s1_Flow",
    "sourceRepo": "./schemes/s1_Flow",
    "sourceBranch": "main",
    "sourceCommit": "abc123def456",
    "derivedAt": "2025-12-24T14:00:00Z",
    "derivationReason": "用户喜欢动线分区，但想换成北欧风格"
  }
}
```

### zones.json - Zone 类型

```json
{
  "zones": [
    {
      "id": "z1",
      "type": "designable",      // ZoneType 枚举
      "rawBoundary": [...],      // 原始边界
      "computedBoundary": [...]  // 计算后的可用边界
    }
  ]
}
```

**ZoneType 枚举值**：

| 类型 | 说明 | 用途 |
|------|------|------|
| `exclusion` | 禁区 | 门扇开启范围等，不可布置家具 |
| `room` | 房间 | 物理房间边界 |
| `designable` | 可设计区 | AI 可布置家具的区域 |
| `circulation` | 动线区 | 通道、走廊，保持通行 |

**注意**：柱子作为建筑原始结构，与墙体同等处理（在 `baseline/architecture.json` 中定义），不作为 Zone 禁区。

---

### finishes.json - range 表示

```json
{
  "segments": [
    {
      "sourceLineId": "ll1",
      "range": [500, 2500],        // 绝对 mm 值，从定位线起点偏移
      "finishModuleId": "bedhead_panel_modern_001",
      "thickness": 30
    }
  ]
}
```

**为什么用绝对 mm 值而非比例**：
- baseline 不可变，定位线长度不会变
- AI 计算更直观（"从墙角偏移 500mm 开始"）
- 调试时一眼能和图纸对照

### modules.json - facing 朝向

```json
{
  "modules": [
    {
      "bounds": [[600, 400], [2400, 400], [2400, 2400], [600, 2400]],
      "facing": "north",           // 语义方向或 Vec2D
      "items": [...]
    }
  ]
}
```

**facing 可选值**：
- 语义字符串：`north`, `south`, `east`, `west`, `northeast`, `northwest`, `southeast`, `southwest`
- Vec2D：`[0.707, 0.707]`（任意角度单位向量）

---

## Dirty 机制

| status | 允许操作 | 禁止操作 | 触发动作 |
|--------|----------|----------|----------|
| **valid** | 全部 | - | - |
| **dirty** | 编辑、保存 | 导出到 Revit | 提示"底图已变更" |
| **invalid** | 查看 | 编辑、导出 | 强制进入修复模式 |

**触发条件**：
- `dirty`：baseline 文件哈希变化，但结构兼容
- `invalid`：baseline 结构不兼容（如房间被删除）

---

## 典型工作流

### 新建策略
```bash
mkdir schemes/s3_Space
cd schemes/s3_Space
git init
# 创建 strategy.json, zones.json, finishes.json, modules.json
git add . && git commit -m "初始化策略"
```

### 创建变体（存档）
```bash
cd schemes/s1_Flow
git branch v1_backup    # 在重大修改前存档
```

### 切换变体（回溯）
```bash
cd schemes/s1_Flow
git checkout v1_backup  # 回到之前的版本
```

### 变体升级为策略
```bash
cp -r schemes/s1_Flow schemes/s3_FromVariant
cd schemes/s3_FromVariant
rm -rf .git && git init
# 更新 strategy.json 的 origin 字段
```

---

## 与 Gemini 示例的差异

| 对比项 | Gemini 示例 | 本示例 |
|--------|-------------|--------|
| JSON 内容 | 空 `{}` | 完整示例数据 |
| strategy.json 字段 | 缺少 origin, status | 完整字段 |
| 衍生策略演示 | 无 | s2_Derived 演示 origin |
| finishes range | 无 | 展示绝对 mm 值 |
| dirty 机制 | 未体现 | 体现 lastValidatedBaselineHash |

---

## 版本信息

- **架构版本**：v3.0 (Multi-Repo Collection)
- **创建日期**：2025-12-24
- **创建者**：Claude (评审讨论后生成)
