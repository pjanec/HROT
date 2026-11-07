using System;
using System.IO;

namespace Hrot.Tests.ExtensionStaging;

/// <summary>⭐ CE-4293 — stages the test extension (built beside the tests, <c>sample-extension/</c>) into a temp <c>extensions/&lt;name&gt;/</c> folder. Linked into every test project that needs it.</summary>
internal static class SampleExtensionStaging
{
    public static string BuiltDll => Path.Combine(AppContext.BaseDirectory, "sample-extension", "SampleNodeExtension.dll");

    private static readonly Lazy<string> SharedBase = new(() => Stage("sample"));

    /// <summary>One process-wide base directory with the extension in <c>extensions/sample</c> - the same path every time, because an assembly loaded by path cannot be loaded again from another.</summary>
    public static string SharedBaseDirectory => SharedBase.Value;

    /// <summary>A fresh base directory with <c>extensions/&lt;folderName&gt;/</c> holding the extension dll and <paramref name="extraFiles"/> (copied by path).</summary>
    public static string Stage(string folderName, params string[] extraFiles)
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "hrot-ext-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(baseDir, "extensions", folderName);
        Directory.CreateDirectory(folder);
        File.Copy(BuiltDll, Path.Combine(folder, Path.GetFileName(BuiltDll)));
        foreach (string extra in extraFiles)
            File.Copy(extra, Path.Combine(folder, Path.GetFileName(extra)));
        return baseDir;
    }
}
