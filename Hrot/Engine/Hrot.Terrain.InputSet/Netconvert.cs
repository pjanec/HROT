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

    /// <summary>
    /// ⭐ CE-4409 — the SUMO home of the install <paramref name="exe"/> belongs to: the folder holding the install's <c>data</c> / <c>tools</c> (SUMO's own layout, <c>bin/</c> beside them),
    /// found walking up from the executable, else the pip layout's <c>site-packages/sumo</c> under the venv the <c>.local/sumo/&lt;v&gt;</c> installer makes. <see langword="null"/> when none is found.
    /// 🔒 Why it matters (user, 2026-11-10, Windows): netconvert printed <i>"environment variable SUMO_HOME is not set properly"</i> and exited 1 — the child inherited a parent environment without it.
    /// </summary>
    public static string? SumoHomeOf(string exe)
    {
        try
        {
            for (var d = new DirectoryInfo(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe)) ?? "."); d != null; d = d.Parent)
            {
                if (Directory.Exists(System.IO.Path.Combine(d.FullName, "data")) && Directory.Exists(System.IO.Path.Combine(d.FullName, "tools"))) return d.FullName;
                // pip layout: <venv>/lib/pythonX/site-packages/sumo (Linux) · <venv>/Lib/site-packages/sumo (Windows)
                foreach (var site in new[] { System.IO.Path.Combine(d.FullName, "Lib", "site-packages", "sumo") }.Concat(
                             Directory.Exists(System.IO.Path.Combine(d.FullName, "lib")) ? Directory.EnumerateDirectories(System.IO.Path.Combine(d.FullName, "lib"), "python*") .Select(p => System.IO.Path.Combine(p, "site-packages", "sumo")) : Array.Empty<string>()))
                    if (Directory.Exists(System.IO.Path.Combine(site, "data"))) return site;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }

    /// <summary>Runs a process; returns its exit code, stdout and stderr. ⭐ CE-4409 — <paramref name="sumoHome"/>, when given, is the child's <c>SUMO_HOME</c>.</summary>
    public static (int Code, string Stdout, string Stderr) Run(string exe, IEnumerable<string> args, string? workingDirectory, string? sumoHome = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        if (!string.IsNullOrEmpty(sumoHome)) psi.Environment["SUMO_HOME"] = sumoHome;
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
