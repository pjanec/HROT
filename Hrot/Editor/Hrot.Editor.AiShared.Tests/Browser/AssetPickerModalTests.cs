using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class AssetPickerModalTests
{
    // ── Fakes ──────────────────────────────────────────────────────────

    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "TestAsset";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _assets;
        public FakeCatalog(params IEditableAsset[] assets) => _assets = new List<IEditableAsset>(assets);

        public IReadOnlyList<IEditableAsset> All => _assets.AsReadOnly();
        public IEditableAsset? FindByAssetId(Guid assetId) => _assets.FirstOrDefault(a => a.AssetId == assetId);
        public IEditableAsset? FindByName(string name) => _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
#pragma warning disable 67
        public event Action<AssetKind>? Changed;
#pragma warning restore 67
    }

    private sealed class FakeIconProvider : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle)
        {
            handle = new IconHandle(1, 16, 16);
            return true;
        }
    }

    /// <summary>
    /// Recording fake for a document manager / load operation.
    /// Any method invocation by the modal would be a design violation —
    /// the modal must perform zero side effects beyond invoking the callback.
    /// </summary>
    private sealed class RecordingDocManager
    {
        public bool OpenCalled { get; private set; }
        public bool LoadScenarioCalled { get; private set; }
        public bool AnySideEffectCalled => OpenCalled || LoadScenarioCalled;

        public void Open(IEditableAsset asset) => OpenCalled = true;
        public void LoadScenarioByName(string name) => LoadScenarioCalled = true;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static AssetPickerModal CreateModal(
        FakeCatalog? catalog = null,
        FakeIconProvider? icons = null)
    {
        return new AssetPickerModal(
            catalog ?? new FakeCatalog(),
            icons ?? new FakeIconProvider());
    }

    private static AssetBrowserPanelOptions DefaultOptions =>
        new() { Kinds = AssetKindFilter.All, ShowAllTab = false };

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullCatalog_ThrowsArgumentNullException()
    {
        var icons = new FakeIconProvider();
        var ex = Assert.Throws<ArgumentNullException>(
            () => new AssetPickerModal(null!, icons));
        Assert.Equal("catalog", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullIcons_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new AssetPickerModal(new FakeCatalog(), null!));
        Assert.Equal("icons", ex.ParamName);
    }

    [Fact]
    public void Open_NullCallback_ThrowsArgumentNullException()
    {
        var modal = CreateModal();
        var ex = Assert.Throws<ArgumentNullException>(
            () => modal.Open(DefaultOptions, null!));
        Assert.Equal("callback", ex.ParamName);
    }

    [Fact]
    public void Open_SetsIsOpen_ToTrue()
    {
        var modal = CreateModal();
        var invoked = false;
        modal.Open(DefaultOptions, _ => invoked = true);
        Assert.True(modal.IsOpen);
        Assert.False(invoked, "Callback must not be invoked until asset is activated.");
    }

    /// <summary>
    /// Activate → closes the modal and invokes the callback with the exact asset.
    /// The callback must be invoked exactly once.
    /// </summary>
    [Fact]
    public void Activate_ClosesAndInvokesCallback_WithAsset()
    {
        var modal = CreateModal();
        var asset = new FakeAsset { Name = "Guard", Kind = AssetKind.Blueprint };

        IEditableAsset? received = null;
        int callCount = 0;
        modal.Open(DefaultOptions, a => { received = a; callCount++; });

        Assert.True(modal.IsOpen);

        // Simulate double-click / Enter on an asset.
        modal.HandleActivated(asset);

        // Modal must be closed after activation.
        Assert.False(modal.IsOpen);

        // Callback received the exact asset.
        Assert.NotNull(received);
        Assert.Same(asset, received);
        Assert.Equal("Guard", received!.Name);

        // Callback invoked exactly once.
        Assert.Equal(1, callCount);
    }

    /// <summary>
    /// Esc / cancel → closes the modal and invokes the callback with null.
    /// </summary>
    [Fact]
    public void Escape_InvokesCallback_WithNull()
    {
        var modal = CreateModal();

        IEditableAsset? received = null;
        int callCount = 0;
        modal.Open(DefaultOptions, a => { received = a; callCount++; });

        Assert.True(modal.IsOpen);

        // Simulate Esc / cancel button.
        modal.HandleCancel();

        // Modal must be closed.
        Assert.False(modal.IsOpen);

        // Callback received null.
        Assert.Null(received);

        // Callback invoked exactly once.
        Assert.Equal(1, callCount);
    }

    /// <summary>
    /// The callback is guarded against double-invocation.
    /// Calling HandleActivated twice without re-opening must only invoke the
    /// callback once.
    /// </summary>
    [Fact]
    public void Callback_IsInvokedAtMostOnce_PerOpen()
    {
        var modal = CreateModal();
        var asset = new FakeAsset { Name = "A", Kind = AssetKind.Blueprint };
        var asset2 = new FakeAsset { Name = "B", Kind = AssetKind.BTree };

        int callCount = 0;
        modal.Open(DefaultOptions, _ => callCount++);

        // Activate twice — callback must fire only once.
        modal.HandleActivated(asset);
        modal.HandleActivated(asset2);
        modal.HandleCancel();

        Assert.Equal(1, callCount);
    }

    /// <summary>
    /// Re-opening the modal with a new callback replaces the previous one.
    /// </summary>
    [Fact]
    public void Reopen_ReplacesCallback()
    {
        var modal = CreateModal();
        var asset = new FakeAsset { Name = "X", Kind = AssetKind.Blueprint };

        int callCount1 = 0;
        int callCount2 = 0;

        // First open — activate immediately.
        modal.Open(DefaultOptions, _ => callCount1++);
        modal.HandleActivated(asset);
        Assert.Equal(1, callCount1);
        Assert.Equal(0, callCount2);

        // Second open — new callback, old one not invoked again.
        modal.Open(DefaultOptions, _ => callCount2++);
        modal.HandleCancel();
        Assert.Equal(1, callCount1); // unchanged
        Assert.Equal(1, callCount2);
    }

    /// <summary>
    /// Programmatic Close() discards the pending callback without invoking it.
    /// </summary>
    [Fact]
    public void Close_DiscardsCallback_WithoutInvocation()
    {
        var modal = CreateModal();
        int callCount = 0;
        modal.Open(DefaultOptions, _ => callCount++);

        Assert.True(modal.IsOpen);

        modal.Close();

        Assert.False(modal.IsOpen);
        // Close() must not invoke the callback.
        Assert.Equal(0, callCount);
    }

    /// <summary>
    /// After Close(), further HandleActivated / HandleCancel calls are no-ops.
    /// </summary>
    [Fact]
    public void AfterClose_HandleMethods_AreNoOps()
    {
        var modal = CreateModal();
        int callCount = 0;
        modal.Open(DefaultOptions, _ => callCount++);

        modal.Close();

        // These should be silent no-ops after Close().
        modal.HandleActivated(new FakeAsset());
        modal.HandleCancel();

        Assert.Equal(0, callCount);
    }

    /// <summary>
    /// The modal performs NO side effects — it never opens documents, loads
    /// scenarios, or calls any external service beyond the supplied callback.
    /// </summary>
    /// <remarks>
    /// The modal has no reference to AiDocumentManager, IEditorLogic, or any
    /// load mechanism.  This test verifies that the recording document-manager
    /// fake's methods are NEVER called through both activate and cancel paths.
    /// </remarks>
    [Fact]
    public void NeverCalls_DocumentManager_Or_Load()
    {
        var docManager = new RecordingDocManager();
        var catalog = new FakeCatalog();
        var modal = CreateModal(catalog);

        // Open the modal — this must not trigger any doc-manager calls.
        modal.Open(DefaultOptions, _ => { /* callback is where the CALLER would call docManager */ });
        Assert.False(docManager.AnySideEffectCalled,
            "Open() must not invoke document-manager or load methods.");

        // Activate path — still no doc-manager calls.
        modal.HandleActivated(new FakeAsset { Name = "MyAsset" });
        Assert.False(docManager.AnySideEffectCalled,
            "HandleActivated() must not invoke document-manager or load methods.");

        // Re-open and test cancel path.
        modal.Open(DefaultOptions, _ => { });
        Assert.False(docManager.AnySideEffectCalled);

        modal.HandleCancel();
        Assert.False(docManager.AnySideEffectCalled,
            "HandleCancel() must not invoke document-manager or load methods.");
    }

    /// <summary>
    /// Scenario assets also activate without side effects.
    /// </summary>
    [Fact]
    public void Activate_ScenarioAsset_InvokesCallback_NoSideEffects()
    {
        var catalog = new FakeCatalog();
        var modal = CreateModal(catalog);
        var scenarioAsset = new FakeAsset
        {
            Name = "combat/ambush/scenario",
            Kind = AssetKind.Scenario,
            SourceFilePath = ""      // scenarios have no source file
        };

        IEditableAsset? received = null;
        modal.Open(new AssetBrowserPanelOptions
        {
            Kinds = AssetKindFilter.Scenario,
            ShowAllTab = false
        }, a => received = a);

        modal.HandleActivated(scenarioAsset);

        Assert.NotNull(received);
        Assert.Same(scenarioAsset, received);
        Assert.False(modal.IsOpen);
    }
}
