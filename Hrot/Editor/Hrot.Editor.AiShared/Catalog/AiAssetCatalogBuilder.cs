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

    /// <summary>
    /// Initializes the builder.
    /// </summary>
    /// <param name="bTreeContributor">BTree contributor to add to the catalog.</param>
    /// <param name="hsmContributor">HSM contributor to add to the catalog.</param>
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
    public AiAssetCatalogBuilder(
        IAssetCatalogContributor bTreeContributor,
        IAssetCatalogContributor hsmContributor,
        IAssetCatalogContributor blueprintContributor,
        Action<Assembly> bTreeLoadFrom,
        Action<Assembly> hsmLoadFrom,
        Action blueprintRefresh)
    {
        _bTreeLoadFrom    = bTreeLoadFrom    ?? throw new ArgumentNullException(nameof(bTreeLoadFrom));
        _hsmLoadFrom      = hsmLoadFrom      ?? throw new ArgumentNullException(nameof(hsmLoadFrom));
        _blueprintRefresh = blueprintRefresh ?? throw new ArgumentNullException(nameof(blueprintRefresh));

        _catalog.AddContributor(bTreeContributor ?? throw new ArgumentNullException(nameof(bTreeContributor)));
        _catalog.AddContributor(hsmContributor   ?? throw new ArgumentNullException(nameof(hsmContributor)));
        _catalog.AddContributor(blueprintContributor ?? throw new ArgumentNullException(nameof(blueprintContributor)));
    }

    /// <summary>The shared, aggregated catalog managed by this builder.</summary>
    public AssetCatalog Catalog => _catalog;

    /// <summary>
    /// Refreshes all three contributors from the given AI behaviors assembly.
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
}
