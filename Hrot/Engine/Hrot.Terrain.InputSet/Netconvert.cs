using System.Diagnostics;
using System.Text;

namespace Hrot.Terrain.InputSet;

/// <summary>
/// ⭐ Finds and runs SUMO's <c>netconvert</c> — the ONE locator for the synth tool and the OSM pipeline (Terrain From OSM §9.1).
/// Order: <c>HROT_SUMO_HOME</c> → the repo's <c>.local/sumo/&lt;version&gt;</c> (written by <c>scripts/install-sumo.*</c>) → <c>SUMO_HOME</c> → <c>PATH</c>.
/// A netconvert of another version than <c>tools/sumo/version.txt</c> is refused, naming the install script.
/// </summary>
public static class Netconvert
{
    /// <summary>The SUMO version HROT follows (the vendored SumoSharp's); <c>tools/sumo/version.txt</c> when found, else this.</summary>
    public const string DefaultVersion = "1.20.0";

    public sealed record Located(string? Path, string? Version, string? Problem);

    public static string ExpectedVersion()
    {
        string? root = RepoRoot();
        string? file = root == null ? null : System.IO.Path.Combine(root, "tools", "sumo", "version.txt");
        return file != null && File.Exists(file) ? File.ReadAllText(file).Trim() : DefaultVersion;
    }

    public static Located Locate()
    {
        string expected = ExpectedVersion();
        foreach (var exe in Candidates(expected))
        {
            string? v = VersionOf(exe);
            if (v == expected) return new Located(exe, v, null);
            return new Located(exe, v, $"netconvert {v ?? "of an unknown version"} at {exe} — HROT needs {expected}; run scripts/install-sumo.sh (or .ps1)");
        }
        return new Located(null, null, $"netconvert not found — run scripts/install-sumo.sh (or .ps1) to install SUMO {expected}, or set HROT_SUMO_HOME");
    }

    private static IEnumerable<string> Candidates(string version)
    {
        string[] names = OperatingSystem.IsWindows() ? new[] { "netconvert.exe", "netconvert" } : new[] { "netconvert" };
        var dirs = new List<string>();
        void Home(string? h)
        {
            if (string.IsNullOrWhiteSpace(h)) return;
            h = h.Trim().Trim('"');
            dirs.Add(System.IO.Path.Combine(h, "bin")); dirs.Add(h);
        }
        Home(Environment.GetEnvironmentVariable("HROT_SUMO_HOME"));
        string? root = Environment.GetEnvironmentVariable("HROT_SUMO_NO_LOCAL") == "1" ? null : RepoRoot();   // tests: "no install"
        if (root != null)
        {
            string local = System.IO.Path.Combine(root, ".local", "sumo", version);
            dirs.Add(System.IO.Path.Combine(local, "bin"));
            dirs.Add(System.IO.Path.Combine(local, "venv", "bin"));
            dirs.Add(System.IO.Path.Combine(local, "venv", "Scripts"));
        }
        Home(Environment.GetEnvironmentVariable("SUMO_HOME"));
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
            foreach (var d in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) dirs.Add(d.Trim().Trim('"'));
        foreach (var dir in dirs)
            foreach (var name in names)
            {
                string candidate;
                try { candidate = System.IO.Path.Combine(dir, name); } catch (ArgumentException) { continue; }
                if (File.Exists(candidate)) yield return candidate;
            }
    }

    /// <summary>The folder holding <c>HROT.sln</c>, walking up from the working directory and from this assembly.</summary>
    public static string? RepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(System.IO.Path.Combine(d.FullName, "HROT.sln"))) return d.FullName;
        }
        return null;
    }

    private static string? VersionOf(string exe)
    {
        var (code, stdout, _) = Run(exe, new[] { "--version" }, null);
        if (code != 0) return null;
        // "Eclipse SUMO netconvert Version 1.20.0"
        foreach (var line in stdout.Split('\n'))
        {
            int i = line.IndexOf("Version ", StringComparison.Ordinal);
            if (i >= 0) return line[(i + 8)..].Trim().Split(' ')[0];
        }
        return null;
    }

    /// <summary>Runs a process; returns its exit code, stdout and stderr.</summary>
    public static (int Code, string Stdout, string Stderr) Run(string exe, IEnumerable<string> args, string? workingDirectory)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        if (workingDirectory != null) psi.WorkingDirectory = workingDirectory;
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"{exe} could not be started");
        var so = proc.StandardOutput.ReadToEndAsync();
        var se = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        return (proc.ExitCode, so.Result, se.Result);
    }

    /// <summary>netconvert opens its output with a comment holding the run date; dropping it keeps the file byte-reproducible.</summary>
    public static void StripGeneratedHeader(string netPath)
    {
        string text = File.ReadAllText(netPath);
        int start = text.IndexOf("<!-- generated on", StringComparison.Ordinal);
        if (start < 0) return;
        int end = text.IndexOf("-->", start, StringComparison.Ordinal);
        if (end < 0) return;
        end += 3;
        while (end < text.Length && (text[end] == '\r' || text[end] == '\n')) end++;
        File.WriteAllText(netPath, text.Remove(start, end - start), new UTF8Encoding(false));
    }
}
