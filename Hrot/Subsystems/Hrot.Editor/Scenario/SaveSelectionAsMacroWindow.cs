using System;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using ImGuiNET;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐ CE-4210 MA-9 — the dialog of <see cref="SaveSelectionAsMacroFlow"/>: the name, then — when the selection refers to entities outside it — the list of every such reference
/// (<c>entity · field · target</c>) with <i>Make parameters</i> and <i>Cancel</i>. A macro is closed (SR-58): there is no third choice that saves the dangling reference.
/// </summary>
public sealed class SaveSelectionAsMacroWindow : ManagedWindow
{
    public const string WindowId = "editor_save_selection_macro";

    private readonly SaveSelectionAsMacroFlow _flow;

    /// <summary>What the last frame drew (read by the frame rail).</summary>
    public int ReferenceLines { get; private set; }
    public bool MakeParametersEnabled { get; private set; }
    public int BlockerLines { get; private set; }

    public SaveSelectionAsMacroWindow(SaveSelectionAsMacroFlow flow)
        : base(WindowId, "Save selection as macro", "Scenario", WindowScope.PerspectiveBound)
    {
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        IsOpen = false;
        ShowInMenu = false;
    }

    public SaveSelectionAsMacroFlow Flow => _flow;

    /// <summary>Starts the gesture (the menu item and the map's entity menu call this): opens the window at the naming step, or says why nothing can be saved.</summary>
    public void Start() { _flow.Begin(); IsOpen = true; }

    /// <summary>Draws the dialog's body (what the window does inside its frame) — for a frame rail.</summary>
    public void DrawBody() => DrawClientArea();

    protected override void DrawClientArea()
    {
        ReferenceLines = BlockerLines = 0; MakeParametersEnabled = false;
        switch (_flow.Step)
        {
            case SaveSelectionAsMacroFlow.StepKind.Closed:
                IsOpen = false;
                return;

            case SaveSelectionAsMacroFlow.StepKind.Naming:
            {
                ImGui.TextUnformatted("Save the selected entities as a macro under macros/");
                string name = _flow.Name;
                NodeEditor.Core.Widgets.FormGrid.BeginField("macro.rows", "Name");   // ⭐ CE-3396 — a label-first row
                ImGui.SetNextItemWidth(320f);
                bool entered = ImGui.InputText("##macroname", ref name, 96, ImGuiInputTextFlags.EnterReturnsTrue);
                NodeEditor.Core.Widgets.FormGrid.EndField();
                if (entered) { _flow.Name = name; _flow.Confirm(); }
                else _flow.Name = name;
                if (_flow.Message.Length > 0) NodeEditor.Core.Widgets.RowMarks.ErrorLine(_flow.Message);
                if (ImGui.Button("Save##macro")) _flow.Confirm();
                ImGui.SameLine();
                if (ImGui.Button("Cancel##macro")) _flow.Cancel();
                return;
            }

            case SaveSelectionAsMacroFlow.StepKind.Closure:
            {
                var plan = _flow.Plan!;
                ImGui.TextWrapped($"A macro is closed: it may not refer to anything outside it. The {plan.Selected.Count} selected entit{(plan.Selected.Count == 1 ? "y refers" : "ies refer")} to {plan.Outside.Count} outside:");
                ImGui.Separator();
                foreach (var o in plan.Outside)
                {
                    ReferenceLines++;
                    ImGui.BulletText(o.Line);
                    if (!o.CanParameterise && o.WhyNot is { } why) { BlockerLines++; ImGui.SameLine(); NodeEditor.Core.Widgets.RowMarks.WarningLine("(" + why + ")"); }
                }
                ImGui.Separator();
                MakeParametersEnabled = plan.CanMakeParameters;
                ImGui.TextWrapped(plan.CanMakeParameters
                    ? $"Make parameters: each outside target becomes a parameter of the macro ({plan.Params.Count}), the field bound to it. The scenario on screen is not changed."
                    : "Some of these cannot become parameters, so this selection cannot be saved as a macro yet.");
                ImGui.BeginDisabled(!plan.CanMakeParameters);
                if (ImGui.Button("Make parameters##macro")) _flow.MakeParameters();
                ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.Button("Cancel##macro2")) _flow.Cancel();
                return;
            }

            default:
                ImGui.TextWrapped(_flow.Message);
                if (ImGui.Button("OK##macro")) _flow.Dismiss();
                return;
        }
    }
}
