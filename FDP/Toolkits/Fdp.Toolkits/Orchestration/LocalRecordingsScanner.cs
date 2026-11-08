using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Fdp.Toolkit.Orchestration;

/// <summary>
/// ⭐ CE-4330 ③ fact ③ (docs/DESIGN_Recording_Storage.md §7.2) — what a node holds in its OWN exercises staging (<c>OrchestrationConstants.GetNodeExercisesRoot</c>): one <c>"&lt;exerciseGuid&gt;:&lt;totalBytes&gt;"</c> per
/// folder that holds at least one <c>*.fdp</c>, size = every file under the folder. ⛔ HOT PATH (the heartbeat asks every beat): the disk is scanned at most once per <see cref="IntervalSeconds"/> (an injectable clock for the rails),
/// and <see cref="Current"/> returns the SAME list instance until the content changes — so a caller detects a change by reference, and the frames between scans allocate nothing.
/// </summary>
public sealed class LocalRecordingsScanner
{
    private readonly Func<string?> _root;
    private readonly Func<double> _clock;
    private List<string>? _current;
    private double _nextScanAt = double.NegativeInfinity;

    /// <summary>The least time between two disk scans.</summary>
    public double IntervalSeconds { get; }

    /// <param name="root">The node's exercises root (called at each scan; null or a missing folder = nothing held).</param>
    /// <param name="clock">Seconds, monotonic (default: the process clock).</param>
    /// <param name="intervalSeconds">The least time between scans (default 5 s).</param>
    public LocalRecordingsScanner(Func<string?> root, Func<double>? clock = null, double intervalSeconds = 5.0)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        IntervalSeconds = intervalSeconds;
    }

    /// <summary>How many disk scans ran (a rail's probe).</summary>
    public int Scans { get; private set; }

    /// <summary>The recordings held now (null = none): rescanned when the interval passed, else the cached list itself.</summary>
    public List<string>? Current()
    {
        double now = _clock();
        if (now < _nextScanAt) return _current;
        _nextScanAt = now + IntervalSeconds;
        Scans++;
        var scanned = Scan(_root());
        if (scanned != null && !Same(_current, scanned)) _current = scanned.Count == 0 ? null : scanned;
        else if (scanned == null) { /* the disk could not be read: keep what was reported */ }
        return _current;
    }

    private static bool Same(List<string>? a, List<string> b)
    {
        int an = a?.Count ?? 0;
        if (an != b.Count) return false;
        for (int i = 0; i < an; i++) if (!string.Equals(a![i], b[i], StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>The held recordings under <paramref name="exercisesRoot"/> (sorted); null when the disk could not be read.</summary>
    public static List<string>? Scan(string? exercisesRoot)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(exercisesRoot) || !Directory.Exists(exercisesRoot)) return found;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(exercisesRoot))
            {
                if (!Guid.TryParse(Path.GetFileName(dir), out var id)) continue;
                bool hasFdp = false;
                foreach (var _ in Directory.EnumerateFiles(dir, "*.fdp")) { hasFdp = true; break; }
                if (!hasFdp) continue;
                long total = 0;
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)) total += f.Length;
                found.Add(id.ToString("D") + ":" + total.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        found.Sort(StringComparer.Ordinal);
        return found;
    }
}
