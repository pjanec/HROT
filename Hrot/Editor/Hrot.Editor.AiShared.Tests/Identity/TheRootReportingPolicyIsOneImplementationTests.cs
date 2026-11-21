using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Identity;

/// <summary>
/// ⭐⭐⭐ THE RAIL for <c>CE-098</c> (<c>J1-a</c>) — <see cref="AssetRoots.ReportBase"/>.
///
/// <para>📐 <b>Measured <c>2026-08-27</c>:</b> both composition roots ran the same ~9-line reporting block
/// and <b>worded the same fault differently</b>. ⭐ ONE implementation now reports which arm answered and when to warn.</para>
///
/// <para>⭐⭐ <b>CE-4089 (R-4097) changed the DECISION, not the shape:</b> the arms are now <i>config</i> and <i>the NAS root</i>
/// (a source walk-up and the output folder no longer answer for content), so the warning fires when the root the catalog READS holds no
/// <c>assets/</c> folder — i.e. nothing was seeded or published there. ⚠ They deliberately do NOT assert the full sentence:
/// 📌 a rail that pins wording gets edited to match every rephrase and stops meaning anything.</para>
///
/// <para>⚠⚠ <b>These facts mutate <see cref="AssetRoots.ConfiguredRoot"/> and <c>FDP_STAGING_ROOT</c>, both process-global.</b>
/// ⛔ Every one restores them in a <c>finally</c> — same discipline as <c>TheDeployedNodeFindsItsAssetsTests</c>.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.15, <c>docs/DESIGN_Content_Layout.md</c> §2.</para>
/// </summary>
[Collection(AssetRootsTestCollection.Name)]
public sealed class TheRootReportingPolicyIsOneImplementationTests
{
    private sealed class Sink
    {
        public readonly List<string> Info = new();
        public readonly List<string> Warn = new();
    }

    /// <summary>⭐ Segments that cannot resolve a project — they are IGNORED for content (CE-4089), kept so call sites compile.</summary>
    private static readonly string[] NoSuchProject = { "definitely", "not", "here.csproj" };

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

    /// <summary>⚠ Points the NAS root (<c>FDP_STAGING_ROOT</c> + <c>shared</c>) at a fresh temp folder for the body; restores it.</summary>
    private static void WithNasRoot(string staging, Action body)
    {
        const string Var = "FDP_STAGING_ROOT";
        var previous = Environment.GetEnvironmentVariable(Var);
        try
        {
            Environment.SetEnvironmentVariable(Var, staging);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Var, previous);
        }
    }

    private static string ATempDirectory()
        => Directory.CreateDirectory(
               Path.Combine(Path.GetTempPath(), "ce098-" + Guid.NewGuid().ToString("N"))).FullName;

    // ── the info line: ALWAYS, on every arm ───────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The arm that answered is reported on EVERY path, including the happy one.</b>
    /// 📌 <i>"the catalog is empty"</i> and <i>"the catalog is pointed somewhere else"</i> are different
    /// problems, and an operator cannot tell them apart without this line.
    /// </summary>
    [Fact]
    public void The_arm_that_answered_is_always_reported()
    {
        var configured = ATempDirectory();
        var staging = ATempDirectory();
        try
        {
            WithConfiguredRoot(configured, () =>
            {
                var s = Report(NoSuchProject);
                Assert.Single(s.Info);
                Assert.Contains("config", s.Info[0]);
                Assert.Contains(configured, s.Info[0]);
            });

            // ⭐ And with no config, the NAS root is named instead — ⛔ never a source walk-up (CE-4089).
            WithNasRoot(staging, () => WithConfiguredRoot(null, () =>
            {
                var s = Report(NoSuchProject);
                Assert.Single(s.Info);
                Assert.Contains("NAS root", s.Info[0]);
                Assert.Contains(ContentLayout.NasRoot, s.Info[0]);
                Assert.DoesNotContain("source walk-up", s.Info[0]);
            }));
        }
        finally { Directory.Delete(configured, recursive: true); Directory.Delete(staging, recursive: true); }
    }

    // ── the warning: exactly one condition earns it ───────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The warning fires when — and only when — the root the catalog reads has no <c>assets/</c> folder</b>, whichever arm
    /// answered. ⚠ This is the whole decision the two host copies each re-implemented, and the one that could have drifted between them.
    /// </summary>
    [Fact]
    public void The_warning_fires_only_when_the_answering_root_has_no_assets_folder()
    {
        var configured = ATempDirectory();
        var staging = ATempDirectory();
        try
        {
            // ① a configured root WITHOUT assets/ ⇒ the warning (nothing to read there).
            WithConfiguredRoot(configured, () => Assert.Single(Report(NoSuchProject).Warn));

            // ② the same configured root once it holds assets/ ⇒ silent.
            Directory.CreateDirectory(ContentLayout.Assets(configured));
            WithConfiguredRoot(configured, () => Assert.Empty(Report(NoSuchProject).Warn));

            // ③ no config, an UNSEEDED NAS ⇒ the warning.
            WithNasRoot(staging, () => WithConfiguredRoot(null, () => Assert.Single(Report(NoSuchProject).Warn)));

            // ④ no config, a SEEDED NAS ⇒ silent.
            WithNasRoot(staging, () =>
            {
                Directory.CreateDirectory(ContentLayout.Assets(ContentLayout.NasRoot));
                WithConfiguredRoot(null, () => Assert.Empty(Report(NoSuchProject).Warn));
            });
        }
        finally { Directory.Delete(configured, recursive: true); Directory.Delete(staging, recursive: true); }
    }

    /// <summary>
    /// ⭐⭐ The warning carries the two things an operator acts on: <b>which root was empty</b> and <b>what to do</b>.
    /// ⛔ Not the full sentence — see the class remarks on pinning prose.
    /// </summary>
    [Fact]
    public void The_warning_names_the_empty_root_and_the_remedy()
    {
        var staging = ATempDirectory();
        try
        {
            WithNasRoot(staging, () => WithConfiguredRoot(null, () =>
            {
                var warning = Assert.Single(Report(NoSuchProject).Warn);

                Assert.Contains(ContentLayout.NasRoot, warning);   // the root that was empty
                Assert.Contains("--asset-root", warning);          // what to do about it
            }));
        }
        finally { Directory.Delete(staging, recursive: true); }
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
}
