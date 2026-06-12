using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using NodeEditor.UI.Picker;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S13: Tree-layout picker with type icons, folder icons, and match highlighting.</summary>
public sealed class S13_TreeIconPicker : Scenario
{
    public override string Name        => "13 - Tree Icon Picker";
    public override string Description => "Click 'Pick Type Icon' to open a Tree-layout picker with type icons, folder icons, and fuzzy match highlighting.";

    private readonly DemoIconProvider _iconProvider = new();

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Math.Add",      new Vector2(200, 200));
        AddNode(graph, catalog, "Math.Multiply", new Vector2(450, 200));
    }

    public override void DrawOverlay(IEditorHostServices host)
    {
        if (ImGui.SmallButton("Pick Type Icon") && host is FakeHostServices fakeHost)
        {
            // Inject the demo icon provider via the same seam (PickerRegistry.SetServices).
            fakeHost.PickerRegistry_.SetServices(_iconProvider, fakeHost.Theme);

            var request = new PickerRequest
            {
                ContextKey = "demo.tree.icons",
                Title = "Pick a Type",
                Layout = PickerLayout.Tree,
                SelectionMode = PickerSelectionMode.Single,
                ItemsProvider = () => new PickerEntry[]
                {
                    new("Blueprint.AI.BT_Task",     "BT_Task",     null, "Blueprint/AI",       null, null, null, "asset/blueprint"),
                    new("Blueprint.AI.BT_Sequence", "BT_Sequence", null, "Blueprint/AI",       null, null, null, "asset/blueprint"),
                    new("Blueprint.Combat.BP_Enemy","BP_Enemy",    null, "Blueprint/Combat",   null, null, null, "asset/blueprint"),
                    new("HSM.Idle",                  "HSM_Idle",    null, "HSM",                null, null, null, "asset/hsm"),
                    new("HSM.Patrol",                "HSM_Patrol",  null, "HSM",                null, null, null, "asset/hsm"),
                    new("BTree.Sequence",            "BT_Sequence", null, "BTree/Leaves",       null, null, null, "asset/btree"),
                    new("BTree.Selector",            "BT_Selector", null, "BTree/Leaves",       null, null, null, "asset/btree"),
                    new("Uncategorized.Item",        "Uncategorized",null, null,                null, null, null, "asset/default"),
                }
            };

            fakeHost.PickerRegistry_.OpenPicker(request, result =>
            {
                if (!result.Cancelled && result.First is not null)
                {
                    fakeHost.ToastQueue_.Enqueue(new EditorNotification(
                        Id: Guid.NewGuid().ToString(),
                        Severity: NotificationSeverity.Success,
                        Title: "Type Picked",
                        Body: result.First.Name,
                        AutoDismiss: TimeSpan.FromSeconds(3),
                        Actions: null));
                }
            });
        }
    }

    /// <summary>
    /// Demo icon provider that returns distinct fake IconHandles for
    /// type keys and folder keys so a human can visually confirm rendering.
    /// The TextureId values are dummy IntPtrs — ImGui renders white squares
    /// for unknown textures, which is sufficient for layout/UV verification.
    /// </summary>
    private sealed class DemoIconProvider : IIconProvider
    {
        private static readonly Dictionary<string, IconHandle> _icons = new()
        {
            ["asset/blueprint"]  = new IconHandle(1, 16, 16, new Vector2(0.0f, 0.0f), new Vector2(0.25f, 0.25f)),
            ["asset/hsm"]        = new IconHandle(2, 16, 16, new Vector2(0.25f, 0.0f), new Vector2(0.5f, 0.25f)),
            ["asset/btree"]      = new IconHandle(3, 16, 16, new Vector2(0.5f, 0.0f), new Vector2(0.75f, 0.25f)),
            ["asset/default"]    = new IconHandle(4, 16, 16, new Vector2(0.75f, 0.0f), new Vector2(1.0f, 0.25f)),
            ["folder"]           = new IconHandle(5, 16, 16, new Vector2(0.0f, 0.25f), new Vector2(0.25f, 0.5f)),
            ["folder_open"]      = new IconHandle(6, 16, 16, new Vector2(0.25f, 0.25f), new Vector2(0.5f, 0.5f)),
        };

        public bool TryGet(string key, out IconHandle handle)
        {
            return _icons.TryGetValue(key, out handle);
        }
    }
}
