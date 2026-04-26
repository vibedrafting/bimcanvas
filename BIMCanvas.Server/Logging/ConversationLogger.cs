using System.Text;
using System.Text.RegularExpressions;

namespace BIMCanvas.Server.Logging;

/// <summary>
/// 对话日志持久化。
/// 将 Agent 对话内容（START→COMPLETE）保存到项目目录下的日志文件。
/// 同一次 Server 会话（不关闭程序、不切换项目）的所有对话追加到同一个文件。
/// 按阶段批量写入，避免高频 IO。
/// </summary>
public static class ConversationLogger
{
    // ── 配置 ──
    private static string? _currentFilePath;

    // ── 状态机 ──
    private static bool _isActive = false;
    private static readonly List<string> _buffer = new();

    // ── ANSI 转义码清理 ──
    private static readonly Regex AnsiRegex =
        new(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);

    /// <summary>
    /// 初始化日志文件（Server 启动加载项目后调用一次）。
    /// 创建 logs/ 目录并确定本次会话的日志文件路径。
    /// 切换项目时再次调用会创建新文件。
    /// </summary>
    public static void Initialize(string projectPath)
    {
        var logDir = Path.Combine(projectPath, "logs");
        Directory.CreateDirectory(logDir);
        _currentFilePath = Path.Combine(logDir,
            $"chat_{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }

    /// <summary>
    /// 处理一行 Agent stdout 输出。
    /// 由 WriteWithTimestampOnly() 调用，与控制台输出同步。
    /// 仅记录 [START]→[COMPLETE] 之间的对话内容。
    /// </summary>
    public static void ProcessLine(string rawLine)
    {
        if (_currentFilePath == null) return;

        var cleanLine = AnsiRegex.Replace(rawLine, "");
        var timestampedLine = $"[{DateTime.Now:HH:mm:ss}] {cleanLine}";

        if (!_isActive)
        {
            // 未在对话中 → 检测 [START] 标记
            if (cleanLine.Contains("[START]"))
            {
                _buffer.Add(timestampedLine);
                _isActive = true;
            }
            return;
        }

        // 对话中 → 检测是否有新对话开始（上一轮异常中断的恢复）
        if (cleanLine.Contains("[START]"))
        {
            // 上一轮未正常 COMPLETE，先 flush 残留内容
            Flush();
            // 新对话的第一行
            _buffer.Add(timestampedLine);
            return;
        }

        // 正常对话行 → 缓冲
        _buffer.Add(timestampedLine);

        // ── 阶段边界检测 → 触发 Flush ──
        bool shouldFlush =
            cleanLine.Contains("[COMPLETE]") ||
            cleanLine.Contains("[USER]") ||
            cleanLine.Contains("thinking complete") ||
            cleanLine.Contains("[Result]") ||
            (cleanLine.Contains("SUBAGENT") && cleanLine.Contains("COMPLETE"));

        if (shouldFlush)
            Flush();

        // 对话结束 → 回到 Idle（文件不关闭，下次对话继续追加）
        if (cleanLine.Contains("[COMPLETE]"))
        {
            _isActive = false;
        }
    }

    /// <summary>
    /// 将缓冲区内容批量写入文件
    /// </summary>
    private static void Flush()
    {
        if (_currentFilePath == null || _buffer.Count == 0) return;
        try
        {
            File.AppendAllLines(_currentFilePath, _buffer, Encoding.UTF8);
            _buffer.Clear();
        }
        catch
        {
            // 日志写入失败不影响主流程
        }
    }
}

/// <summary>
/// Writes a character stream with timestamps only at physical line starts.
/// This preserves Agent streaming output readability while still emitting completed
/// lines to downstream log processors.
/// </summary>
public sealed class TimestampedLineStreamWriter
{
    private readonly object _gate = new();
    private readonly Action<string> _writeTimestamp;
    private readonly Action<string> _writeMessage;
    private readonly Action<string>? _completedLineHandler;
    private readonly Func<DateTime> _clock;
    private readonly StringBuilder _lineBuffer = new();
    private bool _isAtLineStart = true;

    public TimestampedLineStreamWriter(
        Action<string> writeTimestamp,
        Action<string> writeMessage,
        Action<string>? completedLineHandler = null,
        Func<DateTime>? clock = null)
    {
        _writeTimestamp = writeTimestamp;
        _writeMessage = writeMessage;
        _completedLineHandler = completedLineHandler;
        _clock = clock ?? (() => DateTime.Now);
    }

    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (_gate)
        {
            foreach (var ch in text)
            {
                if (ch == '\r')
                    continue;

                if (ch == '\n')
                {
                    if (!_isAtLineStart || _lineBuffer.Length > 0)
                    {
                        _writeMessage(Environment.NewLine);
                        EmitCompletedLine();
                        _isAtLineStart = true;
                    }
                    else
                    {
                        _writeMessage(Environment.NewLine);
                    }
                    continue;
                }

                if (_isAtLineStart)
                {
                    _writeTimestamp($"[{_clock():HH:mm:ss}] ");
                    _isAtLineStart = false;
                }

                _writeMessage(ch.ToString());
                _lineBuffer.Append(ch);
            }
        }
    }

    public void CompletePendingLine()
    {
        lock (_gate)
        {
            if (_lineBuffer.Length == 0)
                return;

            _writeMessage(Environment.NewLine);
            EmitCompletedLine();
            _isAtLineStart = true;
        }
    }

    private void EmitCompletedLine()
    {
        if (_lineBuffer.Length == 0)
            return;

        _completedLineHandler?.Invoke(_lineBuffer.ToString());
        _lineBuffer.Clear();
    }
}
