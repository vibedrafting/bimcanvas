#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
高级端口测试脚本 (POST /api/git/ai-job)

测试两种场景下的完整 Agent 工作流：

场景 R（真窗口 - Real Window）：
  用户在主仓库的当前分支上工作，请求 AI 执行任务
  - R1: 创建 AI Job（基于当前分支）
  - R2: 模拟 Agent 工作并提交
  - R3: 合并回当前分支
  - R4: 清理 AI Job

场景 V（虚拟窗口 - Virtual Window）：
  用户打开新窗口检出某个分支，在该窗口中请求 AI 执行任务
  - V1: 创建虚拟窗口（模拟用户打开新窗口）
  - V2: 创建 AI Job（基于虚拟窗口分支）
  - V3: 模拟 Agent 工作并提交
  - V4: 合并回虚拟窗口（使用 worktreeName）
  - V5: 清理 AI Job
  - V6: 清理虚拟窗口

用法：
  python test_worktree.py r            # 运行场景 R（R1+R2+R3+R4）
  python test_worktree.py r1           # R1：创建 AI Job
  python test_worktree.py r2           # R2：模拟工作
  python test_worktree.py r3           # R3：合并
  python test_worktree.py r4           # R4：清理

  python test_worktree.py v            # 运行场景 V（V1+V2+V3+V4+V5+V6）
  python test_worktree.py v 分支名     # 运行场景 V，使用指定分支
  python test_worktree.py v1           # V1：创建虚拟窗口
  python test_worktree.py v1 分支名    # V1：使用指定分支创建虚拟窗口
  python test_worktree.py v2           # V2：创建 AI Job
  python test_worktree.py v3           # V3：模拟工作
  python test_worktree.py v4           # V4：合并
  python test_worktree.py v5           # V5：清理 AI Job
  python test_worktree.py v6           # V6：清理虚拟窗口

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


# ============================================================
# Scheme API 封装（可视化 Diff）
# ============================================================

def scheme_get_modules(source):
    """获取模块数据 GET /api/scheme/{source}/modules"""
    return requests.get(f"{BASE_URL}/api/scheme/{source}/modules")


def scheme_put_modules(source, modules, commit_message=None):
    """保存模块数据 PUT /api/scheme/{source}/modules"""
    data = {"modules": modules}
    if commit_message:
        data["commitMessage"] = commit_message
    return requests.put(f"{BASE_URL}/api/scheme/{source}/modules", json=data)


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


def create_ai_job(name, base_branch):
    """创建 AI Job（高级端口）"""
    data = {"name": name, "baseBranch": base_branch}
    resp = api_post("ai-job", data)
    if not resp.ok:
        print(f"  {RED}[DEBUG]{RESET} HTTP {resp.status_code}")
        try:
            print(f"  {RED}[DEBUG]{RESET} Response: {resp.json()}")
        except:
            print(f"  {RED}[DEBUG]{RESET} Response: {resp.text[:500]}")
    return resp.ok, resp.json() if resp.ok else resp.text


def create_worktree(name, branch, base_branch=None):
    """创建 Worktree（基础端口，仅用于创建虚拟窗口）"""
    data = {"name": name, "branch": branch}
    if base_branch:
        data["baseBranch"] = base_branch
    resp = api_post("worktrees", data)
    if not resp.ok:
        print(f"  {RED}[DEBUG]{RESET} HTTP {resp.status_code}")
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
    """合并分支"""
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
# 状态管理
# ============================================================

STATE_FILE = os.path.join(os.path.dirname(__file__), ".test_state.json")


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
# 测试配置
# ============================================================

# 场景 R 配置（真窗口）
WT_R_AI = "ai-job-r"                 # AI Job Worktree 名称
TEST_FILE_R = "test_real_window.txt" # 测试文件

# 场景 V 配置（虚拟窗口）
BRANCH_V = "test/virtual-window"     # 虚拟窗口分支
WT_V_WINDOW = "virtual-window"       # 虚拟窗口 Worktree
WT_V_AI = "ai-job-v"                 # AI Job Worktree 名称
TEST_FILE_V = "test_virtual_window.txt"  # 测试文件


# ============================================================
# 场景 R：真窗口（Real Window）
# ============================================================

def test_r1_create():
    """
    R1: 创建 AI Job（基于当前分支）

    场景：用户在主仓库的当前分支上，请求 AI 执行任务
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}R1: 创建 AI Job（真窗口场景）{RESET}")
    print("=" * 60)

    base_branch = get_current_branch()
    log_info(f"当前分支（基准分支）: {base_branch}")

    # 清理可能残留的 worktree
    if worktree_exists(WT_R_AI):
        log_info(f"清理残留 worktree: {WT_R_AI}")
        delete_worktree(WT_R_AI, delete_branch=True)

    # Step 1: 调用高级端口创建 AI Job
    log_step(1, "调用 POST /api/git/ai-job")
    log_info(f"请求: name={WT_R_AI}, baseBranch={base_branch}")

    success, result = create_ai_job(WT_R_AI, base_branch)
    if not success:
        log_fail(f"创建失败: {result}")
        return False

    log_pass("API 调用成功")
    worktree_path = result.get('worktreePath')
    branch_name = result.get('branchName')
    log_info(f"返回 worktreePath: {worktree_path}")
    log_info(f"返回 branchName: {branch_name}")

    # Step 2: 验证分支名格式
    log_step(2, "验证自动生成的分支名")
    if branch_name and branch_name.startswith(f"feat/{WT_R_AI}-"):
        log_pass(f"分支名格式正确: {branch_name}")
    else:
        log_fail(f"分支名格式不正确: {branch_name}")
        return False

    # Step 3: 验证 worktree 已创建
    log_step(3, "验证 Worktree")
    wt_info = get_worktree_info(WT_R_AI)
    if not wt_info:
        log_fail("Worktree 不在列表中")
        return False

    log_pass("Worktree 存在")
    log_info(f"  名称: {wt_info.get('name')}")
    log_info(f"  路径: {wt_info.get('path')}")
    log_info(f"  分支: {wt_info.get('branch')}")

    # 保存状态
    save_state("r_base_branch", base_branch)
    save_state("r_ai_branch", branch_name)
    save_state("r_ai_path", worktree_path)

    print(f"\n{GREEN}R1 测试通过{RESET}")
    print(f"{YELLOW}>>> AI Job 已创建，运行 R2 模拟工作 <<<{RESET}")
    return True


def test_r2_work():
    """
    R2: 模拟 Agent 工作并提交

    场景：Agent 在 AI Job 中修改文件并提交
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}R2: Agent 工作并提交（真窗口场景）{RESET}")
    print("=" * 60)

    # Step 1: 检查前置条件
    log_step(1, f"检查 Worktree {WT_R_AI} 是否存在")
    if not worktree_exists(WT_R_AI):
        log_warn(f"Worktree {WT_R_AI} 不存在，请先运行 R1")
        return False
    log_pass("Worktree 存在")

    wt_info = get_worktree_info(WT_R_AI)
    worktree_path = wt_info.get('path')
    log_info(f"Worktree 路径: {worktree_path}")

    # Step 2: 在 worktree 中创建测试文件
    log_step(2, "在 AI Job Worktree 中创建测试文件")
    test_file_path = os.path.join(worktree_path, TEST_FILE_R)

    try:
        with open(test_file_path, 'w', encoding='utf-8') as f:
            f.write(f"真窗口测试文件\n创建时间: {time.strftime('%Y-%m-%d %H:%M:%S')}\n")
            f.write("此文件由 Agent 在真窗口场景的 AI Job 中创建\n")
        log_pass(f"测试文件已创建: {TEST_FILE_R}")
    except Exception as e:
        log_fail(f"创建文件失败: {e}")
        return False

    # Step 3: 通过 API 在 worktree 中提交
    log_step(3, "通过 API 提交")
    success, result = commit_in_worktree(WT_R_AI, "Agent 真窗口测试提交")
    if not success:
        log_fail(f"提交失败: {result}")
        return False

    if result.get('committed'):
        log_pass("提交成功")
        log_info(f"  commit: {result.get('commit', {}).get('hash', '?')}")
    else:
        log_warn(f"没有提交: {result.get('message')}")

    print(f"\n{GREEN}R2 测试通过{RESET}")
    print(f"{YELLOW}>>> Agent 工作已完成，运行 R3 合并 <<<{RESET}")
    return True


def test_r3_merge():
    """
    R3: 合并回当前分支

    场景：Agent 完成工作后，把临时分支合并回基准分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}R3: 合并回基准分支（真窗口场景）{RESET}")
    print("=" * 60)

    # 从状态文件读取
    base_branch = load_state("r_base_branch")
    ai_branch = load_state("r_ai_branch")

    if not base_branch:
        base_branch = get_current_branch()
        log_warn(f"无状态文件，使用当前分支: {base_branch}")

    log_info(f"基准分支: {base_branch}")
    log_info(f"AI 分支: {ai_branch or '(从 worktree 读取)'}")

    # Step 1: 检查前置条件
    log_step(1, "检查前置条件")
    wt_info = get_worktree_info(WT_R_AI)
    if not wt_info:
        log_warn(f"Worktree {WT_R_AI} 不存在，请先运行 R1+R2")
        return False

    ai_branch = wt_info.get('branch') or ai_branch
    log_pass(f"AI Job 存在，分支: {ai_branch}")

    # Step 2: 合并
    log_step(2, "通过 API 合并")
    log_info(f"使用 API: merge(sourceBranch={ai_branch}, targetBranch={base_branch})")

    success, result = merge_branch(
        source_branch=ai_branch,
        target_branch=base_branch,
        commit_message=f"合并 Agent 任务: {ai_branch}"
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
        else:
            log_fail(f"合并失败: {result.get('message')}")
            return False

    print(f"\n{GREEN}R3 测试通过{RESET}")
    print(f"{YELLOW}>>> 已合并到基准分支，运行 R4 清理 <<<{RESET}")
    return True


def test_r4_cleanup():
    """
    R4: 清理 AI Job

    场景：删除 AI Job 的 worktree 和临时分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}R4: 清理 AI Job（真窗口场景）{RESET}")
    print("=" * 60)

    ai_branch = load_state("r_ai_branch")
    log_info(f"AI Job 分支: {ai_branch or '(未知)'}")

    # Step 1: 检查 worktree
    log_step(1, f"检查 Worktree {WT_R_AI}")
    if not worktree_exists(WT_R_AI):
        log_info(f"Worktree {WT_R_AI} 已不存在")
    else:
        log_pass("Worktree 存在，将删除")

    # Step 2: 删除 worktree + 分支
    log_step(2, "删除 Worktree + 临时分支")
    if worktree_exists(WT_R_AI):
        success, result = delete_worktree(WT_R_AI, delete_branch=True)
        if not success:
            log_fail(f"删除失败: {result}")
            return False
        log_pass(f"删除成功: {result.get('message')}")

    # Step 3: 验证 worktree 已删除
    log_step(3, "验证 Worktree 已删除")
    time.sleep(0.2)
    if worktree_exists(WT_R_AI):
        log_fail("Worktree 仍存在！")
        return False
    log_pass("Worktree 已删除")

    # Step 4: 验证分支已删除
    log_step(4, "验证临时分支已删除")
    if ai_branch and not branch_exists(ai_branch):
        log_pass(f"分支 '{ai_branch}' 已删除")
    elif ai_branch:
        log_warn(f"分支 '{ai_branch}' 仍存在（可手动删除）")
    else:
        log_info("无法验证分支（状态文件中无分支信息）")

    # Step 5: 清除状态文件
    log_step(5, "清除状态文件")
    clear_state()
    log_pass("状态已清除")

    print(f"\n{GREEN}R4 测试通过{RESET}")
    print(f"\n{GREEN}★ 场景 R（真窗口）完整流程测试通过 ★{RESET}")
    return True


# ============================================================
# 场景 V：虚拟窗口（Virtual Window）
# ============================================================

def test_v1_create_window(custom_branch=None):
    """
    V1: 创建虚拟窗口

    场景：用户打开新窗口，检出一个分支
    注意：这一步使用基础端口，因为这是"用户行为"而非"AI Job"
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}V1: 创建虚拟窗口{RESET}")
    print("=" * 60)

    branch = custom_branch or BRANCH_V
    base_branch = get_current_branch()

    # 保存使用的分支到状态
    save_state("v_window_branch", branch)

    # Step 1: 准备 - 确保虚拟窗口分支存在
    log_step(1, f"准备：确保虚拟窗口分支 {branch} 存在")
    if not branch_exists(branch):
        log_info("分支不存在，先创建...")
        success, _ = create_worktree("temp-create-v", branch, base_branch=base_branch)
        if success:
            delete_worktree("temp-create-v", delete_branch=False)
            log_pass(f"分支 {branch} 已创建")
        else:
            log_fail("无法创建测试分支")
            return False
    else:
        log_pass(f"分支 {branch} 已存在")

    # 清理可能残留的 worktree
    if worktree_exists(WT_V_WINDOW):
        log_info(f"清理残留 worktree: {WT_V_WINDOW}")
        delete_worktree(WT_V_WINDOW, delete_branch=False)

    # Step 2: 创建虚拟窗口 worktree
    log_step(2, f"创建虚拟窗口: {WT_V_WINDOW}")
    log_info(f"检出分支: {branch}")

    success, result = create_worktree(WT_V_WINDOW, branch)
    if not success:
        log_fail(f"创建失败: {result}")
        return False

    log_pass("虚拟窗口创建成功")
    log_info(f"返回路径: {result.get('path')}")

    # Step 3: 验证
    log_step(3, "验证虚拟窗口")
    wt_info = get_worktree_info(WT_V_WINDOW)
    if not wt_info:
        log_fail("Worktree 不在列表中")
        return False

    log_pass("Worktree 存在")
    log_info(f"  名称: {wt_info.get('name')}")
    log_info(f"  路径: {wt_info.get('path')}")
    log_info(f"  分支: {wt_info.get('branch')}")

    # 保存虚拟窗口路径到状态
    save_state("v_window_path", wt_info.get('path'))

    print(f"\n{GREEN}V1 测试通过{RESET}")
    print(f"{YELLOW}>>> 虚拟窗口已创建，运行 V2 创建 AI Job <<<{RESET}")
    return True


def test_v2_create_ai_job():
    """
    V2: 创建 AI Job（基于虚拟窗口分支）

    场景：用户在虚拟窗口中请求 AI 执行任务
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}V2: 创建 AI Job（虚拟窗口场景）{RESET}")
    print("=" * 60)

    # 从状态文件读取虚拟窗口分支
    window_branch = load_state("v_window_branch", BRANCH_V)
    log_info(f"虚拟窗口分支（基准分支）: {window_branch}")

    # Step 1: 检查虚拟窗口是否存在
    log_step(1, f"检查虚拟窗口 {WT_V_WINDOW} 是否存在")
    if not worktree_exists(WT_V_WINDOW):
        log_warn(f"虚拟窗口 {WT_V_WINDOW} 不存在，请先运行 V1")
        return False
    log_pass("虚拟窗口存在")

    # 清理可能残留的 AI Job worktree
    if worktree_exists(WT_V_AI):
        log_info(f"清理残留 worktree: {WT_V_AI}")
        delete_worktree(WT_V_AI, delete_branch=True)

    # Step 2: 调用高级端口创建 AI Job
    log_step(2, "调用 POST /api/git/ai-job")
    log_info(f"请求: name={WT_V_AI}, baseBranch={window_branch}")

    success, result = create_ai_job(WT_V_AI, window_branch)
    if not success:
        log_fail(f"创建失败: {result}")
        return False

    log_pass("API 调用成功")
    worktree_path = result.get('worktreePath')
    branch_name = result.get('branchName')
    log_info(f"返回 worktreePath: {worktree_path}")
    log_info(f"返回 branchName: {branch_name}")

    # Step 3: 验证分支名格式
    log_step(3, "验证自动生成的分支名")
    if branch_name and branch_name.startswith(f"feat/{WT_V_AI}-"):
        log_pass(f"分支名格式正确: {branch_name}")
    else:
        log_fail(f"分支名格式不正确: {branch_name}")
        return False

    # Step 4: 验证 worktree 已创建
    log_step(4, "验证 AI Job Worktree")
    wt_info = get_worktree_info(WT_V_AI)
    if not wt_info:
        log_fail("Worktree 不在列表中")
        return False

    log_pass("Worktree 存在")
    log_info(f"  名称: {wt_info.get('name')}")
    log_info(f"  路径: {wt_info.get('path')}")
    log_info(f"  分支: {wt_info.get('branch')}")

    # 保存状态
    save_state("v_ai_branch", branch_name)
    save_state("v_ai_path", worktree_path)

    print(f"\n{GREEN}V2 测试通过{RESET}")
    print(f"{YELLOW}>>> AI Job 已创建，运行 V3 模拟工作 <<<{RESET}")
    return True


def test_v3_work():
    """
    V3: 模拟 Agent 工作并提交

    场景：Agent 在 AI Job 中修改文件并提交
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}V3: Agent 工作并提交（虚拟窗口场景）{RESET}")
    print("=" * 60)

    # Step 1: 检查前置条件
    log_step(1, f"检查 Worktree {WT_V_AI} 是否存在")
    if not worktree_exists(WT_V_AI):
        log_warn(f"Worktree {WT_V_AI} 不存在，请先运行 V2")
        return False
    log_pass("Worktree 存在")

    wt_info = get_worktree_info(WT_V_AI)
    worktree_path = wt_info.get('path')
    log_info(f"Worktree 路径: {worktree_path}")

    # Step 2: 在 worktree 中创建测试文件
    log_step(2, "在 AI Job Worktree 中创建测试文件")
    test_file_path = os.path.join(worktree_path, TEST_FILE_V)

    try:
        with open(test_file_path, 'w', encoding='utf-8') as f:
            f.write(f"虚拟窗口测试文件\n创建时间: {time.strftime('%Y-%m-%d %H:%M:%S')}\n")
            f.write("此文件由 Agent 在虚拟窗口场景的 AI Job 中创建\n")
        log_pass(f"测试文件已创建: {TEST_FILE_V}")
    except Exception as e:
        log_fail(f"创建文件失败: {e}")
        return False

    # Step 3: 通过 API 在 worktree 中提交
    log_step(3, "通过 API 提交")
    success, result = commit_in_worktree(WT_V_AI, "Agent 虚拟窗口测试提交")
    if not success:
        log_fail(f"提交失败: {result}")
        return False

    if result.get('committed'):
        log_pass("提交成功")
        log_info(f"  commit: {result.get('commit', {}).get('hash', '?')}")
    else:
        log_warn(f"没有提交: {result.get('message')}")

    print(f"\n{GREEN}V3 测试通过{RESET}")
    print(f"{YELLOW}>>> Agent 工作已完成，运行 V4 合并 <<<{RESET}")
    return True


def test_v4_merge():
    """
    V4: 合并回虚拟窗口 ★ 关键区别 ★

    场景：Agent 完成工作后，把临时分支合并回虚拟窗口所在的分支
    关键：使用 worktreeName 参数在虚拟窗口中执行合并
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}V4: 合并回虚拟窗口（使用 worktreeName）★ 关键测试 ★{RESET}")
    print("=" * 60)

    ai_branch = load_state("v_ai_branch")

    # Step 1: 检查前置条件
    log_step(1, "检查前置条件")
    if not worktree_exists(WT_V_WINDOW):
        log_warn(f"虚拟窗口 {WT_V_WINDOW} 不存在，请先运行 V1")
        return False
    log_pass(f"虚拟窗口 {WT_V_WINDOW} 存在")

    wt_info = get_worktree_info(WT_V_AI)
    if not wt_info:
        log_warn(f"AI Job {WT_V_AI} 不存在，请先运行 V2+V3")
        return False

    ai_branch = wt_info.get('branch') or ai_branch
    log_pass(f"AI Job 存在，分支: {ai_branch}")

    window_info = get_worktree_info(WT_V_WINDOW)
    window_path = window_info.get('path')
    log_info(f"虚拟窗口路径: {window_path}")
    log_info(f"虚拟窗口分支: {window_info.get('branch')}")

    # Step 2: 使用 worktreeName 参数合并
    log_step(2, "通过 API 合并（使用 worktreeName）")
    log_info(f"使用 API: merge(sourceBranch={ai_branch}, worktreeName={WT_V_WINDOW})")
    log_info("这会在虚拟窗口 Worktree 中执行合并，而不是在主仓库中")

    success, result = merge_branch(
        source_branch=ai_branch,
        worktree_name=WT_V_WINDOW,  # ★ 关键：使用 worktreeName
        commit_message=f"合并 Agent 任务: {ai_branch}"
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
        else:
            log_fail(f"合并失败: {result.get('message')}")
            return False

    # Step 3: 验证合并结果 - 检查虚拟窗口目录中是否有测试文件
    log_step(3, "验证合并结果")
    merged_file_path = os.path.join(window_path, TEST_FILE_V)
    if os.path.exists(merged_file_path):
        log_pass(f"测试文件已合并到虚拟窗口: {TEST_FILE_V}")
        with open(merged_file_path, 'r', encoding='utf-8') as f:
            content = f.read()
            log_info(f"  文件内容预览: {content[:50]}...")
    else:
        log_fail(f"测试文件未出现在虚拟窗口目录中！")
        log_info(f"  期望路径: {merged_file_path}")
        return False

    print(f"\n{GREEN}V4 测试通过{RESET}")
    print(f"{YELLOW}>>> 已合并到虚拟窗口，运行 V5 清理 AI Job <<<{RESET}")
    return True


def test_v5_cleanup_ai():
    """
    V5: 清理 AI Job

    场景：删除 AI Job 的 worktree 和临时分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}V5: 清理 AI Job（虚拟窗口场景）{RESET}")
    print("=" * 60)

    ai_branch = load_state("v_ai_branch")
    log_info(f"AI Job 分支: {ai_branch or '(未知)'}")

    # Step 1: 检查 AI Job worktree
    log_step(1, f"检查 AI Job Worktree {WT_V_AI}")
    if not worktree_exists(WT_V_AI):
        log_info(f"AI Job Worktree {WT_V_AI} 已不存在")
    else:
        log_pass("AI Job Worktree 存在，将删除")

    # Step 2: 删除 AI Job worktree + 临时分支
    log_step(2, "删除 AI Job Worktree + 临时分支")
    if worktree_exists(WT_V_AI):
        success, result = delete_worktree(WT_V_AI, delete_branch=True)
        if not success:
            log_fail(f"删除失败: {result}")
            return False
        log_pass(f"删除成功: {result.get('message')}")

    # Step 3: 验证
    log_step(3, "验证 AI Job Worktree 已删除")
    time.sleep(0.2)
    if worktree_exists(WT_V_AI):
        log_fail("AI Job Worktree 仍存在！")
        return False
    log_pass("AI Job Worktree 已删除")

    # Step 4: 验证临时分支已删除
    log_step(4, "验证临时分支已删除")
    if ai_branch and not branch_exists(ai_branch):
        log_pass(f"分支 '{ai_branch}' 已删除")
    elif ai_branch:
        log_warn(f"分支 '{ai_branch}' 仍存在")

    print(f"\n{GREEN}V5 测试通过{RESET}")
    print(f"{YELLOW}>>> AI Job 已清理，运行 V6 清理虚拟窗口 <<<{RESET}")
    return True


def test_v6_cleanup_window():
    """
    V6: 清理虚拟窗口

    场景：用户关闭虚拟窗口，删除 worktree 但保留分支
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}V6: 清理虚拟窗口（保留分支）{RESET}")
    print("=" * 60)

    window_branch = load_state("v_window_branch", BRANCH_V)
    is_test_branch = window_branch.startswith("test/")
    log_info(f"虚拟窗口分支: {window_branch}")

    # Step 1: 检查虚拟窗口
    log_step(1, f"检查虚拟窗口 {WT_V_WINDOW}")
    if not worktree_exists(WT_V_WINDOW):
        log_info(f"虚拟窗口 {WT_V_WINDOW} 已不存在")
    else:
        log_pass("虚拟窗口存在，将删除")

    # Step 2: 删除虚拟窗口（保留分支）
    log_step(2, "删除虚拟窗口（保留分支）")
    if worktree_exists(WT_V_WINDOW):
        success, result = delete_worktree(WT_V_WINDOW, delete_branch=False)
        if not success:
            log_fail(f"删除失败: {result}")
            return False
        log_pass(f"删除成功: {result.get('message')}")

    # Step 3: 验证 worktree 已删除
    log_step(3, "验证虚拟窗口 Worktree 已删除")
    time.sleep(0.2)
    if worktree_exists(WT_V_WINDOW):
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

    print(f"\n{GREEN}V6 测试通过{RESET}")

    # 仅清理测试分支
    if is_test_branch:
        print(f"\n{YELLOW}清理测试分支...{RESET}")
        success, _ = create_worktree("cleanup-v", window_branch)
        if success:
            delete_worktree("cleanup-v", delete_branch=True)
            log_pass(f"测试分支 {window_branch} 已清理")
        else:
            log_warn("清理失败（可手动删除）")
    else:
        log_info(f"保留用户分支: {window_branch}（非 test/ 开头）")

    print(f"\n{GREEN}★ 场景 V（虚拟窗口）完整流程测试通过 ★{RESET}")
    return True


# ============================================================
# Scheme API 测试（可视化 Diff）
# ============================================================

def test_scheme():
    """
    测试 SchemeController API：跨分支/Worktree 模块数据读写

    测试流程：
    1. 读取主仓库模块数据 (GET /api/scheme/main/modules)
    2. 创建 AI Job
    3. 读取 AI Job 模块数据 (GET /api/scheme/worktree:{name}/modules)
    4. 对比两者
    5. 清理
    """
    print("\n" + "=" * 60)
    print(f"{CYAN}Scheme API 测试（可视化 Diff）{RESET}")
    print("=" * 60)

    # Step 1: 读取主仓库模块数据
    log_step(1, "读取主仓库模块数据")
    resp = scheme_get_modules("main")
    if not resp.ok:
        log_fail(f"读取失败: {resp.status_code} - {resp.text}")
        return False

    main_data = resp.json()
    log_pass(f"读取成功: source={main_data.get('source')}, branch={main_data.get('branch')}")
    log_info(f"  模块数量: {len(main_data.get('modules', []))}")

    # Step 2: 创建临时 AI Job 用于测试
    log_step(2, "创建临时 AI Job (scheme-test)")
    current_branch = get_current_branch()
    resp = api_post("ai-job", {
        "name": "scheme-test",
        "baseBranch": current_branch
    })

    if not resp.ok:
        log_fail(f"创建失败: {resp.text}")
        return False

    ai_data = resp.json()
    wt_name = "scheme-test"
    log_pass(f"创建成功: {ai_data.get('worktreePath')}")
    log_info(f"  分支: {ai_data.get('branchName')}")

    # Step 3: 读取 AI Job 模块数据
    log_step(3, f"读取 AI Job 模块数据 (worktree:{wt_name})")
    resp = scheme_get_modules(f"worktree:{wt_name}")
    if not resp.ok:
        log_fail(f"读取失败: {resp.status_code} - {resp.text}")
        # 清理
        api_delete(f"worktrees/{wt_name}?deleteBranch=true")
        return False

    wt_data = resp.json()
    log_pass(f"读取成功: source={wt_data.get('source')}, branch={wt_data.get('branch')}")
    log_info(f"  模块数量: {len(wt_data.get('modules', []))}")

    # Step 4: 对比数据
    log_step(4, "对比模块数据")
    main_count = len(main_data.get('modules', []))
    wt_count = len(wt_data.get('modules', []))

    if main_count == wt_count:
        log_pass(f"模块数量一致: {main_count}")
    else:
        log_warn(f"模块数量不一致: main={main_count}, worktree={wt_count}")

    # Step 5: 清理 AI Job
    log_step(5, "清理测试 AI Job")
    resp = api_delete(f"worktrees/{wt_name}?deleteBranch=true")
    if resp.ok:
        log_pass("清理成功")
    else:
        log_warn(f"清理失败: {resp.text}")

    print(f"\n{GREEN}★ Scheme API 测试通过 ★{RESET}")
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
    test_branches = [b for b in branches if b.startswith('test/') or b.startswith('feat/')]
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
    test_wt_names = [WT_R_AI, WT_V_WINDOW, WT_V_AI, "cleanup-v", "cleanup-r", "temp-create-v"]
    worktrees = get_worktrees()
    wt_cleaned = 0

    for wt in worktrees:
        name = wt.get('name', '')
        if wt.get('isMain'):
            continue
        if name in test_wt_names or name.startswith('test-') or name.startswith('cleanup-') or name.startswith('temp-'):
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
    test_branches = [b for b in branches if b.startswith('test/') or b.startswith('feat/ai-job')]
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
    print("  场景 R（真窗口）：")
    print("    r1  创建 AI Job（基于当前分支）")
    print("    r2  模拟 Agent 工作并提交")
    print("    r3  合并回当前分支")
    print("    r4  清理 AI Job")
    print()
    print("  场景 V（虚拟窗口）：")
    print("    v1          创建虚拟窗口（使用默认测试分支）")
    print("    v1 分支名   创建虚拟窗口（使用指定分支）")
    print("    v2          创建 AI Job（基于虚拟窗口分支）")
    print("    v3          模拟 Agent 工作并提交")
    print("    v4          合并回虚拟窗口 ★ 关键测试 ★")
    print("    v5          清理 AI Job")
    print("    v6          清理虚拟窗口")
    print()
    print("  Scheme API（可视化 Diff）：")
    print("    scheme      测试跨分支/Worktree 模块数据读写")
    print()
    print("推荐测试顺序：")
    print("  场景 R:  python test_worktree.py r")
    print("  场景 V:  python test_worktree.py v")
    print("  场景 V:  python test_worktree.py v scheme/xxx  (指定分支)")
    print("  完整:    python test_worktree.py r v")


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

    if 'scheme' in args:
        print(f"Server: {BASE_URL} | 当前分支: {info}")
        test_scheme()
        return

    # 运行测试
    print("=" * 60)
    print("高级端口测试 (POST /api/git/ai-job)")
    print("=" * 60)
    print(f"Server: {BASE_URL}")
    print(f"当前分支: {info}")

    # 解析参数
    v1_branch = None
    test_keys = ['r', 'r1', 'r2', 'r3', 'r4', 'v', 'v1', 'v2', 'v3', 'v4', 'v5', 'v6']

    # 查找 v 或 v1 后面是否跟着分支名
    for i, arg in enumerate(args):
        if arg in ('v', 'v1') and i + 1 < len(raw_args):
            next_arg = raw_args[i + 1]
            if next_arg.lower() not in test_keys and not next_arg.startswith('-'):
                v1_branch = next_arg
                break  # 找到就退出

    # 定义测试
    tests = {
        'r1': ('R1 (创建 AI Job)', test_r1_create),
        'r2': ('R2 (Agent 工作)', test_r2_work),
        'r3': ('R3 (合并)', test_r3_merge),
        'r4': ('R4 (清理)', test_r4_cleanup),
        'v1': ('V1 (创建虚拟窗口)', lambda: test_v1_create_window(v1_branch)),
        'v2': ('V2 (创建 AI Job)', test_v2_create_ai_job),
        'v3': ('V3 (Agent 工作)', test_v3_work),
        'v4': ('V4 (合并) ★', test_v4_merge),
        'v5': ('V5 (清理 AI Job)', test_v5_cleanup_ai),
        'v6': ('V6 (清理虚拟窗口)', test_v6_cleanup_window),
    }

    # 展开 'r', 'v'
    expanded_args = []
    for arg in args:
        if arg == 'r':
            expanded_args.extend(['r1', 'r2', 'r3', 'r4'])
        elif arg == 'v':
            expanded_args.extend(['v1', 'v2', 'v3', 'v4', 'v5', 'v6'])
        elif arg in tests:
            expanded_args.append(arg)

    to_run = []
    for arg in expanded_args:
        if arg not in to_run:
            to_run.append(arg)

    # 如果没有指定，显示帮助
    if not to_run:
        print_usage()
        return

    results = []
    for key in to_run:
        name, func = tests[key]
        if key == 'v1' and v1_branch:
            name = f"V1 (创建虚拟窗口: {v1_branch})"
        passed = func()
        results.append((name, passed))

        # 如果测试失败，提示
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
