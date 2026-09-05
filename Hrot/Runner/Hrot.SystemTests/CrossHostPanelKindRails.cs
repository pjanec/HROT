using System.Reflection;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Presentation.Windows;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐⭐ <b><c>N6</c> — THE REPLACEMENT FOR THE <c>PanelSnapshot</c> RAIL THAT LEFT WITH <c>StrideMock</c>
/// (<c>ST-017</c>), and it is deliberately NOT "the same rail with four instead of five".</b>
/// 📄 <c>docs/DESIGN_Regression_Net.md</c> §7 <c>N6</c> · <c>DESIGN_Stride_Port.md</c> §7 · <c>PanelIds.cs</c>.
///
/// <para>⛔⛔ <b>The trap the handoff named: *"do not fix it by lowering the expected count to four and
/// moving on — that is how coverage leaves quietly."*</b> ⇒ ⭐ so this file answers the two questions the
/// lost rail actually answered, and answers them in a form that a FIFTH or a THIRD host cannot slip past.</para>
///
/// <para>⭐⭐⭐ <b>What was lost, measured.</b>
/// <c>Hrot.StrideMock.Tests/Windows/StrideMockSystemProfilerWindowDumpsItsModelTests.cs</c> *(3 facts)* was
/// one host's snapshot rail for <c>SystemProfilerWindow</c>, and <c>PanelIds.cs</c> named <b>five</b> hosts
/// that must agree on the <c>system-profiler</c> kind. ⇒ it carried two claims:
/// <list type="number">
/// <item>⭐ <b>the kind agrees across hosts</b> — and 📐 <b>that is now true BY CONSTRUCTION, which is
/// strictly stronger than a per-host assertion</b>: <see cref="SystemProfilerWindow"/> holds
/// <c>internal const Kind = PanelIds.SystemProfiler</c> and its constructor takes <b>no kind
/// parameter</b>, so a host has nothing to disagree with. ⭐ <see cref="A_host_cannot_disagree_about_the_kind"/>
/// asserts exactly that structure, so the day someone adds a <c>kind</c> parameter *(re-opening the
/// divergence)*, a rail says so;</item>
/// <item>⭐ <b>that host's window publishes a snapshot</b> — 📐 covered for the shared window by
/// <c>Hrot.Presentation.Tests/Windows/SystemProfilerWindowDumpsItsSnapshotTests.cs</c> and per-host by
/// <c>Hrot.Editor.Tests</c> and <c>Hrot.IG.Tests</c>. ⚠ <b>Genuinely NOT covered per-host for SimHost and
/// CGF — and it never was</b>, before or after the mock. ⛔ Stated rather than quietly implied; it is a
/// finding *(<c>HN-014</c>'s neighbour, filed as part of this batch's report)*, not something this file
/// pretends to have fixed.</item>
/// </list></para>
///
/// <para>⭐⭐ <b>And the count is asserted by ENUMERATION, not by a literal.</b>
/// <see cref="Every_production_host_that_registers_the_profiler_is_accounted_for"/> reads the production
/// sources, so retiring a sixth host reddens and NAMES it — 📌 which is what nobody noticed when the mock
/// took its rail with it.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
public sealed class CrossHostPanelKindRails
{
    private readonly ITestOutputHelper _out;
    public CrossHostPanelKindRails(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// ⭐⭐⭐ <b>The kind agreement, as a STRUCTURAL claim.</b> The shared window owns the kind and no host
    /// can pass one ⇒ divergence is unrepresentable. ⛔ If a <c>kind</c> parameter ever appears, this
    /// reddens and the per-host agreement rails become necessary again.
    /// </summary>
    [Fact]
    public void A_host_cannot_disagree_about_the_kind()
    {
        var kind = typeof(SystemProfilerWindow)
                   .GetField("Kind", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                   ?.GetRawConstantValue() as string;

        Assert.Equal(PanelIds.SystemProfiler, kind);

        var kindParams = typeof(SystemProfilerWindow)
                         .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         .SelectMany(c => c.GetParameters())
                         .Where(p => p.Name!.Contains("kind", StringComparison.OrdinalIgnoreCase))
                         .Select(p => p.Name!)
                         .ToArray();

        Assert.True(kindParams.Length == 0,
            $"SystemProfilerWindow now takes a caller-supplied kind ([{string.Join(", ", kindParams)}]), so "
          + "hosts CAN disagree again. ⭐ Either remove it, or restore a per-host kind-agreement rail for "
          + "every host in PanelIds.SystemProfiler's list (ST-017).");
    }

    /// <summary>
    /// ⭐⭐ <b>Every production host that constructs the profiler window is one we know about.</b>
    ///
    /// <para>⭐ Source-level on purpose: *"which hosts register this panel"* is a fact about call sites, and
    /// ⛔ reflection cannot see a constructor call. ⚠ The list below is the one <c>PanelIds.cs</c> documents;
    /// a new host is a one-line edit here AND a documentation edit there, which is the point — coverage
    /// changes become deliberate.</para>
    /// </summary>
    [Fact]
    public void Every_production_host_that_registers_the_profiler_is_accounted_for()
    {
        // 📄 PanelIds.SystemProfiler's remarks: SimHost · CGF · IG · Editor (StrideMock retired — ST-017).
        var expected = new[] { "CgfSubsystem.cs", "EditorSubsystem.cs", "IgSubsystem.cs", "SimHostSubsystem.cs" };

        var root = RepoRoot();
        var sites = Directory.EnumerateFiles(Path.Combine(root, "Hrot"), "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                            StringComparison.Ordinal)
                             && !f.Contains("Tests", StringComparison.Ordinal))
                    .Where(f => File.ReadAllText(f).Contains("new SystemProfilerWindow(", StringComparison.Ordinal))
                    .Select(Path.GetFileName)
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToArray();

        _out.WriteLine($"production hosts constructing SystemProfilerWindow: [{string.Join(", ", sites)}]");

        Assert.Equal(expected, sites);
    }

    /// <summary>
    /// ⭐ The repo root, found by walking up to the solution file — ⛔ never a path constant, which breaks
    /// the first time the layout moves.
    /// </summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);   // ⛔ a source-level rail cannot run without sources; say so rather than skip (R-131)
        return dir!.FullName;
    }
}
