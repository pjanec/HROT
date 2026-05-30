using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S09: Variable picker — pick a variable from the list.</summary>
public sealed class S09_VariablePicker : Scenario
{
    public override string Name        => "09 — Variable Picker";
    public override string Description => "Canvas shows a 'Pick Variable' button. Click it to open a Compact-layout picker.";

    public override void SetupHost(FakeHostServices host)
    {
        host.Pickers.Register("demo.variables", new FakeVariablePickerSource());
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Util.GetVar", new Vector2(200, 200));
        AddNode(graph, catalog, "Util.SetVar", new Vector2(450, 200));
    }

    public override void DrawOverlay(IEditorHostServices host)
    {
        if (!ImGui.SmallButton("Pick Variable"))
            return;

        host.Pickers.Open("demo.variables", ImGui.GetMousePos(), pick =>
        {
            host.Diagnostics?.Log(DiagnosticSeverity.Info, $"Picked variable: {pick}");
        });
    }

    private sealed class FakeVariablePickerSource : IPickerSource<string>
    {
        public string Title => "Pick Variable";
        public string EmptyResultText => "No variables found.";
        public PickerLayout PreferredLayout => PickerLayout.Compact;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost => QueryCost.Cheap;
        public bool IsAsync => false;
        public bool AllowsDragOut => false;
        public bool AllowsDragIn => false;
        public bool AllowArbitraryTextInput => false;

        private readonly string[] _vars = ["Health", "Speed", "PlayerName", "IsActive"];

        public IReadOnlyList<string> Query(string text, IReadOnlyDictionary<string, object?>? context) => _vars;

        public Task<IReadOnlyList<string>> QueryAsync(
            string text,
            IReadOnlyDictionary<string, object?>? context,
            CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Query(text, context));

        public void RenderItem(string item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
            => ImGui.TextUnformatted(item);

        public void RenderPreview(string item, IPickerRenderContext ctx)
            => ImGui.TextDisabled("(No preview in Compact layout)");

        public bool IsPreviewExpensive(string item) => false;
        public string GetSearchableText(string item) => item;
        public string GetItemKey(string item) => item;
        public bool CanAcceptDrop(object payload) => false;
    }
}
