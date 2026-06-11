using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Fdp.Toolkit.Runner;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Di;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Di;

public class SharedAiEditorDiTests
{
    private static ServiceProvider BuildSp(Action<IEditableAsset>? onAssetActivated = null)
    {
        var services = new ServiceCollection();
        services.AddSharedAiEditor(onAssetActivated);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_EditorSelectionStore()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<EditorSelectionStore>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_IAssetCatalog()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<IAssetCatalog>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_AssetBrowserDockedWindow_WithExpectedId()
    {
        using var sp = BuildSp();
        var w = sp.GetRequiredService<AssetBrowserDockedWindow>();
        Assert.NotNull(w);
        Assert.Equal(AssetBrowserDockedWindow.ExpectedId, w.Id);
        Assert.Equal("AssetBrowser", w.Id);
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_InspectorWindow()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<InspectorWindow>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_RuntimeInspectorWindow()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<RuntimeInspectorWindow>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_TraceTimelineWindow()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<TraceTimelineWindow>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_IDebugSessionRegistry()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<IDebugSessionRegistry>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_IWindowRegistrar_AsSharedAiWindowRegistrar()
    {
        using var sp = BuildSp();
        var registrar = sp.GetRequiredService<IWindowRegistrar>();
        Assert.IsType<SharedAiWindowRegistrar>(registrar);
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_IRefactorService()
    {
        using var sp = BuildSp();
        var svc = sp.GetRequiredService<IRefactorService>();
        Assert.IsType<RefactorService>(svc);
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_FindResultsWindow()
    {
        using var sp = BuildSp();
        var win = sp.GetService<FindResultsWindow>();
        Assert.NotNull(win);
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_DiagnosticsWindow_WithNoValidators()
    {
        using var sp = BuildSp();
        var win = sp.GetRequiredService<DiagnosticsWindow>();
        Assert.NotNull(win);
        Assert.Equal("ai_diagnostics", win.Id);
    }

    // ── DBT-2: docked-host activation callback wiring ────────────────────────

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(AssetKind kind = AssetKind.Blueprint, string name = "TestAsset")
        {
            AssetId = Guid.NewGuid();
            Kind    = kind;
            Name    = name;
        }
        public Guid AssetId { get; }
        public string Name { get; }
        public AssetKind Kind { get; }
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    [Fact]
    public void AddSharedAiEditor_WithActivationCallback_OpensDocumentViaManager()
    {
        var docManager = new AiDocumentManager(k => { });
        var callbackAsset = new FakeAsset(AssetKind.Blueprint, "CallbackTest");

        using var sp = BuildSp(onAssetActivated: asset => docManager.Open(asset));

        var window = sp.GetRequiredService<AssetBrowserDockedWindow>();

        // Access the internal _panel via reflection.
        var panelField = typeof(AssetBrowserDockedWindow)
            .GetField("_panel", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(panelField);

        var panel = (AssetBrowserPanel)panelField!.GetValue(window)!;
        Assert.NotNull(panel);

        // Activate the asset through the panel — this fires AssetActivated,
        // which the window forwards to the callback.
        panel.ActivateAsset(callbackAsset);

        // The callback should have opened the document via AiDocumentManager.Open.
        Assert.Single(docManager.OpenDocuments);
        Assert.Equal("CallbackTest", docManager.OpenDocuments.First().Asset.Name);
        Assert.Same(callbackAsset, docManager.OpenDocuments.First().Asset);
    }

    [Fact]
    public void AddSharedAiEditor_WithNullCallback_DoesNotThrowOnActivation()
    {
        using var sp = BuildSp(onAssetActivated: null);

        var window = sp.GetRequiredService<AssetBrowserDockedWindow>();

        var panelField = typeof(AssetBrowserDockedWindow)
            .GetField("_panel", BindingFlags.NonPublic | BindingFlags.Instance);
        var panel = (AssetBrowserPanel)panelField!.GetValue(window)!;

        var asset = new FakeAsset(AssetKind.BTree, "NoCallback");

        // Activating with null callback should not throw.
        var ex = Record.Exception(() => panel.ActivateAsset(asset));
        Assert.Null(ex);
    }
}
