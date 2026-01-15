#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
测试 Worktree 新建
固定测试名称: test-wt
"""

import requests

BASE_URL = "http://localhost:5000"
WORKTREE_NAME = "test-wt"
BRANCH_NAME = "feat/test-wt-branch"

def main():
    print(f"创建 Worktree: {WORKTREE_NAME}")
    print(f"分支: {BRANCH_NAME}")
    print("-" * 40)

    resp = requests.post(
        f"{BASE_URL}/api/git/worktrees",
        json={"name": WORKTREE_NAME, "branchName": BRANCH_NAME}
    )

    if resp.ok:
        data = resp.json()
        print(f"[PASS] 创建成功")
        print(f"  路径: {data.get('path')}")
        print(f"  分支: {data.get('branch')}")
    else:
        print(f"[FAIL] 创建失败: {resp.status_code}")
        print(f"  {resp.text}")

if __name__ == "__main__":
    main()
