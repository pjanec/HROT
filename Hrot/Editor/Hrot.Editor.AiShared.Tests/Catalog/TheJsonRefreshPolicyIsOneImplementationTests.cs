using System;
using System.Collections.Generic;
using System.IO;
using Hrot.BTree.Editor.Catalog;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Hsm.Editor.Catalog;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Catalog;

/// <summary>
/// ⭐⭐⭐ THE RAIL for <c>CE-091</c> (<c>J2</c> <c>K1</c>) and <c>CE-095</c> (<c>J1</c> <c>K5</c>) —
/// <see cref="AiAssetCatalogBuilder.RefreshJsonContributors"/>.
///
/// <para>🔴 <b>Why this method is new even though its own class documented it.</b> 📐 Measured
/// <c>2026-08-27</c>: <see cref="AiAssetCatalogBuilder.RefreshFromAssembly"/>'s summary has always said
/// <i>"JSON contributors are refreshed separately via <c>RefreshJsonContributors</c>"</i> — and that
/// method <b>did not exist</b>. ⇒ ⛔ both composition roots hand-rolled the same six-line kind-dispatch
/// lambda in its place. 📌 <b>Designed in and never built</b> — the same shape the corpus records for
/// <c>MarshalFromBytes</c>.</para>
///
/// <para>⭐⭐ The POLICY under test is small but exact: <b>the right kind, its own root, resolved at CALL
/// TIME, present on disk, and nothing else touched.</b> ⚠ Each of those is a way the two hand-rolled
/// copies could have drifted apart, which is why each gets its own fact.</para>
///
/// <para>⭐⭐⭐ <b><c>CE-095</c> added the fourth clause — <i>present on disk</i> — and that is why the
/// facts below now use REAL directories.</b> 📐 Measured: <c>CE-091</c> collapsed the kind dispatch but
/// left each host's <c>if (Directory.Exists(root)) Refresh(…) else warn(…)</c> block in place for the
/// INITIAL refresh ⇒ ⛔ <b>two implementations of "refresh the JSON contributor for kind K"</b>, differing
/// in the one clause that decides whether a missing root is silent. ⚠ An earlier version of this rail
/// passed roots like <c>"/bt"</c> that do not exist — it could not have caught that, and would now be
/// asserting the pre-<c>CE-095</c> policy.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.10, §5c.13.</para>
/// </summary>
public sealed class TheJsonRefreshPolicyIsOneImplementationTests : IDisposable
{
    /// <summary>⭐ Records which kind's refresh fired and with what root — the only observable this
    /// policy has, and the one both hosts' lambdas produced.</summary>
    private sealed class Log
    {
        public readonly List<string> Calls = new();
        public readonly List<string> Warnings = new();
        public void BTree(string root) => Calls.Add($"btree:{root}");
        public void Hsm(string root)   => Calls.Add($"hsm:{root}");
        public void Warn(string msg)   => Warnings.Add(msg);
    }

    /// <summary>⚠ Real directories, because <i>"the root exists"</i> is now part of the policy.</summary>
    private readonly List<string> _temp = new();

    private string ARealDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ce095-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _temp.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _temp)
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private static (AiAssetCatalogBuilder B, Log L) Make(
        Func<string?>? bTreeRoot, Func<string?>? hsmRoot)
    {
        var log   = new Log();
        var btree = new BTreeAssetContributor();
        var hsm   = new HsmAssetContributor();
        var bp    = new BlueprintAssetContributor(Path.GetTempPath());

        var builder = new AiAssetCatalogBuilder(
            btree, hsm, bp,
            asm => btree.LoadFrom(asm),
            asm => hsm.LoadFrom(asm),
            () => { },
            bTreeJsonRefresh: log.BTree,
            bTreeJsonRootDir: bTreeRoot,
            hsmJsonRefresh:   log.Hsm,
            hsmJsonRootDir:   hsmRoot,
            warnMissingRoot:  log.Warn);

        return (builder, log);
    }

    private static (AiAssetCatalogBuilder B, Log L) Make(string? bTreeRoot, string? hsmRoot)
        => Make(() => bTreeRoot, () => hsmRoot);

    // ── the kind dispatch ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ Each kind refreshes ITS OWN contributor and no other.
    /// ⛔ The failure this forbids is the one a hand-rolled copy invites: refreshing both, or the wrong
    /// one, because the two `if`s were written by hand twice.
    /// </summary>
    [Fact]
    public void Each_kind_refreshes_only_its_own_contributor()
    {
        var bt  = ARealDirectory();
        var hs  = ARealDirectory();

        var (b1, l1) = Make(bt, hs);
        b1.RefreshJsonContributors(AssetKind.BTree);
        Assert.Equal(new[] { $"btree:{bt}" }, l1.Calls);
        Assert.Empty(l1.Warnings);

        var (b2, l2) = Make(bt, hs);
        b2.RefreshJsonContributors(AssetKind.Hsm);
        Assert.Equal(new[] { $"hsm:{hs}" }, l2.Calls);
        Assert.Empty(l2.Warnings);
    }

    /// <summary>
    /// ⭐⭐ A kind with no JSON contributor is a NO-OP — <b>not an exception</b>.
    /// ⚠ Blueprint genuinely has none, and <c>Scenario</c> is not file-backed at all, so both hosts call
    /// this for kinds it cannot serve. ⛔ Throwing would turn a normal create into a failure.
    /// </summary>
    [Theory]
    [InlineData(AssetKind.Blueprint)]
    [InlineData(AssetKind.Scenario)]
    public void A_kind_with_no_json_contributor_is_a_no_op(AssetKind kind)
    {
        var (b, l) = Make(ARealDirectory(), ARealDirectory());
        b.RefreshJsonContributors(kind);
        Assert.Empty(l.Calls);
        Assert.Empty(l.Warnings);
    }

    // ── the root gate ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ No root ⇒ no refresh, <b>and no warning</b>. 📐 This mirrors the hosts' own
    /// `if (_btreeJsonRootDir != null)` guard, which existed because the field is assigned during
    /// <c>Initialize</c> and is null before that. ⚠ Silent on purpose: an UNSET root is a sequencing
    /// fact, not an operator-visible fault — ⛔ warning here would fire on every host during startup.
    /// </summary>
    [Fact]
    public void A_null_or_empty_root_skips_the_refresh_silently()
    {
        var (b1, l1) = Make((string?)null, null);
        b1.RefreshJsonContributors(AssetKind.BTree);
        Assert.Empty(l1.Calls);
        Assert.Empty(l1.Warnings);

        var (b2, l2) = Make("", "");
        b2.RefreshJsonContributors(AssetKind.Hsm);
        Assert.Empty(l2.Calls);
        Assert.Empty(l2.Warnings);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE ROOT IS RESOLVED AT CALL TIME, NOT CAPTURED.</b>
    ///
    /// <para>🔴 This is the one that would break silently. 📐 The hosts pass <c>() =&gt; _btreeJsonRootDir</c>
    /// — a field assigned LATER in <c>Initialize</c> than the builder is constructed. ⇒ ⛔ a builder that
    /// read the root eagerly would freeze <see langword="null"/> and the JSON contributor would never
    /// refresh, on both hosts, with nothing to see.</para>
    /// </summary>
    [Fact]
    public void The_root_is_read_at_call_time_so_a_later_assignment_is_seen()
    {
        string? root = null;                       // ⚠ as the host's field is, at construction
        var (b, l) = Make(() => root, () => null);

        b.RefreshJsonContributors(AssetKind.BTree);
        Assert.Empty(l.Calls);                     // nothing yet — correct

        var real = ARealDirectory();
        root = real;                               // ⭐ what Initialize does
        b.RefreshJsonContributors(AssetKind.BTree);
        Assert.Equal(new[] { $"btree:{real}" }, l.Calls);
    }

    /// <summary>
    /// ⭐ A host that wired no refresh at all is served silently — ⚠ and that is honest here: it cannot
    /// supply a refresh for a contributor it never built. ⛔ Not a silent default in the harmful sense;
    /// the caller has nothing to pass.
    /// </summary>
    [Fact]
    public void A_host_with_no_json_refresh_wired_is_a_no_op()
    {
        var btree = new BTreeAssetContributor();
        var hsm   = new HsmAssetContributor();
        var bp    = new BlueprintAssetContributor(Path.GetTempPath());
        var builder = new AiAssetCatalogBuilder(
            btree, hsm, bp, asm => btree.LoadFrom(asm), asm => hsm.LoadFrom(asm), () => { });

        builder.RefreshJsonContributors(AssetKind.BTree);   // ⛔ must not throw
        builder.RefreshJsonContributors(AssetKind.Hsm);
    }

    // ── CE-095: the missing-root clause, which used to live twice in the hosts ─────

    /// <summary>
    /// ⭐⭐⭐ <b>A SET root that is not on disk WARNS and does NOT refresh.</b>
    ///
    /// <para>🔴 <b>Both halves matter, and the second is the subtle one.</b> ⛔ Refreshing anyway would
    /// <i>empty</i> the contributor — <c>Discover</c> clears its headers when the directory is gone — so a
    /// root that disappears at runtime would silently drop every asset already loaded. ⭐ Keeping the last
    /// good set and SAYING SO is the behaviour both hosts' inline blocks had, and it is the behaviour that
    /// moved in here.</para>
    /// </summary>
    [Fact]
    public void A_set_root_that_is_not_on_disk_warns_and_does_not_refresh()
    {
        var absent = Path.Combine(Path.GetTempPath(), "ce095-absent-" + Guid.NewGuid().ToString("N"));

        var (b, l) = Make(absent, absent);
        b.RefreshJsonContributors(AssetKind.BTree);

        Assert.Empty(l.Calls);                                    // ⛔ NOT refreshed
        Assert.Single(l.Warnings);                                // ⭐ and reported
        Assert.Contains("BTree JSON root not found", l.Warnings[0]);
        Assert.Contains(absent, l.Warnings[0]);
    }

    /// <summary>
    /// ⭐⭐ The warning NAMES THE KIND, so an operator reading one line knows which tree is missing.
    /// ⚠ Each host used to compose this text itself; the two spellings were near-identical and could
    /// have diverged with nothing to notice.
    /// </summary>
    [Fact]
    public void The_missing_root_warning_names_the_kind()
    {
        var absent = Path.Combine(Path.GetTempPath(), "ce095-absent-" + Guid.NewGuid().ToString("N"));

        var (b, l) = Make(absent, absent);
        b.RefreshJsonContributors(AssetKind.Hsm);

        Assert.Contains("HSM JSON root not found", Assert.Single(l.Warnings));
    }

    /// <summary>
    /// ⚠ <b>The sink is OPTIONAL and its absence must not throw</b> — ⛔ a host that wires the refresh but
    /// no logger still has to survive a missing root. ⭐ Stated as its own fact because the silent-default
    /// rule cuts the other way here: there is no value the caller is withholding.
    /// </summary>
    [Fact]
    public void A_missing_root_with_no_warn_sink_is_silent_not_fatal()
    {
        var absent = Path.Combine(Path.GetTempPath(), "ce095-absent-" + Guid.NewGuid().ToString("N"));
        var btree  = new BTreeAssetContributor();
        var hsm    = new HsmAssetContributor();
        var bp     = new BlueprintAssetContributor(Path.GetTempPath());
        var fired  = 0;

        var builder = new AiAssetCatalogBuilder(
            btree, hsm, bp, asm => btree.LoadFrom(asm), asm => hsm.LoadFrom(asm), () => { },
            bTreeJsonRefresh: _ => fired++,
            bTreeJsonRootDir: () => absent);

        builder.RefreshJsonContributors(AssetKind.BTree);   // ⛔ must not throw
        Assert.Equal(0, fired);
    }
}
