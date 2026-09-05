using System;
using System.Linq;
using System.Reflection;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// <b><c>CE-193</c> — an UNWIRED capability must answer "this host does not offer it", never look like a crash.</b>
/// </summary>
/// <remarks>
/// <para>🔴 <b>The defect.</b> Every <c>/breakpoints/*</c> endpoint threw a bare
/// <c>InvalidOperationException("Breakpoint manager not available.")</c>, and the three <c>/recording|/replay</c>
/// guards threw <c>InvalidOperationException("EcsRecordReplayController not available.")</c>. The host's
/// catch-all turns any unrecognised exception into a <b>500</b> — so <i>"this host wires no breakpoint
/// manager"</i> and <i>"the breakpoint code crashed"</i> arrived at the caller identically.</para>
///
/// <para>⛔ That is <c>CE-190</c>/<c>CE-191</c>'s disease one level up: an instrument that cannot
/// distinguish <b>ABSENT</b> from <b>BROKEN</b>. 📌 The API already had the right shape for it —
/// <see cref="NotSupportedHereException"/> ⇒ <b>501</b> plus the capability key, chosen in
/// <c>Architect_Question_54</c> Q54-1 Option C precisely so <i>"a broken panel and an unported one"</i>
/// could not look the same.</para>
///
/// <para>⚠ <b>Why this rail is TEXTUAL.</b> Constructing a <c>DebugApiService</c> needs nine collaborators
/// and lives in a different test project (<c>CE-192</c>'s <c>EditorHarness.BuildDebugApiService</c>). This
/// asserts the property that actually decayed — that no unwired-dependency guard still throws the
/// untyped exception — which is checkable from the source and fails the moment someone adds a tenth guard
/// in the old shape. ⭐ Same reasoning, and same honesty about being weaker than a behavioural rail, as
/// <c>DebugApiCompositionTests</c> next door.</para>
/// </remarks>
public sealed class AnUnwiredCapabilitySaysSoInsteadOfCrashingTests
{
    private static string ServiceSource()
    {
        // Walk up from the test binary to the repo root, then read the production file.
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems")))
            dir = dir.Parent;

        Assert.True(dir != null, "could not locate the repo root from the test binary");

        string path = System.IO.Path.Combine(
            dir!.FullName, "Hrot", "Subsystems", "Hrot.Editor", "DebugApi", "DebugApiService.cs");

        Assert.True(System.IO.File.Exists(path), $"DebugApiService.cs not found at {path}");
        return System.IO.File.ReadAllText(path);
    }

    /// <summary>
    /// The two dependency absences that used to read as a 500 now carry a capability key.
    /// </summary>
    [Theory]
    [InlineData("_bpManager",    "Breakpoints",  "/breakpoints/* would read as a crash rather than an absence")]
    [InlineData("_rrController", "RecordReplay", "/recording|/replay would read as a crash rather than an absence")]
    public void AnAbsentDependencyThrowsTheTypedAbsence(string field, string capability, string consequence)
    {
        string src = ServiceSource();

        // Every guard on this field must be followed by the typed absence, not a bare throw.
        string[] lines = src.Split('\n');
        int guards = 0, typed = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains($"{field} is null", StringComparison.Ordinal)) continue;
            guards++;

            // The throw is on this line or the next few.
            string window = string.Join('\n', lines.Skip(i).Take(4));
            if (window.Contains("NotSupportedHereException", StringComparison.Ordinal))
                typed++;
        }

        Assert.True(guards > 0, $"no '{field} is null' guard found — this rail is looking at the wrong symbol");
        Assert.Equal(guards, typed);
        Assert.Contains($"DebugCapabilities.{capability}", src);

        // ⛔ And the untyped form must be gone for this field: it is what produced the 500.
        Assert.DoesNotContain(
            $"{field} is null)\n                throw new InvalidOperationException",
            src.Replace("\r", ""));

        _ = consequence;   // documented in the InlineData so a failure names the cost
    }

    /// <summary>
    /// The capability keys exist and are distinct — the manifest and the refusal must be able to
    /// name the same thing.
    /// </summary>
    [Fact]
    public void TheTwoNewCapabilityKeysAreDeclaredAndDistinct()
    {
        Assert.Equal("debug.breakpoints", DebugCapabilities.Breakpoints);
        Assert.Equal("debug.recordReplay", DebugCapabilities.RecordReplay);

        // Every key on the class must be unique — a duplicated literal would make two different
        // absences indistinguishable, which is the whole defect this fixes.
        string[] all = typeof(DebugCapabilities)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The message a caller reads names the capability, so "which thing is missing" survives the trip
    /// through MCP as text.
    /// </summary>
    [Fact]
    public void TheRefusalMessageNamesTheCapability()
    {
        var ex = new NotSupportedHereException(DebugCapabilities.Breakpoints);

        Assert.Equal(DebugCapabilities.Breakpoints, ex.Capability);
        Assert.Contains("NOT_SUPPORTED_HERE", ex.Message);
        Assert.Contains("debug.breakpoints", ex.Message);
    }
}
