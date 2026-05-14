# MigrateProjectSchema

BIMCanvas `.bcp` 项目 schema 一次性清洗工具。合并以下三步：

| Phase | 处理对象 | 改动 |
|---|---|---|
| **Phase 0** | `schemes/*/semantic_plan.json` | 顶层 `Versions` → `Entries`；条目内 `Version` → `Tag`、`ReferenceAnalysisVersion` → `ReferenceAnalysisTag`；顶层 `referenceAnalysis`（旧版内嵌兼容字段）保持不动；**值不变** |
| **Phase 0** | `schemes/*/reference_analysis.json` | 顶层数组，每条目 `Version` → `Tag`；**值不变** |
| **Phase 0b** | `schemes/**/modules.json` + `modules-*.json` | 裸数组 → `{schemeMetadata, modules}`；旧 wrapper `{summary, modules}` 升级到 `{schemeMetadata: {summary,...}, modules}` |
| **Phase D** | `schemes/**/semantic_plan.json` | 条目内 `Tag` 值映射：`v0.1`→`spatial-skeleton`、`v0.2`→`strategic-plan`、`v0.2-meta`→`multi-plan-overview`、`v0.3`→`construction-brief`；reference_analysis 的 `v1/v2/v3+` **不动**（真版本序列） |

## 使用方法

```bash
# 预演（强烈建议先跑一次）
dotnet run --project BIMCanvas.Server\Scripts\MigrateProjectSchema -- "C:\path\to\project" --dry-run

# 实际迁移（全部两阶段）
dotnet run --project BIMCanvas.Server\Scripts\MigrateProjectSchema -- "C:\path\to\project"

# 只跑某一阶段（少见，调试用）
dotnet run --project BIMCanvas.Server\Scripts\MigrateProjectSchema -- "C:\path\to\project" --only=tag
dotnet run --project BIMCanvas.Server\Scripts\MigrateProjectSchema -- "C:\path\to\project" --only=wrapper
dotnet run --project BIMCanvas.Server\Scripts\MigrateProjectSchema -- "C:\path\to\project" --only=tagvalue
```

## `schemeMetadata` best-effort 派生规则（Phase 0b）

| 字段 | 取值 |
|---|---|
| `summary` | 旧 wrapper 的 `summary` 字段；裸数组场景为空字符串 |
| `variantSlug` | 文件名匹配 `modules-{vid}.json` 时取 `{vid}`；canonical 为 `null` |
| `adoptedAt` | 始终 `null`（脚本不识别采纳时机） |
| `sourceWorkflow` | canonical → `single-plan`；`modules-alt-prev-*` → `prev-adopted`；其他变体 → `unknown` |

`summary` 留空意味着 Web chip tooltip 暂无显示——Agent 下次写 modules 时 Server 会从 semantic_plan 重派生。

## 必读注意事项

1. **必须先 git 存档项目目录**（脚本不主动 commit；回滚靠 git）。
2. **必须先部署 Phase 0 + 0b 适配的 Server 版本再跑**——旧 Server 启动会因读不到新 schema 挂掉。顺序：
   - 部署新 Server 二进制
   - 跑迁移脚本
   - 启动新 Server
3. 多个 `.bcp` 项目分别执行。
4. `--dry-run` 仅预演，不写入。
5. 写入采用 `.tmp` + rename 原子模式，中断不损坏现有文件。

## 退出码

- `0` —— 全部成功（含 0 错误的 dry-run）
- `1` —— 有文件错误（其他文件已尝试迁移，详见输出）
- `2` —— 参数错误或项目目录不存在
