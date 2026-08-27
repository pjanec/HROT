using System;
using System.Collections.Generic;
using Hrot.BTree.Editor.Catalog;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Hsm.Editor.Catalog;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Catalog;

/// <summary>
/// ⭐⭐⭐ THE RAIL for <c>CE-091</c> (<c>J2</c> <c>K1</c>) —
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
/// TIME, and nothing else touched.</b> ⚠ Each of those four is a way the two hand-rolled copies could
/// have drifted apart, which is why each gets its own fact.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.10.</para>
/// </summary>
public sealed class TheJsonRefreshPolicyIsOneImplementationTests
{
    /// <summary>⭐ Records which kind's refresh fired and with what root — the only observable this
    /// policy has, and the one both hosts' lambdas produced.</summary>
    private sealed class Log
    {
        public readonly List<string> Calls = new();
        public void BTree(string root) => Calls.Add($"btree:{root}");
        public void Hsm(string root)   => Calls.Add($"hsm:{root}");
    }

    private static (AiAssetCatalogBuilder B, Log L) Make(
        Func<string?>? bTreeRoot, Func<string?>? hsmRoot)
    {
        var log   = new Log();
        var btree = new BTreeAssetContributor();
        var hsm   = new HsmAssetContributor();
        var bp    = new BlueprintAssetContributor(System.IO.Path.GetTempPath());

        var builder = new AiAssetCatalogBuilder(
            btree, hsm, bp,
            asm => btree.LoadFrom(asm),
            asm => hsm.LoadFrom(asm),
            () => { },
            bTreeJsonRefresh: log.BTree,
            bTreeJsonRootDir: bTreeRoot,
            hsmJsonRefresh:   log.Hsm,
            hsmJsonRootDir:   hsmRoot);

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
        var (b1, l1) = Make("/bt", "/hsm");
        b1.RefreshJsonContributors(AssetKind.BTree);
        Assert.Equal(new[] { "btree:/bt" }, l1.Calls);

        var (b2, l2) = Make("/bt", "/hsm");
        b2.RefreshJsonContributors(AssetKind.Hsm);
        Assert.Equal(new[] { "hsm:/hsm" }, l2.Calls);
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
        var (b, l) = Make("/bt", "/hsm");
        b.RefreshJsonContributors(kind);
        Assert.Empty(l.Calls);
    }

    // ── the root gate ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ No root ⇒ no refresh. 📐 This mirrors the hosts' own `if (_btreeJsonRootDir != null)` guard,
    /// which existed because the field is assigned during <c>Initialize</c> and is null before that.
    /// </summary>
    [Fact]
    public void A_null_or_empty_root_skips_the_refresh()
    {
        var (b1, l1) = Make((string?)null, null);
        b1.RefreshJsonContributors(AssetKind.BTree);
        Assert.Empty(l1.Calls);

        var (b2, l2) = Make("", "");
        b2.RefreshJsonContributors(AssetKind.Hsm);
        Assert.Empty(l2.Calls);
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

        root = "/assigned/later";                  // ⭐ what Initialize does
        b.RefreshJsonContributors(AssetKind.BTree);
        Assert.Equal(new[] { "btree:/assigned/later" }, l.Calls);
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
        var bp    = new BlueprintAssetContributor(System.IO.Path.GetTempPath());
        var builder = new AiAssetCatalogBuilder(
            btree, hsm, bp, asm => btree.LoadFrom(asm), asm => hsm.LoadFrom(asm), () => { });

        builder.RefreshJsonContributors(AssetKind.BTree);   // ⛔ must not throw
        builder.RefreshJsonContributors(AssetKind.Hsm);
    }
}
