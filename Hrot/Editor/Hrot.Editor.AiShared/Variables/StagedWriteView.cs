using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <b>Where one variable row's bytes live, in the terms <see cref="IStagedWrites.TryGetPending"/>
/// speaks.</b> 📄 <c>DESIGN_Staged_Live_Write.md</c> §4.
///
/// <para>⚠ <b><c>TypeId</c>, not <c>Type</c>, deliberately.</b> The seam is in <c>Fdp.Core</c> and keys
/// the staged set by the ECS component id — ⛔ <c>Hrot.Editor.AiShared</c> must not be the place that
/// decides how a <see cref="System.Type"/> becomes one *(that is <c>ComponentTypeRegistry</c>'s job, and
/// it is the WRITE path's answer that must be reused — see <see cref="ResolveStagedField"/>)*.</para>
///
/// <para>⭐ <b><c>SizeBytes</c> is carried</b> so a rail can assert the staged payload is the field's
/// width. ⛔ Not used to gate the display: 📌 <c>Q32</c> §2.1's corruption gate runs on the WRITE, and
/// re-deciding it here would be a second implementation of it.</para>
/// </summary>
public readonly record struct StagedFieldAddress(int TypeId, int ByteOffset, int SizeBytes);

/// <summary>
/// ⭐⭐⭐ <b>A row's <c>(asset, variable-path, entity)</c> → its live address.</b>
///
/// <para>⛔⛔ <b>A delegate, and NOT an interface with a Blueprint implementation.</b> 📐 Measured: the
/// only resolver in this codebase is <c>IBlueprintDebugSession.ResolveWorkingStateField</c>, which lives
/// in <c>Hrot.Blueprints.Core</c> — an assembly <c>Hrot.Editor.AiShared</c> does not and must not
/// reference *(<c>Q32</c> ruling 6: the shared table is cross-host)*. ⭐ The production instance is
/// <c>BlueprintLiveValueWriter.ResolveStagedField</c>, which is <b>the same call the WRITE makes</b> —
/// 📌 <c>R-13</c>: route, don't duplicate. ⚠ If the yellow resolved a field by any other route, a panel
/// could paint a value the write never staged.</para>
///
/// <para>⭐ <b><c>null</c> means <i>"this row has no live address"</i></b> — an authoring-time row, a
/// non-Blueprint host, or a dispatch kind whose layout nobody has measured. ⛔ Not an error: those rows
/// simply never yellow, which is exactly what they should do.</para>
/// </summary>
public delegate StagedFieldAddress? ResolveStagedField(VariableRowOrigin origin, Entity entity);

/// <summary>
/// ⭐⭐⭐ <b><c>W4</c> — THE SHARED YELLOW.</b> 📄 <c>DESIGN_Staged_Live_Write.md</c> §3's
/// <c>classDiagram</c> *(<c>StagedWriteView</c>)*, §4 <b>fork A</b>, §7.
///
/// <para>🔒 <b>User, <c>2026-08-21</c>:</b> <i>"if we can share the staged state to both views, even
/// better, both yellow, both showing the same staged value, immediately after user edit."</i></para>
///
/// <para>⭐⭐⭐ <b>ONE INSTANCE, AT THE COMPOSITION ROOT</b> — 📌 <c>R-120</c>: <i>"a view owns no shared
/// state."</i> ⛔ <b>NOT one per <see cref="VariableTableModel"/></b>, which is the shape
/// <see cref="VariableChangeMonitor"/> and <see cref="VariableRowSampler"/> correctly have and this one
/// correctly does not: those are a panel's MEMORY *(per panel, by ruling)*; this is a QUERY over state
/// the whole editor shares. 📐 <c>DESIGN</c> §2 <c>I2</c> measured the failure of getting that backwards
/// — <c>VariableTableModel.cs:122: new VariableChangeMonitor()</c>, per panel ⇒ marking Details pending
/// could never reach the Watch.</para>
///
/// <para>⭐⭐⭐ <b>IT OWNS NO STATE OF ITS OWN, and that is the whole design.</b> 📌 §4 fork A: the yellow,
/// the displayed bytes and the auto-clear all fall out of the ONE staged set —
/// <c>DataBreakpointManager._pendingMutations</c>. ⇒ ⛔ <b>the unwired <c>MarkPending</c>/
/// <c>ClearPending</c> flag was NOT wired; it was DELETED</b> *(<c>R-13</c>)*. ⚠ Had it been wired, a
/// drain would have had to remember to clear it — and 📌 <c>R-126</c>'s reason for making the drain a
/// PULL is precisely that <i>"no path can forget to raise what is never raised."</i></para>
///
/// <para>⭐⭐ <b><c>R-130</c> in one line:</b> <i>pending ⟺ a mutation for this field sits un-drained.</i>
/// ⇒ a directly-applied write *(<c>MIN</c>'s <c>WriteFieldNow</c>, until <c>W3</c> removes it)</c> is
/// never in that set, so it never yellows — exactly as ruled.</para>
///
/// <para>⚠ <b>Cost, measured against the design's own caveat</b> *(§4: "per-row resolve + a small-set
/// query each frame")*: <see cref="IStagedWrites.HasPending"/> is asked FIRST, so a panel with nothing
/// staged — the overwhelmingly common case — pays one boolean per frame and resolves NOTHING.</para>
/// </summary>
public sealed class StagedWriteView
{
    private readonly Func<IStagedWrites?> _writes;
    private readonly ResolveStagedField   _resolve;
    private readonly Func<Entity?>        _selectedEntity;

    /// <param name="writes">
    /// ⭐ The ONE staged set. ⚠ A <see cref="Func{TResult}"/> for the same reason
    /// <c>BlueprintLiveValueWriter</c> takes a session factory: 📐 at the composition root the
    /// <c>DataBreakpointManager</c> field is assigned later than the workspace services bag is built,
    /// and capturing it eagerly would bind <see langword="null"/> for the editor's whole lifetime —
    /// 📌 the construction-order shape that made <c>L3.3</c>'s first wiring register nothing.
    /// </param>
    /// <param name="resolve">See <see cref="ResolveStagedField"/> — the WRITE path's own resolver.</param>
    /// <param name="selectedEntity">
    /// ⭐⭐⭐ <b>The SAME object the write reads its entity from</b> —
    /// <c>EditorSelectionStore.SelectedEntity</c>. ⛔ 📌 <c>R-78</c>: a Details row's
    /// <see cref="VariableRowOrigin.Entity"/> is <c>default</c>, the CHAMELEON SENTINEL for
    /// <i>"whoever is selected"</i>, so reading it would ask about entity 0 and no row would ever
    /// yellow. ⚠ And if the yellow and the write ever disagreed about the entity, a designer would edit
    /// one entity's value and watch another's row turn yellow.
    /// </param>
    public StagedWriteView(
        Func<IStagedWrites?> writes,
        ResolveStagedField   resolve,
        Func<Entity?>        selectedEntity)
    {
        _writes         = writes         ?? throw new ArgumentNullException(nameof(writes));
        _resolve        = resolve        ?? throw new ArgumentNullException(nameof(resolve));
        _selectedEntity = selectedEntity ?? throw new ArgumentNullException(nameof(selectedEntity));
    }

    /// <summary>⭐ True while ANY edit is staged. ⛔ Not per-row — the cheap early-out.</summary>
    public bool HasPending => _writes()?.HasPending == true;

    /// <summary>
    /// ⭐⭐ <b>Which entity this row is about</b> — its own, or the selected one when it carries the
    /// chameleon sentinel. See the <c>selectedEntity</c> parameter for why this rule is not optional.
    /// </summary>
    public Entity EntityFor(VariableRowOrigin origin)
        => origin.Entity.Equals(default(Entity)) ? _selectedEntity() ?? default : origin.Entity;

    /// <summary>⭐ §3's <c>IsPending(origin, entity)</c>, with the entity resolved by <see cref="EntityFor"/>.</summary>
    public bool IsPending(VariableRowOrigin origin) => TryGetTyped(origin, out _);

    /// <inheritdoc cref="IsPending(VariableRowOrigin)"/>
    public bool IsPending(VariableRowOrigin origin, Entity entity) => TryGetTyped(origin, entity, out _);

    /// <summary>⭐ §3's <c>TryGetTyped(origin, entity, out bytes)</c>, entity resolved by <see cref="EntityFor"/>.</summary>
    public bool TryGetTyped(VariableRowOrigin origin, out byte[] bytes)
        => TryGetTyped(origin, EntityFor(origin), out bytes);

    /// <summary>
    /// ⭐⭐⭐ <b>The one query behind BOTH the yellow AND the shown value</b> *(§7)*.
    /// ⛔ Two queries — one for the colour, one for the bytes — would be two chances to disagree about
    /// whether a row is pending, and the disagreement would show as a white cell holding a staged value.
    /// </summary>
    public bool TryGetTyped(VariableRowOrigin origin, Entity entity, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        // ⭐ The early-out. See the class remarks on cost.
        var writes = _writes();
        if (writes is null || !writes.HasPending) return false;

        // ⛔ Entity 0 is never a staged target — asking would be a lookup guaranteed to miss, and the
        //   answer would be indistinguishable from "nothing is staged for the selected entity".
        if (entity.Equals(default(Entity))) return false;

        var address = _resolve(origin, entity);
        if (address is null) return false;

        return writes.TryGetPending(entity, address.Value.TypeId, address.Value.ByteOffset, out bytes);
    }
}
