using System;
using System.Collections.Generic;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>What the compilation knows about a <c>[SharedAiAction]</c> / <c>[SharedAiCondition]</c> method.</summary>
/// <param name="ParamTypeFqn">The method's first <c>ref</c> parameter type, <c>global::</c>-qualified.</param>
/// <param name="ParamTypeId">The same type as a blackboard <c>TypeId</c> (metadata name, <c>+</c> for nesting).</param>
/// <param name="IsCondition">True for <c>[SharedAiCondition]</c>.</param>
/// <param name="ReturnsBool">True when the method returns <c>bool</c>; otherwise it returns <c>NodeStatus</c>.</param>
/// <param name="WritesChannels">Its <c>[WritesChannel(kind)]</c> kinds — a BTree call releases them on <c>Failure</c>.</param>
public sealed class SharedAiMethodInfo
{
    public SharedAiMethodInfo(string paramTypeFqn, string paramTypeId, bool isCondition, bool returnsBool,
        IReadOnlyList<int>? writesChannels = null, string? workingStateTypeFqn = null)
    {
        ParamTypeFqn = paramTypeFqn; ParamTypeId = paramTypeId; IsCondition = isCondition; ReturnsBool = returnsBool;
        WritesChannels = writesChannels ?? Array.Empty<int>();
        WorkingStateTypeFqn = workingStateTypeFqn;
    }

    /// <summary>⭐ <c>CE-504</c> C-2 — the stateful form <c>(ref P, ref WS, Entity, EntityRepository)</c>: WS's <c>global::</c> type.</summary>
    public string? WorkingStateTypeFqn { get; }

    /// <summary>⭐ <c>CE-504</c> C-2 — false for the param-less form <c>(Entity, EntityRepository)</c>; its param types are empty.</summary>
    public bool HasParams => ParamTypeFqn.Length > 0;

    /// <summary>The plain form <c>(ref P, Entity, EntityRepository)</c>.</summary>
    public bool IsPlain => HasParams && WorkingStateTypeFqn == null;
    public IReadOnlyList<int> WritesChannels { get; }
    public string ParamTypeFqn { get; }
    public string ParamTypeId  { get; }
    public bool   IsCondition  { get; }
    public bool   ReturnsBool  { get; }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> B-2 (a′) — one generated call PER BINDING for a C# <c>[SharedAi*]</c> method in an HSM asset.</b>
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §4 B-2, F4/F7.
///
/// <para>🔴 <b>What it replaces.</b> <c>HsmActionGenerator</c> emitted one thunk PER METHOD, keyed <c>Fqn@offset</c> where the
/// offset was the attribute DTO's field offset, and added the source state's seed base at run time. ⇒ an asset's binding
/// (<c>Fqn@hostOffset</c>) found it only when the two offsets happened to agree (F7), a transition action read through its
/// SOURCE state's base (F4), and every call built a string and searched the occurrence store (F9).</para>
///
/// <para>⭐ <b>Now:</b> the asset's own registrar emits one thunk per <c>(method, host offset)</c>, the offset BAKED from the
/// asset's packer — exactly what <c>BTreeBridgeEmitCore</c> already does for a BTree binding. The thunk projects the host
/// variable as the method's <c>ref</c> parameter type and calls it: no occurrence, no base, no allocation. The id is
/// <c>HsmActionKey.ForCompoundKey("Fqn@hostOffset")</c> — the name the blob addresses (<see cref="HsmEmitCore"/>).</para>
/// </summary>
public static class SharedAiBindings
{
    /// <summary>⭐ <c>S8</c> — the three shared C# forms (<c>CE-504</c> C-2), each of which the HSM now binds.</summary>
    public enum Form { NoParams, Plain, Stateful }

    /// <summary>One binding to generate a call for.</summary>
    public sealed class Entry
    {
        public string Key = "";              // the dispatcher name — Fqn@hostOffset (plain), Fqn@hostOffset@slotKey (stateful), Fqn (param-less)
        public string MethodFqn = "";
        public Form Form;
        public int Offset;
        public int Size;
        public string VariableName = "";
        public string VariableTypeId = "";
        public string WsVariable = "";       // S8: the block St member the stateful method's WS is
        public string BlockFqn = "";         // S8: the HSM's block struct, for the St projection
        public SharedAiMethodInfo Method = null!;
        public string Site = "";             // for diagnostics: "state 'X' OnEntry", "transition 'Go' action", …
    }

    /// <summary>
    /// ⭐⭐ <c>S8</c> — the working-state variable a stateful binding names: <c>WorkingStateTargetField</c>, falling back to
    /// <c>ExpressionTargetField</c> — the BTree's rule (<c>BTreeBridgeEmitCore</c>), so one binding means one thing on both hosts.
    /// </summary>
    public static string? WorkingStateVariable(BehaviorActionBindingDto b)
        => string.IsNullOrEmpty(b.WorkingStateTargetField) ? b.ExpressionTargetField : b.WorkingStateTargetField;

    /// <summary>⭐ <c>S8</c> — the stateful slot key, by the BTree's own function (one arithmetic, two hosts).</summary>
    public static int StatefulSlotKey(Guid assetId, string wsVariable)
        => BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, WorkingStateScope.Behavior, Guid.Empty, wsVariable);

    /// <summary>The method's form, or null when it is not a <c>[SharedAi*]</c> method.</summary>
    public static Form? FormOf(SharedAiMethodInfo? info)
        => info == null ? null : !info.HasParams ? Form.NoParams : info.WorkingStateTypeFqn == null ? Form.Plain : Form.Stateful;

    // ⭐ One walk over every binding site, shared by Collect and UnbindableBindings so the two cannot disagree on the set.
    private static void ForEachBinding(HsmAssetDto dto, Action<BehaviorActionBindingDto?, string> visit)
    {
        foreach (var s in dto.States)
        {
            visit(s.OnEntry,  $"state '{s.Name}' OnEntry");
            visit(s.OnExit,   $"state '{s.Name}' OnExit");
            visit(s.Activity, $"state '{s.Name}' Activity");
            visit(s.Timer,    $"state '{s.Name}' Timer");
        }
        foreach (var t in dto.Transitions)
        {
            visit(t.Guard,  $"transition '{t.EventName}' guard");
            visit(t.Action, $"transition '{t.EventName}' action");
        }
        foreach (var g in dto.GlobalTransitions)
        {
            visit(g.Guard,  $"global transition '{g.EventName}' guard");
            visit(g.Action, $"global transition '{g.EventName}' action");
        }
    }

    // ⭐ S8 — the HSM's block St, through the BTree-shaped view the HSM already is (CE-416): (block FQN, slot key → member).
    private static (string BlockFqn, Func<int, string?> Member)? BlockState(HsmAssetDto dto, Func<string, int?>? sizeResolver)
    {
        var owner = HsmBridgeEmitCore.BlackboardOwner(dto);
        if (!owner.Blackboard.Managed || owner.Blackboard.Variables.Count == 0) return null;
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? owned;
        try { owned = BTreeBlackboardPackHelper.Pack(owner.Blackboard.Variables, sizeResolver, out _); }
        catch { return null; }
        string blockFqn = BTreeEmitCore.BlockStructFqn(owner);
        return (blockFqn, key => BTreeBridgeEmitCore.TryGetBlockStateVariable(owner, owned, key, out string? v) ? v : null);
    }

    private static string? VariableTypeId(HsmAssetDto dto, string name)
    {
        foreach (var v in dto.Blackboard?.Variables ?? new List<HsmBlackboardVariableDto>())
            if (string.Equals(v.Name, name, StringComparison.Ordinal)) return v.Type?.TypeId;
        return null;
    }

    private static string Norm(string? t) => (t ?? "").Replace("global::", "").Replace('+', '.');

    // ⭐ S8 — why a stateful binding cannot be bound, or null when it can (its WS is a block St member of the method's WS type).
    private static string? StatefulProblem(
        HsmAssetDto dto, BehaviorActionBindingDto b, SharedAiMethodInfo info,
        (string BlockFqn, Func<int, string?> Member)? block, out string ws)
    {
        ws = WorkingStateVariable(b) ?? "";
        if (ws.Length == 0) return "names no working-state variable (WorkingStateTargetField)";
        if (block == null || block.Value.Member(StatefulSlotKey(dto.AssetId, ws)) == null)
            return $"names working-state variable '{ws}', which is not a Behavior-scoped Role=State variable of this HSM's block";
        string? typeId = VariableTypeId(dto, ws);
        if (!string.Equals(Norm(typeId), Norm(info.WorkingStateTypeFqn), StringComparison.Ordinal))
            return $"names working-state variable '{ws}' of type '{typeId}', but the method works on '{Norm(info.WorkingStateTypeFqn)}'";
        return null;
    }

    /// <summary>Every C# <c>[SharedAi*]</c> binding in <paramref name="dto"/> that can be called, by key.</summary>
    public static IReadOnlyList<Entry> Collect(
        HsmAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packed,
        Func<string, SharedAiMethodInfo?>? sharedAi,
        Func<string, int?>? sizeResolver = null)
    {
        var result = new List<Entry>();
        if (sharedAi == null) return result;

        var byName = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packed) byName[f.Name] = f;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var block = BlockState(dto, sizeResolver);

        ForEachBinding(dto, (b, site) =>
        {
            if (b == null || string.IsNullOrEmpty(b.MethodFqn)) return;
            var info = sharedAi(b.MethodFqn!);
            var form = FormOf(info);
            if (form == null) return;

            // ⭐ S8 — the param-less form binds no variable: one call, addressed by the bare FQN.
            if (form == Form.NoParams)
            {
                if (seen.Add(b.MethodFqn!))
                    result.Add(new Entry { Key = b.MethodFqn!, MethodFqn = b.MethodFqn!, Form = Form.NoParams, Method = info!, Site = site });
                return;
            }

            if (string.IsNullOrEmpty(b.ExpressionTargetField)) return;
            if (!byName.TryGetValue(b.ExpressionTargetField!, out var field)) return;
            string key = Fdp.Toolkit.Behavior.Shared.HsmActionKey.CompoundKeyName(b.MethodFqn, field.ByteOffset);   // ⭐ CE-2032 — the one spelling
            var entry = new Entry
            {
                MethodFqn = b.MethodFqn!, Form = form.Value, Offset = field.ByteOffset, Size = field.ByteSize,
                VariableName = field.Name, VariableTypeId = field.TypeId, Method = info!, Site = site,
            };
            if (form == Form.Stateful)
            {
                // ⭐ S8 — the WS is the block St member (S8-1); an unbindable one is reported by UnbindableBindings.
                if (StatefulProblem(dto, b, info!, block, out string ws) != null) return;
                key = Fdp.Toolkit.Behavior.Shared.HsmActionKey.CompoundKeyName(b.MethodFqn, field.ByteOffset, StatefulSlotKey(dto.AssetId, ws));   // the BTree's Fqn@paramOffset@slotKey (S8-2)
                entry.WsVariable = block!.Value.Member(StatefulSlotKey(dto.AssetId, ws))!;
                entry.BlockFqn = block.Value.BlockFqn;
            }
            if (!seen.Add(key)) return;
            entry.Key = key;
            result.Add(entry);
        });
        return result;
    }

    /// <summary>
    /// ⭐ <c>S8</c> — every HSM binding of a shared method that cannot be called: a stateful method whose working-state variable
    /// is missing, not a block <c>St</c> member, or of the wrong type. ⛔ <c>Collect</c> skips these, so the generator must
    /// REPORT them — a skipped binding is otherwise an unbound node at run time. (Param-less and plain are always bindable;
    /// before <c>S8</c> this reported every non-plain method.)
    /// </summary>
    public static IReadOnlyList<(string Site, string MethodFqn, string Problem)> UnbindableBindings(
        HsmAssetDto dto, Func<string, SharedAiMethodInfo?>? sharedAi, Func<string, int?>? sizeResolver = null)
    {
        var result = new List<(string, string, string)>();
        if (sharedAi == null) return result;
        var block = BlockState(dto, sizeResolver);
        ForEachBinding(dto, (b, site) =>
        {
            if (b == null || string.IsNullOrEmpty(b.MethodFqn)) return;
            var info = sharedAi(b.MethodFqn!);
            if (FormOf(info) != Form.Stateful) return;
            if (StatefulProblem(dto, b, info!, block, out _) is string problem) result.Add((site, b.MethodFqn!, problem));
        });
        return result;
    }

    /// <summary>
    /// The name a binding is addressed by in the blob — the SAME rule <see cref="Collect"/> registers under, so the two can
    /// never disagree: <c>Fqn@hostOffset</c> (plain), <c>Fqn@hostOffset@slotKey</c> (stateful, the BTree's spelling), the bare
    /// FQN for a param-less method or anything that is not a bound <c>[SharedAi*]</c> method.
    /// </summary>
    public static string NameFor(
        BehaviorActionBindingDto b,
        IReadOnlyDictionary<string, int> paramOffsets,
        Func<string, SharedAiMethodInfo?> sharedAi,
        Guid assetId)
    {
        string fqn = b.MethodFqn!;
        var form = FormOf(sharedAi(fqn));
        if (form == null || form == Form.NoParams) return fqn;
        if (string.IsNullOrEmpty(b.ExpressionTargetField)) return fqn;
        if (!paramOffsets.TryGetValue(b.ExpressionTargetField!, out int off)) return fqn;
        if (form == Form.Stateful && WorkingStateVariable(b) is { Length: > 0 } ws)
            return Fdp.Toolkit.Behavior.Shared.HsmActionKey.CompoundKeyName(fqn, off, StatefulSlotKey(assetId, ws));
        return Fdp.Toolkit.Behavior.Shared.HsmActionKey.CompoundKeyName(fqn, off);
    }

    /// <summary>The C# for one thunk method (an action or a guard), with the offset and type baked.</summary>
    public static void EmitThunk(System.Text.StringBuilder sb, Entry e, string thunkName, string pad)
    {
        string call = e.Form switch
        {
            Form.NoParams => $"global::{e.MethodFqn}(__bridge->Self, __repo)",
            Form.Stateful => $"global::{e.MethodFqn}(ref *({e.Method.ParamTypeFqn}*)(__root + {e.Offset}), ref __ws, __bridge->Self, __repo)",
            _             => $"global::{e.MethodFqn}(ref *({e.Method.ParamTypeFqn}*)(__root + {e.Offset}), __bridge->Self, __repo)",
        };
        string what = e.Form switch
        {
            Form.NoParams => "binds no variable",
            Form.Stateful => $"bound to <c>{e.VariableName}</c> at host offset {e.Offset}, working state <c>St.{e.WsVariable}</c>",
            _             => $"bound to <c>{e.VariableName}</c> at host offset {e.Offset}",
        };
        sb.AppendLine($"{pad}/// <summary>CE-417/S8: {e.Site} — <c>{e.Key}</c>, {what}.</summary>");
        if (e.Method.IsCondition)
            sb.AppendLine($"{pad}private static unsafe bool {thunkName}(void* instancePtr, void* contextPtr, ushort eventId, global::Fhsm.Kernel.Data.HsmCommandWriter* writer)");
        else
            sb.AppendLine($"{pad}private static unsafe void {thunkName}(void* instancePtr, void* contextPtr, global::Fhsm.Kernel.Data.HsmCommandWriter* writer)");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    var __bridge = (global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*)contextPtr;");
        sb.AppendLine($"{pad}    var __repo   = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(__bridge->WorldHandle).Target!;");
        if (e.Form != Form.NoParams)
        {
            sb.AppendLine($"{pad}    // ⭐ The host variable, LIVE (§P.3), at the offset baked from this asset's packer — no occurrence, no state base.");
            sb.AppendLine($"{pad}    byte* __root = global::Fdp.Toolkit.Behavior.RootParamsAccess.RequireRootBytes(__repo, __bridge->Self, out int __len);");
            sb.AppendLine($"{pad}    if ({e.Offset} + sizeof({e.Method.ParamTypeFqn}) > __len)");
            sb.AppendLine($"{pad}        throw new global::System.InvalidOperationException(\"CE-417: {e.Key} does not fit the root block.\");");
        }
        if (e.Form == Form.Stateful)
        {
            sb.AppendLine($"{pad}    // ⭐ S8 — the working state is a member of this HSM's OWN block St (one home, CE-437), never an occurrence slot.");
            sb.AppendLine($"{pad}    if (sizeof({e.BlockFqn}) > __len)");
            sb.AppendLine($"{pad}        throw new global::System.InvalidOperationException(\"S8: {e.Key}'s block does not fit the root block.\");");
            sb.AppendLine($"{pad}    ref var __ws = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<{e.BlockFqn}>(__root).St.{e.WsVariable};");
        }
        if (e.Method.IsCondition)
            sb.AppendLine(e.Method.ReturnsBool
                ? $"{pad}    return {call};"
                : $"{pad}    return {call} == global::Fbt.NodeStatus.Success;");
        else
            sb.AppendLine($"{pad}    {call};");
        sb.AppendLine($"{pad}}}");
        sb.AppendLine();
    }
}

/// <summary>
/// ⭐ <c>CE-417</c> — the ONE rule for the name an HSM binding is addressed by in the blob and the builder.
/// <para>With the compilation's answer (<c>sharedAi</c>): <c>Fqn@hostOffset</c> for a bound <c>[SharedAi*]</c> method,
/// else the bare FQN — <see cref="SharedAiBindings.NameFor"/>. Without it (a unit-test emit with no compilation): the
/// pre-CE-417 behaviour — only a transition ACTION is compound-named (<c>E7b</c>), by its field's packed offset.</para>
/// </summary>
public sealed class BindingNamer
{
    private readonly IReadOnlyDictionary<string, int> _offsets;
    private readonly Func<string, SharedAiMethodInfo?>? _sharedAi;
    private readonly Guid _assetId;   // S8: the stateful slot key is per asset

    public BindingNamer(IReadOnlyDictionary<string, int> offsets, Func<string, SharedAiMethodInfo?>? sharedAi, Guid assetId = default)
    {
        _offsets = offsets; _sharedAi = sharedAi; _assetId = assetId;
    }

    /// <summary>⭐ <c>CE-2083</c> — the action name a state's SOP order is addressed by (its <c>ActionKey</c> at the baked host
    /// offset), or null when the state issues none.</summary>
    public string? SopOrderName(BTree.SopOrderPayloadDto? order, string where)
        => order == null ? null : order.ActionKey(SopOrderEmit.Offset(order, _offsets, where));

    /// <summary>The name for <paramref name="b"/>, or null when it names no method.</summary>
    public string? Name(BehaviorActionBindingDto? b, bool legacyCompound = false)
    {
        if (b == null || string.IsNullOrEmpty(b.MethodFqn)) return null;
        if (_sharedAi != null) return SharedAiBindings.NameFor(b, _offsets, _sharedAi, _assetId);
        return legacyCompound ? HsmEmitCore.EffectiveActionName(b.MethodFqn!, b.ExpressionTargetField, _offsets) : b.MethodFqn!;
    }
}

