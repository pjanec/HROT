using System;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// ⭐⭐⭐ <c>CE-308</c> — the slot picker for <see cref="BehaviorParamPredicateDto.WorkingSlotKey"/>.
/// Offers the sibling behaviour's ROOT PARAMS plus each of its typed working-state slots, by label
/// and scope, and stores the chosen <c>SlotKey</c>.
///
/// <para>⭐⭐ <b>This is what the retired <c>BlackboardTarget</c> enum could not express.</b> A
/// two-member enum names a REGION; a behaviour with two stateful actions has two working-state
/// regions and the enum could not say which. ⇒ the picker lists the slots the behaviour actually
/// declares, so the ambiguous case is the ordinary case rather than an unsupported one.</para>
///
/// <para>⚠ <b>The choices come from <see cref="BehaviorParamSlotResolver"/></b> — the same type the
/// compiler binds through. ⛔ Offering a slot the compiler cannot bind would produce a search that
/// silently matches nothing.</para>
/// </summary>
internal sealed class WorkingSlotFieldDrawer : IImGuiFieldDrawer
{
    private readonly BehaviorRegistry _registry;
    private readonly IEditSession _session;

    public WorkingSlotFieldDrawer(IEditSession session, BehaviorRegistry registry)
    {
        _session  = session  ?? throw new ArgumentNullException(nameof(session));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public Type TargetType => typeof(int);

    public bool DrawInput(ref object value, EditNode node)
    {
        int current = value is int i ? i : BehaviorParamSlotResolver.RootParamsSlotKey;

        var def = ResolveSiblingBehavior(node);
        var choices = BehaviorParamSlotResolver.GetChoices(def);

        if (choices.Count == 0)
        {
            // ⛔ No behaviour chosen yet, or it declares no typed region. Say so rather than showing
            //    an empty combo the user cannot act on — and rather than an InputInt, which would
            //    invite typing a raw key that nothing validates.
            ImGuiApi.TextDisabled(def == null ? "(pick a behavior first)" : "(no typed regions)");
            return false;
        }

        string currentLabel = choices.FirstOrDefault(c => c.SlotKey == current).Label
                              ?? $"(unknown slot 0x{current:X8})";

        bool changed = false;
        if (ImGuiApi.BeginCombo("##wslot", currentLabel))
        {
            foreach (var choice in choices)
            {
                bool selected = choice.SlotKey == current;
                if (selected) ImGuiApi.SetItemDefaultFocus();
                if (ImGuiApi.Selectable($"{choice.Label}  ({choice.DtoType.Name})", selected))
                {
                    value   = choice.SlotKey;
                    changed = true;
                }
            }
            ImGuiApi.EndCombo();
        }
        return changed;
    }

    /// <summary>
    /// Reads the sibling <see cref="BehaviorParamPredicateDto.BehaviorId"/> and resolves its
    /// definition. ⚠ Mirrors the sibling walk the two value drawers already do — the predicate's
    /// fields are siblings under one parent node, and a field drawer sees only its own node.
    /// </summary>
    private BehaviorDefinition? ResolveSiblingBehavior(EditNode node)
    {
        string jsonPath = node.JsonPath;
        int lastDot = jsonPath.LastIndexOf('.');
        if (lastDot <= 0) return null;

        EditNode? parent = FindNodeByPath(_session.Document.Root, jsonPath.Substring(0, lastDot));
        if (parent == null) return null;

        foreach (EditNode child in parent.Children)
        {
            if (child.Name != nameof(BehaviorParamPredicateDto.BehaviorId)) continue;
            if (child.Binding?.GetBoxed() is int hash && hash != 0
                && _registry.TryGetDefinition(hash, out var def))
                return def;
            return null;
        }
        return null;
    }

    private static EditNode? FindNodeByPath(EditNode current, string targetPath)
    {
        if (current.JsonPath == targetPath) return current;
        foreach (EditNode child in current.Children)
        {
            var found = FindNodeByPath(child, targetPath);
            if (found != null) return found;
        }
        return null;
    }
}
