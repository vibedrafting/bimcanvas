using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace BIMCanvas.Server.Logging
{
    /// <summary>
    /// 自定义 Console 日志格式化器，统一 Server 日志输出格式
    /// 格式: [HH:mm:ss] [Server] 消息
    /// </summary>
    public sealed class ServerConsoleFormatter : ConsoleFormatter, IDisposable
    {
        private readonly IDisposable? _optionsReloadToken;
        private ServerConsoleFormatterOptions _options;

        public const string FormatterName = "ServerFormat";

        public ServerConsoleFormatter(IOptionsMonitor<ServerConsoleFormatterOptions> options)
            : base(FormatterName)
        {
            _options = options.CurrentValue;
            _optionsReloadToken = options.OnChange(ReloadOptions);
        }

        private void ReloadOptions(ServerConsoleFormatterOptions options) => _options = options;

        public override void Write<TState>(
            in LogEntry<TState> logEntry,
            IExternalScopeProvider? scopeProvider,
            TextWriter textWriter)
        {
            var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
            if (message is null)
                return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var prefix = GetPrefix(logEntry.LogLevel);
            var prefixColor = GetPrefixColor(logEntry.LogLevel);

            // 输出格式: [HH:mm:ss] [Server] 消息
            WriteColoredLine(textWriter, timestamp, prefix, prefixColor, message);

            // 输出异常信息（如果有）
            if (logEntry.Exception != null)
            {
                WriteExceptionDetails(textWriter, timestamp, prefix, prefixColor, logEntry.Exception);
            }
        }

        private static string GetPrefix(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "[Server:TRC]",
                LogLevel.Debug => "[Server:DBG]",
                LogLevel.Information => "[Server]",
                LogLevel.Warning => "[Server:WARN]",
                LogLevel.Error => "[Server:ERR]",
                LogLevel.Critical => "[Server:CRIT]",
                _ => "[Server]"
            };
        }

        private static ConsoleColor GetPrefixColor(LogLevel logLevel)
        {
            // Server 使用白色系（低调风格，与默认文本颜色一致）
            return logLevel switch
            {
                LogLevel.Trace => ConsoleColor.DarkGray,
                LogLevel.Debug => ConsoleColor.DarkGray,
                LogLevel.Information => ConsoleColor.White,
                LogLevel.Warning => ConsoleColor.DarkYellow,
                LogLevel.Error => ConsoleColor.DarkGray,
                LogLevel.Critical => ConsoleColor.DarkGray,
                _ => ConsoleColor.White
            };
        }

        private static void WriteColoredLine(
            TextWriter writer,
            string timestamp,
            string prefix,
            ConsoleColor prefixColor,
            string message)
        {
            // 使用 ANSI escape codes 输出颜色
            var timestampColor = "\x1b[90m";  // DarkGray
            var prefixAnsi = GetAnsiColor(prefixColor);
            var reset = "\x1b[0m";

            writer.WriteLine($"{timestampColor}[{timestamp}]{reset} {prefixAnsi}{prefix}{reset} {message}");
        }

        private static void WriteExceptionDetails(
            TextWriter writer,
            string timestamp,
            string prefix,
            ConsoleColor prefixColor,
            Exception exception)
        {
            var timestampColor = "\x1b[90m";
            var prefixAnsi = GetAnsiColor(prefixColor);
            var reset = "\x1b[0m";
            var exceptionColor = "\x1b[31m";  // Red for exception details

            // 输出异常类型和消息
            writer.WriteLine($"{timestampColor}[{timestamp}]{reset} {prefixAnsi}{prefix}{reset} {exceptionColor}  {exception.GetType().Name}: {exception.Message}{reset}");

            // 输出堆栈跟踪的关键部分（只显示 BIMCanvas 相关的行）
            if (exception.StackTrace != null)
            {
                var lines = exception.StackTrace.Split('\n');
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.Contains("BIMCanvas"))
                    {
                        writer.WriteLine($"{timestampColor}[{timestamp}]{reset} {prefixAnsi}{prefix}{reset} {exceptionColor}  {trimmedLine}{reset}");
                    }
                }
            }
        }

        private static string GetAnsiColor(ConsoleColor color)
        {
            return color switch
            {
                ConsoleColor.Black => "\x1b[30m",
                ConsoleColor.DarkBlue => "\x1b[34m",
                ConsoleColor.DarkGreen => "\x1b[32m",
                ConsoleColor.DarkCyan => "\x1b[36m",
                ConsoleColor.DarkRed => "\x1b[31m",
                ConsoleColor.DarkMagenta => "\x1b[35m",
                ConsoleColor.DarkYellow => "\x1b[33m",
                ConsoleColor.Gray => "\x1b[37m",
                ConsoleColor.DarkGray => "\x1b[90m",
                ConsoleColor.Blue => "\x1b[94m",
                ConsoleColor.Green => "\x1b[92m",
                ConsoleColor.Cyan => "\x1b[96m",
                ConsoleColor.Red => "\x1b[91m",
                ConsoleColor.Magenta => "\x1b[95m",
                ConsoleColor.Yellow => "\x1b[93m",
                ConsoleColor.White => "\x1b[97m",
                _ => "\x1b[0m"
            };
        }

        public void Dispose() => _optionsReloadToken?.Dispose();
    }

    /// <summary>
    /// Server Console Formatter 配置选项
    /// </summary>
    public class ServerConsoleFormatterOptions : ConsoleFormatterOptions
    {
        // 可扩展配置项
    }

    /// <summary>
    /// 日志扩展方法
    /// </summary>
    public static class ServerConsoleFormatterExtensions
    {
        /// <summary>
        /// 添加 Server 自定义日志格式化器
        /// </summary>
        public static ILoggingBuilder AddServerConsoleFormatter(this ILoggingBuilder builder)
        {
            builder.AddConsole(options =>
            {
                options.FormatterName = ServerConsoleFormatter.FormatterName;
            });

            builder.AddConsoleFormatter<ServerConsoleFormatter, ServerConsoleFormatterOptions>();

            return builder;
        }
    }
}
