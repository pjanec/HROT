using System;
using System.IO;

namespace Hrot.Editor.AiShared.Tests;

/// <summary>
/// ⭐⭐ <b>Locates a repository source file from inside the test host.</b>
///
/// <para>📌 Why source-reading rails exist at all: <c>R-21</c>/<c>R-62</c> — <b>no headless rail can
/// drive ImGui</b>, so a contract that lives in the DRAW path can only be asserted over the sources
/// that express it. ⭐ Weaker than a behavioural rail and honest about it; ⛔ still far stronger than
/// nothing, which is what the draw layer had.</para>
///
/// <para>⭐ One implementation, because two would drift the moment the test host's output layout
/// changes *(ruling 9)*.</para>
/// </summary>
internal static class RepoFiles
{
    /// <summary>Absolute path of <paramref name="relative"/>, searched upward from the test binaries.</summary>
    /// <exception cref="FileNotFoundException">When no ancestor directory contains it.</exception>
    public static string Find(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException(
            $"Could not locate '{relative}' above the test host ({AppContext.BaseDirectory}).");
    }

    /// <summary>The file's text.</summary>
    public static string Read(string relative) => File.ReadAllText(Find(relative));

    /// <summary>The file's lines.</summary>
    public static string[] Lines(string relative) => File.ReadAllLines(Find(relative));
}
