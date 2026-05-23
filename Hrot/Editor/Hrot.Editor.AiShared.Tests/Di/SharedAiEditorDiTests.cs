using Microsoft.Extensions.DependencyInjection;
using Hrot.Editor.AiShared.Di;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Fdp.Toolkit.Runner;

namespace Hrot.Editor.AiShared.Tests.Di;

public class SharedAiEditorDiTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSharedAiEditor();
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
    public void AddSharedAiEditor_Resolves_AssetBrowserWindow_WithCorrectId()
    {
        using var sp = BuildSp();
        var w = sp.GetRequiredService<AssetBrowserWindow>();
        Assert.NotNull(w);
        Assert.Equal("ai_asset_browser", w.Id);
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
}
