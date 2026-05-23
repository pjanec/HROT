using Hrot.Editor.AiShared.Refactor;

namespace Hrot.Editor.AiShared.Tests.Refactor;

public sealed class AtomicMultiFileWriterTests
{
    private static string GetTempFilePath() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    [Fact]
    public void Write_empty_dictionary_succeeds_with_no_written_files()
    {
        var writer = new AtomicMultiFileWriter();
        var result = writer.Write(new Dictionary<string, string>());
        Assert.True(result.Success);
        Assert.Empty(result.SuccessfullyWritten);
    }

    [Fact]
    public void Write_single_file_creates_file_with_correct_content()
    {
        var writer = new AtomicMultiFileWriter();
        var path = GetTempFilePath();
        try
        {
            var result = writer.Write(new Dictionary<string, string> { [path] = "hello" });
            Assert.True(result.Success);
            Assert.Equal("hello", File.ReadAllText(path, System.Text.Encoding.UTF8));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Write_multiple_files_creates_all_files_with_correct_content()
    {
        var writer = new AtomicMultiFileWriter();
        var path1 = GetTempFilePath();
        var path2 = GetTempFilePath();
        try
        {
            var result = writer.Write(new Dictionary<string, string>
            {
                [path1] = "content1",
                [path2] = "content2",
            });
            Assert.True(result.Success);
            Assert.Equal("content1", File.ReadAllText(path1, System.Text.Encoding.UTF8));
            Assert.Equal("content2", File.ReadAllText(path2, System.Text.Encoding.UTF8));
        }
        finally
        {
            if (File.Exists(path1)) File.Delete(path1);
            if (File.Exists(path2)) File.Delete(path2);
        }
    }

    [Fact]
    public void Write_overwrites_existing_file()
    {
        var writer = new AtomicMultiFileWriter();
        var path = GetTempFilePath();
        try
        {
            File.WriteAllText(path, "old content");
            var result = writer.Write(new Dictionary<string, string> { [path] = "new content" });
            Assert.True(result.Success);
            Assert.Equal("new content", File.ReadAllText(path, System.Text.Encoding.UTF8));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Write_returns_success_true_on_success()
    {
        var writer = new AtomicMultiFileWriter();
        var path = GetTempFilePath();
        try
        {
            var result = writer.Write(new Dictionary<string, string> { [path] = "data" });
            Assert.True(result.Success);
            Assert.Null(result.FailureReason);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Write_to_invalid_path_returns_failure()
    {
        var writer = new AtomicMultiFileWriter();
        var invalidPath = Path.Combine(Path.GetTempPath(),
            "nonexistent_batch33_" + Guid.NewGuid().ToString("N"), "file.txt");
        var result = writer.Write(new Dictionary<string, string> { [invalidPath] = "data" });
        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Write_to_invalid_path_does_not_leave_temp_files_behind()
    {
        var writer = new AtomicMultiFileWriter();
        var tempDir = Path.GetTempPath();
        var invalidPath = Path.Combine(tempDir,
            "nonexistent_batch33_" + Guid.NewGuid().ToString("N"), "file.txt");
        var beforeFiles = new HashSet<string>(Directory.GetFiles(tempDir, "*.tmp"));
        writer.Write(new Dictionary<string, string> { [invalidPath] = "data" });
        var afterFiles = new HashSet<string>(Directory.GetFiles(tempDir, "*.tmp"));
        afterFiles.ExceptWith(beforeFiles);
        Assert.Empty(afterFiles);
    }
}
