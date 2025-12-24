# BIMCanvas Project Structure Demo (Multi-Repo Architecture)

此文件夹展示了基于 **[2025-12-24] 架构评审** 达成的最终数据存储方案。

## 1. 核心架构理念

本架构采用 **"Multi-Repo Collection" (多仓库集合)** 模式，旨在平衡并行开发与版本回溯的需求。

*   **策略 (Strategy) = 独立文件夹 + 独立 Git 仓库**
    *   物理隔离，互不干扰。
    *   适用于不同的设计大方向（如“动线优先” vs “空间优先”）。
    *   支持多 Agent 并行生成不同策略。
*   **变体 (Variant) = Git 分支 (Branch)**
    *   逻辑隔离，线性演化。
    *   适用于同一策略下的存档、微调或回溯（如 `v1_backup`, `v2_try_kitchen`）。
    *   **注意**：变体不体现为物理文件夹（除非被升级）。

## 2. 目录结构概览

```text
MyDesignProject/                         # [项目根目录] (非 Git 仓库)
│
├── project.json                         # [项目入口]
│                                        # 记录 activeSchemeId 和所有注册的策略路径
│
├── baseline/                            # [基准数据] (只读 / ReadOnly)
│   ├── architecture.json                # 墙、柱、板 (物理事实)
│   ├── openings.json                    # 门窗
│   └── location_lines.json              # 原始墙面定位线
│
├── schemes/                             # [策略集合]
│   │
│   ├── s1_Flow/                         # [策略A: 动线优先] (独立 Git 仓库)
│   │   ├── .git/                        # Git 历史 (包含 main, v1_backup 等分支)
│   │   ├── strategy.json                # 策略元信息 (含 origin 血缘, baselineHash)
│   │   ├── zones.json                   # 分区定义
│   │   ├── finishes.json                # 完成面配置 (引用 baseline 定位线)
│   │   ├── modules.json                 # 家具布置
│   │   └── .gitignore                   # 忽略 Assets/
│   │
│   └── s2_Space/                        # [策略B: 空间优先] (独立 Git 仓库)
│       ├── .git/
│       └── ...
│
├── Assets/                              # [全局资产]
│   ├── s1_Flow/                         # 策略A 的截图/渲染图
│   └── s2_Space/
│
└── context/                             # [上下文]
    └── requirements.md                  # 用户需求与约束
```

## 3. 关键工作流 (Workflows)

### 3.1 新建策略 (New Strategy)
*   **场景**：开始一个全新的设计思路。
*   **操作**：
    1.  创建 `schemes/s3_New` 文件夹。
    2.  `git init` 初始化仓库。
    3.  创建 `strategy.json`，引用 `../../baseline`。

### 3.2 创建变体/存档 (Archive Variant)
*   **场景**：当前方案不错，想存个档再继续修改；或者想试错。
*   **操作**：
    1.  进入 `schemes/s1_Flow`。
    2.  `git branch v1_backup` (创建分支)。
    3.  继续在 `main` 分支工作，或 `git checkout v1_backup` 查看旧版。

### 3.3 变体升级为策略 (Promote Variant)
*   **场景**：`v1_backup` 变体非常有潜力，想基于它发展出一个全新的独立策略，且不影响原策略。
*   **机制**：**Copy & Detach (复制与脱离)**。
*   **操作**：
    1.  **物理复制**：`cp -r schemes/s1_Flow schemes/s4_FromV1`。
    2.  **切换分支**：在 `s4` 中 `git checkout v1_backup` 并重命名为 `main`。
    3.  **记录血缘**：在 `s4/strategy.json` 中写入 `origin` 字段：
        ```json
        "origin": {
            "repo": "../s1_Flow",
            "branch": "v1_backup",
            "commit": "sha_of_v1..."
        }
        ```
    4.  **结果**：`s4` 拥有完整的 Git 历史，且逻辑上指向 `s1`。

## 4. 数据一致性保障

*   **Baseline 只读**：`baseline/` 文件夹不应被修改。
*   **Dirty Check**：
    *   `strategy.json` 记录 `lastValidatedBaselineHash`。
    *   App 启动时计算 `baseline/` 的当前 Hash。
    *   若不匹配，标记策略为 `dirty`，提示用户重新校验或修复。
