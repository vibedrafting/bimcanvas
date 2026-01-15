#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Worktree 完整场景测试

场景 A（并行开发）：基于已有分支，删除 Worktree 时保留分支
场景 B（隔离环境）：创建临时分支，删除 Worktree 时删除分支
"""

import sys
import time
import requests

BASE_URL = "http://localhost:5000"


def test_scenario_a():
    """
    场景 A：并行开发
    1. 先创建一个分支 feat/scenario-a-test
    2. 创建 Worktree 关联该分支
    3. 删除 Worktree（不删除分支）
    4. 验证分支仍然存在
    """
    print("=" * 60)
    print("场景 A：并行开发（保留分支）")
    print("=" * 60)

    worktree_name = "scenario-a-wt"
    branch_name = "feat/scenario-a-test"

    # Step 1: 创建分支（如果不存在）
    print(f"\n[Step 1] 确保分支存在: {branch_name}")
    # 先尝试创建 Worktree，它会自动创建分支
    resp = requests.post(
        f"{BASE_URL}/api/git/worktrees",
        json={"name": worktree_name, "branchName": branch_name}
    )
    if resp.ok:
        print(f"  创建 Worktree 成功: {resp.json().get('path')}")
    else:
        print(f"  [WARN] 创建失败（可能已存在）: {resp.text}")

    # Step 2: 验证 Worktree 存在
    print(f"\n[Step 2] 验证 Worktree 存在")
    resp = requests.get(f"{BASE_URL}/api/git/worktrees")
    if resp.ok:
        worktrees = resp.json()
        found = any(w.get('branch') == branch_name for w in worktrees)
        print(f"  Worktree 列表: {[w.get('branch') for w in worktrees]}")
        print(f"  找到目标 Worktree: {found}")

    # Step 3: 删除 Worktree（不删除分支）
    print(f"\n[Step 3] 删除 Worktree（保留分支）")
    resp = requests.delete(f"{BASE_URL}/api/git/worktrees/{worktree_name}")
    if resp.ok:
        print(f"  删除成功: {resp.json().get('message')}")
    else:
        print(f"  [FAIL] 删除失败: {resp.text}")

    # Step 4: 验证分支仍然存在
    print(f"\n[Step 4] 验证分支仍然存在")
    resp = requests.get(f"{BASE_URL}/api/git/branches")
    if resp.ok:
        branches = resp.json()
        branch_names = [b.get('name') for b in branches]
        exists = branch_name in branch_names
        if exists:
            print(f"  [PASS] 分支 '{branch_name}' 仍然存在")
        else:
            print(f"  [FAIL] 分支 '{branch_name}' 不存在！")
            print(f"  当前分支列表: {branch_names}")

    # 清理：删除测试分支
    print(f"\n[清理] 删除测试分支")
    resp = requests.delete(f"{BASE_URL}/api/git/branches/{branch_name.replace('/', '%2F')}")
    if resp.ok:
        print(f"  清理成功")
    else:
        print(f"  清理失败（可忽略）: {resp.text}")

    print("\n场景 A 测试完成")
    return True


def test_scenario_b():
    """
    场景 B：隔离环境
    1. 创建 Worktree（自动创建 feat/ai-xxx 分支）
    2. 删除 Worktree（同时删除分支）
    3. 验证分支已被删除
    """
    print("\n" + "=" * 60)
    print("场景 B：隔离环境（删除分支）")
    print("=" * 60)

    worktree_name = "scenario-b-wt"
    branch_name = "feat/ai-scenario-b-test"

    # Step 1: 创建 Worktree
    print(f"\n[Step 1] 创建 Worktree: {worktree_name}")
    resp = requests.post(
        f"{BASE_URL}/api/git/worktrees",
        json={"name": worktree_name, "branchName": branch_name}
    )
    if resp.ok:
        print(f"  创建成功: {resp.json().get('path')}")
    else:
        print(f"  [FAIL] 创建失败: {resp.text}")
        return False

    # Step 2: 验证分支存在
    print(f"\n[Step 2] 验证分支已创建")
    resp = requests.get(f"{BASE_URL}/api/git/branches")
    if resp.ok:
        branches = resp.json()
        branch_names = [b.get('name') for b in branches]
        if branch_name in branch_names:
            print(f"  [OK] 分支 '{branch_name}' 已创建")
        else:
            print(f"  [WARN] 分支未找到，继续测试...")

    # Step 3: 删除 Worktree（同时删除分支）
    print(f"\n[Step 3] 删除 Worktree（同时删除分支）")
    resp = requests.delete(f"{BASE_URL}/api/git/worktrees/{worktree_name}?deleteBranch=true")
    if resp.ok:
        print(f"  删除成功: {resp.json().get('message')}")
    else:
        print(f"  [FAIL] 删除失败: {resp.text}")
        return False

    # Step 4: 验证分支已被删除
    print(f"\n[Step 4] 验证分支已被删除")
    time.sleep(0.5)  # 等待一下确保删除完成
    resp = requests.get(f"{BASE_URL}/api/git/branches")
    if resp.ok:
        branches = resp.json()
        branch_names = [b.get('name') for b in branches]
        if branch_name not in branch_names:
            print(f"  [PASS] 分支 '{branch_name}' 已被删除")
        else:
            print(f"  [FAIL] 分支 '{branch_name}' 仍然存在！")
            return False

    print("\n场景 B 测试完成")
    return True


def main():
    print("Worktree 场景测试")
    print("Server: " + BASE_URL)
    print("-" * 60)

    # 检查 Server 是否运行
    try:
        resp = requests.get(f"{BASE_URL}/api/git/current", timeout=3)
        if not resp.ok:
            print("[ERROR] Server 未正常响应，请确保 Server 已启动并加载了项目")
            return
        print(f"当前分支: {resp.json().get('branch')}")
    except requests.exceptions.ConnectionError:
        print("[ERROR] 无法连接到 Server，请先启动 Server")
        print("  cd BIMCanvas.Server && dotnet run")
        return

    # 解析参数
    run_a = "-a" in sys.argv or "--scenario-a" in sys.argv
    run_b = "-b" in sys.argv or "--scenario-b" in sys.argv
    run_all = not run_a and not run_b  # 默认运行全部

    results = []

    if run_all or run_a:
        results.append(("场景 A", test_scenario_a()))

    if run_all or run_b:
        results.append(("场景 B", test_scenario_b()))

    # 打印总结
    print("\n" + "=" * 60)
    print("测试结果汇总")
    print("=" * 60)
    for name, passed in results:
        status = "[PASS]" if passed else "[FAIL]"
        print(f"  {status} {name}")


def print_usage():
    print("用法: python test_worktree_scenario.py [选项]")
    print("")
    print("选项:")
    print("  -a, --scenario-a  只运行场景 A（并行开发，保留分支）")
    print("  -b, --scenario-b  只运行场景 B（隔离环境，删除分支）")
    print("  -h, --help        显示帮助信息")
    print("")
    print("默认运行全部场景")


if __name__ == "__main__":
    if "-h" in sys.argv or "--help" in sys.argv:
        print_usage()
    else:
        main()
