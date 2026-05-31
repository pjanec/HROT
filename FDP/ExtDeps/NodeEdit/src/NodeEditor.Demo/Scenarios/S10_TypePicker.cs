using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using NodeEditor.UI.Picker;
using System;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S10: Type picker — nested category tree layout.</summary>
public sealed class S10_TypePicker : Scenario
{
    public override string Name        => "10 — Type Picker (Nested)";
    public override string Description => "Click 'Pick Type' in the overlay to open a Tree-layout type picker.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Math.Add",      new Vector2(200, 200));
        AddNode(graph, catalog, "Math.Multiply", new Vector2(450, 200));
    }

    public override void DrawOverlay(IEditorHostServices host)
    {
        if (ImGui.SmallButton("Pick Type") && host is FakeHostServices fakeHost)
        {
            // Architecturally critical: Bypass IPickerSource and supply structural 
            // metadata directly via the native UI PickerRequest API.
            var request = new PickerRequest
            {
                ContextKey = "demo.types.all",
                Title = "Pick a Type",
                Layout = PickerLayout.Tree,
                SelectionMode = PickerSelectionMode.Single,
                ItemsProvider = () =>
                [
                    new PickerEntry("System.Boolean", "Boolean", null, "System/Primitives", null, null, new TypeKey("System.Boolean")),
                    new PickerEntry("System.Int32", "Int32", null, "System/Primitives", null, null, new TypeKey("System.Int32")),
                    new PickerEntry("System.Single", "Single", null, "System/Primitives", null, null, new TypeKey("System.Single")),
                    new PickerEntry("System.String", "String", null, "System", null, null, new TypeKey("System.String")),
                    new PickerEntry("System.Numerics.Vector2", "Vector2", null, "System/Numerics", null, null, new TypeKey("System.Numerics.Vector2")),
                    new PickerEntry("System.Numerics.Vector3", "Vector3", null, "System/Numerics", null, null, new TypeKey("System.Numerics.Vector3")),
                    new PickerEntry("System.Numerics.Vector4", "Vector4", null, "System/Numerics", null, null, new TypeKey("System.Numerics.Vector4")),
                    new PickerEntry("System.Numerics.Quaternion", "Quaternion", null, "System/Numerics", null, null, new TypeKey("System.Numerics.Quaternion")),
                    new PickerEntry("NodeEditor.Color", "Color", null, "NodeEditor", null, null, new TypeKey("NodeEditor.Color"))
                ]
            };

            fakeHost.PickerRegistry_.OpenPicker(request, result =>
            {
                if (!result.Cancelled && result.First?.Tag is TypeKey chosenType)
                {
                    fakeHost.ToastQueue_.Enqueue(new EditorNotification(
                        Id: Guid.NewGuid().ToString(),
                        Severity: NotificationSeverity.Success,
                        Title: "Type Picked",
                        Body: chosenType.Id,
                        AutoDismiss: TimeSpan.FromSeconds(3),
                        Actions: null));
                }
            });
        }
    }
}
