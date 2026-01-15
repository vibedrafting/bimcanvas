#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Worktree 功能测试脚本

测试用例：
  A1: 创建 Worktree（检出已有分支）
  A2: 删除 Worktree（保留分支）
  B1: 创建 Worktree（创建新分支）
  B2: 删除 Worktree（删除分支）

用法：
  python test_worktree.py              # 运行全部测试
  python test_worktree.py a1           # 只运行 A1
  python test_worktree.py a2           # 只运行 A2
  python test_worktree.py b1           # 只运行 B1
  python test_worktree.py b2           # 只运行 B2
  python test_worktree.py a1 a2        # 运行 A1 + A2
  python test_worktree.py --list       # 列出当前 worktree
  python test_worktree.py --clean      # 清理测试残留
"""

import sys
import time
import requests

BASE_URL = "http://localhost:5000"

# ANSI 颜色
GREEN = '\033[92m'
RED = '\033[91m'
YELLOW = '\033[93m'
BLUE = '\033[94m'
CYAN = '\033[96m'
RESET = '\033[0m'


def log_pass(msg): print(f"  {GREEN}[PASS]{RESET} {msg}")
def log_fail(msg): print(f"  {RED}[FAIL]{RESET} {msg}")
def log_warn(msg): print(f"  {YELLOW}[WARN]{RESET} {msg}")
def log_info(msg): print(f"  {msg}")
def log_step(n, msg): print(f"\n{BLUE}[Step {n}]{RESET} {msg}")


# ============================================================
# API 封装
# ============================================================

def api_get(endpoint):
    return requests.get(f"{BASE_URL}/api/git/{endpoint}")


def api_post(endpoint, data=None):
    return requests.post(f"{BASE_URL}/api/git/{endpoint}", json=data)


def api_delete(endpoint):
    return requests.delete(f"{BASE_URL}/api/git/{endpoint}")


def get_branches():
    """获取所有分支"""
    resp = api_get("branches")
    return [b.get('name') for b in resp.json()] if resp.ok else []


def get_worktrees():
    """获取所有 worktree"""
    resp = api_get("worktrees")
    return resp.json() if resp.ok else []


def get_current_branch():
    """获取当前分支"""
    resp = api_get("current")
    return resp.json().get('branch') if resp.ok else None


def branch_exists(name):
    return name in get_branches()


def worktree_exists(name):
    return any(wt.get('name') == name for wt in get_worktrees())


def get_worktree_info(name):
    """获取指定 worktree 的信息"""
    for wt in get_worktrees():
        if wt.get('name') == name:
            return wt
    return None


def create_worktree(name, branch, base_branch=None):
    """创建 Worktree"""
    data = {"name": name, "branch": branch}
    if base_branch:
        data["baseBranch"] = base_branch
    resp = api_post("worktrees", data)
    if not resp.ok:
        print(f"  {RED}[DEBUG]{RESET} HTTP {resp.status_code}")
        print(f"  {RED}[DEBUG]{RESET} Request: {data}")
        try:
            print(f"  {RED}[DEBUG]{RESET} Response: {resp.json()}")
        except:
            print(f"  {RED}[DEBUG]{RESET} Response: {resp.text[:500]}")
    return resp.ok, resp.json() if resp.ok else resp.text


def delete_worktree(name, delete_branch=False):
    """删除 Worktree"""
    url = f"worktrees/{name}"
    if delete_branch:
        url += "?deleteBranch=true"
    resp = api_delete(url)
    if not resp.ok:
        print(f"  {RED}[DEBUG]{RESET} HTTP {resp.status_code}")
        try:
            print(f"  {RED}[DEBUG]{RESET} Response: {resp.json()}")
        except:
            print(f"  {RED}[DEBUG]{RESET} Response: {resp.text[:500]}")
    return resp.ok, resp.json() if resp.ok else resp.text


# ============================================================
# 测试配置
# ============================================================

# 场景 A 配置
BRANCH_A = "test/parallel-dev"
WT_A_PREP = "test-a-prep"      # 准备用的临时 worktree
WT_A = "test-a-worktree"       # 场景 A 的测试 worktree

# 场景 B 配置
BRANCH_B = "test/isolated-temp"
WT_B = "test-b-worktree"       # 场景 B 的测试 worktree


# ============================================================
# A1: 创建 Worktree（检出已有分支）
# ============================================================

def test_a1_create():
    """
    A1: 创建 Worktree - 检出已有分支

    前置条件：分支 test/parallel-dev 已存在
    测试内容：创建 worktree 检出该分支
    验证：worktree 存在且关联正确的分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}A1: 创建 Worktree（检出已有分支）{RESET}")
    print("=" * 60)

    base_branch = get_current_branch()

    # 准备：确保测试分支存在
    log_step(1, f"准备：确保分支 {BRANCH_A} 存在")
    if not branch_exists(BRANCH_A):
        log_info("分支不存在，先创建...")
        success, _ = create_worktree(WT_A_PREP, BRANCH_A, base_branch=base_branch)
        if success:
            delete_worktree(WT_A_PREP, delete_branch=False)
            log_pass(f"分支 {BRANCH_A} 已创建")
        else:
            log_fail("无法创建测试分支")
            return False
    else:
        log_pass(f"分支 {BRANCH_A} 已存在")

    # 清理可能残留的 worktree
    if worktree_exists(WT_A):
        log_info(f"清理残留 worktree: {WT_A}")
        delete_worktree(WT_A, delete_branch=False)

    # 核心测试：创建 worktree 检出已有分支
    log_step(2, f"创建 Worktree: {WT_A}")
    log_info(f"目标分支: {BRANCH_A}（已存在，不传 baseBranch）")

    success, result = create_worktree(WT_A, BRANCH_A)  # 不传 base_branch
    if not success:
        log_fail(f"创建失败: {result}")
        return False

    log_pass("API 调用成功")
    log_info(f"返回路径: {result.get('path')}")
    log_info(f"返回分支: {result.get('branch')}")

    # 验证
    log_step(3, "验证 Worktree")
    wt_info = get_worktree_info(WT_A)
    if not wt_info:
        log_fail("Worktree 不在列表中")
        return False

    log_pass(f"Worktree 存在")
    log_info(f"  名称: {wt_info.get('name')}")
    log_info(f"  路径: {wt_info.get('path')}")
    log_info(f"  分支: {wt_info.get('branch')}")
    log_info(f"  提交: {(wt_info.get('commitHash') or '?')[:7]}")

    if wt_info.get('branch') == BRANCH_A:
        log_pass("分支关联正确")
    else:
        log_fail(f"分支不匹配: 期望 {BRANCH_A}, 实际 {wt_info.get('branch')}")
        return False

    print(f"\n{GREEN}A1 测试通过{RESET}")
    print(f"{YELLOW}>>> Worktree '{WT_A}' 已创建，请手动检查后运行 A2 删除 <<<{RESET}")
    return True


# ============================================================
# A2: 删除 Worktree（保留分支）
# ============================================================

def test_a2_delete():
    """
    A2: 删除 Worktree - 保留分支

    前置条件：worktree test-a-worktree 存在
    测试内容：删除 worktree，delete_branch=False
    验证：worktree 不存在，但分支仍存在
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}A2: 删除 Worktree（保留分支）{RESET}")
    print("=" * 60)

    # 检查前置条件
    log_step(1, f"检查 Worktree {WT_A} 是否存在")
    if not worktree_exists(WT_A):
        log_warn(f"Worktree {WT_A} 不存在，请先运行 A1")
        return False
    log_pass("Worktree 存在")

    # 记录当前分支
    wt_info = get_worktree_info(WT_A)
    branch = wt_info.get('branch') if wt_info else BRANCH_A
    log_info(f"关联分支: {branch}")

    # 删除 worktree
    log_step(2, f"删除 Worktree: {WT_A}")
    log_info("参数: delete_branch=False（保留分支）")

    success, result = delete_worktree(WT_A, delete_branch=False)
    if not success:
        log_fail(f"删除失败: {result}")
        return False

    log_pass(f"删除成功: {result.get('message')}")

    # 验证 worktree 不存在
    log_step(3, "验证 Worktree 已删除")
    time.sleep(0.2)
    if worktree_exists(WT_A):
        log_fail("Worktree 仍存在！")
        return False
    log_pass("Worktree 已删除")

    # 验证分支仍存在
    log_step(4, "验证分支仍存在")
    if branch_exists(branch):
        log_pass(f"分支 '{branch}' 保留成功")
    else:
        log_fail(f"分支 '{branch}' 被意外删除！")
        return False

    print(f"\n{GREEN}A2 测试通过{RESET}")

    # 清理测试分支
    print(f"\n{YELLOW}清理测试分支...{RESET}")
    success, _ = create_worktree("cleanup-a", branch)
    if success:
        delete_worktree("cleanup-a", delete_branch=True)
        log_pass(f"测试分支 {branch} 已清理")
    else:
        log_warn("清理失败（可手动删除）")

    return True


# ============================================================
# B1: 创建 Worktree（创建新分支）
# ============================================================

def test_b1_create():
    """
    B1: 创建 Worktree - 创建新分支

    前置条件：分支 test/isolated-temp 不存在
    测试内容：创建 worktree + 新分支，指定 baseBranch
    验证：worktree 存在，分支已创建
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}B1: 创建 Worktree（创建新分支）{RESET}")
    print("=" * 60)

    base_branch = get_current_branch()

    # 准备：确保测试分支不存在
    log_step(1, f"准备：确保分支 {BRANCH_B} 不存在")
    if branch_exists(BRANCH_B):
        log_info("分支已存在，先删除...")
        # 用 worktree 删除分支
        success, _ = create_worktree("cleanup-b-prep", BRANCH_B)
        if success:
            delete_worktree("cleanup-b-prep", delete_branch=True)
            log_pass("旧分支已删除")
        else:
            log_warn("无法删除旧分支，继续测试")
    else:
        log_pass(f"分支 {BRANCH_B} 不存在（符合预期）")

    # 清理可能残留的 worktree
    if worktree_exists(WT_B):
        log_info(f"清理残留 worktree: {WT_B}")
        delete_worktree(WT_B, delete_branch=True)

    # 核心测试：创建 worktree + 新分支
    log_step(2, f"创建 Worktree + 新分支")
    log_info(f"Worktree: {WT_B}")
    log_info(f"新分支: {BRANCH_B}")
    log_info(f"基准分支: {base_branch}")

    success, result = create_worktree(WT_B, BRANCH_B, base_branch=base_branch)
    if not success:
        log_fail(f"创建失败: {result}")
        return False

    log_pass("API 调用成功")
    log_info(f"返回路径: {result.get('path')}")
    log_info(f"返回分支: {result.get('branch')}")

    # 验证 worktree
    log_step(3, "验证 Worktree")
    wt_info = get_worktree_info(WT_B)
    if not wt_info:
        log_fail("Worktree 不在列表中")
        return False

    log_pass(f"Worktree 存在")
    log_info(f"  名称: {wt_info.get('name')}")
    log_info(f"  路径: {wt_info.get('path')}")
    log_info(f"  分支: {wt_info.get('branch')}")
    log_info(f"  提交: {(wt_info.get('commitHash') or '?')[:7]}")

    # 验证分支已创建
    log_step(4, "验证分支已创建")
    time.sleep(0.2)
    if branch_exists(BRANCH_B):
        log_pass(f"分支 '{BRANCH_B}' 已创建")
    else:
        log_warn("分支未在主仓库列表中（可能是 Worktree 独占）")

    print(f"\n{GREEN}B1 测试通过{RESET}")
    print(f"{YELLOW}>>> Worktree '{WT_B}' 已创建，请手动检查后运行 B2 删除 <<<{RESET}")
    return True


# ============================================================
# B2: 删除 Worktree（删除分支）
# ============================================================

def test_b2_delete():
    """
    B2: 删除 Worktree - 删除分支

    前置条件：worktree test-b-worktree 存在
    测试内容：删除 worktree，delete_branch=True
    验证：worktree 不存在，分支也不存在
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}B2: 删除 Worktree（删除分支）{RESET}")
    print("=" * 60)

    # 检查前置条件
    log_step(1, f"检查 Worktree {WT_B} 是否存在")
    if not worktree_exists(WT_B):
        log_warn(f"Worktree {WT_B} 不存在，请先运行 B1")
        return False
    log_pass("Worktree 存在")

    # 记录当前分支
    wt_info = get_worktree_info(WT_B)
    branch = wt_info.get('branch') if wt_info else BRANCH_B
    log_info(f"关联分支: {branch}")

    # 删除 worktree + 分支
    log_step(2, f"删除 Worktree: {WT_B}")
    log_info("参数: delete_branch=True（删除分支）")

    success, result = delete_worktree(WT_B, delete_branch=True)
    if not success:
        log_fail(f"删除失败: {result}")
        return False

    log_pass(f"删除成功: {result.get('message')}")

    # 验证 worktree 不存在
    log_step(3, "验证 Worktree 已删除")
    time.sleep(0.2)
    if worktree_exists(WT_B):
        log_fail("Worktree 仍存在！")
        return False
    log_pass("Worktree 已删除")

    # 验证分支也不存在
    log_step(4, "验证分支已删除")
    if not branch_exists(branch):
        log_pass(f"分支 '{branch}' 已删除")
    else:
        log_fail(f"分支 '{branch}' 仍存在！")
        return False

    print(f"\n{GREEN}B2 测试通过{RESET}")
    return True


# ============================================================
# 辅助命令
# ============================================================

def list_worktrees():
    """列出所有 Worktree"""
    print("\n当前 Worktree 列表:")
    print("-" * 60)
    worktrees = get_worktrees()
    if not worktrees:
        print("  (空)")
        return

    for wt in worktrees:
        prefix = "[主] " if wt.get('isMain') else "     "
        branch = wt.get('branch', '(detached)')
        commit = (wt.get('commitHash') or '?')[:7]
        print(f"{prefix}{wt.get('name')}")
        print(f"       分支: {branch} ({commit})")
        print(f"       路径: {wt.get('path')}")


def list_branches():
    """列出测试相关分支"""
    print("\n测试相关分支:")
    print("-" * 60)
    branches = get_branches()
    test_branches = [b for b in branches if b.startswith('test/')]
    if test_branches:
        for b in test_branches:
            print(f"  - {b}")
    else:
        print("  (无)")


def clean_test():
    """清理测试残留"""
    print("\n清理测试残留...")
    print("-" * 60)

    # 清理 worktree
    test_wt_prefixes = ["test-", "cleanup-"]
    worktrees = get_worktrees()
    wt_cleaned = 0

    for wt in worktrees:
        name = wt.get('name', '')
        if wt.get('isMain'):
            continue
        if any(name.startswith(p) for p in test_wt_prefixes):
            print(f"  删除 Worktree: {name}")
            delete_worktree(name, delete_branch=True)
            wt_cleaned += 1

    if wt_cleaned:
        print(f"  已清理 {wt_cleaned} 个 Worktree")
    else:
        print("  无 Worktree 需要清理")

    # 清理分支
    time.sleep(0.3)
    branches = get_branches()
    test_branches = [b for b in branches if b.startswith('test/')]
    br_cleaned = 0

    for branch in test_branches:
        print(f"  删除分支: {branch}")
        # 用临时 worktree 删除分支
        success, _ = create_worktree("cleanup-temp", branch)
        if success:
            delete_worktree("cleanup-temp", delete_branch=True)
            br_cleaned += 1

    if br_cleaned:
        print(f"  已清理 {br_cleaned} 个分支")
    else:
        print("  无分支需要清理")


# ============================================================
# 主函数
# ============================================================

def check_server():
    """检查 Server 是否运行"""
    try:
        resp = requests.get(f"{BASE_URL}/api/git/current", timeout=3)
        if resp.ok:
            return True, resp.json().get('branch')
        return False, "Server 未正常响应"
    except requests.exceptions.ConnectionError:
        return False, "无法连接到 Server"


def print_usage():
    print(__doc__)
    print("测试用例说明:")
    print("-" * 60)
    print("  A1  创建 Worktree 检出已有分支（场景 A 上半部分）")
    print("  A2  删除 Worktree 但保留分支（场景 A 下半部分）")
    print("  B1  创建 Worktree + 新分支（场景 B 上半部分）")
    print("  B2  删除 Worktree + 分支（场景 B 下半部分）")
    print()
    print("推荐测试顺序:")
    print("  1. python test_worktree.py a1    # 创建")
    print("  2. 手动检查 worktree 目录")
    print("  3. python test_worktree.py a2    # 删除")
    print("  4. python test_worktree.py b1    # 创建")
    print("  5. 手动检查 worktree 目录")
    print("  6. python test_worktree.py b2    # 删除")


def main():
    args = [a.lower() for a in sys.argv[1:]]

    if '-h' in args or '--help' in args:
        print_usage()
        return

    # 检查 Server
    ok, info = check_server()
    if not ok:
        print(f"{RED}[ERROR]{RESET} {info}")
        print("请先启动 Server: cd BIMCanvas.Server && dotnet run")
        return

    if '--list' in args:
        print(f"Server: {BASE_URL} | 当前分支: {info}")
        list_worktrees()
        list_branches()
        return

    if '--clean' in args:
        print(f"Server: {BASE_URL} | 当前分支: {info}")
        clean_test()
        return

    # 运行测试
    print("=" * 60)
    print("Worktree 功能测试")
    print("=" * 60)
    print(f"Server: {BASE_URL}")
    print(f"当前分支: {info}")

    # 解析要运行的测试
    tests = {
        'a1': ('A1 (创建-检出已有分支)', test_a1_create),
        'a2': ('A2 (删除-保留分支)', test_a2_delete),
        'b1': ('B1 (创建-新分支)', test_b1_create),
        'b2': ('B2 (删除-删除分支)', test_b2_delete),
    }

    # 筛选要运行的测试
    to_run = []
    for arg in args:
        if arg in tests:
            to_run.append(arg)

    # 如果没有指定，运行全部（按顺序）
    if not to_run:
        to_run = ['a1', 'a2', 'b1', 'b2']

    results = []
    for key in to_run:
        name, func = tests[key]
        passed = func()
        results.append((name, passed))

    # 汇总
    print("\n" + "=" * 60)
    print("测试结果")
    print("=" * 60)
    all_passed = True
    for name, passed in results:
        status = f"{GREEN}[PASS]{RESET}" if passed else f"{RED}[FAIL]{RESET}"
        print(f"  {status} {name}")
        if not passed:
            all_passed = False

    print("=" * 60)
    if all_passed:
        print(f"{GREEN}全部测试通过{RESET}")
    else:
        print(f"{RED}存在失败的测试{RESET}")


if __name__ == "__main__":
    main()
