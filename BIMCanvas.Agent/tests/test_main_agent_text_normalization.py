from src.agent.main_agent import MainAgent


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
