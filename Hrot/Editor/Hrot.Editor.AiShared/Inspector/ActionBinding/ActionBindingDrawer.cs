using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Inspector.ActionBinding;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> slice 4b — THE drawer for a binding, every site, both hosts.</b>
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.4.
///
/// <para>Draws a <see cref="BehaviorActionBindingFacet"/> leaf (made one leaf by <see cref="BehaviorActionBindingFieldEditor"/>):
/// a method combo, a blueprint combo when the field's <see cref="ActionBindingAttribute.AllowsBlueprint"/> says so, and the
/// target variable — a combo filtered by THIS binding's own method or blueprint, "Promote to new variable" when nothing
/// fits, or a note for a whole-blackboard binding. ⭐ Because it sees the whole binding, no side channel carries "the
/// selected node's method" to a per-field drawer any more.</para>
///
/// <para>⭐ Method and blueprint are mutually exclusive (design §9 ③): picking one clears the other
/// (<see cref="PickMethod"/>, <see cref="PickBlueprint"/>). The validator still rejects an asset that names both.</para>
/// </summary>
public sealed class ActionBindingDrawer : IImGuiFieldDrawer
{
    private readonly ActionBindingSources _sources;

    public ActionBindingDrawer(ActionBindingSources sources)
        => _sources = sources ?? throw new ArgumentNullException(nameof(sources));

    public Type TargetType => typeof(BehaviorActionBindingFacet);

    /// <summary>The lists this drawer shows.</summary>
    public ActionBindingSources Sources => _sources;

    /// <summary>The slot attribute on <paramref name="node"/>'s field, or an Action slot without blueprints.</summary>
    public static ActionBindingAttribute SlotOf(EditNode? node)
        => node?.Metadata.CustomAttributes.OfType<ActionBindingAttribute>().FirstOrDefault()
           ?? new ActionBindingAttribute(BindingSlotKind.Action);

    /// <summary>Picks a method (empty = none). ⭐ A method replaces a blueprint.</summary>
    public static void PickMethod(ref BehaviorActionBindingFacet b, string? fqn)
    {
        b.MethodFqn = string.IsNullOrEmpty(fqn) ? null : fqn;
        if (b.MethodFqn is not null) b.BlueprintName = null;
    }

    /// <summary>Picks a blueprint (empty = none). ⭐ A blueprint replaces a method.</summary>
    public static void PickBlueprint(ref BehaviorActionBindingFacet b, string? name)
    {
        b.BlueprintName = string.IsNullOrEmpty(name) ? null : name;
        if (b.BlueprintName is not null) b.MethodFqn = null;
    }

    /// <summary>Picks the target variable (empty = none).</summary>
    public static void PickVariable(ref BehaviorActionBindingFacet b, string? name)
        => b.ExpressionTargetField = string.IsNullOrEmpty(name) ? null : name;

    /// <inheritdoc/>
    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        if (value is not BehaviorActionBindingFacet b) return false;

        var slot    = SlotOf(node);
        bool changed = false;
        float width = Math.Max(60f, ImGuiNET.ImGui.GetContentRegionAvail().X - 70f);

        // ── method ──
        ImGuiNET.ImGui.SetNextItemWidth(width);
        var methods = _sources.GetMethods(slot.Kind);
        string? picked;
        if (Combo("method##abm", b.MethodFqn, methods, _sources.MethodLabel, out picked))
        {
            PickMethod(ref b, picked);
            changed = true;
        }

        // ── blueprint ──
        if (slot.AllowsBlueprint)
        {
            ImGuiNET.ImGui.SetNextItemWidth(width);
            if (Combo("blueprint##abb", b.BlueprintName, _sources.GetBlueprints(), null, out picked))
            {
                PickBlueprint(ref b, picked);
                changed = true;
            }
        }

        // ── target variable ──
        if (b.TargetsWholeBlackboard)
        {
            ImGuiNET.ImGui.TextDisabled("Operates on the full blackboard — no per-binding variable.");
        }
        else if (_sources.HasNoCompatibleVariables(b))
        {
            ImGuiNET.ImGui.TextDisabled(ActionBindingCompatibility.NoCompatibleVariablesDisplay);
            ImGuiNET.ImGui.SameLine();
            if (ImGuiNET.ImGui.SmallButton("Promote to new variable") && _sources.Promote(b) is { } newName)
            {
                // ⭐ B-2 / Corrective Task 0 — create the variable AND bind this binding to it, in one gesture.
                PickVariable(ref b, newName);
                changed = true;
            }
            if (ImGuiNET.ImGui.IsItemHovered())
                ImGuiNET.ImGui.SetTooltip(
                    "Creates a blackboard variable of this binding's parameter type and binds this binding to it.\n\n" +
                    "The parameters are stored in the entity's blackboard at a fixed offset, are editable here, " +
                    "and can be shared with other bindings that target the same variable (zero-copy).");
        }
        else
        {
            ImGuiNET.ImGui.SetNextItemWidth(width);
            if (Combo("variable##abv", b.ExpressionTargetField, _sources.GetVariables(b), null, out picked))
            {
                PickVariable(ref b, picked);
                changed = true;
            }
        }

        if (changed) value = b;
        return changed;
    }

    /// <summary>One combo with a "(none)" row; the ImGui id of each row is its VALUE so equal labels stay distinct.</summary>
    private static bool Combo(string id, string? current, IReadOnlyList<string> items,
                              Func<string, string>? label, out string? picked)
    {
        picked = null;
        current ??= string.Empty;
        bool changed = false;
        string shown = current.Length == 0 ? "(none)" : label?.Invoke(current) ?? current;
        if (ImGuiNET.ImGui.BeginCombo(id, shown))
        {
            if (ImGuiNET.ImGui.Selectable("(none)", current.Length == 0) && current.Length != 0)
            {
                picked  = string.Empty;
                changed = true;
            }
            foreach (var name in items)
            {
                bool sel = name == current;
                if (ImGuiNET.ImGui.Selectable($"{label?.Invoke(name) ?? name}##{name}", sel) && !sel)
                {
                    picked  = name;
                    changed = true;
                }
                if (sel) ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }
        return changed;
    }
}
