using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.UI.Picker;
using System;
using System.Linq;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S12: Grid-layout asset picker with fake assets.</summary>
public sealed class S12_AssetGridPicker : Scenario
{
    public override string Name        => "12 — Asset Grid Picker";
    public override string Description => "Click 'Pick Asset' to open a Grid-layout picker showing fake asset entries.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
    }

    public override void DrawOverlay(IEditorHostServices host)
    {
        if (ImGui.SmallButton("Pick Asset") && host is FakeHostServices fakeHost)
        {
            var request = new PickerRequest
            {
                ContextKey = "demo.assets",
                Title = "Pick Asset",
                Layout = PickerLayout.Grid,
                SelectionMode = PickerSelectionMode.Single,
                ItemsProvider = () => Enumerable.Range(1, 14).Select(i => 
                    new PickerEntry(
                        Id: $"asset_{i}",
                        Name: $"Fake Asset {i}",
                        Description: $"Detailed description for fake asset {i}.\nThis text is displayed in the bottom detail strip.",
                        Category: null,
                        Keywords: null,
                        IconTextureId: null, // Null forces GridLayout to draw a placeholder rect
                        Tag: $"asset_{i}")
                ).ToArray()
            };

            fakeHost.PickerRegistry_.OpenPicker(request, result =>
            {
                if (result.Cancelled) return;
                
                fakeHost.ToastQueue_.Enqueue(new EditorNotification(
                    Id: Guid.NewGuid().ToString(),
                    Severity: NotificationSeverity.Success,
                    Title: "Asset Picked",
                    Body: result.First?.Name,
                    AutoDismiss: TimeSpan.FromSeconds(3),
                    Actions: null));
            });
        }
    }
}
