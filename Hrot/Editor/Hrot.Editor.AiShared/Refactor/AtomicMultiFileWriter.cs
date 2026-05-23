namespace Hrot.Editor.AiShared.Refactor;

public sealed class AtomicMultiFileWriter
{
    public AtomicWriteResult Write(IReadOnlyDictionary<string, string> filePathToContent)
    {
        // 1. Write each file to a temp path in the same directory.
        var tempFiles = new List<(string TempPath, string FinalPath)>();
        foreach (var (finalPath, content) in filePathToContent)
        {
            var tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, content, System.Text.Encoding.UTF8);
                tempFiles.Add((tempPath, finalPath));
            }
            catch (Exception ex)
            {
                // Roll back all temp files written so far.
                foreach (var (t, _) in tempFiles)
                    TryDelete(t);
                TryDelete(tempPath);
                return new AtomicWriteResult(false, Array.Empty<string>(), ex.Message);
            }
        }

        // 2. Move all temp files to their final paths (overwrite).
        var written = new List<string>();
        foreach (var (tempPath, finalPath) in tempFiles)
        {
            try
            {
                File.Move(tempPath, finalPath, overwrite: true);
                written.Add(finalPath);
            }
            catch (Exception ex)
            {
                // Partial failure: log but do not roll back already-moved files.
                TryDelete(tempPath);
                return new AtomicWriteResult(false, written.AsReadOnly(), ex.Message);
            }
        }
        return new AtomicWriteResult(true, written.AsReadOnly(), null);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort */ }
    }
}

public sealed record AtomicWriteResult(
    bool Success,
    IReadOnlyList<string> SuccessfullyWritten,
    string? FailureReason);
