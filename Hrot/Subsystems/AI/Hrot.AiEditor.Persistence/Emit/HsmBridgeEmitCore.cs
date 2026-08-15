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

        // ⛔⛔ W3 (Batch 59) — THE COUNTER-ALLOCATED STUB REGISTRATIONS ARE GONE.
        //
        // This used to emit, for each action FQN in the asset:
        //     static void __hsActionStub(void*, void*, HsmCommandWriter*) { }
        //     HsmActionDispatcher.RegisterAction(100++, &__hsActionStub);
        // and the guard twin from 200. ⭐ Two facts, both measured, make that pure hazard:
        //
        //   1. 🔴 NOTHING EVER LOOKED THEM UP. `HsmFlattener:111` builds its action table as
        //      `actionTable[name] = ComputeHash(name)` and `:172-175` / `:233` / `:376` set every
        //      `OnEntryActionId` / `ActivityActionId` / transition `ActionId` from THAT table. ⇒ the
        //      blob addresses hashed ids only; 100.. and 200.. were never reachable. (The one bypass,
        //      a numeric `entryActionId` in the JSON — `JsonStateMachineParser:48` — is used by
        //      neither shipped asset.)
        //   2. 🔴🔴 AND THEY COULD OVERWRITE A REAL ACTION. `HsmActionDispatcher.RegisterAction` is
        //      `ActionTable[id] = a` — last writer wins, silently — while `ComputeHash` ranges over the
        //      whole `0…65535`, INCLUDING 100.. and 200… A real action whose name hashed into the
        //      window was replaced by a body that does nothing: no crash, no log, one state that
        //      quietly did nothing, forever.
        //
        // ⚠ Nothing is lost by deleting them. The bodies they registered were empty, so even in the
        //   hot-reload case the comment invoked ("the bridge ensures the IDs are known to the
        //   dispatcher"), what was known was a no-op. The real bodies come from the hand-authored
        //   `[HsmAction]` methods, registered by `HsmActionRegistrar.RegisterAll()` under the hashed
        //   ids the blob actually uses.
        //
        // ⭐ `BHU_020` (Batch 58) is the rail that proves this stays gone: it ranges over the FINAL id
        //   set, so a reintroduced counter-allocated registration colliding with a hashed one fails
        //   the build instead of silently winning.

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

    // ⚠ W3 (Batch 59) removed `CollectActions`/`CollectGuards` with the stub registrations they fed.
    //   ⛔ They are NOT a general "what does this asset call" service — nothing else read them, and the
    //   authoritative answer already lives in `HsmFlattener`'s action table, which is what the blob is
    //   addressed by. Keeping an unused collector here would have been a second source for a question
    //   that already has one.

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
