"""File-backed persistence for conversation history (display projection).

把 Web 可回放的 ``SessionHistoryEntry`` 事件流落到 ``{project_path}/.history/``,
让对话在「会话关闭 / Agent 进程重启」后仍能恢复。这是 **gitignored 的运行时基础设施**,
不是 .bcp 业务真理源(baseline/computed/schemes)。

落点结构::

    {project_path}/.history/
    ├── index.json                    会话索引(列表面板数据源,按 lastActiveAt 倒序)
    └── sessions/{sessionId}/
        ├── meta.json                 会话快照 + sdkSessionId
        └── events.jsonl              每行一个 SessionHistoryEntry.to_public_dict()

本模块全部是同步文件 I/O;调用方用 ``asyncio.to_thread`` 包裹以免阻塞事件循环。
函数失败抛异常,由 store 层吞掉(log + 继续),持久化绝不拖垮在线回合。
"""

from __future__ import annotations

import json
import os
import threading
from pathlib import Path
from typing import Any

HISTORY_DIRNAME = ".history"

# 保护共享的 per-project index.json 的读-改-写。events.jsonl 的追加不加锁:
# 同一会话的追加已由调用方的 await 顺序串行化,不同会话写不同文件。
_index_lock = threading.Lock()


def history_root(project_path: str) -> Path:
    return Path(project_path) / HISTORY_DIRNAME


def _sessions_dir(project_path: str) -> Path:
    return history_root(project_path) / "sessions"


def _session_dir(project_path: str, session_id: str) -> Path:
    return _sessions_dir(project_path) / session_id


def _index_path(project_path: str) -> Path:
    return history_root(project_path) / "index.json"


def append_entry_line(project_path: str, session_id: str, entry: dict[str, Any]) -> None:
    """追加一条 SessionHistoryEntry 公共字典到 events.jsonl。"""
    sdir = _session_dir(project_path, session_id)
    sdir.mkdir(parents=True, exist_ok=True)
    with (sdir / "events.jsonl").open("a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False))
        f.write("\n")


def write_meta(project_path: str, session_id: str, meta: dict[str, Any]) -> None:
    """原子写会话 meta.json(快照 + sdkSessionId)。"""
    sdir = _session_dir(project_path, session_id)
    sdir.mkdir(parents=True, exist_ok=True)
    _atomic_write_json(sdir / "meta.json", meta)


def load_index(project_path: str) -> list[dict[str, Any]]:
    """读项目历史索引;缺失或损坏返回空表。"""
    try:
        with _index_path(project_path).open("r", encoding="utf-8") as f:
            data = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError, ValueError, OSError):
        return []
    return data if isinstance(data, list) else []


def upsert_index(project_path: str, summary: dict[str, Any]) -> None:
    """把一条会话摘要插入或合并进 per-project 索引(读-改-写,加锁)。

    ``summary`` 必带 sessionId。可变字段(lastActiveAt/status/closedAt/turnCount/
    sdkSessionId)覆盖;createdAt/windowId 仅首次写入;title 仅在原值为空时填入。
    """
    session_id = summary.get("sessionId")
    if not session_id:
        return
    with _index_lock:
        index = load_index(project_path)
        existing = next(
            (e for e in index if e.get("sessionId") == session_id), None
        )
        if existing is None:
            index.append(dict(summary))
        else:
            for key, value in summary.items():
                if key in ("createdAt", "windowId"):
                    existing.setdefault(key, value)
                elif key == "title":
                    if not existing.get("title"):
                        existing["title"] = value
                else:
                    existing[key] = value
        index.sort(key=lambda e: e.get("lastActiveAt") or "", reverse=True)
        history_root(project_path).mkdir(parents=True, exist_ok=True)
        _atomic_write_json(_index_path(project_path), index)


def load_session_events(project_path: str, session_id: str) -> list[dict[str, Any]]:
    """读回某会话的事件流(逐行解析,跳过损坏行)。"""
    path = _session_dir(project_path, session_id) / "events.jsonl"
    entries: list[dict[str, Any]] = []
    try:
        with path.open("r", encoding="utf-8") as f:
            for raw in f:
                line = raw.strip()
                if not line:
                    continue
                try:
                    entries.append(json.loads(line))
                except (json.JSONDecodeError, ValueError):
                    continue
    except (FileNotFoundError, OSError):
        return []
    return entries


def latest_session_for_window(
    project_path: str, window_id: str
) -> dict[str, Any] | None:
    """索引中该窗口最近一条会话摘要(索引已按 lastActiveAt 倒序,首个命中即最新)。"""
    for entry in load_index(project_path):
        if entry.get("windowId") == window_id:
            return entry
    return None


def _atomic_write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    with tmp.open("w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    os.replace(tmp, path)
