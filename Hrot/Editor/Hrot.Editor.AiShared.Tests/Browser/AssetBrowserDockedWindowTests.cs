using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class AssetBrowserDockedWindowTests
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

    // ── Helpers ────────────────────────────────────────────────────────

    private static WindowManager CreateWindowManager()
        => new(new IconAtlas(IntPtr.Zero, 16f, 16f));

    private static AssetBrowserDockedWindow CreateWindow(
        Action<IEditableAsset>? onAssetActivated = null,
        string? id = null,
        string? title = null,
        string owningPerspective = "Authoring")
    {
        return new AssetBrowserDockedWindow(
            new FakeCatalog(),
            new FakeIconProvider(),
            new AssetBrowserPanelOptions { Kinds = AssetKindFilter.All, ShowAllTab = false },
            onAssetActivated ?? (_ => { }),
            owningPerspective: owningPerspective,
            id: id,
            title: title);
    }

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsExpectedId()
    {
        var window = CreateWindow();
        Assert.Equal(AssetBrowserDockedWindow.ExpectedId, window.Id);
        Assert.Equal("AssetBrowser", window.Id);
    }

    [Fact]
    public void Constructor_SetsExpectedTitle()
    {
        var window = CreateWindow();
        Assert.Equal(AssetBrowserDockedWindow.DefaultTitle, window.Title);
        Assert.Equal("Asset Browser", window.Title);
    }

    [Fact]
    public void Constructor_SetsGlobalScope()
    {
        var window = CreateWindow();
        Assert.Equal(WindowScope.Global, window.Scope);
    }

    [Fact]
    public void Constructor_SetsOwningPerspective()
    {
        var window = CreateWindow(owningPerspective: "MyPerspective");
        Assert.Equal("MyPerspective", window.OwningPerspective);
    }

    [Fact]
    public void Constructor_AllowsCustomIdAndTitle()
    {
        var window = CreateWindow(id: "CustomId", title: "Custom Title");
        Assert.Equal("CustomId", window.Id);
        Assert.Equal("Custom Title", window.Title);
    }

    [Fact]
    public void Constructor_NullCallback_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AssetBrowserDockedWindow(
                new FakeCatalog(),
                new FakeIconProvider(),
                new AssetBrowserPanelOptions(),
                null!));
        Assert.Equal("onAssetActivated", ex.ParamName);
    }

    /// <summary>
    /// After registering with the WindowManager, the window is retrievable
    /// by its documented Id and has the expected Id and Scope.
    /// </summary>
    [Fact]
    public void Registered_WithExpectedId_AndScope()
    {
        var wm = CreateWindowManager();
        var window = CreateWindow();

        wm.RegisterWindow(window);

        // Must be found by its stable Id.
        Assert.True(wm.TryGetWindow(AssetBrowserDockedWindow.ExpectedId, out var found));
        Assert.Same(window, found);

        // The retrieved window must have the documented Id and Scope.
        Assert.Equal("AssetBrowser", found!.Id);
        Assert.Equal(WindowScope.Global, found.Scope);
        Assert.Equal("Asset Browser", found.Title);
    }

    /// <summary>
    /// Activating an asset invokes the callback with the asset AND the
    /// window stays open (IsOpen remains true).
    /// </summary>
    [Fact]
    public void Activate_InvokesCallback_WindowStaysOpen()
    {
        var asset = new FakeAsset { Name = "Guard", Kind = AssetKind.Blueprint };
        IEditableAsset? received = null;
        int callCount = 0;

        var window = CreateWindow(a => { received = a; callCount++; });
        window.IsOpen = true;

        // Simulate AssetActivated by invoking the internal handler.
        // We access the panel's activation path by subscribing to AssetActivated
        // through a backdoor: create a panel that fires on command.
        // Instead, we test via the window's own panel:
        // The window has no public method for firing activation — but the panel
        // is internal.  We test the behavioral contract: if the panel invokes
        // AssetActivated, the callback is called.  We verify this by creating the
        // panel with an asset that we activate through the panel's ActivateAsset.

        // We need a different approach: create the window with assets in the
        // catalog, then use the panel to activate.  The panel's ActivateAsset is
        // public.  Since the window wraps the panel, we expose the panel via
        // the window's internal surface.  But we're in the test assembly,
        // and the window assembly has InternalsVisibleTo → Hrot.Editor.AiShared.Tests.

        // However, AssetBrowserDockedWindow doesn't expose _panel.
        // We'll use a reflection-based helper to access the internal panel.

        var panelField = typeof(AssetBrowserDockedWindow)
            .GetField("_panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(panelField);

        var panel = (AssetBrowserPanel)panelField!.GetValue(window)!;
        Assert.NotNull(panel);

        // Activate the asset through the panel — this fires AssetActivated,
        // which the window subscribes to and forwards to the callback.
        panel.ActivateAsset(asset);

        // Callback was invoked with the exact asset.
        Assert.NotNull(received);
        Assert.Same(asset, received);
        Assert.Equal("Guard", received!.Name);
        Assert.Equal(1, callCount);

        // Window STAYS OPEN — unlike the modal, the docked window does not close.
        Assert.True(window.IsOpen);
    }

    /// <summary>
    /// Activating multiple assets invokes the callback each time without
    /// closing the window.
    /// </summary>
    [Fact]
    public void Activate_MultipleAssets_StaysOpenEachTime()
    {
        var asset1 = new FakeAsset { Name = "A", Kind = AssetKind.Blueprint };
        var asset2 = new FakeAsset { Name = "B", Kind = AssetKind.BTree };

        var received = new List<IEditableAsset>();
        var window = CreateWindow(a => received.Add(a));
        window.IsOpen = true;

        var panelField = typeof(AssetBrowserDockedWindow)
            .GetField("_panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = (AssetBrowserPanel)panelField!.GetValue(window)!;

        // First activation.
        panel.ActivateAsset(asset1);
        Assert.True(window.IsOpen);
        Assert.Single(received);
        Assert.Same(asset1, received[0]);

        // Second activation.
        panel.ActivateAsset(asset2);
        Assert.True(window.IsOpen);
        Assert.Equal(2, received.Count);
        Assert.Same(asset2, received[1]);
    }

    /// <summary>
    /// Closing and re-opening the window does not lose the callback — a new
    /// panel is created on each open? No — the panel is created in the ctor
    /// and lives for the window's lifetime, consistent with ManagedWindow
    /// lifecycle.  Activating after re-show still invokes the callback.
    /// </summary>
    [Fact]
    public void Activate_AfterReopen_StillInvokesCallback()
    {
        var asset = new FakeAsset { Name = "X", Kind = AssetKind.Blueprint };
        IEditableAsset? received = null;

        var window = CreateWindow(a => received = a);

        // Open, close, and re-open the window.
        window.IsOpen = true;
        window.IsOpen = false;
        window.IsOpen = true;

        var panelField = typeof(AssetBrowserDockedWindow)
            .GetField("_panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = (AssetBrowserPanel)panelField!.GetValue(window)!;

        panel.ActivateAsset(asset);

        Assert.NotNull(received);
        Assert.Same(asset, received);
        Assert.True(window.IsOpen, "Window must stay open after activation even after re-open.");
    }
}
