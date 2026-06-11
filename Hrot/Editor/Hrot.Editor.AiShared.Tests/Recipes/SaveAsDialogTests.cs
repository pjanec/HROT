using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Recipes;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Recipes;

// ─────────────────────────────────────────────────────────────────────────────
// MTB-P6-T6 — SaveAsDialog headless tests
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for <see cref="SaveAsDialog"/> model logic: fresh-id minting,
/// rel-path respect, collision guard, and the <c>EmptySourcePath→SaveAs</c>
/// routing handshake (DEC-9 cross-check with MTB-P2-T4).
/// </summary>
public sealed class SaveAsDialogTests
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

        public FakeNewAssetService(AssetKind kind)
        {
            Kind = kind;
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
            => new List<IEditableAsset>
            {
                new FakeAsset { Name = "Empty", Kind = Kind },
            };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static SaveAsDialog MakeDialog(
        IEditableAsset sourceAsset,
        INewAssetService service,
        IEnumerable<string>? knownFolders = null,
        Func<string, IEnumerable<string>>? fileLister = null,
        Action<IEditableAsset, string>? saveMintAsset = null,
        Action<string>? saveScenarioAs = null,
        string? assetRootOverride = null)
    {
        var dict = new Dictionary<AssetKind, INewAssetService>
        {
            [service.Kind] = service,
        };
        return new SaveAsDialog(
            sourceAsset, dict, knownFolders, fileLister,
            saveMintAsset, saveScenarioAs, assetRootOverride);
    }

    /// <summary>
    /// Creates a source BTree asset on disk under <paramref name="tempRoot"/>
    /// and returns the <see cref="IEditableAsset"/> adapter. The source is
    /// created via <see cref="BTreeNewAssetService.CreateNew"/> so it has a
    /// valid DTO and file path.
    /// </summary>
    private static IEditableAsset CreateSourceBTreeAsset(
        string tempRoot, string name, string relPath = "")
    {
        var svc = new BTreeNewAssetService(tempRoot);
        var emptyRecipe = svc.AvailableRecipes().First(r => r.Name == "Empty");
        return svc.CreateNew(emptyRecipe, name, relPath);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 1: SaveAs_WritesNewFile_WithFreshAssetId
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Save-As over a source BTree asset writes a NEW file whose
    /// <see cref="IEditableAsset.AssetId"/> differs from the source's
    /// (duplicate semantics per §18.5).
    /// </summary>
    [Fact]
    public void SaveAs_WritesNewFile_WithFreshAssetId()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B20_T1_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            // 1. Create a source asset on disk.
            var sourceAsset = CreateSourceBTreeAsset(tempRoot, "SourceBTree");
            var sourceId = sourceAsset.AssetId;
            Assert.NotEqual(Guid.Empty, sourceId);

            // 2. Create SaveAsDialog with that source as the recipe.
            var svc = new BTreeNewAssetService(tempRoot);
            var dialog = MakeDialog(sourceAsset, svc,
                fileLister: _ => Array.Empty<string>(),
                assetRootOverride: tempRoot);
            dialog.Name = "ClonedBTree";
            dialog.FolderPicker.SelectedRelPath = "";

            // 3. Confirm Save-As.
            IEditableAsset? callbackAsset = null;
            var result = dialog.Confirm(a => callbackAsset = a);

            // 4. Assert success + fresh AssetId.
            Assert.True(result.IsSuccess, result.Error ?? "Expected success");
            Assert.NotNull(result.Asset);
            Assert.NotNull(callbackAsset);
            Assert.NotEqual(Guid.Empty, result.Asset!.AssetId);
            Assert.True(result.Asset.AssetId != sourceId,
                "Save-As MUST mint a fresh AssetId (≠ source) per §18.5 duplicate semantics.");

            // 5. File exists at the expected path.
            var expectedFile = Path.Combine(tempRoot, "ClonedBTree.btree.json");
            Assert.True(File.Exists(expectedFile),
                $"Expected file not found: {expectedFile}");

            // 6. Verify the file contains the fresh AssetId (not the source's).
            var json = File.ReadAllText(expectedFile);
            var deserialized = BTreeJsonServices.Deserialize(json);
            Assert.NotNull(deserialized);
            Assert.Equal(result.Asset.AssetId, deserialized!.AssetId);
            Assert.NotEqual(sourceId, deserialized.AssetId);
            Assert.Equal("ClonedBTree", deserialized.Name);

            // 7. Source file still exists unchanged.
            Assert.True(File.Exists(sourceAsset.SourceFilePath),
                "Source file must not be deleted by Save-As.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 2: SaveAs_RespectsPickedRelPath
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Save-As writes the new file under the picked relpath:
    /// <c>Assets/BTrees/combat/Guard/Patrol.btree.json</c>.
    /// </summary>
    [Fact]
    public void SaveAs_RespectsPickedRelPath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B20_T2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            // 1. Create a source asset.
            var sourceAsset = CreateSourceBTreeAsset(tempRoot, "SourceBTree");

            // 2. SaveAsDialog with a nested relPath.
            var svc = new BTreeNewAssetService(tempRoot);
            var dialog = MakeDialog(sourceAsset, svc,
                knownFolders: new[] { "combat", "combat/Guard" },
                fileLister: _ => Array.Empty<string>(),
                assetRootOverride: tempRoot);
            dialog.Name = "Patrol";
            dialog.FolderPicker.SelectedRelPath = "combat/Guard";

            // 3. Confirm.
            var result = dialog.Confirm();

            // 4. Assert success.
            Assert.True(result.IsSuccess, result.Error ?? "Expected success");

            // 5. File is at the correct nested path.
            var expectedFile = Path.Combine(tempRoot, "combat", "Guard", "Patrol.btree.json");
            Assert.True(File.Exists(expectedFile),
                $"Expected file at '{expectedFile}' but it was not found.");

            // 6. Verify the file content.
            var json = File.ReadAllText(expectedFile);
            var deserialized = BTreeJsonServices.Deserialize(json);
            Assert.NotNull(deserialized);
            Assert.Equal("Patrol", deserialized!.Name);
            Assert.NotEqual(Guid.Empty, deserialized.AssetId);

            // 7. File is NOT at the root (was not saved flat).
            var rootPath = Path.Combine(tempRoot, "Patrol.btree.json");
            Assert.False(File.Exists(rootPath),
                "File must NOT be saved at the root when a relPath is picked.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 3: CollisionGuard_RejectsExistingBaseName
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The target directory already contains a <c>.cs</c> file with the same
    /// logical base name → the collision guard rejects it. No file is written.
    /// </summary>
    [Fact]
    public void CollisionGuard_RejectsExistingBaseName()
    {
        // Simulate: the target directory contains "Guard.cs".
        var fileLister = new Func<string, IEnumerable<string>>(dir =>
            new[] { Path.Combine(dir, "Guard.cs"), Path.Combine(dir, "Other.btree.json") });

        var sourceAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Original",
            Kind = AssetKind.BTree,
        };
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialog(sourceAsset, fakeService,
            fileLister: fileLister);

        dialog.Name = "Guard";
        dialog.FolderPicker.SelectedRelPath = "";

        bool callbackCalled = false;
        var result = dialog.Confirm(_ => callbackCalled = true);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("[D5]", result.Error);
        Assert.Contains("Guard", result.Error);
        Assert.False(callbackCalled, "Callback must not be called on collision failure.");
        Assert.Equal(0, fakeService.CreateCallCount);
    }

    /// <summary>
    /// Collision guard also works when a subfolder contains a conflicting
    /// <c>.cs</c> file.
    /// </summary>
    [Fact]
    public void CollisionGuard_RejectsExistingBaseName_WhenCsExistsInSubfolder()
    {
        var fileLister = new Func<string, IEnumerable<string>>(dir =>
            new[] { Path.Combine(dir, "Patrol.cs") });

        var sourceAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Original",
            Kind = AssetKind.Hsm,
        };
        var fakeService = new FakeNewAssetService(AssetKind.Hsm);
        var dialog = MakeDialog(sourceAsset, fakeService,
            knownFolders: new[] { "machines" },
            fileLister: fileLister);

        dialog.Name = "Patrol";
        dialog.FolderPicker.SelectedRelPath = "machines";

        var result = dialog.Confirm();

        Assert.False(result.IsSuccess);
        Assert.Contains("Patrol", result.Error!);
        Assert.Equal(0, fakeService.CreateCallCount);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 4: EmptySourcePathSave_RoutesToSaveAs
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cross-check with MTB-P2-T4: a <c>shell.save</c> whose active document
    /// has an empty <see cref="IEditableAsset.SourceFilePath"/> routes into the
    /// Save-As path via <c>requestSaveAs</c> → <see cref="SaveAsDialog"/>.
    ///
    /// This test verifies the full handshake: <c>ShellSaveCommands</c> detects
    /// the empty path, calls <c>requestSaveAs</c>, which creates a
    /// <see cref="SaveAsDialog"/> seeded from the document's asset, confirms
    /// it with a fresh name, and produces a new asset with a fresh
    /// <see cref="AssetId"/>.
    /// </summary>
    [Fact]
    public void EmptySourcePathSave_RoutesToSaveAs()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B20_T4_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            // 1. Create an "unsaved" BTree source asset with empty SourceFilePath.
            //    We use BTreeNewAssetService to get a proper BTreeEditableAssetAdapter
            //    with a valid DTO, then manually clear SourceFilePath to simulate
            //    an in-memory-only asset that hasn't been persisted yet.
            var tempSvc = new BTreeNewAssetService(tempRoot);
            var emptyRecipe = tempSvc.AvailableRecipes().First(r => r.Name == "Empty");
            var sourceAsset = (BTreeEditableAssetAdapter)tempSvc.CreateNew(emptyRecipe, "UnsavedAsset", "");
            // Simulate "not yet persisted" by giving it an empty path.
            // CreateNew writes the file, so we delete it to create a true empty-path situation.
            var sourceFilePath = sourceAsset.SourceFilePath;
            if (File.Exists(sourceFilePath))
                File.Delete(sourceFilePath);

            // Create an adapter with empty SourceFilePath but valid DTO.
            var unsavedAdapter = new BTreeEditableAssetAdapter(sourceAsset.Dto, "");
            var sourceId = unsavedAdapter.AssetId;

            // 2. Open it as a document.
            var docManager = new AiDocumentManager(_ => { });
            var doc = docManager.Open(unsavedAdapter);
            doc.MarkDirty();

            // 3. Set up ShellSaveCommands. The requestSaveAs seam creates a
            //    SaveAsDialog, feeds it a new name, confirms, and records the
            //    result.
            IEditableAsset? saveAsResult = null;
            ConfirmResult? confirmResult = null;

            var (descriptors, actions) = MakeRecordingRegister();

            var btreeService = new BTreeNewAssetService(tempRoot);

            ShellSaveCommands.Register(
                register:      RecordingRegister(descriptors, actions),
                docManager:    docManager,
                saveBlueprint: null,
                saveBTree:     null, // Not called — routes to Save-As instead.
                saveHsm:       null,
                saveScenario:  null,
                requestSaveAs: d =>
                {
                    // DEC-9: the SaveAsDialog is seeded from the document's asset.
                    var dialog = new SaveAsDialog(
                        d.Asset,
                        new Dictionary<AssetKind, INewAssetService>
                        {
                            [AssetKind.BTree] = btreeService,
                        },
                        knownFolderPaths: Array.Empty<string>(),
                        listFilesInDir: _ => Array.Empty<string>(),
                        assetRootOverride: tempRoot);

                    // The UI would populate these; in this test we simulate
                    // the user entering a new name.
                    dialog.Name = "SavedFromEmpty";
                    dialog.FolderPicker.SelectedRelPath = "";

                    confirmResult = dialog.Confirm(a => saveAsResult = a);
                },
                report: null);

            // 4. Invoke shell.save — must route to requestSaveAs (empty path).
            InvokeAction(descriptors, actions, ShellSaveCommands.SaveId);

            // 5. Assert the SaveAsDialog was invoked and succeeded.
            Assert.NotNull(confirmResult);
            Assert.True(confirmResult!.IsSuccess,
                confirmResult.Error ?? "SaveAsDialog.Confirm should succeed");
            Assert.NotNull(saveAsResult);
            Assert.NotEqual(Guid.Empty, saveAsResult!.AssetId);
            Assert.True(saveAsResult!.AssetId != sourceId,
                "Save-As MUST mint a fresh AssetId (≠ source).");
            Assert.Equal("SavedFromEmpty", saveAsResult.Name);

            // 6. The new file exists on disk.
            var expectedFile = Path.Combine(tempRoot, "SavedFromEmpty.btree.json");
            Assert.True(File.Exists(expectedFile),
                $"Expected Save-As output at '{expectedFile}'.");
            var json = File.ReadAllText(expectedFile);
            var deserialized = BTreeJsonServices.Deserialize(json);
            Assert.NotNull(deserialized);
            Assert.Equal(saveAsResult.AssetId, deserialized!.AssetId);

            // 7. The original document is still dirty (Save-As doesn't automatically
            //    replace the document's backing asset — the caller does that).
            Assert.True(doc.IsDirty,
                "Document should remain dirty after Save-As " +
                "(the caller replaces the backing asset separately).");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    // ── CanConfirm seam ───────────────────────────────────────────────────

    [Fact]
    public void CanConfirm_NameSetServiceRegistered_ReturnsTrue()
    {
        var source = new FakeAsset { Name = "Src", Kind = AssetKind.BTree };
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialog(source, fakeService);
        dialog.Name = "NewName";

        Assert.True(dialog.CanConfirm());
    }

    [Fact]
    public void CanConfirm_EmptyName_ReturnsFalse()
    {
        var source = new FakeAsset { Name = "Src", Kind = AssetKind.BTree };
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialog(source, fakeService);
        dialog.Name = "";

        Assert.False(dialog.CanConfirm());
    }

    [Fact]
    public void CanConfirm_UnregisteredKind_ReturnsFalse()
    {
        var source = new FakeAsset { Name = "Src", Kind = AssetKind.Hsm };
        var fakeService = new FakeNewAssetService(AssetKind.BTree); // BTree registered, not HSM
        var dialog = MakeDialog(source, fakeService);
        dialog.Name = "NewName";

        Assert.False(dialog.CanConfirm());
    }

    [Fact]
    public void Confirm_WhenCannotConfirm_ReturnsFailure()
    {
        var source = new FakeAsset { Name = "Src", Kind = AssetKind.BTree };
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialog(source, fakeService);
        dialog.Name = ""; // Cannot confirm.

        var result = dialog.Confirm();

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(0, fakeService.CreateCallCount);
    }

    // ── Scenario Save-As path ─────────────────────────────────────────────

    [Fact]
    public void Confirm_Scenario_CallsSaveScenarioAs_WithFreshId()
    {
        var source = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "LoadedScenario",
            Kind = AssetKind.Scenario,
        };
        string? savedName = null;
        var fakeService = new FakeNewAssetService(AssetKind.Scenario);

        var dialog = MakeDialog(source, fakeService,
            saveScenarioAs: name => savedName = name);
        dialog.Name = "ClonedScenario";
        dialog.FolderPicker.SelectedRelPath = "";

        IEditableAsset? callbackAsset = null;
        var result = dialog.Confirm(a => callbackAsset = a);

        Assert.True(result.IsSuccess, result.Error ?? "Expected success");
        Assert.Equal("ClonedScenario", savedName);
        Assert.NotNull(callbackAsset);
        Assert.Equal("ClonedScenario", callbackAsset!.Name);
        Assert.Equal(AssetKind.Scenario, callbackAsset.Kind);
        Assert.NotEqual(Guid.Empty, callbackAsset.AssetId);
        Assert.True(callbackAsset.AssetId != source.AssetId,
            "Scenario Save-As MUST mint a fresh AssetId.");

        // Scenario Save-As does NOT call CreateNew (it uses saveScenarioAs).
        Assert.True(fakeService.CreateCallCount == 0,
            "Scenario Save-As must NOT call CreateNew (uses saveScenarioAs delegate).");
    }

    [Fact]
    public void Confirm_Scenario_WithNestedRelPath()
    {
        var source = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "LoadedScenario",
            Kind = AssetKind.Scenario,
        };
        string? savedName = null;
        var fakeService = new FakeNewAssetService(AssetKind.Scenario);

        var dialog = MakeDialog(source, fakeService,
            knownFolders: new[] { "combat", "combat/Guard" },
            saveScenarioAs: name => savedName = name);
        dialog.Name = "Patrol";
        dialog.FolderPicker.SelectedRelPath = "combat/Guard";

        var result = dialog.Confirm();

        Assert.True(result.IsSuccess, result.Error ?? "Expected success");
        Assert.Equal("combat/Guard/Patrol", savedName);
    }

    [Fact]
    public void Confirm_Scenario_NoSaveDelegate_ReturnsFailure()
    {
        var source = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "LoadedScenario",
            Kind = AssetKind.Scenario,
        };
        var fakeService = new FakeNewAssetService(AssetKind.Scenario);
        var dialog = MakeDialog(source, fakeService,
            saveScenarioAs: null); // No scenario save delegate.

        dialog.Name = "ClonedScenario";

        var result = dialog.Confirm();

        Assert.False(result.IsSuccess);
        Assert.Contains("No scenario save delegate", result.Error!);
    }

    // ── Blueprint Save-As (saveMintOnlyAsset delegate) ────────────────────

    [Fact]
    public void Confirm_Blueprint_CallsSaveMintOnlyAsset()
    {
        var source = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "SourceBP",
            Kind = AssetKind.Blueprint,
        };
        var fakeService = new FakeNewAssetService(AssetKind.Blueprint);
        bool saveCalled = false;
        string? savedPath = null;
        IEditableAsset? savedAsset = null;

        var dialog = MakeDialog(source, fakeService,
            fileLister: _ => Array.Empty<string>(),
            saveMintAsset: (a, p) =>
            {
                saveCalled = true;
                savedAsset = a;
                savedPath = p;
            });
        dialog.Name = "ClonedBP";

        var result = dialog.Confirm();

        Assert.True(result.IsSuccess, result.Error ?? "Expected success");
        Assert.True(saveCalled, "saveMintOnlyAsset must be called for Blueprint Save-As.");
        Assert.NotNull(savedAsset);
        Assert.NotNull(savedPath);
        Assert.EndsWith("ClonedBP.bp.json", savedPath!);
        Assert.True(savedAsset!.AssetId != source.AssetId,
            "Blueprint Save-As MUST mint a fresh AssetId.");
    }

    // ── Caller callback ───────────────────────────────────────────────────

    [Fact]
    public void Callback_ReceivesNewAsset_OnSuccess()
    {
        var source = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Source",
            Kind = AssetKind.BTree,
        };
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialog(source, fakeService,
            fileLister: _ => Array.Empty<string>());
        dialog.Name = "Dest";

        IEditableAsset? callbackAsset = null;
        var result = dialog.Confirm(a => callbackAsset = a);

        Assert.True(result.IsSuccess);
        Assert.NotNull(callbackAsset);
        Assert.Equal("Dest", callbackAsset!.Name);
        Assert.NotEqual(source.AssetId, callbackAsset.AssetId);
    }

    // ── Kind is fixed to source ───────────────────────────────────────────

    [Fact]
    public void Kind_MatchesSourceAssetKind()
    {
        var source = new FakeAsset { Name = "Src", Kind = AssetKind.Hsm };
        var fakeService = new FakeNewAssetService(AssetKind.Hsm);
        var dialog = MakeDialog(source, fakeService);

        Assert.Equal(AssetKind.Hsm, dialog.Kind);
    }

    // ── Name defaults to source name ──────────────────────────────────────

    [Fact]
    public void Name_DefaultsToSourceAssetName()
    {
        var source = new FakeAsset { Name = "MyAsset", Kind = AssetKind.BTree };
        var fakeService = new FakeNewAssetService(AssetKind.BTree);
        var dialog = MakeDialog(source, fakeService);

        Assert.Equal("MyAsset", dialog.Name);
    }

    // ── Exact file exists collision ───────────────────────────────────────

    [Fact]
    public void Confirm_FileAlreadyExists_ReturnsFailure()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B20_Exist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            // Create a file at the target path BEFORE confirming.
            var targetPath = Path.Combine(tempRoot, "Existing.btree.json");
            var dto = new BehaviorTreeAssetDto
            {
                AssetId            = Guid.NewGuid(),
                Name               = "Existing",
                TargetNamespace    = "",
                BlackboardTypeName = "",
                ContextTypeName    = "",
                Canvas             = new CanvasDto { Zoom = 1.0f },
                Nodes              = new List<BTreeNodeDto>(),
                Pills              = new List<BTreePillDto>(),
                SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
                Suppressions       = new SuppressionsDto(),
                Blackboard         = new BlackboardBlockDto(),
            };
            File.WriteAllText(targetPath, BTreeJsonServices.Serialize(dto));

            var sourceAsset = CreateSourceBTreeAsset(tempRoot, "Source");
            var svc = new BTreeNewAssetService(tempRoot);
            var dialog = MakeDialog(sourceAsset, svc,
                fileLister: _ => new[] { targetPath },
                assetRootOverride: tempRoot);
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

    // ── Test infrastructure helpers ───────────────────────────────────────

    private static (List<EditorCommandDescriptor> descriptors,
                    List<Action<EditorCommandContext>> actions)
        MakeRecordingRegister()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var actions = new List<Action<EditorCommandContext>>();
        return (descriptors, actions);
    }

    private static Action<EditorCommandDescriptor, Action<EditorCommandContext>>
        RecordingRegister(List<EditorCommandDescriptor> descriptors,
                          List<Action<EditorCommandContext>> actions)
    {
        return (d, a) => { descriptors.Add(d); actions.Add(a); };
    }

    private static void InvokeAction(
        List<EditorCommandDescriptor> descriptors,
        List<Action<EditorCommandContext>> actions,
        string id)
    {
        int idx = descriptors.FindIndex(x => x.Id == id);
        Assert.True(idx >= 0, $"Command '{id}' not found in recorded descriptors");
        actions[idx](default);
    }
}
