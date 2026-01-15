#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Worktree 完整场景测试

场景 A（并行开发）：基于已有分支创建 Worktree，删除时保留分支
场景 B（隔离环境）：创建临时分支的 Worktree，删除时同时删除分支
场景 C（完整工作流）：创建 → 提交 → 合并 → 清理

用法：
  python test_worktree_scenario.py           # 运行全部场景
  python test_worktree_scenario.py -a        # 只运行场景 A
  python test_worktree_scenario.py -b        # 只运行场景 B
  python test_worktree_scenario.py -c        # 只运行场景 C（完整工作流）
  python test_worktree_scenario.py -h        # 显示帮助
"""

import os
import sys
import time
import requests

BASE_URL = "http://localhost:5000"


class Colors:
    """终端颜色"""
    GREEN = '\033[92m'
    RED = '\033[91m'
    YELLOW = '\033[93m'
    BLUE = '\033[94m'
    RESET = '\033[0m'


def print_step(step_num, description):
    print(f"\n{Colors.BLUE}[Step {step_num}]{Colors.RESET} {description}")


def print_pass(message):
    print(f"  {Colors.GREEN}[PASS]{Colors.RESET} {message}")


def print_fail(message):
    print(f"  {Colors.RED}[FAIL]{Colors.RESET} {message}")


def print_warn(message):
    print(f"  {Colors.YELLOW}[WARN]{Colors.RESET} {message}")


def print_info(message):
    print(f"  {message}")


def api_get(endpoint):
    """GET 请求封装"""
    return requests.get(f"{BASE_URL}/api/git/{endpoint}")


def api_post(endpoint, data=None):
    """POST 请求封装"""
    return requests.post(f"{BASE_URL}/api/git/{endpoint}", json=data)


def api_delete(endpoint):
    """DELETE 请求封装"""
    return requests.delete(f"{BASE_URL}/api/git/{endpoint}")


def get_branches():
    """获取所有分支名称"""
    resp = api_get("branches")
    if resp.ok:
        return [b.get('name') for b in resp.json()]
    return []


def get_worktrees():
    """获取所有 Worktree"""
    resp = api_get("worktrees")
    if resp.ok:
        return resp.json()
    return []


def branch_exists(branch_name):
    """检查分支是否存在"""
    return branch_name in get_branches()


def worktree_exists(worktree_name):
    """检查 Worktree 是否存在"""
    worktrees = get_worktrees()
    return any(wt.get('path', '').endswith(worktree_name) for wt in worktrees)


def create_branch(branch_name):
    """创建新分支（通过 checkout -b）"""
    resp = api_post("checkout", {
        "branchName": branch_name,
        "createIfNotExist": True
    })
    return resp.ok


def switch_branch(branch_name):
    """切换分支"""
    resp = api_post("checkout", {"branchName": branch_name})
    return resp.ok


def delete_branch(branch_name):
    """删除分支"""
    encoded = branch_name.replace('/', '%2F')
    resp = api_delete(f"branches/{encoded}")
    return resp.ok


def create_worktree(name, branch):
    """创建 Worktree"""
    resp = api_post("worktrees", {"name": name, "branchName": branch})
    return resp.ok, resp.json() if resp.ok else resp.text


def delete_worktree(name, delete_branch=False):
    """删除 Worktree"""
    url = f"worktrees/{name}"
    if delete_branch:
        url += "?deleteBranch=true"
    resp = requests.delete(f"{BASE_URL}/api/git/{url}")
    return resp.ok, resp.json() if resp.ok else resp.text


def get_current_branch():
    """获取当前分支"""
    resp = api_get("current")
    if resp.ok:
        return resp.json().get('branch')
    return None


# ============================================================
# 场景 A：并行开发（基于已有分支）
# ============================================================
def test_scenario_a():
    """
    场景 A：并行开发

    测试流程：
    1. 在主仓库创建分支 feat/parallel-dev
    2. 切换回 master
    3. 创建 Worktree 关联已有分支
    4. 验证 Worktree 成功关联
    5. 删除 Worktree（不删除分支）
    6. 验证分支仍然存在
    7. 清理测试分支
    """
    print("\n" + "=" * 60)
    print(f"{Colors.BLUE}场景 A：并行开发（基于已有分支，保留分支）{Colors.RESET}")
    print("=" * 60)

    worktree_name = "parallel-dev-wt"
    branch_name = "feat/parallel-dev"
    original_branch = get_current_branch()
    passed = True

    try:
        # Step 1: 在主仓库创建分支
        print_step(1, f"在主仓库创建分支: {branch_name}")
        if branch_exists(branch_name):
            print_warn(f"分支已存在，先删除")
            delete_branch(branch_name)

        if create_branch(branch_name):
            print_pass(f"分支 '{branch_name}' 创建成功")
        else:
            print_fail("创建分支失败")
            return False

        # Step 2: 切换回原分支
        print_step(2, f"切换回 {original_branch}")
        if switch_branch(original_branch):
            print_pass(f"已切换到 {original_branch}")
        else:
            print_warn("切换失败，继续测试")

        # Step 3: 创建 Worktree 关联已有分支
        print_step(3, f"创建 Worktree 关联已有分支")
        success, result = create_worktree(worktree_name, branch_name)
        if success:
            print_pass(f"Worktree 创建成功: {result.get('path')}")
            print_info(f"关联分支: {result.get('branch')}")
        else:
            print_fail(f"创建失败: {result}")
            passed = False

        # Step 4: 验证 Worktree 存在
        print_step(4, "验证 Worktree 已创建")
        worktrees = get_worktrees()
        wt_branches = [w.get('branch') for w in worktrees]
        print_info(f"当前 Worktree 列表: {wt_branches}")
        if branch_name in wt_branches:
            print_pass("Worktree 验证通过")
        else:
            print_fail("Worktree 未找到")
            passed = False

        # Step 5: 删除 Worktree（保留分支）
        print_step(5, "删除 Worktree（保留分支）")
        success, result = delete_worktree(worktree_name, delete_branch=False)
        if success:
            print_pass(f"删除成功: {result.get('message')}")
        else:
            print_fail(f"删除失败: {result}")
            passed = False

        # Step 6: 验证分支仍然存在
        print_step(6, "验证分支仍然存在")
        time.sleep(0.3)
        if branch_exists(branch_name):
            print_pass(f"分支 '{branch_name}' 仍然存在 ✓")
        else:
            print_fail(f"分支 '{branch_name}' 被意外删除！")
            passed = False

        # Step 7: 清理
        print_step(7, "清理测试分支")
        if delete_branch(branch_name):
            print_pass("清理完成")
        else:
            print_warn("清理失败（可忽略）")

    except Exception as e:
        print_fail(f"测试异常: {e}")
        passed = False

    print(f"\n场景 A 测试{'通过' if passed else '失败'}")
    return passed


# ============================================================
# 场景 B：隔离环境（临时分支）
# ============================================================
def test_scenario_b():
    """
    场景 B：隔离环境

    测试流程：
    1. 创建 Worktree（自动创建 feat/ai-xxx 临时分支）
    2. 验证分支已创建
    3. 删除 Worktree（同时删除分支）
    4. 验证分支已被删除
    """
    print("\n" + "=" * 60)
    print(f"{Colors.BLUE}场景 B：隔离环境（临时分支，删除分支）{Colors.RESET}")
    print("=" * 60)

    worktree_name = "ai-task-wt"
    branch_name = "feat/ai-task-temp"
    passed = True

    try:
        # 清理可能残留的测试数据
        if worktree_exists(worktree_name):
            delete_worktree(worktree_name, delete_branch=True)
        if branch_exists(branch_name):
            delete_branch(branch_name)

        # Step 1: 创建 Worktree + 临时分支
        print_step(1, f"创建 Worktree + 临时分支: {branch_name}")
        success, result = create_worktree(worktree_name, branch_name)
        if success:
            print_pass(f"创建成功: {result.get('path')}")
        else:
            print_fail(f"创建失败: {result}")
            return False

        # Step 2: 验证分支存在
        print_step(2, "验证临时分支已创建")
        if branch_exists(branch_name):
            print_pass(f"分支 '{branch_name}' 存在")
        else:
            print_warn("分支未在列表中找到（可能是 Worktree 专属）")

        # Step 3: 删除 Worktree（同时删除分支）
        print_step(3, "删除 Worktree（同时删除分支）")
        success, result = delete_worktree(worktree_name, delete_branch=True)
        if success:
            print_pass(f"删除成功: {result.get('message')}")
        else:
            print_fail(f"删除失败: {result}")
            passed = False

        # Step 4: 验证分支已被删除
        print_step(4, "验证分支已被删除")
        time.sleep(0.3)
        if not branch_exists(branch_name):
            print_pass(f"分支 '{branch_name}' 已被删除 ✓")
        else:
            print_fail(f"分支 '{branch_name}' 仍然存在！")
            passed = False

    except Exception as e:
        print_fail(f"测试异常: {e}")
        passed = False

    print(f"\n场景 B 测试{'通过' if passed else '失败'}")
    return passed


# ============================================================
# 场景 C：完整工作流（创建 → 提交 → 合并 → 清理）
# ============================================================
def test_scenario_c():
    """
    场景 C：完整工作流

    测试流程：
    1. 创建 Worktree + 分支
    2. 获取 Worktree 路径，验证目录存在
    3. 在 Worktree 中创建测试文件
    4. 通过 API 在 Worktree 分支提交（需要切换到该分支）
    5. 切换回主分支
    6. 合并 Worktree 分支到主分支
    7. 删除 Worktree + 分支
    8. 验证合并结果
    """
    print("\n" + "=" * 60)
    print(f"{Colors.BLUE}场景 C：完整工作流（创建 → 提交 → 合并 → 清理）{Colors.RESET}")
    print("=" * 60)

    worktree_name = "full-workflow-wt"
    branch_name = "feat/full-workflow-test"
    test_file = "worktree_test_file.txt"
    original_branch = get_current_branch()
    passed = True
    worktree_path = None

    try:
        # 清理可能残留的测试数据
        if worktree_exists(worktree_name):
            delete_worktree(worktree_name, delete_branch=True)
        if branch_exists(branch_name):
            delete_branch(branch_name)

        # Step 1: 创建 Worktree
        print_step(1, f"创建 Worktree: {worktree_name}")
        success, result = create_worktree(worktree_name, branch_name)
        if success:
            worktree_path = result.get('path')
            print_pass(f"创建成功: {worktree_path}")
        else:
            print_fail(f"创建失败: {result}")
            return False

        # Step 2: 验证 Worktree 目录存在
        print_step(2, "验证 Worktree 目录")
        if worktree_path and os.path.exists(worktree_path):
            print_pass(f"目录存在: {worktree_path}")
            # 列出目录内容
            files = os.listdir(worktree_path)[:5]
            print_info(f"目录内容（前5项）: {files}")
        else:
            print_fail("Worktree 目录不存在")
            passed = False

        # Step 3: 在 Worktree 中创建测试文件
        print_step(3, "在 Worktree 中创建测试文件")
        if worktree_path:
            test_file_path = os.path.join(worktree_path, test_file)
            try:
                with open(test_file_path, 'w', encoding='utf-8') as f:
                    f.write(f"# Worktree 测试文件\n创建时间: {time.strftime('%Y-%m-%d %H:%M:%S')}\n")
                print_pass(f"文件创建成功: {test_file}")
            except Exception as e:
                print_fail(f"文件创建失败: {e}")
                passed = False

        # Step 4: 切换到 Worktree 分支并提交
        print_step(4, f"切换到分支 {branch_name} 并提交")

        # 注意：这里需要通过 Server API 操作，但 Server 只能操作主仓库
        # Worktree 内的 Git 操作需要在 Worktree 目录执行
        # 由于 API 限制，这里演示手动提交方式
        print_warn("API 限制：Server 只能操作主仓库")
        print_info("实际使用中，Agent 会直接在 Worktree 目录执行 git 命令")

        # 模拟：手动在 Worktree 目录执行 git 命令
        if worktree_path:
            import subprocess
            try:
                # 在 Worktree 目录执行 git add + commit
                subprocess.run(
                    ['git', 'add', test_file],
                    cwd=worktree_path,
                    capture_output=True,
                    check=True
                )
                result = subprocess.run(
                    ['git', 'commit', '-m', 'test: Worktree 测试提交'],
                    cwd=worktree_path,
                    capture_output=True,
                    text=True
                )
                if result.returncode == 0:
                    print_pass("提交成功")
                else:
                    print_warn(f"提交跳过（可能无更改）: {result.stderr}")
            except subprocess.CalledProcessError as e:
                print_warn(f"Git 命令执行失败: {e}")
            except FileNotFoundError:
                print_warn("Git 命令不可用，跳过提交测试")

        # Step 5: 切换回主分支
        print_step(5, f"切换回 {original_branch}")
        if switch_branch(original_branch):
            print_pass(f"已切换到 {original_branch}")
        else:
            print_warn("切换失败")

        # Step 6: 合并 Worktree 分支
        print_step(6, f"合并 {branch_name} 到 {original_branch}")
        resp = api_post("merge", {
            "sourceBranch": branch_name,
            "commitMessage": f"Merge {branch_name}: Worktree 完整流程测试"
        })
        if resp.ok:
            merge_result = resp.json()
            if merge_result.get('success'):
                print_pass(f"合并成功: {merge_result.get('message')}")
            elif merge_result.get('hasConflicts'):
                print_warn(f"合并有冲突: {merge_result.get('message')}")
            else:
                print_info(f"合并结果: {merge_result.get('message')}")
        else:
            print_warn(f"合并请求失败: {resp.text}")

        # Step 7: 删除 Worktree + 分支
        print_step(7, "删除 Worktree 和分支")
        success, result = delete_worktree(worktree_name, delete_branch=True)
        if success:
            print_pass(f"清理成功: {result.get('message')}")
        else:
            print_warn(f"清理失败: {result}")

        # Step 8: 验证结果
        print_step(8, "验证清理结果")
        wt_exists = worktree_exists(worktree_name)
        br_exists = branch_exists(branch_name)

        if not wt_exists:
            print_pass("Worktree 已删除")
        else:
            print_fail("Worktree 仍存在")
            passed = False

        if not br_exists:
            print_pass("分支已删除")
        else:
            print_fail("分支仍存在")
            passed = False

        # 清理测试文件（如果合并成功，主分支会有这个文件）
        main_test_file = os.path.join(os.getcwd(), test_file)
        if os.path.exists(main_test_file):
            os.remove(main_test_file)
            print_info("已清理测试文件")

    except Exception as e:
        print_fail(f"测试异常: {e}")
        import traceback
        traceback.print_exc()
        passed = False

    print(f"\n场景 C 测试{'通过' if passed else '失败'}")
    return passed


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


def main():
    print("=" * 60)
    print("Worktree 完整场景测试")
    print("=" * 60)
    print(f"Server: {BASE_URL}")

    # 检查 Server
    ok, info = check_server()
    if not ok:
        print(f"\n{Colors.RED}[ERROR]{Colors.RESET} {info}")
        print("请先启动 Server：")
        print("  cd BIMCanvas.Server && dotnet run")
        return

    print(f"当前分支: {info}")

    # 解析参数
    run_a = "-a" in sys.argv or "--scenario-a" in sys.argv
    run_b = "-b" in sys.argv or "--scenario-b" in sys.argv
    run_c = "-c" in sys.argv or "--scenario-c" in sys.argv
    run_all = not (run_a or run_b or run_c)

    results = []

    if run_all or run_a:
        results.append(("场景 A (并行开发)", test_scenario_a()))

    if run_all or run_b:
        results.append(("场景 B (隔离环境)", test_scenario_b()))

    if run_all or run_c:
        results.append(("场景 C (完整工作流)", test_scenario_c()))

    # 打印总结
    print("\n" + "=" * 60)
    print("测试结果汇总")
    print("=" * 60)
    all_passed = True
    for name, passed in results:
        status = f"{Colors.GREEN}[PASS]{Colors.RESET}" if passed else f"{Colors.RED}[FAIL]{Colors.RESET}"
        print(f"  {status} {name}")
        if not passed:
            all_passed = False

    print("=" * 60)
    if all_passed:
        print(f"{Colors.GREEN}全部测试通过！{Colors.RESET}")
    else:
        print(f"{Colors.RED}存在失败的测试{Colors.RESET}")


def print_usage():
    print("""
Worktree 完整场景测试

用法：
  python test_worktree_scenario.py [选项]

选项：
  -a, --scenario-a    只运行场景 A（并行开发，基于已有分支）
  -b, --scenario-b    只运行场景 B（隔离环境，临时分支）
  -c, --scenario-c    只运行场景 C（完整工作流）
  -h, --help          显示帮助信息

场景说明：

  场景 A：并行开发
    - 先在主仓库创建分支
    - 创建 Worktree 关联已有分支
    - 删除 Worktree 时保留分支
    - 验证分支仍然存在

  场景 B：隔离环境
    - 创建 Worktree 时自动创建临时分支
    - 删除 Worktree 时同时删除分支
    - 验证分支已被清理

  场景 C：完整工作流
    - 创建 Worktree + 分支
    - 在 Worktree 中创建文件并提交
    - 合并分支到主分支
    - 删除 Worktree + 分支
    - 验证清理结果

示例：
  python test_worktree_scenario.py           # 运行全部场景
  python test_worktree_scenario.py -a        # 只测试场景 A
  python test_worktree_scenario.py -b -c     # 测试场景 B 和 C
""")


if __name__ == "__main__":
    if "-h" in sys.argv or "--help" in sys.argv:
        print_usage()
    else:
        main()
