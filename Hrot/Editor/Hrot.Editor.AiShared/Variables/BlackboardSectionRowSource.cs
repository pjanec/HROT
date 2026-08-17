using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <b>The rows of ONE section of ONE authored blackboard.</b> This is what an outline click
/// resolves to, and it is what makes the Variables window show something in the running editor.
///
/// <para><b>Why it exists beside <see cref="SectionVariableRowSource"/>.</b> 📐 That source takes an
/// <c>IVariablesSchemaSource</c> and tags every row with a section string — ⛔ <b>it does not FILTER by
/// section</b>, so routing through it would show the whole blackboard under every heading. It is also
/// built for a live <c>(asset, entity)</c> pair with a byte reader; the editor's own outline has
/// neither at authoring time.</para>
///
/// <para>⭐⭐ <b>One classification, not two.</b> Membership is
/// <see cref="BlackboardMyBlueprintModel.SectionOf"/> — the same predicate the outline itself uses —
/// so a variable cannot appear under one heading in the tree and another in the table. And the row
/// kind comes from <see cref="VariableRow.KindOf"/>, so the node-owned/passthrough precedence has one
/// home across every source.</para>
///
/// <para>⚠ <b>No bytes at authoring time, and that is honest.</b> There is no entity yet, so
/// <c>HasEverBeenWritten</c> is <c>false</c> and the Value column renders <c>(pending)</c> — ⛔ NOT
/// <c>&lt;unreadable&gt;</c>, which would claim a decode failure that did not happen. A live host
/// supplies a reader through <paramref name="readRaw"/> and the same rows go live.</para>
/// </summary>
public sealed class BlackboardSectionRowSource : IVariableRowSource
{
    private readonly Func<IBlackboardManagedAsset?> _asset;
    private readonly Guid                           _assetId;
    private readonly string                         _section;
    private readonly Entity                         _entity;
    private readonly Func<string, byte[]>?          _readRaw;

    /// <param name="asset">
    /// ⭐ A DELEGATE, not a snapshot: the active asset changes as the designer switches documents, and
    /// a captured one would go stale on the first switch.
    /// </param>
    /// <param name="readRaw">
    /// Optional byte reader. ⚠ Null at authoring time — see the class remarks; ⛔ a production host
    /// that HAS one must pass it.
    /// </param>
    public BlackboardSectionRowSource(
        Func<IBlackboardManagedAsset?> asset,
        Guid assetId,
        string section,
        Entity entity = default,
        Func<string, byte[]>? readRaw = null)
    {
        _asset   = asset ?? throw new ArgumentNullException(nameof(asset));
        _assetId = assetId;
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _entity  = entity;
        _readRaw = readRaw;
    }

    public IReadOnlyList<VariableRow> GetRows()
    {
        var asset = _asset();
        if (asset == null) return Array.Empty<VariableRow>();

        string assetName = asset.Name;
        return asset.BlackboardVariables
            .Where(v => BlackboardMyBlueprintModel.SectionOf(v) == _section)
            .Select(v => ToRow(v, assetName))
            .ToList();
    }

    private VariableRow ToRow(BlackboardVariableEntry v, string assetName)
    {
        var reader = _readRaw;
        return new VariableRow(
            Origin:    new VariableRowOrigin(_assetId, _entity, _section, v.Name, assetName),
            ShortName: v.Name,
            TypeText:  v.FieldType.Name,
            ClrType:   v.FieldType,
            ReadValue: reader == null ? () => Array.Empty<byte>() : () => reader(v.Name),
            AssetTick: null,
            // ⭐ ONE home for the precedence. An authored entry has no passthrough flag, so the
            //   second argument is false by construction rather than by omission.
            RowKind:   VariableRow.KindOf(v.IsAutoManaged, isReadOnly: false),
            IsStale:   false,
            HasEverBeenWritten: reader != null);
    }
}
