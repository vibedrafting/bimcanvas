#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
测试 Worktree 删除
支持两种场景：
- 场景 A（并行开发）：deleteBranch=false，保留分支
- 场景 B（隔离环境）：deleteBranch=true，删除分支
"""

import sys
import requests

BASE_URL = "http://localhost:5000"
WORKTREE_NAME = "test-wt"


def main():
    # 解析命令行参数
    delete_branch = "--delete-branch" in sys.argv or "-d" in sys.argv

    scenario = "B (隔离环境，删除分支)" if delete_branch else "A (并行开发，保留分支)"
    print(f"删除 Worktree: {WORKTREE_NAME}")
    print(f"场景: {scenario}")
    print("-" * 40)

    # 构建 URL（带查询参数）
    url = f"{BASE_URL}/api/git/worktrees/{WORKTREE_NAME}"
    if delete_branch:
        url += "?deleteBranch=true"

    resp = requests.delete(url)

    if resp.ok:
        data = resp.json()
        print(f"[PASS] 删除成功")
        print(f"  消息: {data.get('message')}")
    else:
        print(f"[FAIL] 删除失败: {resp.status_code}")
        print(f"  {resp.text}")


def print_usage():
    print("用法: python test_worktree_delete.py [选项]")
    print("")
    print("选项:")
    print("  -d, --delete-branch  删除关联分支（场景 B）")
    print("  -h, --help           显示帮助信息")
    print("")
    print("示例:")
    print("  python test_worktree_delete.py           # 场景 A：保留分支")
    print("  python test_worktree_delete.py -d        # 场景 B：删除分支")


if __name__ == "__main__":
    if "-h" in sys.argv or "--help" in sys.argv:
        print_usage()
    else:
        main()
