from src.agent.main_agent import MainAgent


# ─── _normalize_assistant_text（文本路径，含 tool_use_error 剥离）───

def test_normalize_assistant_text_filters_exact_placeholder() -> None:
    assert MainAgent._normalize_assistant_text("(no content)") is None


def test_normalize_assistant_text_filters_placeholder_with_surrounding_whitespace() -> None:
    assert MainAgent._normalize_assistant_text("  [No content]  \n") is None


def test_normalize_assistant_text_filters_blank_content() -> None:
    assert MainAgent._normalize_assistant_text("  \n\t  ") is None


def test_normalize_assistant_text_preserves_normal_content() -> None:
    assert MainAgent._normalize_assistant_text("主卧布局已生成。") == "主卧布局已生成。"


def test_normalize_assistant_text_does_not_filter_mixed_real_content() -> None:
    text = "(no content)\n随后开始读取房间数据。"
    assert MainAgent._normalize_assistant_text(text) == text


# ─── _normalize_visible_content（统一可见内容过滤，覆盖 text + thinking）───

def test_normalize_visible_content_filters_no_content() -> None:
    assert MainAgent._normalize_visible_content("(no content)") is None


def test_normalize_visible_content_filters_bracket_variant() -> None:
    assert MainAgent._normalize_visible_content("[no content]") is None


def test_normalize_visible_content_filters_case_insensitive() -> None:
    assert MainAgent._normalize_visible_content("(No Content)") is None
    assert MainAgent._normalize_visible_content("[NO CONTENT]") is None


def test_normalize_visible_content_filters_none() -> None:
    assert MainAgent._normalize_visible_content(None) is None


def test_normalize_visible_content_filters_empty() -> None:
    assert MainAgent._normalize_visible_content("") is None


def test_normalize_visible_content_filters_whitespace() -> None:
    assert MainAgent._normalize_visible_content("  \n\t  ") is None


def test_normalize_visible_content_filters_with_surrounding_whitespace() -> None:
    assert MainAgent._normalize_visible_content("  (no content)  \n") is None


def test_normalize_visible_content_preserves_normal_thinking() -> None:
    text = "让我分析一下这个房间的布局..."
    assert MainAgent._normalize_visible_content(text) == text


def test_normalize_visible_content_preserves_mixed_content() -> None:
    """含占位文本但有真实内容时，保留原文。"""
    text = "(no content)\n真实思考内容"
    assert MainAgent._normalize_visible_content(text) == text


def test_normalize_visible_content_preserves_normal_text() -> None:
    text = "主卧布局已生成。"
    assert MainAgent._normalize_visible_content(text) == text
