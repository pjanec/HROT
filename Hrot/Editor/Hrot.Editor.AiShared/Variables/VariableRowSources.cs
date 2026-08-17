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
    private readonly Func<string, byte[]>     _readRaw;
    private readonly ReadAssetTick?           _assetTick;

    public SectionVariableRowSource(
        Guid assetId, string assetName, Entity entity, string section,
        IVariablesSchemaSource schema,
        Func<string, byte[]> readRaw,
        ReadAssetTick? assetTick = null)
    {
        _assetId   = assetId;
        _assetName = assetName;
        _entity    = entity;
        _section   = section;
        _schema    = schema ?? throw new ArgumentNullException(nameof(schema));
        _readRaw   = readRaw ?? throw new ArgumentNullException(nameof(readRaw));
        _assetTick = assetTick;
    }

    public IReadOnlyList<VariableRow> GetRows()
        => _schema.Variables.Select(ToRow).ToList();

    private VariableRow ToRow(VariableViewModel v)
    {
        // ⭐ Row kind is measured off the view model, not passed in: `IsAutoManaged` is the editor-owned
        //   (node-owned) marker and `IsReadOnly` the passthrough one -- §5's "editability = run state ∧
        //   row kind" needs both, and neither is a property the designer sets.
        var kind = v.IsAutoManaged ? VariableRowKind.NodeOwned
                 : v.IsReadOnly    ? VariableRowKind.ReadOnlyPassthrough
                 :                   VariableRowKind.Normal;

        byte[] cached = Array.Empty<byte>();
        return new VariableRow(
            Origin:    new VariableRowOrigin(_assetId, _entity, _section, v.Name, _assetName),
            ShortName: v.Name,
            TypeText:  v.TypeName,
            ClrType:   v.FieldType,
            ReadValue: () => (cached = _readRaw(v.Name)),
            AssetTick: _assetTick,
            RowKind:   kind,
            IsStale:   false,
            HasEverBeenWritten: true);
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
