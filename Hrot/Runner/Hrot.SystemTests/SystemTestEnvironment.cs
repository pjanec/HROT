using System.Reflection;
using System.Runtime.InteropServices;

namespace Hrot.SystemTests;

/// <summary>
/// Answers the two questions every system test asks before it can run: <b>where is the editor
/// binary</b>, and <b>can this host actually launch one</b>.
///
/// <para><b>Why a skip and not a failure.</b> The suite needs a real editor process and, on Linux,
/// a display server. A host without Xvfb cannot provide that — a red there would say "the system is
/// broken" when it means "this machine cannot host the test". So the cases skip with the reason
/// stated. ⚠ A skip is only honest while it names an ENVIRONMENT limit: a case that skips because
/// the editor failed to boot would be hiding a defect, so <see cref="EditorProcessFixture"/> FAILS
/// on a boot problem and never converts one into a skip.</para>
/// </summary>
public static class SystemTestEnvironment
{
    /// <summary>Set to <c>0</c>/<c>false</c> to opt a host out of the suite entirely.</summary>
    public const string EnableVar = "HROT_SYSTEM_TESTS";

    /// <summary>Explicit path to <c>Hrot.ClusterRunner.dll</c>; overrides all discovery.</summary>
    public const string EditorDllVar = "HROT_EDITOR_DLL";

    /// <summary>Seconds to wait for a launched editor to answer <c>GET /status</c>.</summary>
    public const string BootTimeoutVar = "HROT_SYSTEM_TESTS_BOOT_TIMEOUT";

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>The editor dll, or <see langword="null"/> when it could not be found.</summary>
    public static string? EditorDll { get; } = ResolveEditorDll();

    /// <summary>
    /// Absolute path to the <c>Xvfb</c> server on Linux; <see langword="null"/> elsewhere/absent.
    /// ⚠ The server itself, not the <c>xvfb-run</c> wrapper — the fixture owns the display's
    /// lifetime so that killing an editor cannot orphan its X server.
    /// </summary>
    public static string? Xvfb { get; } = IsWindows ? null : FindOnPath("Xvfb");

    public static int BootTimeoutSeconds =>
        int.TryParse(Environment.GetEnvironmentVariable(BootTimeoutVar), out var s) && s > 0 ? s : 180;

    /// <summary>
    /// Non-null when this host cannot run the suite — the text becomes the xUnit skip reason, so it
    /// must say what is missing and how to supply it.
    /// </summary>
    public static string? SkipReason { get; } = ComputeSkipReason();

    private static string? ComputeSkipReason()
    {
        var enable = Environment.GetEnvironmentVariable(EnableVar);
        if (enable is "0" or "false" or "False")
            return $"disabled by {EnableVar}={enable}";

        if (EditorDll is null)
            return $"Hrot.ClusterRunner.dll not found (build the solution, or set {EditorDllVar} to its path).";

        if (!IsWindows && Xvfb is null)
            return "Xvfb not found — the editor needs a display server on Linux (apt-get install xvfb).";

        return null;
    }

    /// <summary>
    /// Editor-binary discovery, most explicit first: an env override, then the path MSBuild stamped
    /// in at build time from the ProjectReference, then an upward search for the repo root. The
    /// upward search is the last resort precisely because it is the one that can go stale.
    /// </summary>
    private static string? ResolveEditorDll()
    {
        const string dllName = "Hrot.ClusterRunner.dll";

        var explicitPath = Environment.GetEnvironmentVariable(EditorDllVar);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return Path.GetFullPath(explicitPath);

        var stamped = typeof(SystemTestEnvironment).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ClusterRunnerOutputDir")?.Value;
        if (!string.IsNullOrWhiteSpace(stamped))
        {
            var candidate = Path.Combine(stamped, dllName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        // Last resort: walk up to the repo root and into the known project output.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln")))
            {
                var runnerBin = Path.Combine(dir.FullName, "Hrot", "Runner", "Hrot.ClusterRunner", "bin");
                if (Directory.Exists(runnerBin))
                {
                    // Prefer the configuration this test assembly was built in.
                    var config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
                        ? "Release" : "Debug";
                    var preferred = Path.Combine(runnerBin, config, "net8.0", dllName);
                    if (File.Exists(preferred)) return preferred;

                    var any = Directory.EnumerateFiles(runnerBin, dllName, SearchOption.AllDirectories).FirstOrDefault();
                    if (any is not null) return any;
                }
                break;
            }
            dir = dir.Parent;
        }

        return null;
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(segment, executable);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* an unusable PATH segment is not an error here */ }
        }
        return null;
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself, with a stated reason, on a host that cannot run
/// an editor. Pair it with <c>[Trait("Category","SystemSmoke")]</c> on the test class so the whole
/// suite stays filterable off the fast per-edit path (design D10).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SystemSmokeFactAttribute : FactAttribute
{
    public SystemSmokeFactAttribute()
    {
        if (SystemTestEnvironment.SkipReason is { } reason)
            Skip = reason;
    }
}
