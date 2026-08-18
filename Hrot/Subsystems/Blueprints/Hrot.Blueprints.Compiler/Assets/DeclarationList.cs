using System.Collections;
using System.Linq;

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
    /// <remarks>
    /// ⭐⭐ <b>Batch 86 — two kinds, not three</b> *(<c>R-01</c>)*. ⚠ <b>Order preservation is by
    /// CONSTRUCTION:</b> the store is populated in the on-disk list order and the two state groups now
    /// map to ONE kind, so a merged run is still <i>old-working-state-first, then old-variable</i>.
    /// ⛔ Nothing re-derives or re-sorts it.
    /// </remarks>
    public static IReadOnlyList<DeclarationKind> KindOrder { get; } = new[]
    {
        DeclarationKind.Parameter, DeclarationKind.Variable,
    };

    /// <summary>
    /// ⭐ <b>U-12 — reads the store directly.</b> ⚠ The store is kept grouped in <see cref="KindOrder"/>,
    /// so one kind is one contiguous run: a start plus a count.
    /// </summary>
    private int StartOf(DeclarationKind kind)
    {
        var start = 0;
        foreach (var k in KindOrder)
        {
            if (k == kind) return start;
            start += CountOf(k);
        }
        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
    }

    private int CountOf(DeclarationKind kind)
    {
        var n = 0;
        foreach (var d in _asset.DeclarationStore) if (d.Kind == kind) n++;
        return n;
    }

    /// <summary>
    /// ⭐⭐ <b>Since <c>U-12</c> this returns the STORED declaration, not a fresh facade.</b> Under
    /// <c>U-9</c> every read allocated a new <see cref="BlueprintDeclaration"/> wrapping the same decl,
    /// which is why identity had to be defined on <see cref="BlueprintDeclaration.Backing"/>. That rule
    /// still holds and is still the one to rely on — ⚠ <b>reference equality of the wrapper is now
    /// accidentally true, and code that starts depending on it would break the moment anything
    /// re-wraps.</b>
    /// </summary>
    private BlueprintDeclaration AtLocal(DeclarationKind kind, int i)
        => _asset.DeclarationStore[StartOf(kind) + i];

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

    /// <summary>
    /// <b>U-11 — the declaration at <paramref name="local"/> within its OWN list.</b>
    ///
    /// <para>
    /// ⭐⭐ <b>O(1) and allocation-free, and that is the reason it exists.</b> This is the shape
    /// <c>VariableRef</c> addresses (<c>U-3</c>: kind + <b>list-relative</b> index), and the consumers
    /// that use it sit in the emit path. ⛔ Writing <c>Of(kind).ElementAt(i)</c> there would turn a
    /// field lookup into a walk with an iterator allocation per call — a projection that is correct and
    /// quietly worse is still a regression.
    /// </para>
    /// </summary>
    public BlueprintDeclaration At(DeclarationKind kind, int local)
    {
        var n = CountOf(kind);
        if (local < 0 || local >= n)
            throw new ArgumentOutOfRangeException(
                nameof(local), local, $"{kind} has {n} declaration(s).");
        return AtLocal(kind, local);
    }

    /// <summary>How many declarations of <paramref name="kind"/> — without walking them.</summary>
    /// <remarks>⚠ Not an overload of <see cref="Count"/>: <c>IList</c> already defines that as a property.</remarks>
    public int CountIn(DeclarationKind kind) => CountOf(kind);

    /// <summary>
    /// The declaration with this id, in <b>resolution priority order</b> — ⚠ <c>Variable</c>, then
    /// <c>WorkingState</c>, then <c>Parameter</c>, which is <c>Stage5.FindVariableRef</c>'s order and
    /// ⛔ <b>NOT</b> <see cref="KindOrder"/>. Returns null when nothing matches.
    /// </summary>
    public BlueprintDeclaration? ById(Guid id)
    {
        foreach (var kind in ResolutionOrder)
        {
            var n = CountOf(kind);
            for (int i = 0; i < n; i++)
            {
                var d = AtLocal(kind, i);
                if (d.Id == id) return d;
            }
        }
        return null;
    }

    /// <summary>
    /// ⚠⚠ <b>Resolution priority — deliberately NOT <see cref="KindOrder"/>.</b> <c>Stage5</c> searches
    /// <c>Variables</c> → <c>WorkingState</c> → <c>Parameters</c> when disambiguating a name or id;
    /// storage order is the reverse. ⭐ The two orders are different questions and <c>BP-226</c> was
    /// what happened when one integer answered both.
    /// </summary>
    public static IReadOnlyList<DeclarationKind> ResolutionOrder { get; } = new[]
    {
        DeclarationKind.Variable, DeclarationKind.Parameter,
    };

    public int Count => CountOf(DeclarationKind.Parameter)
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

            _asset.DeclarationStore[StartOf(kind) + local] = value;
        }
    }

    public void Add(BlueprintDeclaration item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        _asset.DeclarationStore.Insert(StartOf(item.Kind) + CountOf(item.Kind), item);
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

        _asset.DeclarationStore.Insert(StartOf(item.Kind) + local, item);
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
        _asset.DeclarationStore.RemoveAt(StartOf(kind) + local);
        ForgetOrder(kind, id);
    }

    /// <summary>
    /// ⭐⭐ <b>Batch 86 — the state kind has TWO order lists, and both are still storage.</b>
    /// 📌 The handoff: <i>"keep BOTH order lists"</i> — they are the order evidence <c>R-24</c> depends
    /// on, and <c>Stage5.ConcatOrder</c> reads them in sequence. ⇒ a removal drops the id from
    /// whichever list holds it, ⛔ not from a guessed one: a stale id is invisible until the panel next
    /// sorts and quietly drops a row.
    /// </summary>
    private void ForgetOrder(DeclarationKind kind, Guid id)
    {
        if (kind == DeclarationKind.Parameter) { _asset.ParameterOrder?.Remove(id); return; }
        _asset.WorkingStateOrder?.Remove(id);
        _asset.VariableOrder?.Remove(id);
    }

    /// <summary>
    /// <b>U-11 — replace ONE kind's list wholesale.</b> The operation an undo snapshot restore needs:
    /// <c>Clear</c> + <c>AddRange</c> over a single kind.
    ///
    /// <para>
    /// ⚠⚠ <b>Deliberately does NOT touch the display-order list</b>, unlike <see cref="Remove"/>. A
    /// snapshot restore is putting back a state that was captured whole — the order list belongs to
    /// that same snapshot and is restored by whoever took it. ⛔ Dropping ids here would make undo
    /// silently lose the designer's ordering, which is the opposite of what an undo is for.
    /// </para>
    /// </summary>
    public void ReplaceAll(DeclarationKind kind, IEnumerable<BlueprintDeclaration> declarations)
    {
        if (declarations is null) throw new ArgumentNullException(nameof(declarations));
        var items = declarations.ToList();
        foreach (var d in items)
            if (d.Kind != kind)
                throw new ArgumentException(
                    $"Cannot put a {d.Kind} declaration into the {kind} list — moving a declaration "
                    + "between lists changes the struct it is laid out in.", nameof(declarations));

        var start = StartOf(kind);
        _asset.DeclarationStore.RemoveRange(start, CountOf(kind));
        _asset.DeclarationStore.InsertRange(start, items);
    }

    public void Clear()
    {
        _asset.DeclarationStore.Clear();
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
