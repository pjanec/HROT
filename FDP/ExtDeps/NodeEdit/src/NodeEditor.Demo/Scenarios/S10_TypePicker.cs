using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S10: Type picker — nested category tree layout.</summary>
public sealed class S10_TypePicker : Scenario
{
    public override string Name        => "10 — Type Picker (Nested)";
    public override string Description => "Click 'Pick Type' in the overlay to open a Tree-layout type picker.";

    public override void SetupHost(FakeHostServices host)
    {
        // Enforce boundary: register the mock source strictly for this scenario
        host.Pickers.Register("demo.types.all", new TypePickerSource(host.TypeSystem));
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Math.Add",      new Vector2(200, 200));
        AddNode(graph, catalog, "Math.Multiply", new Vector2(450, 200));
    }

    public override void DrawOverlay(IEditorHostServices host)
    {
        if (ImGui.SmallButton("Pick Type"))
        {
            host.Pickers.Open("demo.types.all", ImGui.GetMousePos(), pick =>
            {
                if (host is FakeHostServices fakeHost && pick is TypeKey chosenType)
                {
                    // Route the selection to the notification queue
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

    private sealed class TypePickerSource : IPickerSource<TypeKey>
    {
        private readonly ITypeSystem _types;
        public TypePickerSource(ITypeSystem t) { _types = t; }

        public string Title => "Pick a Type";
        public string EmptyResultText => "No matching types.";
        public PickerLayout PreferredLayout => PickerLayout.Tree;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost => QueryCost.Cheap;
        public bool IsAsync => false;
        public bool AllowsDragOut => false;
        public bool AllowsDragIn => false;
        public bool AllowArbitraryTextInput => false;

        public IReadOnlyList<TypeKey> Query(string text, IReadOnlyDictionary<string, object?>? context)
        {
            return new[]
            {
                new TypeKey("System.Boolean"),
                new TypeKey("System.Int32"),
                new TypeKey("System.Single"),
                new TypeKey("System.String"),
                new TypeKey("System.Numerics.Vector2"),
                new TypeKey("System.Numerics.Vector3"),
                new TypeKey("System.Numerics.Vector4"),
                new TypeKey("System.Numerics.Quaternion"),
                new TypeKey("NodeEditor.Color"),
            }.Where(t => text.Length == 0 || t.Id.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public Task<IReadOnlyList<TypeKey>> QueryAsync(
            string text, 
            IReadOnlyDictionary<string, object?>? context, 
            CancellationToken ct) 
            => Task.FromResult(Query(text, context));

        public void RenderItem(TypeKey item, bool selected, bool keyboardFocused, IPickerRenderContext ctx) 
            => ImGui.TextUnformatted(item.Id);

        public void RenderPreview(TypeKey item, IPickerRenderContext ctx) 
            => ImGui.TextDisabled($"(Preview for {item.Id})");

        public bool IsPreviewExpensive(TypeKey item) => false;
        public string GetSearchableText(TypeKey item) => item.Id;
        public string GetItemKey(TypeKey item) => item.Id;
        public bool CanAcceptDrop(object payload) => false;
    }
}
