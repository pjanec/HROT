using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Hrot.AiEditor.Persistence;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> — upgrades a <c>schemaVersion 1</c> <c>.btree.json</c> / <c>.hsm.json</c> DOM to version 2:
/// every action / condition / activity / guard becomes ONE <see cref="BehaviorActionBindingDto"/>.</b>
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §3 (sequence) and §4 B-2 / B-4.
///
/// <para>⭐ It runs INSIDE <c>BTreeJsonServices.Deserialize</c> / <c>HsmJsonServices.Deserialize</c> — the single read
/// chokepoint every production reader goes through (editor and both generators) — so nothing downstream ever sees a v1
/// shape. ⛔ It is pure DOM surgery: no catalog, no type resolution (it runs inside a netstandard2.0 source generator).</para>
///
/// <para>⚠ <b>The migration PRESERVES today's lookups (B-2):</b> an HSM state's one <c>ExpressionTargetField</c> was shared
/// by all four slots, so it goes to EACH slot binding that is set — and a state with no slot yet keeps it on an Activity
/// binding that names nothing, so the state-wide lookup it fed is unchanged; a transition's one field goes to each of its
/// Guard / Action that is set.</para>
/// </summary>
public static class ActionBindingMigrator
{
    /// <summary>The schema version this migrator produces.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The <c>$meta.schemaVersion</c> of a document, or 1 when it has none (legacy files carry no envelope).</summary>
    public static int SchemaVersionOf(JsonObject root)
    {
        if (root["$meta"] is JsonObject meta && meta["schemaVersion"] is JsonValue v && v.TryGetValue(out int n))
            return n;
        return 1;
    }

    // ── BTree ─────────────────────────────────────────────────────────────────────────────

    /// <summary>v1 → v2 for a BTree: <c>DelegateShape</c> moves from the payload to the node; the payload IS the binding.</summary>
    public static void UpgradeBTree(JsonObject root)
    {
        if (SchemaVersionOf(root) >= CurrentSchemaVersion) return;
        if (root["Nodes"] is not JsonArray nodes) return;

        foreach (var n in nodes)
        {
            if (n is not JsonObject node) continue;
            string? kind = (node["kind"] as JsonValue)?.GetValue<string>();
            string? slot = kind == "Action" ? "Action" : kind == "Condition" ? "Condition" : null;
            if (slot == null || node[slot] is not JsonObject payload) continue;

            if (payload.TryGetPropertyValue("DelegateShape", out var shape))
            {
                payload.Remove("DelegateShape");
                node["DelegateShape"] = shape?.DeepClone();
            }
            // v1 always wrote MethodFqn (default ""); v2 omits an absent method.
            if (payload["MethodFqn"] is JsonValue m && m.TryGetValue(out string? fqn) && string.IsNullOrEmpty(fqn))
                payload.Remove("MethodFqn");
        }
    }

    // ── HSM ───────────────────────────────────────────────────────────────────────────────

    /// <summary>v1 → v2 for an HSM: four state slots, a transition's / global transition's guard and action.</summary>
    public static void UpgradeHsm(JsonObject root)
    {
        if (SchemaVersionOf(root) >= CurrentSchemaVersion) return;

        if (root["States"] is JsonArray states)
        {
            foreach (var n in states)
            {
                if (n is not JsonObject st) continue;
                string? etf = TakeString(st, "ExpressionTargetField");

                // v1: all four slots shared the ONE state field ⇒ each slot that is set starts with it (B-2).
                bool anySlot = MoveMethod(st, "OnEntryAction", "OnEntry", etf);
                anySlot |= MoveMethod(st, "OnExitAction", "OnExit", etf);
                anySlot |= MoveMethod(st, "TimerAction", "Timer", etf);

                var activity = Binding(TakeString(st, "ActivityAction"),
                                       TakeString(st, "ActivityBlueprintAssetId"),
                                       TakeString(st, "ActivityBlueprintName"));
                if (activity != null)
                {
                    if (etf != null) activity["ExpressionTargetField"] = etf;
                    st["Activity"] = activity;
                }
                else if (!anySlot && etf != null)
                {
                    // A field bound before any action was chosen: it lives on an Activity binding with no method
                    // and no blueprint yet, so the state-wide lookup it fed is unchanged.
                    st["Activity"] = new JsonObject { ["ExpressionTargetField"] = etf };
                }
            }
        }

        if (root["Transitions"] is JsonArray transitions)
        {
            foreach (var n in transitions)
            {
                if (n is not JsonObject tr) continue;
                string? etf = TakeString(tr, "ExpressionTargetField");
                var guard = Binding(TakeString(tr, "GuardFunction"),
                                    TakeString(tr, "GuardBlueprintAssetId"),
                                    TakeString(tr, "GuardBlueprintName"));
                var action = Binding(TakeString(tr, "ActionFunction"), null, null);

                if (guard != null)
                {
                    if (etf != null) guard["ExpressionTargetField"] = etf;
                    tr["Guard"] = guard;
                }
                if (action != null)
                {
                    if (etf != null) action["ExpressionTargetField"] = etf;
                    tr["Action"] = action;
                }
            }
        }

        if (root["GlobalTransitions"] is JsonArray globals)
        {
            foreach (var n in globals)
            {
                if (n is not JsonObject gt) continue;
                string? etf = TakeString(gt, "ExpressionTargetField");
                var guard = Binding(TakeString(gt, "GuardFunction"), null, null);
                var action = Binding(TakeString(gt, "ActionFunction"), null, null);
                if (guard != null) { if (etf != null) guard["ExpressionTargetField"] = etf; gt["Guard"] = guard; }
                if (action != null) { if (etf != null) action["ExpressionTargetField"] = etf; gt["Action"] = action; }
            }
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private static bool MoveMethod(JsonObject owner, string oldName, string newName, string? etf)
    {
        var b = Binding(TakeString(owner, oldName), null, null);
        if (b == null) return false;
        if (etf != null) b["ExpressionTargetField"] = etf;
        owner[newName] = b;
        return true;
    }

    /// <summary>A binding object from a method and/or a blueprint pair; null when all three are absent.</summary>
    private static JsonObject? Binding(string? method, string? blueprintId, string? blueprintName)
    {
        bool hasBlueprint = blueprintId != null && Guid.TryParse(blueprintId, out var g) && g != Guid.Empty;
        if (string.IsNullOrEmpty(method) && !hasBlueprint) return null;
        var b = new JsonObject();
        if (!string.IsNullOrEmpty(method)) b["MethodFqn"] = method;
        if (hasBlueprint)
        {
            b["BlueprintAssetId"] = blueprintId;
            if (!string.IsNullOrEmpty(blueprintName)) b["BlueprintName"] = blueprintName;
        }
        return b;
    }

    /// <summary>Removes a property and returns its string value (null when absent, JSON null, or not a string).</summary>
    private static string? TakeString(JsonObject owner, string name)
    {
        if (!owner.TryGetPropertyValue(name, out var node)) return null;
        owner.Remove(name);
        return node is JsonValue v && v.TryGetValue(out string? s) ? s : null;
    }
}
