using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐ A source of rows. ⛔ <b>The control depends on THIS, never on an asset</b> — §1a's whole point.
/// </summary>
public interface IVariableRowSource
{
    IReadOnlyList<VariableRow> GetRows();
}

/// <summary>
/// ⭐⭐ <b><c>SectionSource(asset, section)</c> — the Details source (§1a).</b> Homogeneous: one asset,
/// one section.
///
/// <para>
/// ⚠ <b>Homogeneous is not the same as "the control may assume homogeneous".</b> This batch ships only
/// this source — <c>PinnedSource</c> (Watch) is <c>C-watch</c> — but the control is already
/// source-agnostic, and §9's heterogeneous rail proves it by feeding hand-built rows from two assets
/// and two entities. ⛔ Waiting for <c>PinnedSource</c> to discover the control assumed one asset is
/// exactly the failure that rail exists to prevent.
/// </para>
///
/// <para>
/// 🔴 <b><see cref="ReadAssetTick"/> is passed through, not invented here.</b> Batch 68 measured that
/// no per-asset tick exists anywhere in the codebase, so the Details host passes <c>null</c> and the
/// highlight is inert. ⭐ The moment a real per-<c>(asset, entity)</c> tick exists, it is supplied here
/// and nothing else changes.
/// </para>
/// </summary>
public sealed class SectionVariableRowSource : IVariableRowSource
{
    private readonly Guid                     _assetId;
    private readonly string                   _assetName;
    private readonly Entity                   _entity;
    private readonly string                   _section;
    private readonly IVariablesSchemaSource   _schema;
    private readonly Func<string, byte[]>?    _readRaw;
    private readonly ReadAssetTick?           _assetTick;

    public SectionVariableRowSource(
        Guid assetId, string assetName, Entity entity, string section,
        IVariablesSchemaSource schema,
        // ⭐ U-6: OPTIONAL. At authoring time there is no entity and therefore no bytes; a host that
        //   HAS a reader still passes it (the 2026-08-16 rule), and one that does not says so instead
        //   of handing over a lambda that fabricates emptiness.
        Func<string, byte[]>? readRaw = null,
        ReadAssetTick? assetTick = null)
    {
        _assetId   = assetId;
        _assetName = assetName;
        _entity    = entity;
        _section   = section;
        _schema    = schema ?? throw new ArgumentNullException(nameof(schema));
        _readRaw   = readRaw;
        _assetTick = assetTick;
    }

    public IReadOnlyList<VariableRow> GetRows()
        => _schema.Variables.Select(ToRow).ToList();

    private VariableRow ToRow(VariableViewModel v)
    {
        // ⭐ Row kind is measured off the view model, not passed in -- and the precedence lives in
        //   ONE place so a second source cannot spell it differently.
        var kind = VariableRow.KindOf(v.IsAutoManaged, v.IsReadOnly);

        byte[] cached = Array.Empty<byte>();
        var reader = _readRaw;
        return new VariableRow(
            Origin:    new VariableRowOrigin(_assetId, _entity, _section, v.Name, _assetName),
            ShortName: v.Name,
            TypeText:  v.TypeName,
            ClrType:   v.FieldType,
            ReadValue: reader == null ? () => Array.Empty<byte>() : () => (cached = reader(v.Name)),
            AssetTick: _assetTick,
            RowKind:   kind,
            IsStale:   false,
            // ⚠ NOT an unconditional true. With no reader there is nothing to have been written, and
            //   the cell must read "(pending)" — ⛔ NOT "<unreadable>", which would send a designer
            //   hunting a decode bug that did not happen. Same rule as BlackboardSectionRowSource.
            HasEverBeenWritten: reader != null);
    }
}

/// <summary>A source over a fixed row list. ⭐ Used by §9's heterogeneous rail, and by any host that
/// has already built its rows.</summary>
public sealed class FixedVariableRowSource : IVariableRowSource
{
    private readonly IReadOnlyList<VariableRow> _rows;
    public FixedVariableRowSource(IReadOnlyList<VariableRow> rows) => _rows = rows;
    public IReadOnlyList<VariableRow> GetRows() => _rows;
}

/// <summary>
/// ⭐⭐⭐ <b><c>PinnedSource</c> — the Watch source (§1a).</b> Rows from <b>ARBITRARY assets and
/// entities, mixed</b>.
///
/// <para>
/// ⭐⭐ <b>This is the case <c>C-table</c>'s heterogeneous rail was written for</b>, which is why this
/// type is thin: the control already qualifies names the grouping has not hoisted, already keys the
/// highlight cache by <c>(AssetId, Entity, VariablePath)</c>, and already renders a stale row while
/// refusing its dialog. ⛔ Nothing here teaches it about Watch.
/// </para>
///
/// <para>
/// 🔴 <b>It does NOT go through <c>Watch._valueBuffer</c>.</b> That buffer is <c>new byte[64]</c> and
/// <c>WriteValue</c> <b>throws</b> above it, so <c>MemberSlotList</c> (96), <c>WaveState</c> (104) and
/// <c>HillAttackSharedState</c> (136) cannot pass through it at all. ⇒ ⭐ a pinned row reads its bytes
/// through the same <see cref="ReadRawValue"/> every other row uses, and the 64-byte limit stays a
/// property of that one carrier.
/// </para>
/// </summary>
public sealed class PinnedVariableRowSource : IVariableRowSource
{
    private readonly List<VariableRow> _pinned = new();

    /// <summary>Pins a row. ⚠ Re-pinning the same identity replaces it rather than duplicating.</summary>
    public void Pin(VariableRow row)
    {
        int existing = _pinned.FindIndex(r => r.Origin.Key.Equals(row.Origin.Key));
        if (existing >= 0) _pinned[existing] = row;
        else               _pinned.Add(row);
    }

    public bool Unpin(VariableRowOrigin origin)
    {
        int i = _pinned.FindIndex(r => r.Origin.Key.Equals(origin.Key));
        if (i < 0) return false;
        _pinned.RemoveAt(i);
        return true;
    }

    /// <summary>
    /// ⭐ Marks a row stale — its asset or entity is gone. ⛔ <b>Stale rows are KEPT, not removed</b>
    /// (§1a): a Watch row outliving its asset shows its last value greyed and refuses its dialog.
    /// Dropping it would make the list silently shrink under the designer.
    /// </summary>
    public bool MarkStale(VariableRowOrigin origin)
    {
        int i = _pinned.FindIndex(r => r.Origin.Key.Equals(origin.Key));
        if (i < 0) return false;
        _pinned[i] = _pinned[i] with { IsStale = true };
        return true;
    }

    public IReadOnlyList<VariableRow> GetRows() => _pinned;
}
