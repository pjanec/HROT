using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Recipes;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Blueprint implementation of <see cref="INewAssetService"/>.
/// Creates new in-memory Blueprint assets from recipes (via
/// <see cref="NewFromRecipeService"/>) or one of the built-in blank templates
/// (see <see cref="BlankTemplates"/>).
/// </summary>
public sealed class BlueprintNewAssetService : INewAssetService
{
    /// <summary>
    /// One row per built-in blank-template recipe the "New Blueprint" picker offers.
    /// This is the dispatch choice (BP-92): rather than a two-way toggle, each offered
    /// <see cref="BlueprintDispatchKind"/> gets its own blank-template recipe entry —
    /// exactly how Unreal offers "Blueprint Class / Function Library / Macro Library"
    /// as separate create-asset entries. A fourth row (MacroLibrary) slots in here
    /// without a data migration, per docs/blueprints/Architect_Question_25_Macros.md —
    /// that is why this is a table and not a bool.
    ///
    /// ⭐ CE-461 — AiPrimitive IS offered now, as the Action and Condition rows: the Primitive declaration
    /// and its hostings are populated from the row's intent (see <see cref="MakeEmptyBlueprint"/>).
    ///
    /// <para>
    /// BP-103 — <c>SeedGraphName</c> is the one starter Function graph every blank-template
    /// asset is minted with (see <see cref="MakeEmptyBlueprint"/>). A graphless asset crashes
    /// <see cref="BlueprintDocumentFactory.Build"/> on open and, once written to disk, bricks
    /// <c>dotnet build</c> via BP5001 (<c>LibraryLowering</c> requires >=1 Function graph for a
    /// Library asset). "Tick" for the Instance template is not arbitrary: InstanceEmitter.EmitTickMethod
    /// selects the tick graph by <c>Kind == IrGraphKind.Function &amp;&amp; Name == "Tick"</c>
    /// (falling back to the first Function graph) — a graph named "Tick" of <c>Event</c> kind would
    /// NOT match and the new blueprint would silently never tick. "NewFunction" for Library is forced
    /// by BP5001 itself.
    /// </para>
    /// </summary>
    private readonly record struct BlankTemplateRow(
        string Name, BlueprintDispatchKind Dispatch, string Description, string SeedGraphName,
        AiPrimitiveIntent? Intent = null);

    private static readonly BlankTemplateRow[] BlankTemplates =
    {
        new("Empty",
            BlueprintDispatchKind.Instance,
            "Start from scratch with an empty blueprint. Runs on an entity instance; graphs may contain latent nodes such as Delay.",
            SeedGraphName: "Tick"),
        new("Function Library",
            BlueprintDispatchKind.Library,
            "A shared library of pure Functions, callable from any other blueprint. Compiles to static methods, so its graphs cannot contain latent nodes such as Delay.",
            SeedGraphName: "NewFunction"),
        // ⭐ CE-460 (E4 ②) — a blueprint BEHAVIOUR (Q77 §5.9): the brain ticks its "Tick" graph each frame
        //   and a Return finishes it. "Tick" for the same reason as the Instance row — the emitter selects
        //   the tick graph by name. Listed under File / New Behavior… AND under New Asset / Blueprint.
        new("Behavior",
            BlueprintDispatchKind.Behavior,
            "A behaviour an entity runs, implemented as a blueprint: its Tick graph runs every frame until a Return node finishes it. May contain latent nodes such as Delay.",
            SeedGraphName: "Tick"),
        // ⭐⭐ CE-461 (E4 ③) — an AiPrimitive per intent. 🔒 User, 2026-09-30: an action is usable by BTrees,
        //   HSMs AND blueprint behaviours; a condition as a BTree condition AND an HSM guard ⇒ the Primitive
        //   declares EVERY hosting valid for its intent, READ from the compiler's AiPrimitiveHostingRules (the
        //   table BP1022/BP1023 enforce) — ⛔ never a second copy here. Narrowing is an asset property.
        //   "Main" matches the shipped primitives; the emitter takes the first Function graph either way.
        new("Action",
            BlueprintDispatchKind.AiPrimitive,
            "An action, implemented as a blueprint: usable from behaviour trees, HSMs and blueprint behaviours. Its Main graph runs when the action is invoked; a Return reports Success or Failure.",
            SeedGraphName: "Main",
            Intent: AiPrimitiveIntent.Action),
        new("Condition",
            BlueprintDispatchKind.AiPrimitive,
            "A condition, implemented as a blueprint: usable as a behaviour-tree condition, an HSM guard and from blueprint behaviours. Its Main graph returns Success (true) or Failure (false) and must not contain latent nodes.",
            SeedGraphName: "Main",
            Intent: AiPrimitiveIntent.Condition),
    };

    private readonly NewFromRecipeService _newFromRecipeService = new();
    private readonly BlueprintEditableAssetAdapter[] _blankTemplateRecipes;

    public BlueprintNewAssetService()
    {
        _blankTemplateRecipes = new BlueprintEditableAssetAdapter[BlankTemplates.Length];
        for (int i = 0; i < BlankTemplates.Length; i++)
        {
            var row   = BlankTemplates[i];
            var asset = MakeEmptyBlueprint(row.Dispatch, row.Name, row.SeedGraphName, row.Intent);
            // The recipe entry in AvailableRecipes carries recipe metadata.
            asset.EditorMetadata.Recipe = new Core.Assets.RecipeMetadata
            {
                DisplayName = row.Name,
                Description = row.Description,
            };
            _blankTemplateRecipes[i] = new BlueprintEditableAssetAdapter(asset);
        }
    }

    public AssetKind Kind => AssetKind.Blueprint;

    /// <inheritdoc />
    public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must not be empty.", nameof(name));

        BlueprintAsset newAsset;

        if (recipe == null)
        {
            // Null recipe preserves today's default behaviour: the first table row (Instance).
            newAsset = MakeEmptyBlueprint(BlankTemplates[0].Dispatch, name, BlankTemplates[0].SeedGraphName);
            newAsset.AssetId = Guid.NewGuid();
        }
        else if (TryGetBlankTemplateRow(recipe, out var row))
        {
            newAsset = MakeEmptyBlueprint(row.Dispatch, name, row.SeedGraphName, row.Intent);
            newAsset.AssetId = Guid.NewGuid();
        }
        else
        {
            var bpRecipe = ExtractBlueprintAsset(recipe);
            newAsset = _newFromRecipeService.CreateFromRecipe(bpRecipe, name);
        }

        // SourceFilePath may be set to a non-empty value by file-writing phases.
        return new BlueprintEditableAssetAdapter(newAsset);
    }

    /// <inheritdoc />
    public IReadOnlyList<IEditableAsset> AvailableRecipes()
    {
        var recipes = new List<IEditableAsset>(_blankTemplateRecipes);

        foreach (var bpRecipe in BlueprintEditorBootstrap.DiscoverRecipes())
        {
            recipes.Add(new BlueprintEditableAssetAdapter(bpRecipe));
        }

        return recipes;
    }

    /// <inheritdoc />
    public bool IsBlankTemplate(IEditableAsset recipe)
        => TryGetBlankTemplateRow(recipe, out _);

    /// <summary>
    /// ⭐ <c>CE-460</c> (E4) — a blueprint's product is read off the ASSET, so a blank template and a
    /// content recipe from disk answer the same way: <c>Dispatch=Behavior</c> ⇒ Behavior; an AiPrimitive ⇒
    /// its declared intent. ⛔ Instance and Library blueprints are none of the three and stay New-Asset-only.
    /// </summary>
    public AuthoringProduct? ProductOf(IEditableAsset recipe)
    {
        if (recipe is not BlueprintEditableAssetAdapter { Asset: { } asset })
            return null;

        return asset.Dispatch switch
        {
            BlueprintDispatchKind.Behavior => AuthoringProduct.Behavior,
            BlueprintDispatchKind.AiPrimitive => asset.Primitive?.Intent switch
            {
                AiPrimitiveIntent.Action    => AuthoringProduct.Action,
                AiPrimitiveIntent.Condition => AuthoringProduct.Condition,
                _                           => null,
            },
            _ => null,
        };
    }

    /// <summary>
    /// Returns true (and the matching <see cref="BlankTemplateRow"/>) when <paramref name="recipe"/>
    /// is exactly one of the cached built-in blank-template instances that <see cref="AvailableRecipes"/>
    /// returned — matched by <see cref="IEditableAsset.AssetId"/>, not by name, since the picker
    /// hands back the very instances this service created.
    /// </summary>
    private bool TryGetBlankTemplateRow(IEditableAsset recipe, out BlankTemplateRow row)
    {
        for (int i = 0; i < _blankTemplateRecipes.Length; i++)
        {
            if (_blankTemplateRecipes[i].AssetId == recipe.AssetId)
            {
                row = BlankTemplates[i];
                return true;
            }
        }

        row = default;
        return false;
    }

    private static BlueprintAsset ExtractBlueprintAsset(IEditableAsset recipe)
    {
        if (recipe is BlueprintEditableAssetAdapter adapter)
            return adapter.Asset;

        throw new ArgumentException(
            $"Recipe must be a {nameof(BlueprintEditableAssetAdapter)} wrapping a BlueprintAsset.",
            nameof(recipe));
    }

    /// <summary>
    /// Synthesizes a minimal valid BlueprintAsset in code — no disk read, no file I/O.
    /// The returned asset has no recipe metadata; callers that use this as a blank-template
    /// recipe entry (the constructor) add it afterwards.
    ///
    /// <para>
    /// BP-103 — "minimal valid" must include at least one graph: <see cref="BlueprintDocumentFactory.Build"/>
    /// throws <see cref="InvalidOperationException"/> on a graphless asset (crashes on open), and an
    /// empty <c>Library</c> fails compilation with BP5001. Seeds a Function graph named
    /// <paramref name="seedGraphName"/> via <see cref="BlueprintDocumentFactory.CreateFunctionGraph"/>
    /// — the same path production "Create Function" uses — with <c>markDirty: null, view: null</c> so
    /// the append is direct, no undo entry.
    /// </para>
    /// </summary>
    private static BlueprintAsset MakeEmptyBlueprint(
        BlueprintDispatchKind dispatch, string name, string seedGraphName, AiPrimitiveIntent? intent = null)
    {
        var asset = new BlueprintAsset
        {
            Header         = new Header(),
            AssetId        = Guid.NewGuid(),
            Name           = name,
            Dispatch       = dispatch,
            EditorMetadata = new AssetMetadata(),
            // ⭐ CE-461 — BP1020 requires the block, BP1021 at least one hosting; the hostings are the
            //   compiler's own "every hosting valid for this intent".
            Primitive      = intent is { } i
                ? new AiPrimitiveDecl { Intent = i, Hostings = AiPrimitiveHostingRules.AllValidFor(i).ToList() }
                : null,
        };

        // seedGraphName is always one of the two hard-coded, valid-identifier names in
        // BlankTemplates ("Tick" / "NewFunction") against a freshly-minted, graphless asset — a
        // null return here would mean CreateFunctionGraph's own validity/collision rules regressed,
        // not a legitimate empty-asset outcome. Fail loudly rather than silently ship the exact
        // graphless asset this fix exists to prevent.
        var graph = BlueprintDocumentFactory.CreateFunctionGraph(
            asset, seedGraphName, markDirty: null, view: null);
        if (graph is null)
            throw new InvalidOperationException(
                $"BP-103: failed to seed starter graph '{seedGraphName}' for blank-template asset " +
                $"'{name}' (dispatch={dispatch}). CreateFunctionGraph rejected the name — this should " +
                "be impossible for a fresh, graphless asset and a hard-coded valid identifier.");

        return asset;
    }
}
