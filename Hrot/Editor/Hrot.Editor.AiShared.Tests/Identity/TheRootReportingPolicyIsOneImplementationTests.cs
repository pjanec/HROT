using System;
using System.Collections.Generic;
using System.IO;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Identity;

/// <summary>
/// ⭐⭐⭐ THE RAIL for <c>CE-098</c> (<c>J1-a</c>) — <see cref="AssetRoots.ReportBase"/>.
///
/// <para>📐 <b>Measured <c>2026-08-27</c>:</b> both composition roots ran the same ~9-line reporting block
/// and <b>worded the same fault differently</b> — <i>"editor-owned BTree/HSM JSON assets will only load
/// if…"</i> vs <i>"the catalog will be empty unless…"</i>. ⚠⚠ <b>The editor's copy was introduced by
/// <c>J1</c> itself</b>, which fixed a resolution drift by cloning CGF's reporting across.</para>
///
/// <para>⭐⭐ <b>What this pins is a DECISION, not prose.</b> ⛔ The facts below assert <i>whether</i> the
/// warning fires per arm, plus the two substantive tokens an operator greps for (<c>--asset-root</c> and the
/// searched-for project path). ⚠ They deliberately do NOT assert the full sentence: 📌 a rail that pins
/// wording gets edited to match every rephrase and stops meaning anything.</para>
///
/// <para>⚠⚠ <b>These facts mutate <see cref="AssetRoots.ConfiguredRoot"/>, which is process-global</b>
/// (<c>AssetRoots.Configure</c>). ⛔ Every one restores it in a <c>finally</c> — 📌 <c>CE-084</c>/<c>CE-088</c>
/// are this assembly's standing warning about order-dependent greens, and an unrestored static here would
/// manufacture exactly that. ⭐ Same discipline as <c>TheDeployedNodeFindsItsAssetsTests</c>.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.15.</para>
/// </summary>
[Collection(AssetRootsTestCollection.Name)]
public sealed class TheRootReportingPolicyIsOneImplementationTests
{
    private sealed class Sink
    {
        public readonly List<string> Info = new();
        public readonly List<string> Warn = new();
    }

    /// <summary>⭐ Segments that cannot resolve — so the walk-up arm is genuinely absent.</summary>
    private static readonly string[] NoSuchProject = { "definitely", "not", "here.csproj" };

    /// <summary>⭐ The real segments, which DO resolve from a source checkout.</summary>
    private static readonly string[] RealProject =
        { "Hrot", "Editor", "Hrot.Editor.AiShared", "Hrot.Editor.AiShared.csproj" };

    private static Sink Report(string[] segments)
    {
        var sink = new Sink();
        AssetRoots.ReportBase(sink.Info.Add, sink.Warn.Add, segments);
        return sink;
    }

    /// <summary>⚠ Restores the process-global config whatever the body does.</summary>
    private static void WithConfiguredRoot(string? root, Action body)
    {
        var previous = AssetRoots.ConfiguredRoot;
        try
        {
            AssetRoots.Configure(root);
            body();
        }
        finally
        {
            AssetRoots.Configure(previous);
        }
    }

    private static string ARealDirectory()
        => Directory.CreateDirectory(
               Path.Combine(Path.GetTempPath(), "ce098-" + Guid.NewGuid().ToString("N"))).FullName;

    // ── the info line: ALWAYS, on every arm ───────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The arm that answered is reported on EVERY path, including the happy one.</b>
    /// 📌 <i>"the catalog is empty"</i> and <i>"the catalog is pointed somewhere else"</i> are different
    /// problems, and an operator cannot tell them apart without this line. ⛔ Reporting only on failure is
    /// the shape that made ruling 67 hard to diagnose in the first place.
    /// </summary>
    [Fact]
    public void The_arm_that_answered_is_always_reported()
    {
        var configured = ARealDirectory();
        try
        {
            WithConfiguredRoot(configured, () =>
            {
                var s = Report(NoSuchProject);
                Assert.Single(s.Info);
                Assert.Contains("config", s.Info[0]);
                Assert.Contains(configured, s.Info[0]);
            });

            // ⭐ And with no config, from a source checkout, the walk-up arm is named instead.
            WithConfiguredRoot(null, () =>
            {
                var s = Report(RealProject);
                Assert.Single(s.Info);
                Assert.Contains("source walk-up", s.Info[0]);
            });
        }
        finally { Directory.Delete(configured, recursive: true); }
    }

    // ── the warning: exactly one arm earns it ─────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The ruling-67 warning fires when — and only when — the OUTPUT-DIRECTORY arm answered.</b>
    /// ⚠ This is the whole decision the two host copies each re-implemented, and the one that could have
    /// drifted between them with nothing to see.
    /// </summary>
    [Fact]
    public void The_warning_fires_only_when_neither_config_nor_source_tree_answered()
    {
        var configured = ARealDirectory();
        try
        {
            // ① a CONFIGURED root ⇒ silent, even with no source tree at all.
            WithConfiguredRoot(configured, () => Assert.Empty(Report(NoSuchProject).Warn));

            // ② no config but a real SOURCE TREE ⇒ silent.
            WithConfiguredRoot(null, () => Assert.Empty(Report(RealProject).Warn));

            // ③ neither ⇒ the warning. ⛔ The only arm that earns it.
            WithConfiguredRoot(null, () => Assert.Single(Report(NoSuchProject).Warn));
        }
        finally { Directory.Delete(configured, recursive: true); }
    }

    /// <summary>
    /// ⭐⭐ The warning carries the two things an operator acts on: <b>what was searched for</b> and
    /// <b>what to do</b>. ⛔ Not the full sentence — see the class remarks on pinning prose.
    /// </summary>
    [Fact]
    public void The_warning_names_the_searched_path_and_the_remedy()
    {
        WithConfiguredRoot(null, () =>
        {
            var warning = Assert.Single(Report(NoSuchProject).Warn);

            Assert.Contains(Path.Combine(NoSuchProject), warning);   // what it looked for
            Assert.Contains("--asset-root", warning);                // what to do about it
        });
    }

    // ── the sinks ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Both sinks are OPTIONAL and their absence must not throw — ⛔ a host that wants only the warning
    /// should not have to invent an info sink, and this is called during composition where a throw would
    /// take the whole boot with it.
    /// </summary>
    [Fact]
    public void Absent_sinks_are_silent_not_fatal()
    {
        WithConfiguredRoot(null, () =>
        {
            AssetRoots.ReportBase(null, null, NoSuchProject);            // ⛔ must not throw
            AssetRoots.ReportBase(info: _ => { }, warn: null, NoSuchProject);
            AssetRoots.ReportBase(info: null, warn: _ => { }, NoSuchProject);
        });
    }

    /// <summary>
    /// ⭐⭐ <b>Anti-vacuity, and it is the fact that matters most here.</b> ⛔ Every assertion above would
    /// pass trivially if <see cref="NoSuchProject"/> secretly resolved or the real segments secretly did
    /// not — so both premises are measured directly, in the same environment the facts run in.
    /// </summary>
    [Fact]
    public void The_two_premises_this_rail_rests_on_actually_hold()
    {
        WithConfiguredRoot(null, () =>
        {
            Assert.Null(AssetRoots.ResolveProjectDir(NoSuchProject));
            Assert.NotNull(AssetRoots.ResolveProjectDir(RealProject));
        });
    }
}
