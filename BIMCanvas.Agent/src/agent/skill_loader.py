"""Skill loader for dynamic skill injection."""

import logging
from pathlib import Path
from dataclasses import dataclass
from typing import Optional
from functools import lru_cache

logger = logging.getLogger(__name__)


@dataclass
class SkillConfig:
    """Skill 配置"""
    name: str           # Skill 名称（目录名）
    content: str        # SKILL.md 内容
    target: str         # 注入目标：'main' | 'layout-agent' | 其他 agent 名


class SkillLoader:
    """
    Skill 加载器

    职责：
    1. 从 skills/ 目录加载 SKILL.md 文件
    2. 根据目标 agent 筛选并返回相关 skill 内容
    3. 支持按需加载（query 任务不加载详细流程）

    目录结构：
    ~/.bimcanvas/
    └── skills/
        ├── git-workflow/
        │   └── SKILL.md        # MainAgent 的 Git 工作流指导
        └── layout-guide/
            └── SKILL.md        # layout-agent 的操作指导
    """

    # Skill 与目标 Agent 的映射关系
    SKILL_TARGET_MAP = {
        'git-workflow': 'main',          # MainAgent 专用
        'layout-guide': 'layout-agent',  # layout-agent 专用
    }

    def __init__(self, config_dir: Path | str = None):
        """
        初始化 Skill 加载器

        Args:
            config_dir: 配置目录路径，默认为 ~/.bimcanvas
        """
        if config_dir is None:
            self.config_dir = Path.home() / ".bimcanvas"
        else:
            self.config_dir = Path(config_dir) if isinstance(config_dir, str) else config_dir

        self.skills_dir = self.config_dir / "skills"
        self._cache: dict[str, SkillConfig] = {}

    def _ensure_skills_dir(self) -> None:
        """确保 skills 目录存在"""
        self.skills_dir.mkdir(parents=True, exist_ok=True)

    def load_skill(self, skill_name: str) -> Optional[SkillConfig]:
        """
        加载单个 Skill

        Args:
            skill_name: Skill 名称（目录名）

        Returns:
            SkillConfig 或 None（如果不存在）
        """
        # 检查缓存
        if skill_name in self._cache:
            return self._cache[skill_name]

        skill_path = self.skills_dir / skill_name / "SKILL.md"
        if not skill_path.exists():
            logger.warning(f"Skill 不存在: {skill_path}")
            return None

        try:
            with open(skill_path, 'r', encoding='utf-8') as f:
                content = f.read()

            target = self.SKILL_TARGET_MAP.get(skill_name, 'main')
            skill = SkillConfig(
                name=skill_name,
                content=content,
                target=target
            )
            self._cache[skill_name] = skill
            logger.debug(f"已加载 Skill: {skill_name} -> {target}")
            return skill

        except Exception as e:
            logger.error(f"加载 Skill 失败 {skill_name}: {e}")
            return None

    def load_skills_for_target(self, target: str) -> list[SkillConfig]:
        """
        加载指定目标的所有 Skill

        Args:
            target: 目标 agent 名称（'main' 或 agent 名如 'layout-agent'）

        Returns:
            该目标的 SkillConfig 列表
        """
        self._ensure_skills_dir()
        skills = []

        for skill_name, skill_target in self.SKILL_TARGET_MAP.items():
            if skill_target == target:
                skill = self.load_skill(skill_name)
                if skill:
                    skills.append(skill)

        return skills

    def get_skill_content_for_target(self, target: str) -> str:
        """
        获取指定目标的所有 Skill 内容（合并）

        Args:
            target: 目标 agent 名称

        Returns:
            合并后的 Skill 内容字符串
        """
        skills = self.load_skills_for_target(target)
        if not skills:
            return ""

        parts = []
        for skill in skills:
            parts.append(f"\n\n---\n## Skill: {skill.name}\n\n{skill.content}")

        return "\n".join(parts)

    def get_main_agent_skills(self) -> str:
        """获取 MainAgent 的所有 Skill 内容"""
        return self.get_skill_content_for_target('main')

    def get_subagent_skills(self, agent_name: str) -> str:
        """
        获取 SubAgent 的所有 Skill 内容

        Args:
            agent_name: SubAgent 名称（如 'layout-agent'）
        """
        return self.get_skill_content_for_target(agent_name)

    def clear_cache(self) -> None:
        """清除 Skill 缓存"""
        self._cache.clear()


@lru_cache()
def get_skill_loader() -> SkillLoader:
    """获取全局 SkillLoader 实例（单例）"""
    return SkillLoader()
