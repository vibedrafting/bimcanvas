#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Worktree 功能测试脚本

测试两种场景的完整流程：

场景 A：并行开发（多窗口）
  - 分支特点：持久分支（用户手动创建）
  - A1: 创建 Worktree（检出已有分支）
  - A2: 删除 Worktree（保留分支）

场景 B：隔离环境（Agent 任务）
  - 分支特点：临时分支（自动创建）
  - B1: 创建隔离环境
  - B2: 模拟 Agent 工作
  - B3: 合并回基础分支
  - B4: 清理隔离环境

用法：
  python test_worktree.py              # 运行全部测试
  python test_worktree.py a            # 运行场景 A（A1+A2）
  python test_worktree.py a1           # A1：创建（用默认分支）
  python test_worktree.py a1 分支名    # A1：创建（用指定分支）
  python test_worktree.py f1 分支名    # F1：创建虚拟窗口（用指定分支）
  python test_worktree.py a2           # A2：删除
  python test_worktree.py b            # 运行场景 B（B1+B2+B3+B4）
  python test_worktree.py b1           # B1：创建隔离环境
  python test_worktree.py b2           # B2：模拟工作
  python test_worktree.py b3           # B3：合并
  python test_worktree.py b4           # B4：清理
  python test_worktree.py --list       # 列出当前 worktree
  python test_worktree.py --clean      # 清理测试残留
"""

import os
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


def commit_in_worktree(worktree_name, message):
    """在指定 Worktree 中提交"""
    data = {"message": message, "worktreeName": worktree_name}
    resp = api_post("commit", data)
    if not resp.ok:
        print(f"  {RED}[DEBUG]{RESET} HTTP {resp.status_code}")
        try:
            print(f"  {RED}[DEBUG]{RESET} Response: {resp.json()}")
        except:
            print(f"  {RED}[DEBUG]{RESET} Response: {resp.text[:500]}")
    return resp.ok, resp.json() if resp.ok else resp.text


def merge_branch(source_branch, target_branch=None, worktree_name=None, commit_message=None):
    """合并分支

    Args:
        source_branch: 源分支（要合并进来的分支）
        target_branch: 目标分支（在主仓库中切换后合并，场景 B）
        worktree_name: Worktree 名称（在指定 Worktree 中合并，场景 F）
        commit_message: 合并提交信息
    """
    data = {"sourceBranch": source_branch}
    if worktree_name:
        data["worktreeName"] = worktree_name
    elif target_branch:
        data["targetBranch"] = target_branch
    if commit_message:
        data["commitMessage"] = commit_message
    resp = api_post("merge", data)
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
WT_A = "test-a-wt"

# 场景 B 配置
BRANCH_B = "test/ai-temp"
WT_B = "test-b-wt"
TEST_FILE = "test_agent_work.txt"
STATE_FILE = os.path.join(os.path.dirname(__file__), ".test_state.json")  # 状态文件

# 场景 F 配置（多窗口独立任务）
BRANCH_F = "test/scheme-f"       # 虚拟窗口分支
BRANCH_F_AI = "test/ai-temp-f"   # Agent 临时分支
WT_F_WINDOW = "window-f"         # 虚拟窗口 Worktree
WT_F_AGENT = "ai-job-f"          # Agent Worktree
TEST_FILE_F = "test_scene_f.txt" # 场景 F 测试文件


def save_state(key, value):
    """保存测试状态"""
    state = {}
    if os.path.exists(STATE_FILE):
        try:
            with open(STATE_FILE, 'r', encoding='utf-8') as f:
                import json
                state = json.load(f)
        except:
            pass
    state[key] = value
    with open(STATE_FILE, 'w', encoding='utf-8') as f:
        import json
        json.dump(state, f)


def load_state(key, default=None):
    """读取测试状态"""
    if not os.path.exists(STATE_FILE):
        return default
    try:
        with open(STATE_FILE, 'r', encoding='utf-8') as f:
            import json
            state = json.load(f)
            return state.get(key, default)
    except:
        return default


def clear_state():
    """清除测试状态"""
    if os.path.exists(STATE_FILE):
        os.remove(STATE_FILE)


# ============================================================
# 场景 A：并行开发
# ============================================================

def test_a1_create(custom_branch=None):
    """
    A1: 创建 Worktree - 检出已有分支

    场景：用户想在新窗口中打开一个已存在的分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}A1: 创建 Worktree（检出已有分支）{RESET}")
    print("=" * 60)

    branch = custom_branch or BRANCH_A
    is_custom = custom_branch is not None
    base_branch = get_current_branch()

    # Step 1: 确保分支存在
    log_step(1, f"准备：确保分支 {branch} 存在")
    if not branch_exists(branch):
        if is_custom:
            log_fail(f"指定分支 '{branch}' 不存在！")
            log_info("请确认分支名称是否正确，或先创建该分支")
            return False
        else:
            log_info("测试分支不存在，先创建...")
            # 用临时 worktree 创建分支
            success, _ = create_worktree("temp-create-branch", branch, base_branch=base_branch)
            if success:
                delete_worktree("temp-create-branch", delete_branch=False)
                log_pass(f"分支 {branch} 已创建")
            else:
                log_fail("无法创建测试分支")
                return False
    else:
        log_pass(f"分支 {branch} 已存在")

    # 清理可能残留的 worktree
    if worktree_exists(WT_A):
        log_info(f"清理残留 worktree: {WT_A}")
        delete_worktree(WT_A, delete_branch=False)

    # Step 2: 创建 worktree 检出已有分支
    log_step(2, f"创建 Worktree: {WT_A}")
    log_info(f"目标分支: {branch}（已存在，不传 baseBranch）")

    success, result = create_worktree(WT_A, branch)  # 不传 base_branch
    if not success:
        log_fail(f"创建失败: {result}")
        return False

    log_pass("API 调用成功")
    log_info(f"返回路径: {result.get('path')}")

    # Step 3: 验证
    log_step(3, "验证 Worktree")
    wt_info = get_worktree_info(WT_A)
    if not wt_info:
        log_fail("Worktree 不在列表中")
        return False

    log_pass(f"Worktree 存在")
    log_info(f"  名称: {wt_info.get('name')}")
    log_info(f"  路径: {wt_info.get('path')}")
    log_info(f"  分支: {wt_info.get('branch')}")

    if wt_info.get('branch') == branch:
        log_pass("分支关联正确")
    else:
        log_fail(f"分支不匹配: 期望 {branch}, 实际 {wt_info.get('branch')}")
        return False

    print(f"\n{GREEN}A1 测试通过{RESET}")
    print(f"{YELLOW}>>> Worktree '{WT_A}' 已创建，运行 A2 删除 <<<{RESET}")
    return True


def test_a2_delete():
    """
    A2: 删除 Worktree - 保留分支

    场景：用户关闭窗口，但分支是持久分支，需要保留
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}A2: 删除 Worktree（保留分支）{RESET}")
    print("=" * 60)

    # Step 1: 检查前置条件
    log_step(1, f"检查 Worktree {WT_A} 是否存在")
    if not worktree_exists(WT_A):
        log_warn(f"Worktree {WT_A} 不存在，请先运行 A1")
        return False
    log_pass("Worktree 存在")

    wt_info = get_worktree_info(WT_A)
    branch = wt_info.get('branch') if wt_info else BRANCH_A
    log_info(f"关联分支: {branch}")

    # Step 2: 删除 worktree
    log_step(2, f"删除 Worktree: {WT_A}")
    log_info("参数: delete_branch=False（保留分支）")

    success, result = delete_worktree(WT_A, delete_branch=False)
    if not success:
        log_fail(f"删除失败: {result}")
        return False

    log_pass(f"删除成功: {result.get('message')}")

    # Step 3: 验证 worktree 不存在
    log_step(3, "验证 Worktree 已删除")
    time.sleep(0.2)
    if worktree_exists(WT_A):
        log_fail("Worktree 仍存在！")
        return False
    log_pass("Worktree 已删除")

    # Step 4: 验证分支仍存在
    log_step(4, "验证分支仍存在")
    if branch_exists(branch):
        log_pass(f"分支 '{branch}' 保留成功")
    else:
        log_fail(f"分支 '{branch}' 被意外删除！")
        return False

    print(f"\n{GREEN}A2 测试通过{RESET}")

    # 清理测试分支
    if branch == BRANCH_A:
        print(f"\n{YELLOW}清理测试分支...{RESET}")
        success, _ = create_worktree("cleanup-a", branch)
        if success:
            delete_worktree("cleanup-a", delete_branch=True)
            log_pass(f"测试分支 {branch} 已清理")
        else:
            log_warn("清理失败（可手动删除）")

    return True


# ============================================================
# 场景 B：隔离环境（Agent 任务）
# ============================================================

def test_b1_create_isolation(custom_base_branch=None):
    """
    B1: 创建隔离环境

    场景：Agent 需要在隔离环境中执行任务

    Args:
        custom_base_branch: 自定义基础分支（可选），不传则使用当前分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}B1: 创建隔离环境{RESET}")
    print("=" * 60)

    # 确定基础分支
    if custom_base_branch:
        if not branch_exists(custom_base_branch):
            log_fail(f"指定的基础分支 '{custom_base_branch}' 不存在！")
            return False
        base_branch = custom_base_branch
        log_info(f"使用指定基础分支: {base_branch}")
    else:
        base_branch = get_current_branch()
        log_info(f"使用当前分支作为基础: {base_branch}")

    # Step 1: 准备 - 确保测试分支不存在
    log_step(1, f"准备：确保临时分支 {BRANCH_B} 不存在")
    if branch_exists(BRANCH_B):
        log_info("分支已存在，先删除...")
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

    # Step 2: 创建 worktree + 新分支
    log_step(2, "创建 Worktree + 临时分支")
    log_info(f"Worktree: {WT_B}")
    log_info(f"临时分支: {BRANCH_B}")
    log_info(f"基准分支: {base_branch}")

    success, result = create_worktree(WT_B, BRANCH_B, base_branch=base_branch)
    if not success:
        log_fail(f"创建失败: {result}")
        return False

    log_pass("API 调用成功")
    log_info(f"返回路径: {result.get('path')}")

    # Step 3: 验证 worktree
    log_step(3, "验证 Worktree")
    wt_info = get_worktree_info(WT_B)
    if not wt_info:
        log_fail("Worktree 不在列表中")
        return False

    log_pass("Worktree 存在")
    log_info(f"  名称: {wt_info.get('name')}")
    log_info(f"  路径: {wt_info.get('path')}")
    log_info(f"  分支: {wt_info.get('branch')}")

    # Step 4: 验证分支已创建
    log_step(4, "验证临时分支已创建")
    time.sleep(0.2)
    if branch_exists(BRANCH_B):
        log_pass(f"分支 '{BRANCH_B}' 已创建")
    else:
        log_warn("分支未在列表中（可能是 Worktree 独占）")

    # 保存基础分支到状态文件（供 B3 合并使用）
    save_state("base_branch", base_branch)
    log_info(f"已保存基础分支状态: {base_branch}")

    print(f"\n{GREEN}B1 测试通过{RESET}")
    print(f"{YELLOW}>>> 隔离环境已创建，运行 B2 模拟工作 <<<{RESET}")
    return True


def test_b2_agent_work():
    """
    B2: 模拟 Agent 工作

    场景：Agent 在隔离环境中修改文件并提交
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}B2: 模拟 Agent 工作{RESET}")
    print("=" * 60)

    # Step 1: 检查前置条件
    log_step(1, f"检查 Worktree {WT_B} 是否存在")
    if not worktree_exists(WT_B):
        log_warn(f"Worktree {WT_B} 不存在，请先运行 B1")
        return False
    log_pass("Worktree 存在")

    wt_info = get_worktree_info(WT_B)
    worktree_path = wt_info.get('path')
    log_info(f"Worktree 路径: {worktree_path}")

    # Step 2: 在 worktree 中创建测试文件
    log_step(2, "在 Worktree 中创建测试文件")
    test_file_path = os.path.join(worktree_path, TEST_FILE)

    try:
        with open(test_file_path, 'w', encoding='utf-8') as f:
            f.write(f"Agent 测试文件\n创建时间: {time.strftime('%Y-%m-%d %H:%M:%S')}\n")
        log_pass(f"测试文件已创建: {TEST_FILE}")
    except Exception as e:
        log_fail(f"创建文件失败: {e}")
        return False

    # Step 3: 通过 API 在 worktree 中提交
    log_step(3, "通过 API 在 Worktree 中提交")
    log_info(f"使用新 API: commit(worktreeName={WT_B})")

    success, result = commit_in_worktree(WT_B, "Agent 测试提交")
    if not success:
        log_fail(f"提交失败: {result}")
        return False

    if result.get('committed'):
        log_pass("提交成功")
        log_info(f"  commit: {result.get('commit', {}).get('hash', '?')}")
        log_info(f"  worktree: {result.get('worktree')}")
    else:
        log_warn(f"没有提交: {result.get('message')}")

    print(f"\n{GREEN}B2 测试通过{RESET}")
    print(f"{YELLOW}>>> Agent 工作已完成，运行 B3 合并回基础分支 <<<{RESET}")
    return True


def test_b3_merge_back():
    """
    B3: 合并回基础分支

    场景：Agent 完成工作后，把临时分支合并回基础分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}B3: 合并回基础分支{RESET}")
    print("=" * 60)

    # 从状态文件读取基础分支（B1 保存的）
    saved_base = load_state("base_branch")
    current_branch = get_current_branch()

    if saved_base:
        base_branch = saved_base
        log_info(f"从状态文件读取基础分支: {base_branch}")
        if base_branch != current_branch:
            log_info(f"（注意：当前分支是 {current_branch}，将切换到 {base_branch} 执行合并）")
    else:
        base_branch = current_branch
        log_info(f"无状态文件，使用当前分支: {base_branch}")

    # Step 1: 检查前置条件
    log_step(1, "检查前置条件")
    if not branch_exists(BRANCH_B):
        log_warn(f"临时分支 {BRANCH_B} 不存在，请先运行 B1+B2")
        return False
    log_pass(f"临时分支 {BRANCH_B} 存在")
    log_info(f"目标基础分支: {base_branch}")

    # Step 2: 通过 API 合并
    log_step(2, "通过 API 合并临时分支到基础分支")
    log_info(f"使用新 API: merge(source={BRANCH_B}, target={base_branch})")

    success, result = merge_branch(
        source_branch=BRANCH_B,
        target_branch=base_branch,
        commit_message=f"合并 Agent 任务: {BRANCH_B}"
    )

    if not success:
        log_fail(f"合并失败: {result}")
        return False

    if result.get('success'):
        log_pass("合并成功")
        log_info(f"  消息: {result.get('message')}")
    else:
        if result.get('hasConflicts'):
            log_warn("合并有冲突，需要手动解决")
            log_info(f"  消息: {result.get('message')}")
        else:
            log_fail(f"合并失败: {result.get('message')}")
            return False

    # Step 3: 验证合并结果
    log_step(3, "验证合并结果")
    # 检查测试文件是否存在于基础分支
    # （简单验证：如果合并成功，文件应该存在）
    log_pass("合并已完成")

    print(f"\n{GREEN}B3 测试通过{RESET}")
    print(f"{YELLOW}>>> 已合并到基础分支，运行 B4 清理隔离环境 <<<{RESET}")
    return True


def test_b4_cleanup():
    """
    B4: 清理隔离环境

    场景：Agent 任务完成，删除临时分支和 worktree
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}B4: 清理隔离环境{RESET}")
    print("=" * 60)

    # Step 1: 检查 worktree
    log_step(1, f"检查 Worktree {WT_B}")
    if not worktree_exists(WT_B):
        log_info(f"Worktree {WT_B} 已不存在")
    else:
        log_pass("Worktree 存在，将删除")

        wt_info = get_worktree_info(WT_B)
        branch = wt_info.get('branch') if wt_info else BRANCH_B
        log_info(f"关联分支: {branch}")

    # Step 2: 删除 worktree + 分支
    log_step(2, f"删除 Worktree + 临时分支")
    log_info("参数: delete_branch=True（删除临时分支）")

    if worktree_exists(WT_B):
        success, result = delete_worktree(WT_B, delete_branch=True)
        if not success:
            log_fail(f"删除失败: {result}")
            return False
        log_pass(f"删除成功: {result.get('message')}")

    # Step 3: 验证 worktree 已删除
    log_step(3, "验证 Worktree 已删除")
    time.sleep(0.2)
    if worktree_exists(WT_B):
        log_fail("Worktree 仍存在！")
        return False
    log_pass("Worktree 已删除")

    # Step 4: 验证分支已删除
    log_step(4, "验证临时分支已删除")
    if not branch_exists(BRANCH_B):
        log_pass(f"分支 '{BRANCH_B}' 已删除")
    else:
        log_warn(f"分支 '{BRANCH_B}' 仍存在（可能合并后未自动删除）")
        # 尝试手动清理
        success, _ = create_worktree("cleanup-b-final", BRANCH_B)
        if success:
            delete_worktree("cleanup-b-final", delete_branch=True)
            log_pass("已手动清理")

    # Step 5: 清除状态文件
    log_step(5, "清除状态文件")
    clear_state()
    log_pass("状态已清除")

    print(f"\n{GREEN}B4 测试通过{RESET}")
    return True


# ============================================================
# 场景 F：多窗口独立任务
# ============================================================

def test_f1_create_window(custom_branch=None):
    """
    F1: 创建虚拟窗口

    场景：用户打开新窗口，检出一个分支

    Args:
        custom_branch: 自定义分支名（可选，默认使用 BRANCH_F）
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}F1: 创建虚拟窗口{RESET}")
    print("=" * 60)

    branch = custom_branch or BRANCH_F
    base_branch = get_current_branch()

    # 保存使用的分支到状态（供后续 F2-F6 使用）
    save_state("f_branch", branch)

    # Step 1: 准备 - 确保虚拟窗口分支存在
    log_step(1, f"准备：确保虚拟窗口分支 {branch} 存在")
    if not branch_exists(branch):
        log_info("分支不存在，先创建...")
        success, _ = create_worktree("temp-create-f", branch, base_branch=base_branch)
        if success:
            delete_worktree("temp-create-f", delete_branch=False)
            log_pass(f"分支 {branch} 已创建")
        else:
            log_fail("无法创建测试分支")
            return False
    else:
        log_pass(f"分支 {branch} 已存在")

    # 清理可能残留的 worktree
    if worktree_exists(WT_F_WINDOW):
        log_info(f"清理残留 worktree: {WT_F_WINDOW}")
        delete_worktree(WT_F_WINDOW, delete_branch=False)

    # Step 2: 创建虚拟窗口 worktree
    log_step(2, f"创建虚拟窗口: {WT_F_WINDOW}")
    log_info(f"检出分支: {branch}")

    success, result = create_worktree(WT_F_WINDOW, branch)
    if not success:
        log_fail(f"创建失败: {result}")
        return False

    log_pass("API 调用成功")
    log_info(f"返回路径: {result.get('path')}")

    # Step 3: 验证
    log_step(3, "验证虚拟窗口")
    wt_info = get_worktree_info(WT_F_WINDOW)
    if not wt_info:
        log_fail("Worktree 不在列表中")
        return False

    log_pass("Worktree 存在")
    log_info(f"  名称: {wt_info.get('name')}")
    log_info(f"  路径: {wt_info.get('path')}")
    log_info(f"  分支: {wt_info.get('branch')}")

    # 保存虚拟窗口路径到状态
    save_state("window_path", wt_info.get('path'))

    print(f"\n{GREEN}F1 测试通过{RESET}")
    print(f"{YELLOW}>>> 虚拟窗口已创建，运行 F2 创建 Agent 隔离环境 <<<{RESET}")
    return True


def test_f2_create_agent():
    """
    F2: 在虚拟窗口中创建 Agent 隔离环境

    场景：用户在虚拟窗口中请求 AI 执行任务，系统创建隔离环境
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}F2: 创建 Agent 隔离环境（基于虚拟窗口分支）{RESET}")
    print("=" * 60)

    # 从状态文件读取虚拟窗口分支（F1 保存的）
    window_branch = load_state("f_branch", BRANCH_F)
    log_info(f"虚拟窗口分支: {window_branch}")

    # Step 1: 检查前置条件
    log_step(1, f"检查虚拟窗口 {WT_F_WINDOW} 是否存在")
    if not worktree_exists(WT_F_WINDOW):
        log_warn(f"虚拟窗口 {WT_F_WINDOW} 不存在，请先运行 F1")
        return False
    log_pass("虚拟窗口存在")

    # 清理可能残留的 Agent worktree
    if worktree_exists(WT_F_AGENT):
        log_info(f"清理残留 worktree: {WT_F_AGENT}")
        delete_worktree(WT_F_AGENT, delete_branch=True)

    # 清理可能残留的临时分支
    if branch_exists(BRANCH_F_AI):
        log_info(f"清理残留分支: {BRANCH_F_AI}")
        success, _ = create_worktree("cleanup-f-ai", BRANCH_F_AI)
        if success:
            delete_worktree("cleanup-f-ai", delete_branch=True)

    # Step 2: 创建 Agent worktree，基于虚拟窗口分支
    log_step(2, "创建 Agent Worktree + 临时分支")
    log_info(f"Worktree: {WT_F_AGENT}")
    log_info(f"临时分支: {BRANCH_F_AI}")
    log_info(f"基准分支: {window_branch}（虚拟窗口分支）")

    success, result = create_worktree(WT_F_AGENT, BRANCH_F_AI, base_branch=window_branch)
    if not success:
        log_fail(f"创建失败: {result}")
        return False

    log_pass("API 调用成功")
    log_info(f"返回路径: {result.get('path')}")

    # Step 3: 验证
    log_step(3, "验证 Agent Worktree")
    wt_info = get_worktree_info(WT_F_AGENT)
    if not wt_info:
        log_fail("Worktree 不在列表中")
        return False

    log_pass("Worktree 存在")
    log_info(f"  名称: {wt_info.get('name')}")
    log_info(f"  路径: {wt_info.get('path')}")
    log_info(f"  分支: {wt_info.get('branch')}")

    # 保存 Agent worktree 路径到状态
    save_state("agent_path", wt_info.get('path'))

    print(f"\n{GREEN}F2 测试通过{RESET}")
    print(f"{YELLOW}>>> Agent 隔离环境已创建，运行 F3 模拟 Agent 工作 <<<{RESET}")
    return True


def test_f3_agent_work():
    """
    F3: Agent 在隔离环境中工作并提交

    场景：Agent 在隔离环境中修改文件并提交
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}F3: Agent 工作并提交{RESET}")
    print("=" * 60)

    # Step 1: 检查前置条件
    log_step(1, f"检查 Agent Worktree {WT_F_AGENT} 是否存在")
    if not worktree_exists(WT_F_AGENT):
        log_warn(f"Agent Worktree {WT_F_AGENT} 不存在，请先运行 F2")
        return False
    log_pass("Agent Worktree 存在")

    wt_info = get_worktree_info(WT_F_AGENT)
    agent_path = wt_info.get('path')
    log_info(f"Agent Worktree 路径: {agent_path}")

    # Step 2: 在 Agent worktree 中创建测试文件
    log_step(2, "在 Agent Worktree 中创建测试文件")
    test_file_path = os.path.join(agent_path, TEST_FILE_F)

    try:
        with open(test_file_path, 'w', encoding='utf-8') as f:
            f.write(f"场景 F 测试文件\n创建时间: {time.strftime('%Y-%m-%d %H:%M:%S')}\n")
            f.write("此文件由 Agent 在隔离环境中创建\n")
        log_pass(f"测试文件已创建: {TEST_FILE_F}")
    except Exception as e:
        log_fail(f"创建文件失败: {e}")
        return False

    # Step 3: 通过 API 在 Agent worktree 中提交
    log_step(3, "通过 API 在 Agent Worktree 中提交")
    log_info(f"使用 API: commit(worktreeName={WT_F_AGENT})")

    success, result = commit_in_worktree(WT_F_AGENT, "Agent 场景 F 测试提交")
    if not success:
        log_fail(f"提交失败: {result}")
        return False

    if result.get('committed'):
        log_pass("提交成功")
        log_info(f"  commit: {result.get('commit', {}).get('hash', '?')}")
    else:
        log_warn(f"没有提交: {result.get('message')}")

    print(f"\n{GREEN}F3 测试通过{RESET}")
    print(f"{YELLOW}>>> Agent 工作已完成，运行 F4 合并回虚拟窗口 <<<{RESET}")
    return True


def test_f4_merge_to_window():
    """
    F4: 合并回虚拟窗口分支 ★ 场景 F 关键测试点 ★

    场景：Agent 完成工作后，把临时分支合并回虚拟窗口所在的分支
    这是场景 F 的核心：使用 worktreeName 参数在虚拟窗口中执行合并
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}F4: 合并回虚拟窗口（使用 worktreeName）★ 关键测试 ★{RESET}")
    print("=" * 60)

    # Step 1: 检查前置条件
    log_step(1, "检查前置条件")
    if not worktree_exists(WT_F_WINDOW):
        log_warn(f"虚拟窗口 {WT_F_WINDOW} 不存在，请先运行 F1")
        return False
    log_pass(f"虚拟窗口 {WT_F_WINDOW} 存在")

    if not branch_exists(BRANCH_F_AI):
        log_warn(f"Agent 临时分支 {BRANCH_F_AI} 不存在，请先运行 F2+F3")
        return False
    log_pass(f"Agent 临时分支 {BRANCH_F_AI} 存在")

    window_info = get_worktree_info(WT_F_WINDOW)
    window_path = window_info.get('path')
    log_info(f"虚拟窗口路径: {window_path}")
    log_info(f"虚拟窗口分支: {window_info.get('branch')}")

    # Step 2: 使用 worktreeName 参数合并
    log_step(2, "通过 API 合并（使用 worktreeName）")
    log_info(f"使用新 API: merge(sourceBranch={BRANCH_F_AI}, worktreeName={WT_F_WINDOW})")
    log_info("这会在虚拟窗口 Worktree 中执行合并，而不是在主仓库中")

    success, result = merge_branch(
        source_branch=BRANCH_F_AI,
        worktree_name=WT_F_WINDOW,  # ★ 关键：使用 worktreeName 而不是 targetBranch
        commit_message=f"合并 Agent 任务: {BRANCH_F_AI}"
    )

    if not success:
        log_fail(f"合并失败: {result}")
        return False

    if result.get('success'):
        log_pass("合并成功")
        log_info(f"  消息: {result.get('message')}")
    else:
        if result.get('hasConflicts'):
            log_warn("合并有冲突，需要手动解决")
            log_info(f"  消息: {result.get('message')}")
        else:
            log_fail(f"合并失败: {result.get('message')}")
            return False

    # Step 3: 验证合并结果 - 检查虚拟窗口目录中是否有测试文件
    log_step(3, "验证合并结果")
    merged_file_path = os.path.join(window_path, TEST_FILE_F)
    if os.path.exists(merged_file_path):
        log_pass(f"测试文件已合并到虚拟窗口: {TEST_FILE_F}")
        with open(merged_file_path, 'r', encoding='utf-8') as f:
            content = f.read()
            log_info(f"  文件内容预览: {content[:50]}...")
    else:
        log_fail(f"测试文件未出现在虚拟窗口目录中！")
        log_info(f"  期望路径: {merged_file_path}")
        return False

    print(f"\n{GREEN}F4 测试通过{RESET}")
    print(f"{YELLOW}>>> 已合并到虚拟窗口，运行 F5 清理 Agent Worktree <<<{RESET}")
    return True


def test_f5_cleanup_agent():
    """
    F5: 清理 Agent Worktree

    场景：Agent 任务完成，删除 Agent 临时 worktree 和分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}F5: 清理 Agent Worktree{RESET}")
    print("=" * 60)

    # Step 1: 检查 Agent worktree
    log_step(1, f"检查 Agent Worktree {WT_F_AGENT}")
    if not worktree_exists(WT_F_AGENT):
        log_info(f"Agent Worktree {WT_F_AGENT} 已不存在")
    else:
        log_pass("Agent Worktree 存在，将删除")

    # Step 2: 删除 Agent worktree + 临时分支
    log_step(2, "删除 Agent Worktree + 临时分支")
    log_info("参数: delete_branch=True（删除临时分支）")

    if worktree_exists(WT_F_AGENT):
        success, result = delete_worktree(WT_F_AGENT, delete_branch=True)
        if not success:
            log_fail(f"删除失败: {result}")
            return False
        log_pass(f"删除成功: {result.get('message')}")

    # Step 3: 验证
    log_step(3, "验证 Agent Worktree 已删除")
    time.sleep(0.2)
    if worktree_exists(WT_F_AGENT):
        log_fail("Agent Worktree 仍存在！")
        return False
    log_pass("Agent Worktree 已删除")

    # Step 4: 验证临时分支已删除
    log_step(4, "验证临时分支已删除")
    if not branch_exists(BRANCH_F_AI):
        log_pass(f"分支 '{BRANCH_F_AI}' 已删除")
    else:
        log_warn(f"分支 '{BRANCH_F_AI}' 仍存在")

    print(f"\n{GREEN}F5 测试通过{RESET}")
    print(f"{YELLOW}>>> Agent 已清理，运行 F6 清理虚拟窗口 <<<{RESET}")
    return True


def test_f6_cleanup_window():
    """
    F6: 清理虚拟窗口

    场景：用户关闭虚拟窗口，删除 worktree 但保留分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}F6: 清理虚拟窗口（保留分支）{RESET}")
    print("=" * 60)

    # 从状态文件读取虚拟窗口分支（F1 保存的）
    window_branch = load_state("f_branch", BRANCH_F)
    is_test_branch = window_branch.startswith("test/")  # 判断是否是测试分支
    log_info(f"虚拟窗口分支: {window_branch}")

    # Step 1: 检查虚拟窗口
    log_step(1, f"检查虚拟窗口 {WT_F_WINDOW}")
    if not worktree_exists(WT_F_WINDOW):
        log_info(f"虚拟窗口 {WT_F_WINDOW} 已不存在")
    else:
        log_pass("虚拟窗口存在，将删除")

    # Step 2: 删除虚拟窗口（保留分支）
    log_step(2, "删除虚拟窗口（保留分支）")
    log_info("参数: delete_branch=False（保留分支）")

    if worktree_exists(WT_F_WINDOW):
        success, result = delete_worktree(WT_F_WINDOW, delete_branch=False)
        if not success:
            log_fail(f"删除失败: {result}")
            return False
        log_pass(f"删除成功: {result.get('message')}")

    # Step 3: 验证 worktree 已删除
    log_step(3, "验证虚拟窗口 Worktree 已删除")
    time.sleep(0.2)
    if worktree_exists(WT_F_WINDOW):
        log_fail("虚拟窗口 Worktree 仍存在！")
        return False
    log_pass("虚拟窗口 Worktree 已删除")

    # Step 4: 验证分支仍存在
    log_step(4, "验证分支仍存在")
    if branch_exists(window_branch):
        log_pass(f"分支 '{window_branch}' 保留成功")
    else:
        log_fail(f"分支 '{window_branch}' 被意外删除！")
        return False

    # Step 5: 清除状态文件
    log_step(5, "清除状态文件")
    clear_state()
    log_pass("状态已清除")

    print(f"\n{GREEN}F6 测试通过{RESET}")

    # 仅清理测试分支（test/ 开头的分支才删除，用户自定义分支保留）
    if is_test_branch:
        print(f"\n{YELLOW}清理测试分支...{RESET}")
        success, _ = create_worktree("cleanup-f", window_branch)
        if success:
            delete_worktree("cleanup-f", delete_branch=True)
            log_pass(f"测试分支 {window_branch} 已清理")
        else:
            log_warn("清理失败（可手动删除）")
    else:
        log_info(f"保留用户分支: {window_branch}（非 test/ 开头）")

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
    test_wt_prefixes = ["test-", "cleanup-", "temp-"]
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
        success, _ = create_worktree("cleanup-temp", branch)
        if success:
            delete_worktree("cleanup-temp", delete_branch=True)
            br_cleaned += 1

    if br_cleaned:
        print(f"  已清理 {br_cleaned} 个分支")
    else:
        print("  无分支需要清理")

    # 清理状态文件
    if os.path.exists(STATE_FILE):
        clear_state()
        print("  已清除状态文件")


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
    print("  场景 A（并行开发）:")
    print("    a1  创建 Worktree，检出已有分支")
    print("    a2  删除 Worktree，保留分支")
    print()
    print("  场景 B（隔离环境）:")
    print("    b1  创建隔离环境（临时分支 + Worktree）")
    print("    b2  模拟 Agent 工作（创建文件 + 提交）")
    print("    b3  合并回基础分支")
    print("    b4  清理（删除 Worktree + 临时分支）")
    print()
    print("  场景 F（多窗口独立任务）:")
    print("    f1  创建虚拟窗口（检出 test/scheme-f）")
    print("    f2  创建 Agent 隔离环境（基于虚拟窗口分支）")
    print("    f3  Agent 工作并提交")
    print("    f4  合并回虚拟窗口（使用 worktreeName）★")
    print("    f5  清理 Agent Worktree + 临时分支")
    print("    f6  清理虚拟窗口（保留分支）")
    print()
    print("推荐测试顺序:")
    print("  场景 A: python test_worktree.py a1 && python test_worktree.py a2")
    print("  场景 B: python test_worktree.py b1 && python test_worktree.py b2 && ...")
    print("  场景 F: python test_worktree.py f  (完整 F1-F6)")
    print("  完整:   python test_worktree.py a b f")


def main():
    raw_args = sys.argv[1:]
    args = [a.lower() for a in raw_args]

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

    # 解析参数
    a1_branch = None
    b1_base_branch = None
    f1_branch = None
    test_keys = ['a', 'a1', 'a2', 'b', 'b1', 'b2', 'b3', 'b4', 'f', 'f1', 'f2', 'f3', 'f4', 'f5', 'f6']

    # 查找 a1/b1/f1 后面是否跟着分支名
    for i, arg in enumerate(args):
        if arg == 'a1' and i + 1 < len(raw_args):
            next_arg = raw_args[i + 1]
            if next_arg.lower() not in test_keys and not next_arg.startswith('-'):
                a1_branch = next_arg
        elif arg == 'b1' and i + 1 < len(raw_args):
            next_arg = raw_args[i + 1]
            if next_arg.lower() not in test_keys and not next_arg.startswith('-'):
                b1_base_branch = next_arg
        elif arg == 'f1' and i + 1 < len(raw_args):
            next_arg = raw_args[i + 1]
            if next_arg.lower() not in test_keys and not next_arg.startswith('-'):
                f1_branch = next_arg

    # 定义测试
    tests = {
        'a1': ('A1 (创建-检出已有分支)', lambda: test_a1_create(a1_branch)),
        'a2': ('A2 (删除-保留分支)', test_a2_delete),
        'b1': ('B1 (创建隔离环境)', lambda: test_b1_create_isolation(b1_base_branch)),
        'b2': ('B2 (模拟 Agent 工作)', test_b2_agent_work),
        'b3': ('B3 (合并回基础分支)', test_b3_merge_back),
        'b4': ('B4 (清理隔离环境)', test_b4_cleanup),
        'f1': ('F1 (创建虚拟窗口)', lambda: test_f1_create_window(f1_branch)),
        'f2': ('F2 (创建 Agent 隔离环境)', test_f2_create_agent),
        'f3': ('F3 (Agent 工作并提交)', test_f3_agent_work),
        'f4': ('F4 (合并回虚拟窗口) ★', test_f4_merge_to_window),
        'f5': ('F5 (清理 Agent)', test_f5_cleanup_agent),
        'f6': ('F6 (清理虚拟窗口)', test_f6_cleanup_window),
    }

    # 展开 'a', 'b', 'f'
    expanded_args = []
    for arg in args:
        if arg == 'a':
            expanded_args.extend(['a1', 'a2'])
        elif arg == 'b':
            expanded_args.extend(['b1', 'b2', 'b3', 'b4'])
        elif arg == 'f':
            expanded_args.extend(['f1', 'f2', 'f3', 'f4', 'f5', 'f6'])
        elif arg in tests:
            expanded_args.append(arg)

    to_run = []
    for arg in expanded_args:
        if arg not in to_run:
            to_run.append(arg)

    # 如果没有指定，运行全部
    if not to_run:
        to_run = ['a1', 'a2', 'b1', 'b2', 'b3', 'b4']

    results = []
    for key in to_run:
        name, func = tests[key]
        if key == 'a1' and a1_branch:
            name = f"A1 (创建-检出分支: {a1_branch})"
        elif key == 'f1' and f1_branch:
            name = f"F1 (创建虚拟窗口: {f1_branch})"
        passed = func()
        results.append((name, passed))

        # 如果测试失败，询问是否继续
        if not passed and len(to_run) > 1:
            print(f"\n{YELLOW}测试 {name} 失败，后续测试可能受影响{RESET}")

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
