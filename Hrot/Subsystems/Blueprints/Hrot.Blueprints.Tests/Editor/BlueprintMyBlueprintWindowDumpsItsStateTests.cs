using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Windows;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — <c>BlueprintMyBlueprintWindow</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⭐ <c>PanelKind</c> must equal <c>AiMyBlueprintWindow.Kind</c> — both cite
/// <see cref="PanelIds.MyBlueprint"/> — so the two implementations are comparable by construction.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class BlueprintMyBlueprintWindowDumpsItsStateTests : IDisposable
{
    private sealed class StubHostServices : IEditorHostServices
    {
        public INodeCatalog      NodeCatalog   => null!;
        public ITypeSystem       TypeSystem    => null!;
        public ILinkValidator    LinkValidator => null!;
        public IGraphCommandSink CommandSink   => null!;
        public IPickerRegistry   Pickers       => null!;
        public IClipboard        Clipboard     => null!;
        public IIconProvider     Icons         => null!;
        public IDiagnosticsSink? Diagnostics   => null;
        public IDebugSession?    Debug         => null;
        public IInputSource      Input         => null!;
        public IEditorTheme      Theme         => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }

    public BlueprintMyBlueprintWindowDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static BlueprintAsset MakeAsset()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "PanelSnapshotHost",
            Dispatch = BlueprintDispatchKind.AiPrimitive,
            Graphs   = new List<Graph>(),
            Header   = new Header(),
        };
        BlueprintDocumentFactory.CreateVariable(asset, "Count", "System.Int32");
        return asset;
    }

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "my_blueprint_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new BlueprintMyBlueprintWindow(idOverride: id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);
    }

    [Fact]
    public void WithNoBlueprintOpen_TheDumpSaysSo()
    {
        const string id = "my_blueprint_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var window = new BlueprintMyBlueprintWindow(idOverride: id);

        window.SimulateDrawClientArea();

        var stored = PanelSnapshot.TryGet(id);
        Assert.NotNull(stored);
        Assert.Equal(id, stored!.PanelId);
        Assert.Equal(BlueprintMyBlueprintWindow.Kind, stored.PanelKind);
        // ⭐ PanelIds.MyBlueprint is the constant BOTH implementations cite (AiMyBlueprintWindow.Kind is
        //   `internal` to Hrot.Editor.AiShared and not visible here) — comparing against the shared
        //   constant is the cross-host agreement this rail is for.
        Assert.Equal(PanelIds.MyBlueprint, stored.PanelKind);

        var dump = stored.Dump();
        Assert.False(dump["hasPanel"]!.GetValue<bool>());
        Assert.Equal("No blueprint open.", dump["emptyReason"]!.GetValue<string>());
    }

    [Fact]
    public void AfterRetarget_TheDumpCarriesTheSectionsAndItems()
    {
        const string id = "my_blueprint_rail3";
        PanelSnapshot.CaptureEnabled = true;
        var window = new BlueprintMyBlueprintWindow(idOverride: id);
        var asset  = MakeAsset();
        var commands = new EditorCommandsImpl();

        window.Retarget(null, asset, new StubHostServices(), commands, null, () => Guid.Empty);

        var vm = window.SimulateDrawClientArea();

        Assert.True(vm.HasPanel);
        Assert.Null(vm.EmptyReason);

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        // ⚠ Retarget's first argument (editableAsset) was passed null above — only blueprintAsset was
        //   supplied — so hasEditableAsset reports that honestly rather than inferring it from the
        //   BlueprintAsset the sections were actually built from.
        Assert.False(dump["hasEditableAsset"]!.GetValue<bool>());
        var sections = dump["sections"]!.AsArray();
        Assert.True(sections.Count > 0);

        // ⭐ The "Count" variable created above appears somewhere in the projected sections.
        bool foundCount = false;
        foreach (var section in sections)
            foreach (var item in section!["items"]!.AsArray())
                if (item!["displayName"]!.GetValue<string>() == "Count")
                    foundCount = true;
        Assert.True(foundCount, "expected the 'Count' variable to appear in the dumped sections");
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        const string id = "my_blueprint_rail4";
        var window = new BlueprintMyBlueprintWindow(idOverride: id);   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.False(vm.HasPanel);
    }
}
