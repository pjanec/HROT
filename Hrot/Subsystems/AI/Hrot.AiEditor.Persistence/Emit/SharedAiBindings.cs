using System;
using System.Collections.Generic;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>What the compilation knows about a <c>[SharedAiAction]</c> / <c>[SharedAiCondition]</c> method.</summary>
/// <param name="ParamTypeFqn">The method's first <c>ref</c> parameter type, <c>global::</c>-qualified.</param>
/// <param name="ParamTypeId">The same type as a blackboard <c>TypeId</c> (metadata name, <c>+</c> for nesting).</param>
/// <param name="IsCondition">True for <c>[SharedAiCondition]</c>.</param>
/// <param name="ReturnsBool">True when the method returns <c>bool</c>; otherwise it returns <c>NodeStatus</c>.</param>
public sealed class SharedAiMethodInfo
{
    public SharedAiMethodInfo(string paramTypeFqn, string paramTypeId, bool isCondition, bool returnsBool)
    {
        ParamTypeFqn = paramTypeFqn; ParamTypeId = paramTypeId; IsCondition = isCondition; ReturnsBool = returnsBool;
    }
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
    /// <summary>One binding to generate a call for.</summary>
    public sealed class Entry
    {
        public string Key = "";              // Fqn@hostOffset — the dispatcher name
        public string MethodFqn = "";
        public int Offset;
        public int Size;
        public string VariableName = "";
        public string VariableTypeId = "";
        public SharedAiMethodInfo Method = null!;
        public string Site = "";             // for diagnostics: "state 'X' OnEntry", "transition 'Go' action", …
    }

    /// <summary>Every C# <c>[SharedAi*]</c> binding in <paramref name="dto"/> whose field names a packed variable, by key.</summary>
    public static IReadOnlyList<Entry> Collect(
        HsmAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packed,
        Func<string, SharedAiMethodInfo?>? sharedAi)
    {
        var result = new List<Entry>();
        if (sharedAi == null || packed.Count == 0) return result;

        var byName = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packed) byName[f.Name] = f;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(BehaviorActionBindingDto? b, string site)
        {
            if (b == null || string.IsNullOrEmpty(b.MethodFqn) || string.IsNullOrEmpty(b.ExpressionTargetField)) return;
            if (!byName.TryGetValue(b.ExpressionTargetField!, out var field)) return;
            var info = sharedAi(b.MethodFqn!);
            if (info == null) return;
            string key = b.MethodFqn + "@" + field.ByteOffset;   // MIRROR of HsmActionKey.CompoundKeyName
            if (!seen.Add(key)) return;
            result.Add(new Entry
            {
                Key = key, MethodFqn = b.MethodFqn!, Offset = field.ByteOffset, Size = field.ByteSize,
                VariableName = field.Name, VariableTypeId = field.TypeId, Method = info, Site = site,
            });
        }

        foreach (var s in dto.States)
        {
            Add(s.OnEntry,  $"state '{s.Name}' OnEntry");
            Add(s.OnExit,   $"state '{s.Name}' OnExit");
            Add(s.Activity, $"state '{s.Name}' Activity");
            Add(s.Timer,    $"state '{s.Name}' Timer");
        }
        foreach (var t in dto.Transitions)
        {
            Add(t.Guard,  $"transition '{t.EventName}' guard");
            Add(t.Action, $"transition '{t.EventName}' action");
        }
        foreach (var g in dto.GlobalTransitions)
        {
            Add(g.Guard,  $"global transition '{g.EventName}' guard");
            Add(g.Action, $"global transition '{g.EventName}' action");
        }
        return result;
    }

    /// <summary>
    /// The name a binding is addressed by in the blob: <c>Fqn@hostOffset</c> for a bound <c>[SharedAi*]</c> method, the bare
    /// FQN otherwise. ⭐ The SAME rule <see cref="Collect"/> registers under, so the two can never disagree.
    /// </summary>
    public static string NameFor(
        BehaviorActionBindingDto b,
        IReadOnlyDictionary<string, int> paramOffsets,
        Func<string, SharedAiMethodInfo?> sharedAi)
    {
        string fqn = b.MethodFqn!;
        if (string.IsNullOrEmpty(b.ExpressionTargetField)) return fqn;
        if (!paramOffsets.TryGetValue(b.ExpressionTargetField!, out int off)) return fqn;
        return sharedAi(fqn) != null ? fqn + "@" + off : fqn;
    }

    /// <summary>The C# for one thunk method (an action or a guard), with the offset and type baked.</summary>
    public static void EmitThunk(System.Text.StringBuilder sb, Entry e, string thunkName, string pad)
    {
        string call = $"global::{e.MethodFqn}(ref *({e.Method.ParamTypeFqn}*)(__root + {e.Offset}), __bridge->Self, __repo)";
        sb.AppendLine($"{pad}/// <summary>CE-417: {e.Site} — <c>{e.Key}</c>, bound to <c>{e.VariableName}</c> at host offset {e.Offset}.</summary>");
        if (e.Method.IsCondition)
            sb.AppendLine($"{pad}private static unsafe bool {thunkName}(void* instancePtr, void* contextPtr, ushort eventId, global::Fhsm.Kernel.Data.HsmCommandWriter* writer)");
        else
            sb.AppendLine($"{pad}private static unsafe void {thunkName}(void* instancePtr, void* contextPtr, global::Fhsm.Kernel.Data.HsmCommandWriter* writer)");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    var __bridge = (global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*)contextPtr;");
        sb.AppendLine($"{pad}    var __repo   = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(__bridge->WorldHandle).Target!;");
        sb.AppendLine($"{pad}    // ⭐ The host variable, LIVE (§P.3), at the offset baked from this asset's packer — no occurrence, no state base.");
        sb.AppendLine($"{pad}    byte* __root = global::Fdp.Toolkit.Behavior.RootParamsAccess.RequireRootBytes(__repo, __bridge->Self, out int __len);");
        sb.AppendLine($"{pad}    if ({e.Offset} + sizeof({e.Method.ParamTypeFqn}) > __len)");
        sb.AppendLine($"{pad}        throw new global::System.InvalidOperationException(\"CE-417: {e.Key} does not fit the root block.\");");
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

    public BindingNamer(IReadOnlyDictionary<string, int> offsets, Func<string, SharedAiMethodInfo?>? sharedAi)
    {
        _offsets = offsets; _sharedAi = sharedAi;
    }

    /// <summary>The name for <paramref name="b"/>, or null when it names no method.</summary>
    public string? Name(BehaviorActionBindingDto? b, bool legacyCompound = false)
    {
        if (b == null || string.IsNullOrEmpty(b.MethodFqn)) return null;
        if (_sharedAi != null) return SharedAiBindings.NameFor(b, _offsets, _sharedAi);
        return legacyCompound ? HsmEmitCore.EffectiveActionName(b.MethodFqn!, b.ExpressionTargetField, _offsets) : b.MethodFqn!;
    }
}

