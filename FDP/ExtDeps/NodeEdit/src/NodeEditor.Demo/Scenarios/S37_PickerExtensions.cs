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

/// <summary>
/// S37: ⭐ CE-4168 (AQ108 D26 P1–P5) — the ONE generic picker with all five opt-in extensions: a side pane with host-drawn
/// interactive content (P1/P2), footer actions (P3), a Tree ⇄ Grid toggle remembered per context (P4) and the shared lazy image
/// cache (P5 — entries carry an <c>ImageKey</c>; the demo host wires no GPU loader, so the pane shows text only).
/// </summary>
public sealed class S37_PickerExtensions : Scenario
{
    public override string Name        => "37 - Picker Extensions";
    public override string Description => "Side pane with a variant combo + checkbox, Open / Open and go live buttons, Tree/Thumbnails toggle.";

    private sealed class RunSetup { public int Variant; public bool Autostart = true; }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
    }

    public override void DrawOverlay(IEditorHostServices host)
    {
        if (!ImGui.SmallButton("Pick Scenario") || host is not FakeHostServices fakeHost) return;

        var setup = new RunSetup();
        string[] variants = { "random", "1", "2", "3" };
        var request = new PickerRequest
        {
            ContextKey = "demo.scenario-ext",
            Title = "Open Scenario",
            Layout = PickerLayout.Tree,
            LayoutChoices = new[] { PickerLayout.Tree, PickerLayout.Grid },
            ShowSidePane = true,
            DrawPaneAfterDefault = true,
            DrawPane = (entry, pane) =>
            {
                if (ImGui.CollapsingHeader("Variant"))
                    ImGui.Combo("##variant", ref setup.Variant, variants, variants.Length);
                ImGui.Checkbox("Autostart", ref setup.Autostart);
            },
            CapturePaneState = _ => setup,
            Actions = new[]
            {
                new PickerAction("open", "Open", IsDefault: true),
                new PickerAction("golive", "Open and go live",
                    DisabledReason: e => e is null ? "Pick a scenario first" : null),
            },
            ItemsProvider = () => Enumerable.Range(1, 12).Select(i => new PickerEntry(
                Id: $"scn_{i}", Name: $"Scenario {i}",
                Description: $"Scenario {i}: 100% fake; the pane shows this wrapped text.",
                Category: i % 2 == 0 ? "Training/Land" : "Game/Sea",
                Keywords: null, IconTextureId: null, Tag: $"scn_{i}",
                ImageKey: $"preview/scn_{i}.png")).ToArray(),
        };

        fakeHost.PickerRegistry_.OpenPicker(request, result =>
        {
            if (result.Cancelled) return;
            var s = (RunSetup?)result.PaneState;
            fakeHost.ToastQueue_.Enqueue(new EditorNotification(
                Id: Guid.NewGuid().ToString(),
                Severity: NotificationSeverity.Success,
                Title: $"{result.ActionId}: {result.First?.Name}",
                Body: $"variant {variants[s?.Variant ?? 0]}, autostart {s?.Autostart}",
                AutoDismiss: TimeSpan.FromSeconds(3),
                Actions: null));
        });
    }
}
