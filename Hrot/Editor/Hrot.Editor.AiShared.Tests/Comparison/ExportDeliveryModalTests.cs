using Hrot.Editor.AiShared.Comparison.UI;
using System.Text.RegularExpressions;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class ExportDeliveryModalTests : IDisposable
{
    private readonly string _tempDir;

    public ExportDeliveryModalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExportDeliveryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }

    private static ExportDeliveryModalState MakeState(string text = "hello world", string name = "TestAsset")
        => new(text, name);

    // ---- SaveToFile -----------------------------------------------------------

    [Fact]
    public void SaveToFile_WritesContent_AndReturnsNull()
    {
        var state = MakeState("export content here");
        var path = Path.Combine(_tempDir, "out.txt");

        var error = state.SaveToFile(path);

        Assert.Null(error);
        Assert.True(File.Exists(path));
        Assert.Equal("export content here", File.ReadAllText(path, System.Text.Encoding.UTF8));
    }

    [Fact]
    public void SaveToFile_InvalidPath_ReturnsErrorString()
    {
        var state = MakeState("content");

        // BP-64: a path under a directory that does not exist fails on every platform. The literal
        // this replaced, @"Z:\DoesNotExist\file.txt", is only invalid on Windows — on Linux a
        // backslash is an ordinary filename character, so it names a perfectly writable file in the
        // current directory and the save succeeded.
        var badPath = Path.Combine(_tempDir, "no-such-subdirectory", "file.txt");

        var error = state.SaveToFile(badPath);

        Assert.NotNull(error);
    }

    // ---- GetClipboardText -----------------------------------------------------

    [Fact]
    public void GetClipboardText_UnderLimit_ReturnsText()
    {
        var state = MakeState("small text");
        Assert.Equal("small text", state.GetClipboardText());
    }

    [Fact]
    public void GetClipboardText_OverLimit_ReturnsNull()
    {
        // Build a string whose UTF-8 byte count exceeds 8 MB.
        var overLimit = new string('x', ExportDeliveryModalState.MaxClipboardBytes + 1);
        var state = MakeState(overLimit);

        Assert.Null(state.GetClipboardText());
    }

    // ---- GetPreviewText -------------------------------------------------------

    [Fact]
    public void GetPreviewText_40Lines_Returns30LinesWithMarker()
    {
        var lines = Enumerable.Range(1, 40).Select(i => $"line {i}");
        var text = string.Join('\n', lines);
        var state = MakeState(text);

        var preview = state.GetPreviewText();
        var previewLines = preview.Split('\n');

        // First 30 content lines + 1 marker line = 31 entries.
        Assert.Equal(31, previewLines.Length);
        Assert.Contains("[...]", previewLines[30]);
    }

    [Fact]
    public void GetPreviewText_ShowFull_ReturnsFullText()
    {
        var text = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"line {i}"));
        var state = MakeState(text);

        var full = state.GetPreviewText(showFull: true);
        Assert.Equal(text, full);
    }

    // ---- GetDefaultFileName ---------------------------------------------------

    [Fact]
    public void GetDefaultFileName_MatchesExpectedPattern()
    {
        var state = MakeState(name: "OrcGuard_BT");
        var fileName = state.GetDefaultFileName();

        Assert.Matches(@"^OrcGuard_BT_comparison_\d{8}_\d{6}\.txt$", fileName);
    }
}
