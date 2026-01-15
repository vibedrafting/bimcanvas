#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
BIMCanvas Server Git API 测试脚本

用于验证 Server 提供的 Git REST API 端点是否正常工作。

使用方法：
1. 启动 Server: cd BIMCanvas.Server && dotnet run
2. 通过 Web UI 或 API 加载一个 Git 仓库项目
3. 运行测试: python test_git_api.py

依赖：pip install requests
"""

import requests
import json
import sys
import time
from typing import Optional, Any

BASE_URL = "http://localhost:5000"


class GitApiTester:
    """Server Git API 测试器"""

    def __init__(self, base_url: str = BASE_URL):
        self.base_url = base_url
        self.results: list[dict] = []
        self.verbose = True

    def _request(self, method: str, endpoint: str, json_data: Optional[dict] = None) -> requests.Response:
        """发送 HTTP 请求"""
        url = f"{self.base_url}{endpoint}"
        try:
            if method == "GET":
                return requests.get(url, timeout=10)
            elif method == "POST":
                return requests.post(url, json=json_data, timeout=10)
            elif method == "DELETE":
                return requests.delete(url, timeout=10)
            else:
                raise ValueError(f"不支持的方法: {method}")
        except requests.exceptions.ConnectionError:
            print(f"❌ 连接失败: {url}")
            print("   请确保 Server 已启动 (dotnet run)")
            sys.exit(1)

    def _record(self, name: str, resp: requests.Response, expected_ok: bool = True):
        """记录测试结果"""
        # 判断是否符合预期
        if expected_ok:
            success = resp.ok
        else:
            success = not resp.ok  # 期望失败的情况

        self.results.append({
            "name": name,
            "status": resp.status_code,
            "ok": success,
            "expected_ok": expected_ok
        })

        status = "[PASS]" if success else "[FAIL]"
        print(f"{status} {name} -> {resp.status_code}")

        if self.verbose and not success:
            try:
                print(f"   响应: {resp.text[:200]}")
            except:
                pass

    def _print_json(self, data: Any, indent: int = 2):
        """打印 JSON 数据"""
        if self.verbose:
            print(json.dumps(data, indent=indent, ensure_ascii=False))

    # ==================== Git 基础 API ====================

    def test_status(self) -> dict:
        """测试 GET /api/git/status"""
        resp = self._request("GET", "/api/git/status")
        self._record("GET /api/git/status", resp)
        if resp.ok:
            data = resp.json()
            if self.verbose:
                print(f"   isLoaded: {data.get('isLoaded')}, isGitRepo: {data.get('isGitRepo')}")
            return data
        return {}

    def test_current_branch(self) -> dict:
        """测试 GET /api/git/current"""
        resp = self._request("GET", "/api/git/current")
        self._record("GET /api/git/current", resp)
        if resp.ok:
            data = resp.json()
            if self.verbose:
                print(f"   当前分支: {data.get('branch')}")
            return data
        return {}

    def test_branches(self) -> list:
        """测试 GET /api/git/branches"""
        resp = self._request("GET", "/api/git/branches")
        self._record("GET /api/git/branches", resp)
        if resp.ok:
            data = resp.json()
            if self.verbose:
                print(f"   分支数量: {len(data)}")
                for b in data[:5]:  # 最多显示 5 个
                    current = "* " if b.get("isCurrent") else "  "
                    print(f"   {current}{b.get('name')}")
            return data
        return []

    def test_checkout(self, branch_name: str, create: bool = False,
                      commit_before: bool = False, discard_before: bool = False) -> dict:
        """测试 POST /api/git/checkout"""
        resp = self._request("POST", "/api/git/checkout", {
            "branchName": branch_name,
            "createIfNotExist": create,
            "commitBeforeCheckout": commit_before,
            "discardBeforeCheckout": discard_before
        })
        self._record(f"POST /api/git/checkout ({branch_name}, create={create})", resp)
        try:
            return resp.json() if resp.ok else {"error": resp.text}
        except:
            return {"error": resp.text}

    def test_commit(self, message: Optional[str] = None) -> dict:
        """测试 POST /api/git/commit"""
        resp = self._request("POST", "/api/git/commit", {"message": message})
        self._record("POST /api/git/commit", resp)
        try:
            return resp.json() if resp.ok else {"error": resp.text}
        except:
            return {"error": resp.text}

    def test_discard(self) -> dict:
        """测试 POST /api/git/discard"""
        resp = self._request("POST", "/api/git/discard")
        self._record("POST /api/git/discard", resp)
        try:
            return resp.json() if resp.ok else {"error": resp.text}
        except:
            return {"error": resp.text}

    # ==================== Worktree API ====================

    def test_worktrees(self) -> list:
        """测试 GET /api/git/worktrees"""
        resp = self._request("GET", "/api/git/worktrees")
        self._record("GET /api/git/worktrees", resp)
        if resp.ok:
            data = resp.json()
            if self.verbose:
                print(f"   Worktree 数量: {len(data)}")
                for wt in data:
                    main = "(main)" if wt.get("isMain") else ""
                    print(f"   - {wt.get('name')} @ {wt.get('branch')} {main}")
            return data
        return []

    def test_create_worktree(self, name: str, branch: str) -> dict:
        """测试 POST /api/git/worktrees"""
        resp = self._request("POST", "/api/git/worktrees", {
            "name": name,
            "branchName": branch
        })
        self._record(f"POST /api/git/worktrees ({name})", resp)
        try:
            return resp.json() if resp.ok else {"error": resp.text}
        except:
            return {"error": resp.text}

    def test_delete_worktree(self, name: str) -> dict:
        """测试 DELETE /api/git/worktrees/{name}"""
        resp = self._request("DELETE", f"/api/git/worktrees/{name}")
        self._record(f"DELETE /api/git/worktrees/{name}", resp)
        try:
            return resp.json() if resp.ok else {"error": resp.text}
        except:
            return {"error": resp.text}

    # ==================== Merge API ====================

    def test_merge(self, source_branch: str, commit_message: Optional[str] = None) -> dict:
        """测试 POST /api/git/merge"""
        resp = self._request("POST", "/api/git/merge", {
            "sourceBranch": source_branch,
            "commitMessage": commit_message
        })
        self._record(f"POST /api/git/merge ({source_branch})", resp)
        try:
            return resp.json() if resp.ok else {"error": resp.text}
        except:
            return {"error": resp.text}

    def test_merge_branches(self) -> list:
        """测试 GET /api/merge/branches"""
        resp = self._request("GET", "/api/merge/branches")
        self._record("GET /api/merge/branches", resp)
        if resp.ok:
            data = resp.json()
            if self.verbose:
                print(f"   可合并分支数量: {len(data)}")
                for b in data[:5]:
                    ai = "[AI]" if b.get("isAiBranch") else ""
                    print(f"   - {b.get('name')} {ai}")
            return data
        return []

    def test_merge_diff(self, source_branch: str) -> list:
        """测试 GET /api/merge/diff"""
        resp = self._request("GET", f"/api/merge/diff?sourceBranch={source_branch}")
        self._record(f"GET /api/merge/diff ({source_branch})", resp)
        if resp.ok:
            data = resp.json()
            if self.verbose:
                print(f"   差异分区数量: {len(data)}")
            return data
        return []

    # ==================== 测试执行 ====================

    def print_summary(self):
        """打印测试摘要"""
        passed = sum(1 for r in self.results if r["ok"])
        total = len(self.results)
        print(f"\n{'=' * 50}")
        print(f"测试结果: {passed}/{total} 通过")
        print(f"{'=' * 50}")

        if passed < total:
            print("\n失败的测试:")
            for r in self.results:
                if not r["ok"]:
                    print(f"  ❌ {r['name']} (HTTP {r['status']})")

    def run_basic_tests(self):
        """运行基础测试（只读，不修改状态）"""
        print("\n" + "=" * 50)
        print("基础状态测试")
        print("=" * 50)

        status = self.test_status()
        if not status.get("isLoaded"):
            print("\n⚠️  警告: 没有加载项目，部分测试可能失败")
            print("   请先通过 Web UI 或 API 加载一个 Git 仓库项目")

        self.test_current_branch()
        self.test_branches()
        self.test_worktrees()
        self.test_merge_branches()

    def run_worktree_tests(self, cleanup: bool = True):
        """运行 Worktree 测试（会创建和删除 worktree）"""
        print("\n" + "=" * 50)
        print("Worktree 测试")
        print("=" * 50)

        test_wt_name = "test-worktree-api"
        test_branch = "feat/test-api-branch"

        # 创建 Worktree
        result = self.test_create_worktree(test_wt_name, test_branch)
        if "error" not in result:
            print(f"   创建成功: {result.get('path')}")

        # 列出 Worktree
        self.test_worktrees()

        # 清理
        if cleanup:
            print("\n清理测试 Worktree...")
            self.test_delete_worktree(test_wt_name)

    def run_branch_tests(self, cleanup: bool = True):
        """运行分支测试（会创建新分支）"""
        print("\n" + "=" * 50)
        print("分支切换测试")
        print("=" * 50)

        # 记录当前分支
        current = self.test_current_branch()
        original_branch = current.get("branch", "master")

        test_branch = "feat/test-checkout-api"

        # 创建并切换到新分支
        self.test_checkout(test_branch, create=True)

        # 切换回原分支
        if cleanup:
            print("\n切换回原分支...")
            self.test_checkout(original_branch)


def main():
    """主函数"""
    print("BIMCanvas Server Git API 测试")
    print("=" * 50)

    tester = GitApiTester()
    tester.verbose = True

    # 检查 Server 是否运行
    try:
        resp = requests.get(f"{BASE_URL}/api/git/status", timeout=5)
    except requests.exceptions.ConnectionError:
        print(f"❌ 无法连接到 Server: {BASE_URL}")
        print("   请先启动 Server: cd BIMCanvas.Server && dotnet run")
        sys.exit(1)

    # 运行测试
    tester.run_basic_tests()

    # 询问是否运行修改性测试
    print("\n" + "-" * 50)
    print("以下测试会创建临时分支和 Worktree（完成后会清理）")
    answer = input("是否继续? [y/N]: ").strip().lower()

    if answer == "y":
        tester.run_worktree_tests(cleanup=True)
        tester.run_branch_tests(cleanup=True)

    # 打印摘要
    tester.print_summary()


if __name__ == "__main__":
    main()
