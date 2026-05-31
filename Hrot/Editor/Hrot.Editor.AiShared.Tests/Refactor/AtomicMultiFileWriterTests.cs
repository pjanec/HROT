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

    /// <summary>
    /// BPF-037: When the MOVE phase fails (temp file written successfully, but
    /// File.Move to the final path throws), the writer must:
    ///   - return Success = false with a non-null FailureReason
    ///   - leave no .tmp files in the directory
    /// This differs from the write-phase failure tests above which force an error
    /// during File.WriteAllText by targeting an invalid directory.
    /// Here the write succeeds but the move fails because the final path is a
    /// pre-existing directory (File.Move cannot overwrite a directory on Windows).
    /// </summary>
    [Fact]
    public void Write_MidMoveFails_ReturnsFalse_AndLeavesNoTempFiles()
    {
        var baseDir = Directory.CreateTempSubdirectory("atomic_bpf037_").FullName;
        try
        {
            var writer = new AtomicMultiFileWriter();

            // Create a directory at the final path so File.Move into it as a file fails.
            var finalPath = Path.Combine(baseDir, "output.txt");
            Directory.CreateDirectory(finalPath);

            var result = writer.Write(new Dictionary<string, string> { [finalPath] = "data" });

            Assert.False(result.Success);
            Assert.NotNull(result.FailureReason);
            Assert.Empty(Directory.GetFiles(baseDir, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>
    /// FIX2-019: When two files are written and the move phase succeeds for the first file
    /// but fails for the second, SuccessfullyWritten must contain only the first path.
    /// A SortedDictionary is used to guarantee deterministic iteration order.
    /// </summary>
    [Fact]
    public void Write_TwoFiles_FirstSucceeds_SecondFails_PartialSuccessfullyWritten()
    {
        var baseDir = Directory.CreateTempSubdirectory("atomic_fix019_").FullName;
        try
        {
            var writer = new AtomicMultiFileWriter();

            // "a_output.txt" sorts before "b_blockdir" so file-1 is always processed first.
            var path1 = Path.Combine(baseDir, "a_output.txt");
            var path2 = Path.Combine(baseDir, "b_blockdir");

            // Make path2 destination a directory so File.Move into it as a file fails.
            Directory.CreateDirectory(path2);

            var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                [path1] = "content1",
                [path2] = "should-fail",
            };

            var result = writer.Write(files);

            Assert.False(result.Success);
            Assert.Contains(path1, result.SuccessfullyWritten);
            Assert.DoesNotContain(path2, result.SuccessfullyWritten);
            Assert.NotNull(result.FailureReason);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }
}
