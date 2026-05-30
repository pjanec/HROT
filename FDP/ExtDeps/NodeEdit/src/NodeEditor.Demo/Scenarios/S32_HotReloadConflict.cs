using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S32: Hot-Reload Conflict — make the graph dirty, then click 'Simulate External Modify' to trigger the conflict toast.</summary>
public sealed class S32_HotReloadConflict : Scenario
{
    public override string Name        => "32 — Hot-Reload Conflict";
    public override string Description => "Press 'Make Dirty' (menu bar), then 'Simulate External Modify'. A blocking toast appears with Save / Discard / Ignore.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var begin = AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(100, 200));
        var print = AddNode(graph, catalog, "Util.Print",       new Vector2(380, 200));

        // Rename a node to simulate a dirty edit that's been made
        if (graph.FindNode(print) is FakeNodeModel fn)
            fn.Title = "Renamed Node (dirty edit)";

        LinkNodes(graph, begin, 0, print, 0);
    }

    public override void DrawOverlay(IEditorHostServices host)
    {
        if (!ImGui.SmallButton("Simulate External Modify"))
            return;

        if (host is not FakeHostServices fakeHost)
            return;

        fakeHost.ToastQueue_.Enqueue(new EditorNotification(
            "hot-reload-conflict",
            NotificationSeverity.Warning,
            "External changes detected",
            "Save or discard your changes to reload.",
            null,
            new[]
            {
                new NotificationAction("Save",    "editor.save"),
                new NotificationAction("Discard", "editor.discard"),
                new NotificationAction("Ignore",  "editor.ignore"),
            }));
    }
}
