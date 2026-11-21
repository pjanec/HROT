using System.Collections;
using System.Linq;

namespace Hrot.Blueprints.Core.Assets;

/// <summary>
/// <b>U-12 / D4 — one kind's window onto the single declaration store.</b>
///
/// <para>
/// ⭐⭐ <b>This is what makes the store flip possible without lying.</b> After the flip
/// <see cref="BlueprintAsset"/> holds <b>one</b> list of <see cref="BlueprintDeclaration"/>;
/// <c>Parameters</c>, <c>WorkingState</c> and <c>Variables</c> are no longer storage. ⛔ Had they
/// become <c>List&lt;T&gt;</c> snapshots, <c>asset.Variables.Add(v)</c> would still compile, still
/// report success, and quietly write to a list nobody reads — <b>trap #5, on the model type the whole
/// programme is about</b>. A live view is the only shape that keeps every existing call site honest.
/// </para>
///
/// <para>
/// ⭐ <b>Why a concrete class rather than <c>IList&lt;T&gt;</c>.</b> Measured before choosing: the tree
/// has <b>112</b> sites writing <c>Parameters = new()</c> (target-typed, which an interface cannot
/// satisfy) and <b>~7</b> writing <c>= new List&lt;VariableDecl&gt; { … }</c>. A concrete type with a
/// parameterless constructor serves the first; the <see cref="op_Implicit(List{T})"/> conversion serves
/// the second; <see cref="AddRange"/> serves the 3 remaining <c>List&lt;T&gt;</c>-only calls.
/// ⇒ ⭐⭐ <b>the flip lands with zero churn at ~431 measured call sites</b>, which is what leaves the
/// existing assertions free to act as an independent check on it.
/// </para>
///
/// <para>
/// ⚠ <b>Two modes, and the detached one is not a fallback — it is required.</b>
/// <list type="bullet">
///   <item><b>Bound</b> — created by <see cref="BlueprintAsset"/>, reads and writes the store's
///   contiguous segment for one <see cref="DeclarationKind"/>.</item>
///   <item><b>Detached</b> — its own list. This is what <c>new()</c> and, importantly,
///   <b>System.Text.Json</b> construct: STJ builds the collection first and assigns it afterwards, so
///   the setter absorbs a detached view into the store.</item>
/// </list>
/// </para>
///
/// <para>
/// 📌 <b>Element identity is the stored <see cref="ParameterDecl"/>/<see cref="VariableDecl"/></b>, not
/// this view or the <see cref="BlueprintDeclaration"/> wrapping it — so a decl reached through
/// <c>asset.Variables[0]</c> and through <c>asset.Declarations</c> is the same object, and a write
/// through either is visible to both.
/// </para>
/// </summary>
public sealed class DeclarationView<T> : IList<T> where T : class
{
    private readonly BlueprintAsset?   _owner;
    private readonly DeclarationKind   _kind;
    private readonly List<T>?          _detached;

    /// <summary>Detached — <c>new()</c>, and the shape System.Text.Json builds before assigning.</summary>
    public DeclarationView() => _detached = new List<T>();

    internal DeclarationView(BlueprintAsset owner, DeclarationKind kind)
    {
        _owner = owner;
        _kind  = kind;
    }

    /// <summary>⭐ Lets <c>Parameters = new List&lt;ParameterDecl&gt; { … }</c> keep compiling.</summary>
    public static implicit operator DeclarationView<T>(List<T> items)
    {
        var view = new DeclarationView<T>();
        if (items is not null) view._detached!.AddRange(items);
        return view;
    }

    // ── the store segment ───────────────────────────────────────────────────

    private List<BlueprintDeclaration> Store => _owner!.DeclarationStore;

    /// <summary>
    /// ⭐ The store is kept grouped in <see cref="DeclarationList.KindOrder"/>, so each kind occupies
    /// ONE contiguous run and a window is a start plus a count. ⛔ Without that invariant the three
    /// properties could not be windows at all, and insertion order within a kind — which is the struct
    /// field order — would depend on the order JSON properties happened to arrive in.
    /// </summary>
    private int Start
    {
        get
        {
            var start = 0;
            foreach (var k in DeclarationList.KindOrder)
            {
                if (k == _kind) return start;
                start += CountKind(k);
            }
            return start;
        }
    }

    private int CountKind(DeclarationKind kind)
    {
        var n = 0;
        foreach (var d in Store) if (d.Kind == kind) n++;
        return n;
    }

    private T Unwrap(BlueprintDeclaration d)
        => (T)(_kind == DeclarationKind.Parameter ? (object)d.AsParameterDecl! : d.AsVariableDecl!);

    private BlueprintDeclaration Wrap(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        return _kind == DeclarationKind.Parameter
            ? BlueprintDeclaration.For((ParameterDecl)(object)item)
            : BlueprintDeclaration.For(_kind, (VariableDecl)(object)item);
    }

    private bool IsBound => _owner is not null;

    /// <summary>
    /// ⭐ Absorbs <paramref name="source"/> into this window — the operation the property setter is.
    /// ⚠ Deliberately does NOT touch the matching <c>*Order</c> list: display order is separate
    /// metadata that survives the flip, and an assignment to <c>Parameters</c> has never meant
    /// "forget the designer's ordering".
    /// </summary>
    internal void ReplaceWith(IEnumerable<T>? source)
    {
        var items = source?.ToList() ?? new List<T>();
        if (!IsBound) { _detached!.Clear(); _detached.AddRange(items); return; }

        var start = Start;
        var count = CountKind(_kind);
        Store.RemoveRange(start, count);
        Store.InsertRange(start, items.Select(Wrap));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 86 — replace ONE SEGMENT of this window, leaving the rest of the run alone.</b>
    ///
    /// <para>🔴 <b>Why this exists.</b> <c>WorkingState</c> and <c>Variables</c> are the SAME kind and
    /// therefore the SAME run *(<c>R-01</c>)*, but they are still <b>two property setters</b>, and the
    /// deserializer drives both: v2 JSON is migrated <b>down</b> to the v1 three-list shape and bound to
    /// the properties. ⛔ With plain <see cref="ReplaceWith"/> the second setter would wipe what the
    /// first wrote — silently, and only for an asset carrying both groups.</para>
    ///
    /// <para>⭐⭐ <b>Order is preserved for ANY setter order:</b> the leading segment is what arrived
    /// under the <c>WorkingState</c> name and the trailing segment is what arrived under
    /// <c>Variables</c> — the old <c>KindOrder</c> sequence, which is also
    /// <c>StructureHashComputation</c>'s append order. ⇒ 📌 <c>R-24</c> holds by construction rather
    /// than by the corpus happening to have no mixed asset.</para>
    /// </summary>
    /// <returns>How many entries the segment occupies afterwards.</returns>
    internal int ReplaceSegment(int localStart, int localCount, IEnumerable<T>? source)
    {
        var items = source?.ToList() ?? new List<T>();
        if (!IsBound) { _detached!.Clear(); _detached.AddRange(items); return items.Count; }

        var runStart = Start;
        var runCount = CountKind(_kind);

        // ⚠ Clamped rather than trusted: the caller's remembered split can be stale after a direct
        //   Declarations mutation, and a wrong index would corrupt the run rather than misorder it.
        localStart = Math.Max(0, Math.Min(localStart, runCount));
        localCount = Math.Max(0, Math.Min(localCount, runCount - localStart));

        Store.RemoveRange(runStart + localStart, localCount);
        Store.InsertRange(runStart + localStart, items.Select(Wrap));
        return items.Count;
    }

    // ── IList<T> ────────────────────────────────────────────────────────────

    public int Count => IsBound ? CountKind(_kind) : _detached!.Count;

    public bool IsReadOnly => false;

    public T this[int index]
    {
        get
        {
            if (!IsBound) return _detached![index];
            Bounds(index, Count);
            return Unwrap(Store[Start + index]);
        }
        set
        {
            if (!IsBound) { _detached![index] = value; return; }
            Bounds(index, Count);
            Store[Start + index] = Wrap(value);
        }
    }

    private static void Bounds(int index, int count)
    {
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
    }

    public void Add(T item)
    {
        if (!IsBound) { _detached!.Add(item); return; }
        Store.Insert(Start + CountKind(_kind), Wrap(item));
    }

    public void AddRange(IEnumerable<T> items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        foreach (var i in items) Add(i);
    }

    public void Insert(int index, T item)
    {
        if (!IsBound) { _detached!.Insert(index, item); return; }
        if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
        Store.Insert(Start + index, Wrap(item));
    }

    public void RemoveAt(int index)
    {
        if (!IsBound) { _detached!.RemoveAt(index); return; }
        Bounds(index, Count);
        Store.RemoveAt(Start + index);
    }

    public bool Remove(T item)
    {
        var i = IndexOf(item);
        if (i < 0) return false;
        RemoveAt(i);
        return true;
    }

    public void Clear()
    {
        if (!IsBound) { _detached!.Clear(); return; }
        Store.RemoveRange(Start, CountKind(_kind));
    }

    /// <summary>⚠ Reference identity, matching <see cref="BlueprintDeclaration"/>'s own rule — the
    /// decls have no value equality and two distinct declarations may agree on every field.</summary>
    public int IndexOf(T item)
    {
        var n = Count;
        for (int i = 0; i < n; i++) if (ReferenceEquals(this[i], item)) return i;
        return -1;
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        foreach (var item in this) array[arrayIndex++] = item;
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (!IsBound) { foreach (var d in _detached!) yield return d; yield break; }
        var start = Start;
        var n     = CountKind(_kind);
        for (int i = 0; i < n; i++) yield return Unwrap(Store[start + i]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
