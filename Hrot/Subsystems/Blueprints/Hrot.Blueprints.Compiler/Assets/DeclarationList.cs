using System.Collections;

namespace Hrot.Blueprints.Core.Assets;

/// <summary>
/// <b>U-9 / D1 — the three declaration lists, seen as one.</b>
///
/// <para>
/// ⭐⭐ <b>A LIVE view, not a snapshot.</b> Every operation lands in the underlying
/// <see cref="BlueprintAsset.Parameters"/> / <see cref="BlueprintAsset.WorkingState"/> /
/// <see cref="BlueprintAsset.Variables"/> list, and every element is a facade over the stored decl.
/// ⛔ <b>A materialised copy would have been silently lossy for the whole of <c>U-11</c></b>, during
/// which the old lists are still the storage and ~34 consumers are being moved across one bucket at a
/// time.
/// </para>
///
/// <para>
/// ⭐ <b>Enumeration order is STORAGE order</b> — Parameter, then WorkingState, then Variable, each in
/// its own list order. ⚠ <b>Two orders it is deliberately NOT:</b>
/// <list type="bullet">
///   <item><b>Not resolution order.</b> <c>Stage5.FindVariableRef</c> searches Variables →
///   WorkingState → Parameters. That is a <i>priority</i> for an ambiguous name, not an ordering of
///   the declarations, and using it here would put a union index out of step with the struct
///   layout.</item>
///   <item><b>Not display order.</b> <c>ParameterOrder</c> / <c>WorkingStateOrder</c> /
///   <c>VariableOrder</c> are an editor concern applied by <c>BlueprintVariablesWindow.GetOrdered</c>;
///   the index the compiler addresses a field by is the list position.</item>
/// </list>
/// </para>
///
/// <para>
/// 📌 The Order lists are still <b>maintained</b> here: a removal drops the id from the matching Order
/// list, exactly as <c>BlueprintVariableSchemaSource.RemoveVariables</c> does (Batch 46). A stale id
/// left behind is invisible until the panel next sorts and quietly drops a row.
/// </para>
/// </summary>
public sealed class DeclarationList : IList<BlueprintDeclaration>
{
    private readonly BlueprintAsset _asset;

    internal DeclarationList(BlueprintAsset asset) => _asset = asset;

    /// <summary>Storage order — and the struct layout order (Params @0, working @8, State @16).</summary>
    public static IReadOnlyList<DeclarationKind> KindOrder { get; } = new[]
    {
        DeclarationKind.Parameter, DeclarationKind.WorkingState, DeclarationKind.Variable,
    };

    private int CountOf(DeclarationKind kind) => kind switch
    {
        DeclarationKind.Parameter    => _asset.Parameters.Count,
        DeclarationKind.WorkingState => _asset.WorkingState.Count,
        DeclarationKind.Variable     => _asset.Variables.Count,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private BlueprintDeclaration AtLocal(DeclarationKind kind, int i) => kind switch
    {
        DeclarationKind.Parameter    => BlueprintDeclaration.For(_asset.Parameters[i]),
        DeclarationKind.WorkingState => BlueprintDeclaration.For(kind, _asset.WorkingState[i]),
        DeclarationKind.Variable     => BlueprintDeclaration.For(kind, _asset.Variables[i]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Splits a union index into (which list, position within it).</summary>
    private (DeclarationKind Kind, int Local) Locate(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        foreach (var kind in KindOrder)
        {
            var n = CountOf(kind);
            if (index < n) return (kind, index);
            index -= n;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>The union index of a declaration's position within its own list.</summary>
    public int UnionIndexOf(DeclarationKind kind, int local)
    {
        var offset = 0;
        foreach (var k in KindOrder)
        {
            if (k == kind) return offset + local;
            offset += CountOf(k);
        }
        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
    }

    /// <summary>The position of <paramref name="decl"/> within its OWN list, or -1.</summary>
    public int LocalIndexOf(BlueprintDeclaration decl)
    {
        if (decl is null) return -1;
        var n = CountOf(decl.Kind);
        for (int i = 0; i < n; i++)
            if (ReferenceEquals(AtLocal(decl.Kind, i).Backing, decl.Backing)) return i;
        return -1;
    }

    /// <summary>Just the declarations of one kind, in list order.</summary>
    public IEnumerable<BlueprintDeclaration> Of(DeclarationKind kind)
    {
        var n = CountOf(kind);
        for (int i = 0; i < n; i++) yield return AtLocal(kind, i);
    }

    public int Count => CountOf(DeclarationKind.Parameter)
                      + CountOf(DeclarationKind.WorkingState)
                      + CountOf(DeclarationKind.Variable);

    public bool IsReadOnly => false;

    public BlueprintDeclaration this[int index]
    {
        get { var (kind, local) = Locate(index); return AtLocal(kind, local); }
        set
        {
            var (kind, local) = Locate(index);
            if (value is null) throw new ArgumentNullException(nameof(value));

            // ⭐ An honest refusal rather than an invented semantic. Changing a declaration's kind is a
            // MOVE between two lists — it changes which struct the field is laid out in, and therefore
            // its offset. Q-k already rules Role/Scope a move rather than a toggle; assigning through
            // an indexer would make the same change look like an edit.
            if (value.Kind != kind)
                throw new ArgumentException(
                    $"Cannot assign a {value.Kind} declaration over a {kind} one at index {index}: "
                    + "moving a declaration between lists changes the struct it is laid out in. "
                    + "Remove it and Add it to the target kind instead.", nameof(value));

            switch (kind)
            {
                case DeclarationKind.Parameter:    _asset.Parameters[local]   = value.AsParameterDecl!; break;
                case DeclarationKind.WorkingState: _asset.WorkingState[local] = value.AsVariableDecl!;  break;
                default:                           _asset.Variables[local]    = value.AsVariableDecl!;  break;
            }
        }
    }

    public void Add(BlueprintDeclaration item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        switch (item.Kind)
        {
            case DeclarationKind.Parameter:    _asset.Parameters.Add(item.AsParameterDecl!);   break;
            case DeclarationKind.WorkingState: _asset.WorkingState.Add(item.AsVariableDecl!);  break;
            default:                           _asset.Variables.Add(item.AsVariableDecl!);     break;
        }
    }

    /// <summary>
    /// ⚠ <b>The union index is CLAMPED into the item's own list</b>, because the three lists are
    /// contiguous ranges: an index inside another kind's range has no position in this one. Landing
    /// before the range inserts at its start, after it appends — monotone, and it never silently
    /// changes the item's kind to make the index fit.
    /// </summary>
    public void Insert(int index, BlueprintDeclaration item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));

        var start = UnionIndexOf(item.Kind, 0);
        // ⚠ Not Math.Clamp — this project also targets netstandard2.0, which does not have it.
        var local = Math.Max(0, Math.Min(index - start, CountOf(item.Kind)));

        switch (item.Kind)
        {
            case DeclarationKind.Parameter:    _asset.Parameters.Insert(local, item.AsParameterDecl!);   break;
            case DeclarationKind.WorkingState: _asset.WorkingState.Insert(local, item.AsVariableDecl!);  break;
            default:                           _asset.Variables.Insert(local, item.AsVariableDecl!);     break;
        }
    }

    public bool Remove(BlueprintDeclaration item)
    {
        var local = LocalIndexOf(item);
        if (local < 0) return false;
        RemoveLocal(item.Kind, local);
        return true;
    }

    public void RemoveAt(int index)
    {
        var (kind, local) = Locate(index);
        RemoveLocal(kind, local);
    }

    private void RemoveLocal(DeclarationKind kind, int local)
    {
        var id = AtLocal(kind, local).Id;
        switch (kind)
        {
            case DeclarationKind.Parameter:    _asset.Parameters.RemoveAt(local);   break;
            case DeclarationKind.WorkingState: _asset.WorkingState.RemoveAt(local); break;
            default:                           _asset.Variables.RemoveAt(local);    break;
        }
        OrderOf(kind)?.Remove(id);
    }

    private List<Guid>? OrderOf(DeclarationKind kind) => kind switch
    {
        DeclarationKind.Parameter    => _asset.ParameterOrder,
        DeclarationKind.WorkingState => _asset.WorkingStateOrder,
        _                            => _asset.VariableOrder,
    };

    public void Clear()
    {
        _asset.Parameters.Clear();
        _asset.WorkingState.Clear();
        _asset.Variables.Clear();
        _asset.ParameterOrder?.Clear();
        _asset.WorkingStateOrder?.Clear();
        _asset.VariableOrder?.Clear();
    }

    public bool Contains(BlueprintDeclaration item) => LocalIndexOf(item) >= 0;

    public int IndexOf(BlueprintDeclaration item)
    {
        var local = LocalIndexOf(item);
        return local < 0 ? -1 : UnionIndexOf(item.Kind, local);
    }

    public void CopyTo(BlueprintDeclaration[] array, int arrayIndex)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        foreach (var d in this) array[arrayIndex++] = d;
    }

    public IEnumerator<BlueprintDeclaration> GetEnumerator()
    {
        foreach (var kind in KindOrder)
        {
            var n = CountOf(kind);
            for (int i = 0; i < n; i++) yield return AtLocal(kind, i);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
