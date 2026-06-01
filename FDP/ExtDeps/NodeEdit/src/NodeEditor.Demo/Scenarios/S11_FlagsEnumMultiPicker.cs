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

/// <summary>S11: Flags enum multi-select picker.</summary>
public sealed class S11_FlagsEnumMultiPicker : Scenario
{
    public override string Name        => "11 — Flags Enum Multi-Picker";
    public override string Description => "Click 'Pick Flags' to open a multi-select Compact-layout picker.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Flow.Branch", new Vector2(200, 200));
    }

    public override void DrawOverlay(IEditorHostServices host)
    {
        if (ImGui.SmallButton("Pick Flags") && host is FakeHostServices fakeHost)
        {
            var request = new PickerRequest
            {
                ContextKey = "demo.flags",
                Title = "Pick Flags",
                Layout = PickerLayout.Compact,
                SelectionMode = PickerSelectionMode.Multi, // Architecturally critical for S11
                ItemsProvider = () =>
                [
                    new PickerEntry("Flag.Read", "Read", null, null, null, null, "Read"),
                    new PickerEntry("Flag.Write", "Write", null, null, null, null, "Write"),
                    new PickerEntry("Flag.Execute", "Execute", null, null, null, null, "Execute"),
                    new PickerEntry("Flag.Hidden", "Hidden", null, null, null, null, "Hidden"),
                    new PickerEntry("Flag.System", "System", null, null, null, null, "System")
                ]
            };

            fakeHost.PickerRegistry_.OpenPicker(request, result =>
            {
                if (result.Cancelled) return;
                
                string selectedFlags = string.Join(", ", result.Selection.Select(e => e.Name));
                
                fakeHost.ToastQueue_.Enqueue(new EditorNotification(
                    Id: Guid.NewGuid().ToString(),
                    Severity: NotificationSeverity.Info,
                    Title: "Flags Picked",
                    Body: selectedFlags,
                    AutoDismiss: TimeSpan.FromSeconds(3),
                    Actions: null));
            });
        }
    }
}
