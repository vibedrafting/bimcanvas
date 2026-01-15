#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
测试 Worktree 合并
将 test-wt 对应的分支合并到 master
"""

import requests

BASE_URL = "http://localhost:5000"
SOURCE_BRANCH = "feat/test-wt-branch"

def main():
    print(f"合并分支: {SOURCE_BRANCH} -> master")
    print("-" * 40)

    # 先确认当前在 master 分支
    resp = requests.get(f"{BASE_URL}/api/git/current")
    if resp.ok:
        current = resp.json().get("branch")
        print(f"当前分支: {current}")
        if current != "master":
            print("切换到 master 分支...")
            requests.post(
                f"{BASE_URL}/api/git/checkout",
                json={"branchName": "master"}
            )

    # 执行合并
    resp = requests.post(
        f"{BASE_URL}/api/git/merge",
        json={
            "sourceBranch": SOURCE_BRANCH,
            "commitMessage": f"Merge {SOURCE_BRANCH} into master"
        }
    )

    if resp.ok:
        data = resp.json()
        if data.get("success"):
            print(f"[PASS] 合并成功")
            print(f"  消息: {data.get('message')}")
        elif data.get("hasConflicts"):
            print(f"[WARN] 合并有冲突")
            print(f"  消息: {data.get('message')}")
        else:
            print(f"[FAIL] 合并失败")
            print(f"  消息: {data.get('message')}")
    else:
        print(f"[FAIL] 请求失败: {resp.status_code}")
        print(f"  {resp.text}")

if __name__ == "__main__":
    main()
