using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// Emits the <c>[BlueprintRegistrar]</c> self-registration bridge class for an HSM asset.
/// Design §3 D14, §6.3, §14 (PU-203): emits a per-asset isolated static class decorated
/// <c>[BlueprintRegistrar]</c> (NOT <c>[FbtRegistrar]</c>/<c>[HsmActionRegistrar]</c>) with
/// <c>public static void Register(BehaviorRegistry beh, BlueprintRegistryStaging staging)</c>.
///
/// Inside Register:
/// - Compiles the HSM definition blob from the topology-core thunk and calls
///   <c>beh.Register(id, name, BehaviorDefinition)</c> with the HsmDefinition.
/// - Registers HSM action thunks via the STATIC
///   <c>HsmActionDispatcher.RegisterAction(ushort, IntPtr)</c>.
/// - Registers HSM guard thunks via the STATIC
///   <c>HsmActionDispatcher.RegisterGuard(ushort, IntPtr)</c>.
///   (HsmActionDispatcher is a static class and cannot be injected; §14 item 4.)
///
/// The bridge is ADDITIVE: a separate class from the topology-core class (PU-205
/// equivalence compares only the topology core; bridge is excluded per §14 item 3).
/// BTree bridge is analogous — see <see cref="BTreeBridgeEmitCore"/>.
/// </summary>
public static class HsmBridgeEmitCore
{
    private const string Indent = "    ";

    /// <summary>
    /// Emits the [BlueprintRegistrar] bridge class source for the given HSM DTO.
    /// </summary>
    public static string EmitBridge(HsmAssetDto dto)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine(AiEmitCoreBase.BuildHeader(dto.AssetId));

        // Usings
        var usings = CollectBridgeUsings(dto);
        foreach (var ns in usings)
        {
            if (ns.Length == 0)
                sb.AppendLine();
            else
                sb.AppendLine($"using {ns};");
        }
        sb.AppendLine();

        var targetNs    = string.IsNullOrEmpty(dto.TargetNamespace)
            ? "Hrot.AI.Behaviors.Machines"
            : dto.TargetNamespace;
        var coreClass   = SanitizeIdentifier(dto.Name);
        var bridgeClass = coreClass + "Registrar";

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();

        // [BlueprintRegistrar] ONLY — not [FbtRegistrar]/[HsmActionRegistrar] (§14 item 4).
        sb.AppendLine($"[BlueprintRegistrar]");
        sb.AppendLine($"public static class {bridgeClass}");
        sb.AppendLine("{");

        EmitHsmRegisterMethod(sb, dto, coreClass);

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Register method ─────────────────────────────────────────────────────────

    private static void EmitHsmRegisterMethod(
        StringBuilder sb, HsmAssetDto dto, string coreClass)
    {
        string pad  = Indent;
        string pad2 = Indent + Indent;

        int behaviorId = BTreeBridgeEmitCore.DeterministicIdFromGuid(dto.AssetId);
        string name    = dto.Name.Replace("\"", "\\\"");

        sb.AppendLine($"{pad}/// <summary>");
        sb.AppendLine($"{pad}/// Coordinator-injectable registrar (§3 D14, PU-203).");
        sb.AppendLine($"{pad}/// Registers the JSON-owned HSM definition and action/guard thunks.");
        sb.AppendLine($"{pad}/// HsmActionDispatcher is a static class and is called STATICALLY (§14 item 4).");
        sb.AppendLine($"{pad}/// </summary>");
        sb.AppendLine($"{pad}public static void Register(BehaviorRegistry beh, BlueprintRegistryStaging staging)");
        sb.AppendLine($"{pad}{{");

        // Build blob
        sb.AppendLine($"{pad2}// Compile the blob from the topology-core thunk.");
        sb.AppendLine($"{pad2}var blob = {coreClass}.Compile();");
        sb.AppendLine();

        // Register definition
        sb.AppendLine($"{pad2}// Register the JSON-owned HSM definition.");
        sb.AppendLine($"{pad2}beh.Register({behaviorId}, \"{name}\", new BehaviorDefinition");
        sb.AppendLine($"{pad2}{{");
        sb.AppendLine($"{pad2}{Indent}Name          = \"{name}\",");
        sb.AppendLine($"{pad2}{Indent}BrainTier     = BehaviorConstants.BrainTierHsm,");
        sb.AppendLine($"{pad2}{Indent}HsmDefinition = blob,");
        sb.AppendLine($"{pad2}}});");

        // HSM action thunks — static HsmActionDispatcher calls (§14 item 4)
        var actions = CollectActions(dto);
        if (actions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad2}// HSM action thunks registered STATICALLY via HsmActionDispatcher.");
            // Each HSM action is emitted as a stub with nop delegate; real action bodies
            // come from the hand-authored [BTreeAction]/action methods in Brains/*.cs.
            // The bridge merely ensures the IDs are known to the dispatcher after hot reload.
            ushort actionId = 100; // placeholder IDs for JSON-owned HSM thunks
            foreach (var fqn in actions)
            {
                sb.AppendLine($"{pad2}// Action: {fqn}");
                sb.AppendLine($"{pad2}unsafe {{ HsmActionDispatcher.RegisterAction({actionId++},");
                sb.AppendLine($"{pad2}{Indent}(System.IntPtr)(delegate* <void*, void*, Fhsm.Kernel.Data.HsmCommandWriter*, void>)");
                sb.AppendLine($"{pad2}{Indent}static (void* inst, void* ctx, Fhsm.Kernel.Data.HsmCommandWriter* w) => {{ }}); }}");
            }
        }

        // HSM guard thunks
        var guards = CollectGuards(dto);
        if (guards.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad2}// HSM guard thunks registered STATICALLY via HsmActionDispatcher.");
            ushort guardId = 200;
            foreach (var fqn in guards)
            {
                sb.AppendLine($"{pad2}// Guard: {fqn}");
                sb.AppendLine($"{pad2}unsafe {{ HsmActionDispatcher.RegisterGuard({guardId++},");
                sb.AppendLine($"{pad2}{Indent}(System.IntPtr)(delegate* <void*, void*, ushort, bool>)");
                sb.AppendLine($"{pad2}{Indent}static (void* inst, void* ctx, ushort ev) => true); }}");
            }
        }

        sb.AppendLine($"{pad}}}");
    }

    // ── Usings ─────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> CollectBridgeUsings(HsmAssetDto dto)
    {
        var set = new HashSet<string>
        {
            "Fdp.Toolkit.Behavior",
            "Fdp.Toolkit.Blueprints",
            "Fdp.Toolkit.Blueprints.Attributes",
            "Fhsm.Kernel",
            "Fhsm.Kernel.Data",
        };
        return AiEmitCoreBase.SortUsings(set);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static List<string> CollectActions(HsmAssetDto dto)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in dto.States)
        {
            if (s.OnEntryAction  != null) set.Add(s.OnEntryAction);
            if (s.OnExitAction   != null) set.Add(s.OnExitAction);
            if (s.ActivityAction != null) set.Add(s.ActivityAction);
            if (s.TimerAction    != null) set.Add(s.TimerAction);
        }
        foreach (var t in dto.Transitions)
            if (t.ActionFunction != null) set.Add(t.ActionFunction);
        foreach (var gt in dto.GlobalTransitions)
            if (gt.ActionFunction != null) set.Add(gt.ActionFunction);
        return new List<string>(set);
    }

    private static List<string> CollectGuards(HsmAssetDto dto)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in dto.Transitions)
            if (t.GuardFunction != null) set.Add(t.GuardFunction);
        foreach (var gt in dto.GlobalTransitions)
            if (gt.GuardFunction != null) set.Add(gt.GuardFunction);
        return new List<string>(set);
    }

    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        string result = sb.ToString();
        if (result.Length == 0 || char.IsDigit(result[0]))
            result = "_" + result;
        return result;
    }
}
