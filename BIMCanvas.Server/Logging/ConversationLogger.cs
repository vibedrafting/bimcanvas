using System.Text;
using System.Text.RegularExpressions;

namespace BIMCanvas.Server.Logging;

/// <summary>
/// 控制台输出本地存档（Tee 镜像）。
/// 在 Console.Out / Console.Error 上套一层分叉写入器：打到控制台的内容原样保留，
/// 同时去除 ANSI 颜色码后追加到当前项目的日志文件。
/// 复用 Server 既有日志系统（ServerConsoleFormatter / WriteWithTimestampOnly 等）已经
/// 格式化好、已带时间戳的成品，不重复实现格式化；后续日志打印逻辑变动无需同步本文件。
///
/// 生命周期：
/// - Install()：进程启动最早期调用一次（须在日志框架初始化前，否则捕获不到原始 Console.Out）。
/// - Initialize(projectPath)：打开 / 切换项目时调用，关旧文件、在项目 logs/ 下起新文件。
/// - Shutdown()：程序退出时调用，flush 并关闭文件。
///
/// 项目打开前 _fileWriter 为 null，仅输出控制台、不落盘（此时也无项目 logs/ 目录）。
/// </summary>
public static class ConversationLogger
{
    private static TeeTextWriter? _outTee;
    private static TeeTextWriter? _errTee;

    private static readonly object _fileLock = new();
    private static StreamWriter? _fileWriter;

    // ANSI 颜色转义码清理（文件存纯文本，控制台仍保留颜色）
    private static readonly Regex AnsiRegex =
        new(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);

    /// <summary>
    /// 安装控制台分叉（启动最早期调用一次，幂等）。
    /// 须在 ASP.NET 日志框架初始化前调用——Console 日志 provider 在构造时捕获
    /// Console.Out，此后才换回我们的 Tee 已来不及。
    /// </summary>
    public static void Install()
    {
        if (_outTee != null) return; // 幂等

        var originalOut = Console.Out;
        var originalErr = Console.Error;

        // out / err 各自独立缓行（分别受各自 Console 同步包装器串行化，互不共享可变状态）；
        // 文件写入由 _fileLock 统一保护。
        _outTee = new TeeTextWriter(originalOut, WriteLineToFile, originalOut.Encoding);
        _errTee = new TeeTextWriter(originalErr, WriteLineToFile, originalErr.Encoding);

        Console.SetOut(_outTee);
        Console.SetError(_errTee);
    }

    /// <summary>
    /// 打开 / 切换项目时调用：关闭旧文件，在项目 logs/ 下建新文件。
    /// 切项目再次调用即自动滚动到新文件。
    /// </summary>
    public static void Initialize(string projectPath)
    {
        lock (_fileLock)
        {
            FlushAndCloseFileNoLock();

            var logDir = Path.Combine(projectPath, "logs");
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            try
            {
                _fileWriter = new StreamWriter(path, append: true, Encoding.UTF8)
                {
                    AutoFlush = true
                };
            }
            catch
            {
                // 文件打开失败不影响主流程，退化为仅控制台
                _fileWriter = null;
            }
        }
    }

    /// <summary>
    /// 程序退出收尾：flush 并关闭文件。
    /// </summary>
    public static void Shutdown()
    {
        lock (_fileLock)
        {
            FlushAndCloseFileNoLock();
        }
    }

    /// <summary>
    /// 由 Tee 在写出一整行（不含换行符）时回调：去 ANSI 后写入文件。
    /// </summary>
    private static void WriteLineToFile(string line)
    {
        // 文件未就绪（项目未打开 / 打开失败）→ 仅控制台
        if (_fileWriter == null) return;

        var clean = AnsiRegex.Replace(line, "");
        lock (_fileLock)
        {
            if (_fileWriter == null) return;
            try
            {
                _fileWriter.WriteLine(clean);
            }
            catch
            {
                // 日志写入失败不影响主流程
            }
        }
    }

    private static void FlushAndCloseFileNoLock()
    {
        try
        {
            _fileWriter?.Flush();
            _fileWriter?.Dispose();
        }
        catch
        {
            // 收尾失败忽略
        }
        _fileWriter = null;
    }
}

/// <summary>
/// 分叉写入器：转发所有写入给原始控制台（保留颜色），同时按物理行回调存档。
/// 控制台输出的最终必经出口——任何走 Console.* 的内容都被自动镜像，
/// 故 Server 日志打印逻辑变动时无需同步此处。
/// </summary>
internal sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter _inner;       // 原始控制台写入器
    private readonly Action<string> _onLine;  // 整行回调（不含换行）
    private readonly Encoding _encoding;
    private readonly StringBuilder _lineBuffer = new();

    public TeeTextWriter(TextWriter inner, Action<string> onLine, Encoding encoding)
    {
        _inner = inner;
        _onLine = onLine;
        _encoding = encoding;
    }

    public override Encoding Encoding => _encoding;

    public override void Write(char value)
    {
        _inner.Write(value);
        Accumulate(value);
    }

    public override void Write(string? value)
    {
        if (value == null) return;
        _inner.Write(value);
        foreach (var ch in value)
            Accumulate(ch);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        _inner.Write(buffer, index, count);
        for (var i = 0; i < count; i++)
            Accumulate(buffer[index + i]);
    }

    public override void Flush() => _inner.Flush();

    private void Accumulate(char ch)
    {
        if (ch == '\n')
        {
            var line = _lineBuffer.ToString();
            _lineBuffer.Clear();
            _onLine(line);
        }
        else if (ch != '\r')
        {
            _lineBuffer.Append(ch);
        }
    }
}
