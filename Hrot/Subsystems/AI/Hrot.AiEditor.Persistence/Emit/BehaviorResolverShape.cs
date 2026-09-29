using System;
using System.Collections.Generic;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐⭐ <c>CE-434</c> — THE one answer, outside the emitter, to "what does a resolver for this behaviour have
/// to look like?" 📄 <c>Architect_Question_76</c> §12.21.
///
/// <para>⛔ It FORWARDS to the emitter's own namers (<see cref="BTreeEmitCore"/>) rather than restating them:
/// 📌 §12.20d measured that the authored type's suffix varies by asset, so a second copy of the rule is how
/// the editor would derive a name the generator never emits (<c>R-132</c>).</para>
/// </summary>
public sealed class BehaviorResolverShape
{
    /// <summary>One field of the behaviour's block, as a resolver asset declares it.</summary>
    public sealed class BlockVariable
    {
        public string Name { get; }
        public string ClrTypeId { get; }
        public bool IsState { get; }
        public BlockVariable(string name, string clrTypeId, bool isState)
        { Name = name; ClrTypeId = clrTypeId; IsState = isState; }
    }

    /// <summary>FQN (no <c>global::</c>) of the authored DTO — the block's <c>In</c> struct.</summary>
    public string AuthoredTypeId { get; }
    /// <summary>FQN (no <c>global::</c>) of the behaviour's block.</summary>
    public string BlockTypeId { get; }
    /// <summary>The block's fields, in declaration order.</summary>
    public IReadOnlyList<BlockVariable> Variables { get; }
    /// <summary>
    /// ⭐ FNV-1a over exactly what a resolver asset is derived from — namespace, blackboard type name and each
    /// block variable's (name, type, half). ⛔ Node edits do not move it; a renamed or retyped variable does.
    /// </summary>
    public uint ShapeHash { get; }

    private BehaviorResolverShape(string authored, string block, IReadOnlyList<BlockVariable> vars, uint hash)
    { AuthoredTypeId = authored; BlockTypeId = block; Variables = vars; ShapeHash = hash; }

    /// <summary>The shape of <paramref name="dto"/>'s block.</summary>
    public static BehaviorResolverShape Of(BehaviorTreeAssetDto dto)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));
        string ns = BTreeEmitCore.BlackboardStructNamespace(dto);
        var vars = new List<BlockVariable>();
        foreach (var v in dto.Blackboard?.Variables ?? new List<BlackboardVariableDto>())
        {
            if (string.IsNullOrEmpty(v.Name) || string.IsNullOrEmpty(v.Type?.TypeId)) continue;
            bool isState = v.Role == BlackboardVariableRole.State;
            // ⚠ A State variable outside Behavior scope lives on a side slot, not in the block (CE-437).
            if (isState && v.Scope != WorkingStateScope.Behavior) continue;
            vars.Add(new BlockVariable(v.Name, ClrTypeId(v.Type!.TypeId), isState));
        }

        uint h = 2166136261u;
        void Mix(string s) { foreach (char c in s) { h ^= c; h *= 16777619u; } h ^= 0x1F; h *= 16777619u; }
        Mix(ns); Mix(dto.Blackboard?.TypeName ?? "");
        foreach (var v in vars) { Mix(v.Name); Mix(v.ClrTypeId); Mix(v.IsState ? "St" : "In"); }

        return new BehaviorResolverShape(
            ns + "." + BTreeEmitCore.BlackboardStructName(dto),
            ns + "." + BTreeEmitCore.BlockStructName(dto),
            vars, h);
    }

    /// <summary>
    /// A blackboard type alias (<c>float</c>, <c>Vector3</c>) as the CLR type id a blueprint declaration uses
    /// (<c>System.Single</c>, <c>System.Numerics.Vector3</c>); anything else (a struct FQN) passes through.
    /// </summary>
    public static string ClrTypeId(string alias) => alias switch
    {
        "bool"   => "System.Boolean", "byte"  => "System.Byte",   "sbyte" => "System.SByte",
        "short"  => "System.Int16",   "ushort"=> "System.UInt16", "int"   => "System.Int32",
        "uint"   => "System.UInt32",  "long"  => "System.Int64",  "ulong" => "System.UInt64",
        "float"  => "System.Single",  "double"=> "System.Double",
        "Vector2" => "System.Numerics.Vector2", "Vector3" => "System.Numerics.Vector3",
        "Vector4" => "System.Numerics.Vector4", "Quaternion" => "System.Numerics.Quaternion",
        _ => alias,
    };
}
