using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Recipes;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Recipes;

// ─────────────────────────────────────────────────────────────────────────────
// MTB-P6-T5 — NewAssetDialog headless tests
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for <see cref="NewAssetDialog"/> model logic: confirm, collision guard,
/// callback, DEC-12 per-kind reconciliation, and the CanConfirm seam.
/// </summary>
public sealed class NewAssetDialogTests
{
    // ── Fakes ───────────────────────────────────────────────────────────────

    /// <summary>Minimal fake asset for headless tests.</summary>
    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public AssetKind Kind { get; init; }
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    /// <summary>
    /// Fake <see cref="INewAssetService"/> that records calls and returns
    /// predictable assets. Does not touch the filesystem.
    /// </summary>
    private sealed class FakeNewAssetService : INewAssetService
    {
        public AssetKind Kind { get; }
        public IEditableAsset? LastCreatedRecipe { get; private set; }
        public string? LastCreatedName { get; private set; }
        public string? LastCreatedRelPath { get; private set; }
        public int CreateCallCount { get; private set; }

        private readonly List<IEditableAsset> _recipes;

        public FakeNewAssetService(AssetKind kind, IEnumerable<IEditableAsset>? recipes = null)
        {
            Kind = kind;
            _recipes = recipes?.ToList() ?? new List<IEditableAsset>();
        }

        public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
        {
            CreateCallCount++;
            LastCreatedRecipe = recipe;
            LastCreatedName = name;
            LastCreatedRelPath = relPath;
            return new FakeAsset
            {
                AssetId = Guid.NewGuid(),
                Name = name,
                Kind = Kind,
                SourceFilePath = string.IsNullOrEmpty(relPath) ? "" : relPath + "/" + name,
            };
        }

        public IReadOnlyList<IEditableAsset> AvailableRecipes()
        {
            if (_recipes.Count > 0)
                return _recipes;

            // Default: at least the "Empty" recipe.
            return new List<IEditableAsset>
            {
                new FakeAsset { Name = "Empty", Kind = Kind },
            };
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static NewAssetDialog MakeDialog(
        AssetKind kind,
        IEnumerable<INewAssetService>? extraServices = null,
        IEnumerable<string>? knownFolders = null,
        Func<string, IEnumerable<string>>? fileLister = null,
        Action<IEditableAsset, string>? saveMintAsset = null)
    {
        var services = new Dictionary<AssetKind, INewAssetService>();
        // Default services for file-based kinds.
        foreach (var svc in extraServices ?? Array.Empty<INewAssetService>())
            services[svc.Kind] = svc;

        return new NewAssetDialog(services, knownFolders, fileLister, saveMintAsset)
        {
            Kind = kind,
        };
    }

    private static NewAssetDialog MakeDialogWithService(
        INewAssetService service,
        IEnumerable<string>? knownFolders = null,
        Func<string, IEnumerable<string>>? fileLister = null,
        Action<IEditableAsset, string>? saveMintAsset = null,
        string? assetRootOverride = null)
    {
        var dict = new Dictionary<AssetKind, INewAssetService>
        {
            [service.Kind] = service,
        };
        return new NewAssetDialog(dict, knownFolders, fileLister, saveMintAsset, assetRootOverride)
        {
            Kind = service.Kind,
        };
    }

    // ── Test 1: Confirm_WritesFile_AtAssetsRootRelPath_WithFreshId ─────────

    /// <summary>
    /// Uses a real BTreeNewAssetService pointed at a temp root.
    /// Confirm with kind=BTree, recipe=Empty, name="Patrol", relPath="combat/Guard"
    /// must create the file at <c>tempRoot/combat/Guard/Patrol.btree.json</c>
    /// with a fresh (non-empty) <see cref="AssetId"/>.
    /// </summary>
    [Fact]
    public void Confirm_WritesFile_AtAssetsRootRelPath_WithFreshId()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B19_T5_FileWrite_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var svc = new BTreeNewAssetService(tempRoot);
            var dialog = MakeDialogWithService(svc,
                knownFolders: new[] { "combat", "combat/Guard" },
                fileLister: _ => Array.Empty<string>(),
                assetRootOverride: tempRoot);

            dialog.Kind = AssetKind.BTree;
            dialog.Recipe = svc.AvailableRecipes().First(r => r.Name == "Empty");
            dialog.Name = "Patrol";
            dialog.FolderPicker.SelectedRelPath = "combat/Guard";

            IEditableAsset? callbackAsset = null;
            var result = dialog.Confirm(a => callbackAsset = a);

            // Success
            Assert.True(result.IsSuccess, result.Error ?? "Expected success");
            Assert.NotNull(result.Asset);
            Assert.NotNull(callbackAsset);
            Assert.NotEqual(Guid.Empty, result.Asset!.AssetId);

            // File exists at the expected relative path.
            var expectedFile = Path.Combine(tempRoot, "combat", "Guard", "Patrol.btree.json");
            Assert.True(File.Exists(expectedFile));

            // Verify the file contains valid JSON with the fresh id.
            var json = File.ReadAllText(expectedFile);
            var deserialized = BTreeJsonServices.Deserialize(json);
            Assert.NotNull(deserialized);
            Assert.Equal(result.Asset.AssetId, deserialized!.AssetId);
            Assert.Equal("Patrol", deserialized.Name);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    // ── Test 2: CollisionGuard_RejectsExistingBaseName ──────────────────────

    /// <summary>
    /// The directory already contains a <c>.cs</c> file with the same logical
    /// base name as the asset being created → the collision guard rejects it.
    /// No file is written; the error is surfaced.
    /// </summary>
    [Fact]
    public void CollisionGuard_RejectsExistingBaseName()
    {
        // Simulate: the target directory contains "Patrol.cs".
        var fileLister = new Func<string, IEnumerable<string>>(dir =>
            new[] { Path.Combine(dir, "Patrol.cs"), Path.Combine(dir, "Other.btree.json") });

        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialogWithService(fakeService,
            fileLister: fileLister);

        dialog.Kind = AssetKind.BTree;
        dialog.Recipe = fakeService.AvailableRecipes().First();
        dialog.Name = "Patrol";
        dialog.FolderPicker.SelectedRelPath = "";

        bool callbackCalled = false;
        var result = dialog.Confirm(_ => callbackCalled = true);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("[D5]", result.Error);
        Assert.Contains("Patrol", result.Error);
        Assert.False(callbackCalled, "Callback must not be called on collision failure.");
        Assert.Equal(0, fakeService.CreateCallCount);
    }

    /// <summary>
    /// Same-base-name collision in the CS→JSON direction (creating a .cs file
    /// when a JSON file exists would also hit the guard). We test the
    /// JSON→CS direction (which is the dialog's path).
    /// </summary>
    [Fact]
    public void CollisionGuard_RejectsExistingBaseName_WhenCsExistsInSubfolder()
    {
        var fileLister = new Func<string, IEnumerable<string>>(dir =>
            new[] { Path.Combine(dir, "Guard.cs") });

        var fakeService = new FakeNewAssetService(AssetKind.Hsm);
        var dialog = MakeDialogWithService(fakeService,
            knownFolders: new[] { "machines" },
            fileLister: fileLister);

        dialog.Kind = AssetKind.Hsm;
        dialog.Recipe = fakeService.AvailableRecipes().First();
        dialog.Name = "Guard";
        dialog.FolderPicker.SelectedRelPath = "machines";

        var result = dialog.Confirm();

        Assert.False(result.IsSuccess);
        Assert.Contains("Guard", result.Error!);
        Assert.Equal(0, fakeService.CreateCallCount);
    }

    // ── Test 3: Callback_ReceivesNewAsset ───────────────────────────────────

    /// <summary>
    /// On successful Confirm, the callback receives the newly minted asset
    /// with the correct Kind, Name, and a fresh AssetId.
    /// </summary>
    [Fact]
    public void Callback_ReceivesNewAsset()
    {
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialogWithService(fakeService,
            fileLister: _ => Array.Empty<string>());

        dialog.Kind = AssetKind.BTree;
        dialog.Recipe = fakeService.AvailableRecipes().First();
        dialog.Name = "MyNewAsset";
        dialog.FolderPicker.SelectedRelPath = "";

        IEditableAsset? callbackAsset = null;
        var result = dialog.Confirm(a => callbackAsset = a);

        Assert.True(result.IsSuccess);
        Assert.NotNull(callbackAsset);
        Assert.Equal("MyNewAsset", callbackAsset!.Name);
        Assert.Equal(AssetKind.BTree, callbackAsset.Kind);
        Assert.NotEqual(Guid.Empty, callbackAsset.AssetId);
        Assert.Equal(1, fakeService.CreateCallCount);
    }

    /// <summary>
    /// Callback receives the correct Name when using a nested relPath.
    /// </summary>
    [Fact]
    public void Callback_ReceivesNewAsset_WithNestedRelPath()
    {
        var fakeService = new FakeNewAssetService(AssetKind.Blueprint);
        var saveCalledPath = (string?)null;
        IEditableAsset? saveCalledAsset = null;

        var dialog = MakeDialogWithService(fakeService,
            knownFolders: new[] { "combat", "combat/Guard" },
            fileLister: _ => Array.Empty<string>(),
            saveMintAsset: (a, p) => { saveCalledAsset = a; saveCalledPath = p; });

        dialog.Kind = AssetKind.Blueprint;
        dialog.Recipe = fakeService.AvailableRecipes().First();
        dialog.Name = "SniperBrain";
        dialog.FolderPicker.SelectedRelPath = "combat/Guard";

        IEditableAsset? callbackAsset = null;
        var result = dialog.Confirm(a => callbackAsset = a);

        Assert.True(result.IsSuccess, result.Error ?? "Expected success");
        Assert.NotNull(callbackAsset);
        Assert.Equal("SniperBrain", callbackAsset!.Name);
        Assert.Equal(AssetKind.Blueprint, callbackAsset.Kind);
        Assert.NotEqual(Guid.Empty, callbackAsset.AssetId);

        // DEC-12: Blueprint save delegate was called with the path.
        Assert.NotNull(saveCalledAsset);
        Assert.NotNull(saveCalledPath);
        Assert.EndsWith(Path.Combine("combat", "Guard", "SniperBrain.bp.json"), saveCalledPath!);
        Assert.Equal(1, fakeService.CreateCallCount);
    }

    // ── CanConfirm seam ───────────────────────────────────────────────────

    [Fact]
    public void CanConfirm_AllSet_ReturnsTrue()
    {
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialogWithService(fakeService);
        dialog.Kind = AssetKind.BTree;
        dialog.Recipe = fakeService.AvailableRecipes().First();
        dialog.Name = "Asset";

        Assert.True(dialog.CanConfirm());
    }

    [Fact]
    public void CanConfirm_EmptyName_ReturnsFalse()
    {
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialogWithService(fakeService);
        dialog.Kind = AssetKind.BTree;
        dialog.Recipe = fakeService.AvailableRecipes().First();
        dialog.Name = "";

        Assert.False(dialog.CanConfirm());
    }

    [Fact]
    public void CanConfirm_NullRecipe_ReturnsFalse()
    {
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialogWithService(fakeService);
        dialog.Kind = AssetKind.BTree;
        dialog.Recipe = null;
        dialog.Name = "Asset";

        Assert.False(dialog.CanConfirm());
    }

    [Fact]
    public void CanConfirm_UnregisteredKind_ReturnsFalse()
    {
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialogWithService(fakeService);
        dialog.Kind = AssetKind.Hsm; // No service registered for HSM.
        dialog.Recipe = fakeService.AvailableRecipes().First();
        dialog.Name = "Asset";

        Assert.False(dialog.CanConfirm());
    }

    [Fact]
    public void Confirm_WhenCannotConfirm_ReturnsFailure()
    {
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialogWithService(fakeService);
        dialog.Kind = AssetKind.BTree;
        dialog.Recipe = null; // Cannot confirm.

        var result = dialog.Confirm();

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(0, fakeService.CreateCallCount);
    }

    // ── RecipesForKind seam ────────────────────────────────────────────────

    [Fact]
    public void RecipesForKind_RegisteredKind_ReturnsRecipes()
    {
        var fakeService = new FakeNewAssetService(AssetKind.Hsm, new[]
        {
            new FakeAsset { Name = "Empty", Kind = AssetKind.Hsm },
            new FakeAsset { Name = "Patrol Machine", Kind = AssetKind.Hsm },
        });
        var dialog = MakeDialogWithService(fakeService);

        var recipes = dialog.RecipesForKind(AssetKind.Hsm);

        Assert.Equal(2, recipes.Count);
        Assert.Contains(recipes, r => r.Name == "Empty");
        Assert.Contains(recipes, r => r.Name == "Patrol Machine");
    }

    [Fact]
    public void RecipesForKind_UnregisteredKind_ReturnsEmpty()
    {
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialogWithService(fakeService);

        var recipes = dialog.RecipesForKind(AssetKind.Hsm);

        Assert.Empty(recipes);
    }

    // ── DEC-12: Per-kind reconciliation ────────────────────────────────────

    /// <summary>
    /// BTree service persists in CreateNew — the dialog must NOT call a save
    /// delegate for non-Blueprint kinds.
    /// </summary>
    [Fact]
    public void Confirm_BTree_DoesNotDoubleWrite()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B19_T5_NoDW_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var svc = new BTreeNewAssetService(tempRoot);
            bool doubleWriteCalled = false;

            var dialog = MakeDialogWithService(svc,
                knownFolders: Array.Empty<string>(),
                fileLister: _ => Array.Empty<string>(),
                saveMintAsset: (_, _) => doubleWriteCalled = true);

            dialog.Kind = AssetKind.BTree;
            dialog.Recipe = svc.AvailableRecipes().First(r => r.Name == "Empty");
            dialog.Name = "NoDoubleWrite";

            var result = dialog.Confirm();

            Assert.True(result.IsSuccess, result.Error ?? "Expected success");
            Assert.False(doubleWriteCalled,
                "saveMintOnlyAsset must NOT be called for BTree (it already persists in CreateNew)");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Blueprint is mint-only — the save delegate MUST be called.
    /// </summary>
    [Fact]
    public void Confirm_Blueprint_CallsSaveMintOnlyAsset()
    {
        var fakeService = new FakeNewAssetService(AssetKind.Blueprint);
        bool saveCalled = false;
        string? savedPath = null;

        var dialog = MakeDialogWithService(fakeService,
            fileLister: _ => Array.Empty<string>(),
            saveMintAsset: (a, p) => { saveCalled = true; savedPath = p; });

        dialog.Kind = AssetKind.Blueprint;
        dialog.Recipe = fakeService.AvailableRecipes().First();
        dialog.Name = "SaveMe";

        var result = dialog.Confirm();

        Assert.True(result.IsSuccess, result.Error ?? "Expected success");
        Assert.True(saveCalled, "saveMintOnlyAsset must be called for Blueprint (mint-only kind)");
        Assert.NotNull(savedPath);
        Assert.EndsWith("SaveMe.bp.json", savedPath!);
    }

    // ── Collision edge: file already exists at target path ─────────────────

    /// <summary>
    /// The exact target file already exists → Confirm returns failure
    /// without calling the service.
    /// </summary>
    [Fact]
    public void Confirm_FileAlreadyExists_ReturnsFailure()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B19_T5_Exist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            // Write a file at the target path.
            var targetPath = Path.Combine(tempRoot, "Existing.btree.json");
            var dto = new BehaviorTreeAssetDto
            {
                AssetId         = Guid.NewGuid(),
                Name            = "Existing",
                TargetNamespace = "",
                BlackboardTypeName = "",
                ContextTypeName = "",
                Canvas          = new CanvasDto { Zoom = 1.0f },
                Nodes           = new List<BTreeNodeDto>(),
                Pills           = new List<BTreePillDto>(),
                SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
                Suppressions    = new SuppressionsDto(),
                Blackboard      = new BlackboardBlockDto(),
            };
            File.WriteAllText(targetPath, BTreeJsonServices.Serialize(dto));

            var svc = new BTreeNewAssetService(tempRoot);
            var dialog = MakeDialogWithService(svc,
                fileLister: _ => new[] { targetPath },
                assetRootOverride: tempRoot);

            dialog.Kind = AssetKind.BTree;
            dialog.Recipe = svc.AvailableRecipes().First(r => r.Name == "Empty");
            dialog.Name = "Existing";

            var result = dialog.Confirm();

            Assert.False(result.IsSuccess);
            Assert.Contains("already exists", result.Error!);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    // ── Validation: null/whitespace name caught ────────────────────────────

    [Fact]
    public void Confirm_WhitespaceName_ReturnsFailure()
    {
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialogWithService(fakeService,
            fileLister: _ => Array.Empty<string>());
        dialog.Kind = AssetKind.BTree;
        dialog.Recipe = fakeService.AvailableRecipes().First();
        dialog.Name = "   ";

        var result = dialog.Confirm();

        Assert.False(result.IsSuccess);
        Assert.Equal(0, fakeService.CreateCallCount);
    }
}
