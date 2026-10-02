using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hrot.Editor.AiShared.Recipes;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-049</c> (Axis-C <b>E2</b>) — THE ONE CREATE-CORE. Ruling 9.</b>
/// 📄 <b><c>docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md</c></b> §2 *(the inventory row that found the
/// duplicate)* / §3 ② / §4. Also <c>Architect_Question_57</c> *(<c>MA-019</c>…<c>MA-023</c>)* and
/// <c>docs/DESIGN_Mcp_Authoring.md</c> §7 ③ *(<c>AQ56</c>/<c>MA-001</c> — the create path has TWO surfaces
/// and ONE implementation)*.
///
/// <para>🔴🔴 <b>What this replaces: TWO near-verbatim copies of the same eight composition facts.</b>
/// 📐 Measured <c>2026-08-26</c>:
/// <list type="bullet">
///   <item><c>EditorSubsystem.CreateAssetCore</c> — a local function closed over ~8 host fields.</item>
///   <item><c>CgfSubsystem.AssetShellCreate</c> — the same body, re-derived, with the kind-parse and
///     recipe-resolve fused into it.</item>
/// </list>
/// ⚠⚠ <b>And they had already DRIFTED in three places</b>, which is the argument for this type rather
/// than the argument against it: the editor branched early for non-document kinds and CGF did not; the
/// editor wrapped the Blueprint mint-write in a <c>try/catch</c> and CGF did not; and their
/// *"not in the catalog"* messages differed — CGF's named the actual remedy
/// *("pass <c>--asset-root</c> on a deployed node")* and the editor's did not.</para>
///
/// <para>⭐⭐ <b>Why the body cannot just be *"call <c>CreateNew</c>"</b> — this is the reason a third copy
/// would be wrong rather than merely untidy. It encodes FOUR facts a re-derivation gets wrong:
/// <list type="number">
///   <item>Blueprint is <b>mint-only</b> and needs its file written at the host's <b>SOURCE</b> root
///     *(<c>BUG-A6</c>: the dir the contributor scans, ⛔ not <c>bin/</c>)*.</item>
///   <item><c>RefreshFromAssembly</c> refreshes <b>only</b> the assembly contributors.</item>
///   <item>⇒ the JSON contributors must be refreshed <b>separately, per kind</b>, or a just-written
///     <c>.btree.json</c> stays undiscovered.</item>
///   <item>⛔ Only <b>then</b> does <c>FindByAssetId</c> succeed — so the minted id is returned
///     <b>only once the catalog can resolve it</b> *(<c>MA-004</c>)*. ⚠ Returning it earlier hands the
///     caller an id <c>GET /assets</c> cannot find: the silent-wrong-answer shape.</item>
/// </list></para>
///
/// <para>⭐ <b>TWO surfaces, ONE body:</b> <see cref="Create"/> is the typed path *(the New-Asset dialog)*
/// and <see cref="CreateByName"/> is the string path *(<c>POST /assets</c> over MCP)*. ⛔ The string path
/// is a parse-and-resolve wrapper only — it adds no create logic of its own.</para>
/// </summary>
public sealed class AssetCreateController
{
    /// <summary>The assembly whose contributors carry compiled-in AI assets.</summary>
    private const string AiBehaviorsAssemblyName = "Hrot.AI.Behaviors";

    private readonly IReadOnlyDictionary<AssetKind, INewAssetService> _services;
    private readonly Action<IEditableAsset, string>                   _saveMintOnlyAsset;
    private readonly Func<Guid, IEditableAsset?>                      _findCatalogued;
    private readonly Action<Assembly>                                 _refreshFromAssembly;
    private readonly Action<AssetKind>                                _refreshJsonContributor;
    private readonly Action<IEditableAsset>                           _openDocument;
    private readonly Func<string?>                                    _blueprintRootDir;

    /// <param name="services">
    /// The host's per-kind registry. ⚠ <b>Which kinds are present is a per-host FACT, not a shortfall</b>
    /// — CGF composes Blueprint/BTree/Hsm and deliberately no Scenario *(its service needs a session
    /// adapter CGF had no equivalent of)*. ⇒ an unregistered kind is refused with an explanation.
    /// </param>
    /// <param name="saveMintOnlyAsset">
    /// Writes a mint-only asset *(Blueprint)* to a path. ⭐ A delegate because the unwrap
    /// *(<c>BlueprintEditableAssetAdapter</c> → <c>BlueprintAsset</c>)* lives in the per-kind editor
    /// assembly, which this shared assembly does not reference.
    /// </param>
    /// <param name="findCatalogued">Resolves a minted id in the host's catalog; <c>null</c> until it is discoverable.</param>
    /// <param name="refreshFromAssembly">Refreshes the host's assembly-based catalog contributors.</param>
    /// <param name="refreshJsonContributor">
    /// Refreshes the host's JSON contributor for one kind. ⚠ <b>Separate from
    /// <paramref name="refreshFromAssembly"/> on purpose</b> — see fact ② in the class remarks; collapsing
    /// them is <c>BUG-A6</c>.
    /// </param>
    /// <param name="openDocument">Opens the catalogued asset in the host's document manager.</param>
    /// <param name="blueprintRootDir">
    /// The host's Blueprint SOURCE root, as a delegate so a late-resolved root is honoured.
    /// <c>null</c> ⇒ <see cref="AssetSavePath"/>'s default root.
    /// </param>
    public AssetCreateController(
        IReadOnlyDictionary<AssetKind, INewAssetService> services,
        Action<IEditableAsset, string>                   saveMintOnlyAsset,
        Func<Guid, IEditableAsset?>                      findCatalogued,
        Action<Assembly>                                 refreshFromAssembly,
        Action<AssetKind>                                refreshJsonContributor,
        Action<IEditableAsset>                           openDocument,
        Func<string?>?                                   blueprintRootDir = null)
    {
        _services               = services               ?? throw new ArgumentNullException(nameof(services));
        _saveMintOnlyAsset      = saveMintOnlyAsset      ?? throw new ArgumentNullException(nameof(saveMintOnlyAsset));
        _findCatalogued         = findCatalogued         ?? throw new ArgumentNullException(nameof(findCatalogued));
        _refreshFromAssembly    = refreshFromAssembly    ?? throw new ArgumentNullException(nameof(refreshFromAssembly));
        _refreshJsonContributor = refreshJsonContributor ?? throw new ArgumentNullException(nameof(refreshJsonContributor));
        _openDocument           = openDocument           ?? throw new ArgumentNullException(nameof(openDocument));
        _blueprintRootDir       = blueprintRootDir       ?? (() => null);
    }

    /// <summary>The kinds this host can actually create — what <c>GET /assets/recipes</c> should offer.</summary>
    public IEnumerable<AssetKind> SupportedKinds => _services.Keys;

    /// <summary>
    /// ⭐⭐ <b>The STRING surface</b> — <c>POST /assets</c>. Parses the kind, resolves the recipe by name
    /// from the kind's own <c>AvailableRecipes()</c>, then runs <see cref="Create"/>.
    ///
    /// <para>⛔ <b>An unmatched recipe name is REFUSED, with the available names</b> — ⚠ falling back to
    /// the blank template would create something other than what was asked for, which is the
    /// silent-wrong-answer shape <c>MA-004</c> and <c>MA-017</c> both caught.</para>
    /// </summary>
    public (Guid? AssetId, string Status) CreateByName(
        string kindText, string name, string relPath, string? recipeName)
    {
        if (!Enum.TryParse<AssetKind>(kindText, ignoreCase: true, out var kind))
            return (null, $"[ERROR] '{kindText}' is not an AssetKind. Use BTree, Hsm or Blueprint.");

        if (!_services.TryGetValue(kind, out var service))
            return (null, $"[ERROR] This host composes no INewAssetService for {kind}.");

        var (recipe, recipeError) = RecipeByName.Resolve(service, recipeName);
        if (recipeError != null) return (null, recipeError);

        return Create(kind, recipe, name, relPath ?? string.Empty);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The TYPED surface, and the only place the create sequence exists.</b>
    /// See the class remarks for why each step is load-bearing.
    /// </summary>
    public (Guid? AssetId, string Status) Create(
        AssetKind kind, IEditableAsset? recipe, string name, string relPath)
    {
        if (!_services.TryGetValue(kind, out var service))
            return (null, $"[ERROR] This host composes no INewAssetService for {kind}.");

        var minted = service.CreateNew(recipe, name, relPath);

        // ① Blueprint is mint-only — write its file at the chosen folder under the host's SOURCE root
        //    (BUG-A6). BTree/HSM/Scenario persist inside CreateNew.
        if (kind == AssetKind.Blueprint)
        {
            var bpPath = AssetSavePath.Compose(
                AssetKind.Blueprint, relPath, name,
                assetRootOverride: _blueprintRootDir());
            _saveMintOnlyAsset(minted, bpPath);
        }

        // ⛔ Non-document kinds have nothing to refresh or open — the asset IS the create result.
        //    ⚠ Unreachable on a host that registers only the three document kinds; kept because the
        //    EDITOR's core had this branch and dropping it would be a silent behaviour change there.
        if (kind is not (AssetKind.Blueprint or AssetKind.BTree or AssetKind.Hsm))
            return (minted.AssetId, $"[OK] Created {kind}: '{minted.Name}'.");

        // ② the assembly contributors…
        var aiAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == AiBehaviorsAssemblyName);
        if (aiAsm != null) _refreshFromAssembly(aiAsm);

        // ③ …then THIS kind's JSON contributor, separately, or the file stays undiscovered.
        _refreshJsonContributor(kind);

        // ④ and only now is the id addressable.
        var catalogued = _findCatalogued(minted.AssetId);
        if (catalogued == null)
            return (null,
                    $"[INFO] Created '{minted.Name}', but it is not in the catalog. The file was written "
                  + "outside the directory this host's contributor scans, so nothing can address it — "
                  + "check the asset roots (ruling 67: pass --asset-root on a deployed node).");

        _openDocument(catalogued);
        return (catalogued.AssetId, $"[OK] Created {kind}: '{minted.Name}'.");
    }
}
