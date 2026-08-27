using System.Reflection;

namespace Hrot.Editor.AiShared.Catalog;

/// <summary>
/// A composable helper that builds and owns a shared <see cref="AssetCatalog"/>
/// aggregating BTree, HSM, and Blueprint contributors.
/// <para>
/// The three contributors are injected as <see cref="IAssetCatalogContributor"/> instances.
/// Assembly-based refresh (BTree + HSM) is handled via delegate callbacks rather than
/// through the base interface, since <c>LoadFrom(Assembly)</c> is a concrete extension
/// method on the BTree/HSM contributors, not part of <see cref="IAssetCatalogContributor"/>.
/// </para>
/// <para>
/// Optional JSON contributors (<c>bTreeJsonContributor</c> / <c>hsmJsonContributor</c>)
/// implement the JSON half of the PU-301 dual-load strategy.  When provided they are
/// added to the catalog AFTER the assembly contributors so that JSON-loaded assets win
/// on AssetId collision (design §3 D4).
/// </para>
/// <para>
/// This class is intentionally <b>not</b> wired into
/// <c>EditorSubsystem</c> — that composition step is <c>AIE-015 / BATCH-03</c>.
/// It is constructed as a standalone, unit-testable helper.
/// </para>
/// </summary>
public sealed class AiAssetCatalogBuilder
{
    private readonly AssetCatalog _catalog = new();
    private readonly Action<Assembly> _bTreeLoadFrom;
    private readonly Action<Assembly> _hsmLoadFrom;
    private readonly Action _blueprintRefresh;

    // ⭐⭐ CE-091 (J2 K1) — the JSON refresh path this class's own doc has always promised.
    //    ⚠ Same shape as _bTreeLoadFrom above and for the same documented reason: `Refresh(rootDirectory:)`
    //    is a concrete method on the BTree/HSM json contributors, and those projects REFERENCE this
    //    assembly — so naming their types here would be a circular reference. A delegate is the only
    //    legal shape. 📄 DESIGN_Subsystem_Composition_Unification.md §5c.10.
    private readonly Action<string>? _bTreeJsonRefresh;
    private readonly Func<string?>?  _bTreeJsonRootDir;
    private readonly Action<string>? _hsmJsonRefresh;
    private readonly Func<string?>?  _hsmJsonRootDir;

    /// <summary>
    /// Initializes the builder.
    /// </summary>
    /// <param name="bTreeContributor">BTree assembly contributor to add to the catalog.</param>
    /// <param name="hsmContributor">HSM assembly contributor to add to the catalog.</param>
    /// <param name="blueprintContributor">Blueprint contributor to add to the catalog.</param>
    /// <param name="bTreeLoadFrom">
    ///   Callback that invokes <c>BTreeAssetContributor.LoadFrom(assembly)</c>.
    ///   Example: <c>asm => bTreeContrib.LoadFrom(asm)</c>.
    /// </param>
    /// <param name="hsmLoadFrom">
    ///   Callback that invokes <c>HsmAssetContributor.LoadFrom(assembly)</c>.
    ///   Example: <c>asm => hsmContrib.LoadFrom(asm)</c>.
    /// </param>
    /// <param name="blueprintRefresh">
    ///   Callback that invokes <c>BlueprintAssetContributor.Refresh()</c>.
    ///   Example: <c>() => bpContrib.Refresh()</c>.
    /// </param>
    /// <param name="bTreeJsonContributor">
    ///   Optional JSON-file BTree contributor (PU-301).  When provided it is added
    ///   AFTER <paramref name="bTreeContributor"/> so JSON wins on AssetId collision.
    /// </param>
    /// <param name="hsmJsonContributor">
    ///   Optional JSON-file HSM contributor (PU-301).  When provided it is added
    ///   AFTER <paramref name="hsmContributor"/> so JSON wins on AssetId collision.
    /// </param>
    /// <param name="bTreeJsonRefresh">
    ///   ⭐ Callback that invokes <c>BTreeJsonAssetContributor.Refresh(rootDirectory: root)</c>.
    ///   Example: <c>root =&gt; btreeJsonContrib.Refresh(rootDirectory: root)</c>.
    ///   ⚠ Optional because a host may have no JSON contributor at all; ⛔ but a host that HAS one must
    ///   pass this (the silent-default rule) or <see cref="RefreshJsonContributors"/> is inert for it.
    /// </param>
    /// <param name="bTreeJsonRootDir">Where that contributor reads from; a null/empty answer skips it.</param>
    /// <param name="hsmJsonRefresh">Symmetric to <paramref name="bTreeJsonRefresh"/>.</param>
    /// <param name="hsmJsonRootDir">Symmetric to <paramref name="bTreeJsonRootDir"/>.</param>
    public AiAssetCatalogBuilder(
        IAssetCatalogContributor bTreeContributor,
        IAssetCatalogContributor hsmContributor,
        IAssetCatalogContributor blueprintContributor,
        Action<Assembly> bTreeLoadFrom,
        Action<Assembly> hsmLoadFrom,
        Action blueprintRefresh,
        IAssetCatalogContributor? bTreeJsonContributor = null,
        IAssetCatalogContributor? hsmJsonContributor   = null,
        Action<string>? bTreeJsonRefresh = null,
        Func<string?>?  bTreeJsonRootDir = null,
        Action<string>? hsmJsonRefresh   = null,
        Func<string?>?  hsmJsonRootDir   = null)
    {
        _bTreeJsonRefresh = bTreeJsonRefresh;
        _bTreeJsonRootDir = bTreeJsonRootDir;
        _hsmJsonRefresh   = hsmJsonRefresh;
        _hsmJsonRootDir   = hsmJsonRootDir;
        _bTreeLoadFrom    = bTreeLoadFrom    ?? throw new ArgumentNullException(nameof(bTreeLoadFrom));
        _hsmLoadFrom      = hsmLoadFrom      ?? throw new ArgumentNullException(nameof(hsmLoadFrom));
        _blueprintRefresh = blueprintRefresh ?? throw new ArgumentNullException(nameof(blueprintRefresh));

        // Assembly contributors first — JSON contributors are added after and win collisions.
        _catalog.AddContributor(bTreeContributor      ?? throw new ArgumentNullException(nameof(bTreeContributor)));
        _catalog.AddContributor(hsmContributor        ?? throw new ArgumentNullException(nameof(hsmContributor)));
        _catalog.AddContributor(blueprintContributor  ?? throw new ArgumentNullException(nameof(blueprintContributor)));

        // JSON contributors added last: AssetCatalog.All() exposes all contributors;
        // consumers that resolve by AssetId should prefer later registrations
        // (EditorSubsystem iterates catalog.All and the document manager handles collision
        // via a dictionary that takes the last writer — JSON wins per design D4).
        if (bTreeJsonContributor != null)
            _catalog.AddContributor(bTreeJsonContributor);
        if (hsmJsonContributor != null)
            _catalog.AddContributor(hsmJsonContributor);
    }

    /// <summary>The shared, aggregated catalog managed by this builder.</summary>
    public AssetCatalog Catalog => _catalog;

    /// <summary>
    /// Refreshes all three assembly-based contributors from the given AI behaviors assembly.
    /// JSON contributors are refreshed separately via <see cref="RefreshJsonContributors"/>.
    /// <list type="bullet">
    ///   <item>Calls <c>LoadFrom(aiAssembly)</c> on the BTree contributor.</item>
    ///   <item>Calls <c>LoadFrom(aiAssembly)</c> on the HSM contributor.</item>
    ///   <item>Calls <c>Refresh()</c> on the Blueprint contributor.</item>
    /// </list>
    /// Each contributor fires <see cref="IAssetCatalogContributor.ContributorChanged"/>,
    /// which in turn causes <see cref="AssetCatalog.Changed"/> to fire once per contributor.
    /// </summary>
    /// <param name="aiAssembly">The loaded <c>Hrot.AI.Behaviors.dll</c> (or a test-substitute).</param>
    public void RefreshFromAssembly(Assembly aiAssembly)
    {
        if (aiAssembly is null) throw new ArgumentNullException(nameof(aiAssembly));
        _bTreeLoadFrom(aiAssembly);
        _hsmLoadFrom(aiAssembly);
        _blueprintRefresh();
    }

    /// <summary>
    /// ⭐⭐⭐ Re-reads the JSON-backed contributor for <paramref name="kind"/> from its root directory.
    ///
    /// <para>⚠⚠ <b>This method is NEW as of <c>CE-091</c>, and it is the one
    /// <see cref="RefreshFromAssembly"/>'s summary has referenced since this class was written.</b>
    /// 📐 Measured: the <c>&lt;see cref&gt;</c> pointed at nothing, and BOTH composition roots had
    /// hand-rolled the same six-line kind-dispatch lambda in its place. ⇒ ⭐ the policy — which kind maps
    /// to which contributor, and skipping when its root is unset — now lives here ONCE.</para>
    ///
    /// <para>⛔ A kind with no JSON contributor is a NO-OP, deliberately: Blueprint has none, and the two
    /// AI kinds only have one when the host wired it. ⚠ That is absence-by-construction, not a swallowed
    /// error — the caller cannot supply a refresh for a contributor it never built.</para>
    /// </summary>
    /// <param name="kind">The asset kind whose JSON contributor should re-scan.</param>
    public void RefreshJsonContributors(AssetKind kind)
    {
        switch (kind)
        {
            case AssetKind.BTree: Invoke(_bTreeJsonRefresh, _bTreeJsonRootDir); break;
            case AssetKind.Hsm:   Invoke(_hsmJsonRefresh,   _hsmJsonRootDir);   break;
        }

        // ⚠ The root is resolved AT CALL TIME, not captured: the hosts' own lambdas read a field that is
        //   assigned during Initialize, so resolving eagerly here would freeze a null.
        static void Invoke(Action<string>? refresh, Func<string?>? rootDir)
        {
            if (refresh == null) return;
            var root = rootDir?.Invoke();
            if (!string.IsNullOrEmpty(root)) refresh(root!);
        }
    }
}
