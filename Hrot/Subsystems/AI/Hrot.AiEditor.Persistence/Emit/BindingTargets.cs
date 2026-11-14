using System;
using System.Collections.Generic;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>⭐ <c>CE-3182</c> — one field of a struct as the build-time layout places it.</summary>
public readonly struct FieldLayout
{
    public FieldLayout(int offset, int size, string typeId) { Offset = offset; Size = size; TypeId = typeId; }

    /// <summary>Byte offset of the field inside its struct.</summary>
    public int Offset { get; }

    /// <summary>The field's size in bytes.</summary>
    public int Size { get; }

    /// <summary>The field's type id (an FQN, as a variable's <c>Type.TypeId</c>).</summary>
    public string TypeId { get; }
}

/// <summary>
/// ⭐ <c>CE-3182</c> — the layout of <paramref name="field"/> inside the struct <paramref name="structTypeId"/>, or <c>null</c> when it
/// cannot be known. ⭐ Supplied by the generator from Roslyn (<c>StructSizeResolver.FieldResolver</c> over
/// <c>RoslynStructLayout.FieldOffset</c>, pinned to the CLR by the CE-2027 rails); ⛔ a type Roslyn cannot see (a generated sibling
/// struct) answers <c>null</c>, and the path fails closed.
/// </summary>
public delegate FieldLayout? FieldLayoutResolver(string structTypeId, string field);

/// <summary>
/// ⭐⭐ <c>CE-3182</c> — THE resolver from a binding's target (a variable, or <c>Variable.Field[.Sub…]</c>, <see cref="BindingPath"/>)
/// to the bytes it names in the managed block: offset = the variable's packed offset + each field's offset, from the build-time
/// layout (<c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §5d). Built ONCE per asset by each emit entry point.
/// <para>⛔ It replaces eleven private name → offset maps (<c>BTreeBridgeEmitCore</c> ×7, <c>HsmBridgeEmitCore</c>,
/// <c>SharedAiBindings</c>, <c>BTreeEmitCore.BuildVariableOffsets</c>, <c>HsmEmitCore.HsmParamOffsets</c>) that each re-read
/// <c>PackedField.Name</c>. One resolver means the blob key and the thunk key cannot disagree on a field path, exactly as they
/// cannot on a variable.</para>
/// </summary>
public sealed class BindingTargets
{
    private readonly Dictionary<string, BTreeBlackboardPackHelper.PackedField> _byName;
    private readonly FieldLayoutResolver? _fields;
    private readonly Dictionary<string, BTreeBlackboardPackHelper.PackedField?> _paths = new(StringComparer.Ordinal);

    private BindingTargets(IEnumerable<BTreeBlackboardPackHelper.PackedField>? packed, FieldLayoutResolver? fields)
    {
        _byName = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        if (packed != null) foreach (var f in packed) _byName[f.Name] = f;
        _fields = fields;
    }

    /// <summary>No packed variables: nothing resolves.</summary>
    public static readonly BindingTargets Empty = new(null, null);

    /// <summary>The targets of one asset's packed variables; <paramref name="fields"/> lays out a field path (null ⇒ variables only).</summary>
    public static BindingTargets For(IEnumerable<BTreeBlackboardPackHelper.PackedField>? packed, FieldLayoutResolver? fields = null)
        => new(packed, fields);

    /// <summary>True when there is no packed variable at all.</summary>
    public bool IsEmpty => _byName.Count == 0;

    /// <summary>Every packed VARIABLE (not paths), by name.</summary>
    public IReadOnlyDictionary<string, BTreeBlackboardPackHelper.PackedField> Variables => _byName;

    /// <summary>
    /// Resolves <paramref name="path"/> to a packed field named by the path: the variable itself, or the field it ends at, with the
    /// absolute offset in the block and the leaf's type. ⛔ False — never a guess — for an empty path, an unknown variable, an
    /// unknown field, or a struct whose layout is not known.
    /// </summary>
    public bool TryResolve(string? path, out BTreeBlackboardPackHelper.PackedField target)
    {
        target = null!;
        string? root = BindingPath.Root(path);
        if (root == null || !_byName.TryGetValue(root, out var variable)) return false;
        string? rest = BindingPath.Field(path);
        if (rest == null) { target = variable; return true; }

        if (!_paths.TryGetValue(path!, out var resolved))
        {
            resolved = Walk(path!, variable, rest);
            _paths[path!] = resolved;
        }
        if (resolved == null) return false;
        target = resolved;
        return true;
    }

    /// <summary>
    /// Target → offset for every packed variable AND every resolvable path in <paramref name="boundPaths"/> (the asset's
    /// <c>ExpressionTargetField</c>s) — the shape the blob-key builders take, so each <c>TryGetValue(etf)</c> downstream resolves a
    /// field path with no change. ⛔ An unresolvable path is simply absent: the binding fails closed, as an unknown variable does.
    /// </summary>
    public IReadOnlyDictionary<string, int> Offsets(IEnumerable<string?>? boundPaths = null)
    {
        var map = new Dictionary<string, int>(_byName.Count, StringComparer.Ordinal);
        foreach (var kv in _byName) map[kv.Key] = kv.Value.ByteOffset;
        if (boundPaths != null)
            foreach (var path in boundPaths)
                if (BindingPath.Field(path) != null && TryResolve(path, out var t)) map[path!] = t.ByteOffset;
        return map;
    }

    private BTreeBlackboardPackHelper.PackedField? Walk(string path, BTreeBlackboardPackHelper.PackedField variable, string rest)
    {
        if (_fields == null) return null;
        int offset = variable.ByteOffset, size = variable.ByteSize;
        string typeId = variable.TypeId;
        foreach (var segment in rest.Split(BindingPath.Separator))
        {
            if (segment.Length == 0) return null;
            var layout = _fields(typeId, segment);
            if (layout == null) return null;
            offset += layout.Value.Offset;
            size = layout.Value.Size;
            typeId = layout.Value.TypeId;
        }
        return new BTreeBlackboardPackHelper.PackedField(path, typeId, offset, size);
    }
}
