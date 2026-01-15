#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
测试 Worktree 删除
固定测试名称: test-wt
"""

import requests

BASE_URL = "http://localhost:5000"
WORKTREE_NAME = "test-wt"

def main():
    print(f"删除 Worktree: {WORKTREE_NAME}")
    print("-" * 40)

    resp = requests.delete(f"{BASE_URL}/api/git/worktrees/{WORKTREE_NAME}")

    if resp.ok:
        data = resp.json()
        print(f"[PASS] 删除成功")
        print(f"  消息: {data.get('message')}")
    else:
        print(f"[FAIL] 删除失败: {resp.status_code}")
        print(f"  {resp.text}")

if __name__ == "__main__":
    main()
