using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiComposition;

/// <summary>
/// ⭐⭐ <b>The per-host inputs to AI asset-catalog composition — everything the two hosts genuinely
/// differ on, and nothing else.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.19.
/// </summary>
public sealed record AiAssetCatalogOptions
{
    /// <summary>The project-path segments ruling 67's resolver walks. 📐 Measured identical in both
    /// hosts (<c>{"Subsystems","Hrot.AI.Behaviors","Hrot.AI.Behaviors.csproj"}</c>) — ⚠ still an input,
    /// because both expose it as a settable property.</summary>
    public required string[] ProjectPath { get; init; }

    /// <summary>⚠ <c>null</c> on a host that constructs no BTree debug session (CGF — slice 1 §9.4).
    /// ⛔ Not a silent default: symbolication is genuinely unavailable there, and the parameter exists
    /// so a host can SAY so.</summary>
    public Hrot.BTree.Editor.Debug.BTreeDebugSession? BTreeDebugSession { get; init; }

    /// <summary>⭐ Host log routing. The message BODY is the shared one so the two hosts cannot word
    /// the same fault differently; only the prefix/sink is theirs.</summary>
    public required Action<string> Info { get; init; }
    public required Action<string> Warn { get; init; }
}

/// <summary>
/// ⭐ What the host must keep: the builder, and the roots/contributors <c>CREATE</c> needs so a minted
/// asset lands in the SAME directory this catalog scans and refreshes the SAME contributor.
/// 🔒 That is the editor's <c>BUG-A6</c> and ruling 67's own failure mode.
/// </summary>
public sealed record AiAssetCatalogComposition
{
    public required AiAssetCatalogBuilder Builder { get; init; }
    public required string BlueprintRootDir { get; init; }
    public required string BTreeJsonRootDir { get; init; }
    public required string HsmJsonRootDir   { get; init; }
    public required Hrot.BTree.Editor.Catalog.BTreeJsonAssetContributor BTreeJsonContributor { get; init; }
    public required Hrot.Hsm.Editor.Catalog.HsmJsonAssetContributor     HsmJsonContributor   { get; init; }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-342</c> — ONE composition of the AI asset catalogue, for both hosts.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.19.
///
/// <para>⭐⭐ <b>Most of this path was ALREADY unified, and that matters for how the remainder reads.</b>
/// 📐 The `J1`/`J2` programme (<c>CE-091</c>/<c>093</c>/<c>095</c>/<c>098</c>) had already pushed the
/// hard parts into shared code: <see cref="AiAssetCatalogBuilder"/> itself, <c>AssetRoots.ResolveAssetsRoot</c>
/// (ruling 67's config → walk-up → output-dir chain), <c>AssetRoots.ReportBase</c>, and
/// <c>RefreshJsonContributors</c>. ⇒ ⛔ <b>what was left duplicated was the CONSTRUCTION — the
/// thirteen-argument call, the five contributors, and the field capture</b> — which is exactly the
/// residue that <c>Hrot.Editor.AiShared</c> could not absorb, because it cannot NAME the contributor
/// types (their projects reference it). 🔒 That reference wall is why the builder takes
/// <c>LoadFrom</c>/<c>Refresh</c> as delegates in the first place.</para>
///
/// <para>⭐ <b>This assembly is the first place that CAN hold it</b>, which is the whole reason
/// <c>CE-340</c> created it.</para>
///
/// <para>⚠ <b>What is deliberately NOT here: the Scenario contributor.</b> 📐 Measured — the editor
/// enumerates <c>IEditorLogic.AvailableScenarios</c> under <c>EditorBootstrap.ScenariosRoot</c>; CGF
/// enumerates relative paths under <c>OrchestrationConstants.GetSharedScenariosRoot()</c>. ⇒ 🔒 <b>a
/// genuine host difference</b>, so each host adds its own to <c>Builder.Catalog</c> afterwards rather
/// than passing a delegate that would make one look like the other.</para>
/// </summary>
public static class AiAssetCatalogComposer
{
    /// <summary>
    /// ⭐ Build the catalogue the way BOTH hosts need it, and hand back what each must keep.
    /// ⚠ The caller adds its own Scenario contributor (and any host-only contributor) afterwards.
    /// </summary>
    public static AiAssetCatalogComposition Compose(AiAssetCatalogOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        // ⭐ Ruling 67 / CE-098 — the shared reporting policy, routed to this host's log.
        AssetRoots.ReportBase(info: options.Info, warn: options.Warn, options.ProjectPath);

        // ⭐⭐ CE-093 — ALL THREE ROOTS FROM THE SAME RESOLVER. 🔴 The editor once resolved Blueprint
        //    through ResolveAssetsRoot and the two JSON roots through a bare walk-up, which answers
        //    null on a deployed node ⇒ a split brain INSIDE one host. Composing them together is what
        //    makes that class of drift unrepresentable.
        var bpRootDir     = AssetRoots.ResolveAssetsRoot(AssetKind.Blueprint, options.ProjectPath);
        var btreeJsonRoot = AssetRoots.ResolveAssetsRoot(AssetKind.BTree,     options.ProjectPath);
        var hsmJsonRoot   = AssetRoots.ResolveAssetsRoot(AssetKind.Hsm,       options.ProjectPath);

        // ⚠ The debug session reaches ONLY the two BTree contributors — that is what wires
        //   NodeDebugMetadata for symbolication, and it is the single per-host value here.
        var btreeContrib     = new Hrot.BTree.Editor.Catalog.BTreeAssetContributor(options.BTreeDebugSession);
        var hsmContrib       = new Hrot.Hsm.Editor.Catalog.HsmAssetContributor();
        var bpContrib        = new Hrot.Blueprints.Editor.Catalog.BlueprintAssetContributor(bpRootDir);
        var btreeJsonContrib = new Hrot.BTree.Editor.Catalog.BTreeJsonAssetContributor(options.BTreeDebugSession);
        var hsmJsonContrib   = new Hrot.Hsm.Editor.Catalog.HsmJsonAssetContributor();

        var builder = new AiAssetCatalogBuilder(
            btreeContrib,
            hsmContrib,
            bpContrib,
            asm => btreeContrib.LoadFrom(asm),
            asm => hsmContrib.LoadFrom(asm),
            ()  => bpContrib.Refresh(),
            bTreeJsonContributor: btreeJsonContrib,
            hsmJsonContributor:   hsmJsonContrib,
            // ⭐⭐ CE-091 — delegates, because AiAssetCatalogBuilder lives in AiShared and cannot name
            //    these contributor types. ⚠ The roots are known here and immutable, so unlike the old
            //    per-host code these closures need not defer to a field assigned later.
            bTreeJsonRefresh: root => btreeJsonContrib.Refresh(rootDirectory: root),
            bTreeJsonRootDir: () => btreeJsonRoot,
            hsmJsonRefresh:   root => hsmJsonContrib.Refresh(rootDirectory: root),
            hsmJsonRootDir:   () => hsmJsonRoot,
            warnMissingRoot:  options.Warn);

        // ⭐⭐ CE-095 — the initial JSON refresh is THE SAME call every later refresh makes.
        //    🔴 Both hosts once had an inline Directory.Exists + Refresh + Warn pair per kind, i.e. a
        //       second implementation of the policy RefreshJsonContributors owns.
        builder.RefreshJsonContributors(AssetKind.BTree);
        builder.RefreshJsonContributors(AssetKind.Hsm);

        return new AiAssetCatalogComposition
        {
            Builder              = builder,
            BlueprintRootDir     = bpRootDir,
            BTreeJsonRootDir     = btreeJsonRoot,
            HsmJsonRootDir       = hsmJsonRoot,
            BTreeJsonContributor = btreeJsonContrib,
            HsmJsonContributor   = hsmJsonContrib,
        };
    }
}
