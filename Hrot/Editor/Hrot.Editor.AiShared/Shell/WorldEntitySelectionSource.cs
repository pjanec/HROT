using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.IG.Components;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L0.4</c> — THE SELECTION, READ FROM THE WORLD.</b> 📌 <c>R-122</c>:
/// <i>"entity selection is on the entity."</i>
/// 📄 §2's <c>classDiagram</c> — <c>World o-- "0..*" SelectionState</c> and
/// <c>PerspectiveWorkspace ..&gt; World : reads entity selection</c>.
///
/// <para>⭐⭐ <b>The query is the one every other reader already uses</b> —
/// <c>Query().With&lt;SelectionState&gt;()</c>, as in <c>SelectionInteractionSystem:160</c> and
/// <c>IgApplication:1516</c>. ⛔ Not a new mechanism; the same one, from a new reader.</para>
///
/// <para>⭐⭐⭐ <b>PRIMARY FIRST, and that is load-bearing.</b> 📄 <c>UX_Feature_Selection.md</c> §0:
/// <c>SelectionState</c> <i>"is already correct for multi-select — one primary, many selected"</i>.
/// ⇒ ⭐ a view that wants <i>"the</i> entity<i>"</i> takes <c>[0]</c> and gets the one the ring paints
/// green, ⛔ rather than whichever the archetype iteration happened to reach first — ⚠ which would be
/// stable within a run and different across runs, the worst kind of wrong.</para>
///
/// <para>⭐⭐⭐ <b>THE SAME-INSTANCE CONTRACT, and how it is kept honestly.</b> 📄 §6 <c>L0.4</c>:
/// <i>"return the same list instance when unchanged, or every view rebuilds per frame."</i>
/// ⚠ <b>The cache cannot be keyed on a count</b> — a click that swaps one entity for another leaves the
/// count at 1. ⇒ ⭐ the new result is compared <b>elementwise against the last</b> and the OLD array is
/// returned when they match — 📌 exactly the shape <c>L0.1</c>'s <c>SetSubSelections</c> guard uses, and
/// for the same reason.</para>
///
/// <para>⚠ <b>It allocates only when the selection CHANGES.</b> ⛔ The scratch list is reused, so a
/// steady selection costs one query walk and no allocation — ⭐ which is what makes it safe to call
/// once per frame from <see cref="LiveContextSource"/>.</para>
///
/// <para>⛔ <b><c>SelectionState</c> is <c>[DataPolicy(DataPolicy.NoSave)]</c></b> — ⚠ so the selection
/// does not survive a scenario reload. 📄 §6's limits table calls that out and calls it <b>correct</b>
/// *(consistent with <c>94g</c>)*; ⭐ stated here so a later reader does not "fix" it.</para>
/// </summary>
public sealed class WorldEntitySelectionSource : IEntitySelectionSource
{
    private readonly Func<EntityRepository?> _world;

    private readonly List<Entity> _scratch = new();
    private Entity[]              _last    = Array.Empty<Entity>();

    /// <param name="world">
    /// ⭐⭐⭐ <b>RESOLVED AT CALL TIME, not captured.</b> ⚠⚠ 📐 Measured at the composition root: the
    /// world field is <b>nullable and may not be assigned yet</b> when the services bag is built —
    /// <c>EditorSubsystem</c> reads it the same lazy way for its own clock predicate
    /// *(<c>ClockIsHalted</c>: <c>var world = _world; if (world is null) …</c>)*.
    /// ⛔ Capturing it eagerly would bind <see langword="null"/> for the editor's whole lifetime and
    /// the panel would show no entities, silently — 📌 the same construction-order shape that made
    /// <c>L3.3</c>'s first wiring register nothing.
    /// </param>
    public WorldEntitySelectionSource(Func<EntityRepository?> world)
        => _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>⭐ Convenience for a host that already holds a live world.</summary>
    public WorldEntitySelectionSource(EntityRepository world)
        : this(() => world ?? throw new ArgumentNullException(nameof(world))) { }

    /// <inheritdoc/>
    public IReadOnlyList<Entity> Selected()
    {
        // ⚠ No world yet ⇒ nothing is selected. ⛔ NOT a silent default: there is genuinely no
        //   selection to read, and the alternative (a throw during editor start-up) would be worse.
        var world = _world();
        if (world is null) return _last = Array.Empty<Entity>();

        _scratch.Clear();

        // ⭐ Primary first. Two passes over one query beats sorting: the primary is unique
        //   (UX_Feature_Selection.md §0), so this is O(n) with no comparer and no allocation.
        foreach (var e in world.Query().With<SelectionState>().Build())
        {
            if (!world.IsAlive(e)) continue;
            var s = world.GetComponent<SelectionState>(e);
            if (s is { IsSelected: true, IsPrimarySelection: true }) _scratch.Add(e);
        }
        foreach (var e in world.Query().With<SelectionState>().Build())
        {
            if (!world.IsAlive(e)) continue;
            var s = world.GetComponent<SelectionState>(e);
            if (s.IsSelected && !s.IsPrimarySelection) _scratch.Add(e);
        }

        return Unchanged() ? _last : (_last = _scratch.ToArray());
    }

    /// <summary>
    /// ⭐⭐ <b>Elementwise, and ORDER-SENSITIVE</b> — ⚠ order carries the primary, so a selection whose
    /// primary moved is a DIFFERENT selection even with the same members. ⛔ An order-blind comparison
    /// would hold the panel on a stale primary.
    /// </summary>
    private bool Unchanged()
    {
        if (_last.Length != _scratch.Count) return false;
        for (int i = 0; i < _last.Length; i++)
            if (!_last[i].Equals(_scratch[i])) return false;
        return true;
    }
}
