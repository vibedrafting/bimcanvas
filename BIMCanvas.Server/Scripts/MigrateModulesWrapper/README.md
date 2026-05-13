# MigrateModulesWrapper

BIMCanvas Phase 0b 起 `modules.json` 升级为 wrapper `{schemeMetadata, modules}`。本工具把已有 `.bcp` 项目里的裸数组文件批量升级为 wrapper，避免新 Server 启动时读不出旧数据。

## 使用方法

```bash
# 预演（不写入），先看会迁移哪些文件
dotnet run --project BIMCanvas.Server/Scripts/MigrateModulesWrapper -- "C:\path\to\project" --dry-run

# 实际迁移
dotnet run --project BIMCanvas.Server/Scripts/MigrateModulesWrapper -- "C:\path\to\project"
```

## 行为

扫描 `<project-path>/schemes/**/` 下的 `modules.json` + `modules-*.json`（跳过 `*.meta.json`），按以下规则处理：

| 文件状态 | 行为 |
|---|---|
| 裸数组 `[ {...}, ... ]` | 包成 wrapper，`schemeMetadata` 按文件名 best-effort 推导 |
| 已是 wrapper 且含 `schemeMetadata` | 跳过 |
| 已是 wrapper 但缺 `schemeMetadata`（如旧 `{summary, modules}` 形态） | 补 `schemeMetadata` 后重写，保留旧 `summary` |
| 空文件 | 跳过 |
| 其他形态 | 报错并打印文件路径 |

## `schemeMetadata` best-effort 派生规则

| 字段 | 取值 |
|---|---|
| `summary` | 旧 wrapper 的 `summary` 字段；裸数组场景下为空字符串 |
| `variantSlug` | 文件名匹配 `modules-{vid}.json` 时取 `{vid}`；canonical 文件为 `null` |
| `adoptedAt` | 始终 `null`（脚本不识别采纳时机） |
| `sourceWorkflow` | canonical → `"single-plan"`；`modules-alt-prev-*` → `"prev-adopted"`；其他变体 → `"unknown"` |

`summary` 留空意味着 Web chip tooltip 暂无显示——Agent 下次写 modules 时 Server 会从 semantic_plan 重派生。

## 注意事项（必读）

1. **必须先 git 存档项目目录**。脚本不主动 commit，回滚靠 git。
2. **必须先部署 Server wrapper 适配版本**（Phase 0b 之后）再跑迁移。旧 Server 启动会因读不出裸数组挂掉。顺序：
   - 部署新 Server 二进制
   - 跑迁移脚本
   - 启动新 Server
3. 多个 `.bcp` 项目分别执行。
4. `--dry-run` 仅预演，不写入。
5. 脚本写入采用 `.tmp` + rename 原子模式，中断不损坏现有文件。

## 退出码

- `0` —— 全部成功（含 0 错误的 dry-run）
- `1` —— 有文件错误（其他文件已尝试迁移，详见输出）
- `2` —— 参数错误或项目目录不存在
