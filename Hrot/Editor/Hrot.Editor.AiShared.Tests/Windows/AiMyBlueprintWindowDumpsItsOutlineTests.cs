using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>AiMyBlueprintWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Adoption's caller-registers rule.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class AiMyBlueprintWindowDumpsItsOutlineTests : IDisposable
{
    /// <summary>Minimal stub — every member is unused by construction alone (Draw() is never called
    /// by these headless rails).</summary>
    private sealed class StubHostServices : IEditorHostServices
    {
        public INodeCatalog NodeCatalog => null!;
        public ITypeSystem TypeSystem => null!;
        public ILinkValidator LinkValidator => null!;
        public IGraphCommandSink CommandSink => null!;
        public IPickerRegistry Pickers => null!;
        public IClipboard Clipboard => null!;
        public IIconProvider Icons => null!;
        public IDiagnosticsSink? Diagnostics => null;
        public IDebugSession? Debug => null;
        public IInputSource Input => null!;
        public IEditorTheme Theme => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }

    private sealed class StubCommands : IEditorCommands
    {
        public IReadOnlyList<EditorCommandDescriptor> All => Array.Empty<EditorCommandDescriptor>();
        public EditorCommandDescriptor? Get(string commandId) => null;
        public EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null) => default;
#pragma warning disable CS0067
        public event Action<string>? AvailabilityChanged;
#pragma warning restore CS0067
    }

    public AiMyBlueprintWindowDumpsItsOutlineTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static AiMyBlueprintWindow MakeWindow(string id)
        => new(id, "BTree", BlackboardHostKind.BTree);

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "ai_my_blueprint_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);
    }

    [Fact]
    public void AfterRetargeting_TheDumpCarriesTheOutline()
    {
        const string id = "ai_my_blueprint_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var window = MakeWindow(id);

        window.Retarget(
            () => new[] { new BlackboardVariableEntry("Health", typeof(int), Comment: null) },
            new StubHostServices(), new StubCommands());

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id, vm!.PanelId);
        Assert.Equal(AiMyBlueprintWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.True(dump["hasPanel"]!.GetValue<bool>());
        Assert.Equal("BTree", dump["host"]!.GetValue<string>());

        var sections = dump["sections"]!.AsArray();
        var inputs = Assert.Single(sections, s => s!["id"]!.GetValue<string>() == BlackboardMyBlueprintModel.SectionInputs);
        var items = inputs!["items"]!.AsArray();
        Assert.Single(items);
        Assert.Equal("Health", items[0]!["itemId"]!.GetValue<string>());
    }

    [Fact]
    public void WithNoAsset_TheDumpSaysSo()
    {
        const string id = "ai_my_blueprint_rail4";
        PanelSnapshot.CaptureEnabled = true;
        var window = MakeWindow(id);

        window.SimulateDrawClientArea();

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.False(dump["hasPanel"]!.GetValue<bool>());
        Assert.Equal("No asset selected.", dump["emptyReason"]!.GetValue<string>());
        Assert.Empty(dump["sections"]!.AsArray());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        const string id = "ai_my_blueprint_rail3";
        var window = MakeWindow(id);
        window.Retarget(
            () => new[] { new BlackboardVariableEntry("Health", typeof(int), Comment: null) },
            new StubHostServices(), new StubCommands());   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
        Assert.True(vm.HasPanel);
    }
}
