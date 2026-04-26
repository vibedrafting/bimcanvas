using System.Text;
using BIMCanvas.Server.Logging;
using Xunit;

namespace BIMCanvas.Server.Tests;

public sealed class StreamingConsoleWriterTests
{
    private static TimestampedLineStreamWriter CreateWriter(
        StringBuilder output,
        List<string> completedLines)
    {
        return new TimestampedLineStreamWriter(
            writeTimestamp: text => output.Append(text),
            writeMessage: text => output.Append(text),
            completedLineHandler: completedLines.Add,
            clock: () => new DateTime(2026, 4, 26, 15, 0, 1));
    }

    [Fact]
    public void Write_AppendsChunksToSameTimestampedLine()
    {
        var output = new StringBuilder();
        var completedLines = new List<string>();
        var writer = CreateWriter(output, completedLines);

        writer.Write("abc");
        Assert.Empty(completedLines);

        writer.Write("def\n");

        Assert.Equal($"[15:00:01] abcdef{Environment.NewLine}", output.ToString());
        Assert.Equal(["abcdef"], completedLines);
    }

    [Fact]
    public void Write_AddsTimestampOnlyAtPhysicalLineStart()
    {
        var output = new StringBuilder();
        var completedLines = new List<string>();
        var writer = CreateWriter(output, completedLines);

        writer.Write("first\nsecond\n");

        Assert.Equal(
            $"[15:00:01] first{Environment.NewLine}[15:00:01] second{Environment.NewLine}",
            output.ToString());
        Assert.Equal(["first", "second"], completedLines);
    }

    [Fact]
    public void CompletePendingLine_EmitsBufferedPartialLine()
    {
        var output = new StringBuilder();
        var completedLines = new List<string>();
        var writer = CreateWriter(output, completedLines);

        writer.Write("partial");
        Assert.Empty(completedLines);

        writer.CompletePendingLine();

        Assert.Equal($"[15:00:01] partial{Environment.NewLine}", output.ToString());
        Assert.Equal(["partial"], completedLines);
    }
}
