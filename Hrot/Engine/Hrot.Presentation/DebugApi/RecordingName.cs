using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐ <c>CE-1391</c> (docs/DESIGN_Director_Timeline_Editor.md §11u "as built: CE-1391") — a recording, named the ways <c>POST /replay</c> and the
/// director's <c>recording=</c> name one: an exercise id (the inventory under the host's recordings root; <see cref="Guid.Empty"/> = the newest), or the
/// node files of a group (a <c>.fdp</c> path, a recording's folder, or a <c>|</c>-separated list). ⭐ ONE reading of the text — the HTTP routes and the
/// reel runner both parse through <see cref="Parse"/> — and ONE opener takes it (<see cref="IRecordingOpener.Open"/>).
/// </summary>
public sealed record RecordingName(Guid ExerciseId, IReadOnlyList<string> Paths)
{
    /// <summary>The exercise <paramref name="id"/> as the inventory knows it (<see cref="Guid.Empty"/>: the newest).</summary>
    public static RecordingName Exercise(Guid id) => new(id, Array.Empty<string>());

    /// <summary>A node group by its files.</summary>
    public static RecordingName Files(IReadOnlyList<string> paths) => new(Guid.Empty, paths ?? throw new ArgumentNullException(nameof(paths)));

    /// <summary>Named by exercise id (no files).</summary>
    public bool IsExercise => Paths.Count == 0;

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Reads <paramref name="text"/>: an exercise id (a GUID), a folder (its <c>*.fdp</c>, in name order), a <c>|</c>-separated list of files, or one file.
    /// Null with why not (nothing named; a folder that holds no <c>.fdp</c>).
    /// </summary>
    public static (RecordingName? Name, string? Error) Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, "names nothing: a .fdp path, a recording's folder or an exercise id (GET /exercises)");
        text = text.Trim();
        if (Guid.TryParse(text, out var id)) return (Exercise(id), null);
        if (text.Contains('|'))
            return (Files(text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), null);
        if (Directory.Exists(text))
        {
            var files = Directory.GetFiles(text, "*.fdp").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            return files.Length == 0 ? (null, "the folder holds no .fdp file") : (Files(files), null);
        }
        return (Files(new[] { text }), null);
    }

    /// <summary>
    /// The text that opens this recording again through <see cref="Parse"/>: the exercise id when named by one; else the one file, or the folder when it holds
    /// exactly these files, or the files joined by <c>|</c>. (What a reel item stores in its <c>recording</c>.)
    /// </summary>
    public string Text
    {
        get
        {
            if (IsExercise) return ExerciseId.ToString();
            if (Paths.Count == 1) return Paths[0];
            var dir = Path.GetDirectoryName(Path.GetFullPath(Paths[0]));
            if (dir != null && Paths.All(p => PathComparer.Equals(Path.GetDirectoryName(Path.GetFullPath(p)), dir))
                && Directory.Exists(dir) && SameFiles(Directory.GetFiles(dir, "*.fdp"), Paths))
                return dir;
            return string.Join("|", Paths);
        }
    }

    /// <summary>Whether <paramref name="other"/> names the same recording: the same exercise (both know it), or the same set of files.</summary>
    public bool Same(RecordingName? other)
    {
        if (other is null) return false;
        if (ExerciseId != Guid.Empty && ExerciseId == other.ExerciseId) return true;
        return Paths.Count > 0 && other.Paths.Count > 0 && SameFiles(Paths, other.Paths);
    }

    private static bool SameFiles(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var x = new HashSet<string>(a.Select(Path.GetFullPath), PathComparer);
        return x.SetEquals(b.Select(Path.GetFullPath));
    }

    public override string ToString() => Text;
}

/// <summary>
/// ⭐⭐ <c>CE-1391</c> (§11u "as built: CE-1391") — <b>the ONE recording opener</b> (§11r R1: no second path). The Replay Browser implements it (its
/// <see cref="IReplayBrowserControl"/>); every open lands here: <c>POST /replay {path | paths}</c> (<c>DebugApiService.EnterReplay</c>), <c>POST /replay
/// {exerciseId}</c> (the browser's master transition), the director routes' <c>recording=</c> (through <c>EnterReplay</c>), the render queue's job and a reel
/// item's <c>recording</c> (the pack, <c>DirectorPackHost.Recordings</c> — reachable with no debug HTTP host). A host that opens no recordings (the editor, a
/// cluster node) has none. Main thread only.
/// </summary>
public interface IRecordingOpener
{
    /// <summary>Opens <paramref name="recording"/> at <paramref name="atSeconds"/> after its zero; null when opened, else why not.</summary>
    string? Open(RecordingName recording, double atSeconds);

    /// <summary>The recording open now (its files, and its exercise id when the recording names one); null: none.</summary>
    RecordingName? Shown { get; }
}
