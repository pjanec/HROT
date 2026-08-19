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

    /// <summary>
    /// ⭐⭐⭐ Batch 90 (<c>90b</c>) — this frame's already-decoded live values, or <c>null</c>.
    /// ⛔ A map GETTER, not a per-name reader, because 📌 <b>absence from the map is what makes
    /// <c>(pending)</c> honest</b> — a per-name reader could not distinguish "not written" from
    /// "wrote null".
    /// </summary>
    private readonly Func<IReadOnlyDictionary<string, object>?>? _liveObjects;

    public SectionVariableRowSource(
        Guid assetId, string assetName, Entity entity, string section,
        IVariablesSchemaSource schema,
        // ⭐ U-6: OPTIONAL. At authoring time there is no entity and therefore no bytes; a host that
        //   HAS a reader still passes it (the 2026-08-16 rule), and one that does not says so instead
        //   of handing over a lambda that fabricates emptiness.
        Func<string, byte[]>? readRaw = null,
        ReadAssetTick? assetTick = null,
        // ⭐⭐⭐ Batch 90 — the OBJECT arm. Blueprint's live source hands back DECODED values, so it
        //    fills this rather than readRaw. ⛔ Never both: whichever is supplied is what the row reads.
        Func<IReadOnlyDictionary<string, object>?>? liveObjects = null)
    {
        _assetId     = assetId;
        _assetName   = assetName;
        _entity      = entity;
        _section     = section;
        _schema      = schema ?? throw new ArgumentNullException(nameof(schema));
        _readRaw     = readRaw;
        // ⭐⭐⭐ Batch 94 (94b) — ONE tick source for every host, through the EXISTING seam.
        //    📄 Q46 §2 rule 2b (the user's specification): "the brain (cgf) does not tick ANY behavior
        //    when dt=0 so the tick source is not dependent on behavior type."
        // 🔴 Before this, EVERY production row passed AssetTick: null ⇒ VariableChangeMonitor returned
        //    None on its first line, always — R-67's silent default, and the reason no host has ever
        //    shown a change highlight. ⛔ It was not a missing capability; it was a missing wire.
        // ⭐ An explicit assetTick still wins, so a host with a finer clock is not overridden.
        _assetTick   = assetTick ?? (() => BehaviorFrame.Current);
        _liveObjects = liveObjects;
    }

    /// <summary>
    /// ⭐ Rebuilt every frame by <c>VariableTableModel.Build()</c>, which is what makes the live map
    /// resolved HERE a per-frame snapshot rather than a stale capture.
    /// </summary>
    public IReadOnlyList<VariableRow> GetRows()
    {
        var live = _liveObjects?.Invoke();
        return _schema.Variables.Select(v => ToRow(v, live)).ToList();
    }

    private VariableRow ToRow(VariableViewModel v, IReadOnlyDictionary<string, object>? live)
    {
        // ⭐ Row kind is measured off the view model, not passed in -- and the precedence lives in
        //   ONE place so a second source cannot spell it differently.
        var kind = VariableRow.KindOf(v.IsAutoManaged, v.IsReadOnly);

        // ⭐⭐⭐ Batch 90 — the OBJECT arm wins when this frame's map HAS this name.
        //    ⛔⛔ THE HONESTY RULE, and guide row C9 depends on it: presence in the map ⇒ written;
        //    ABSENCE ⇒ (pending). A provider that could not project a variable simply omits it, so
        //    absence is free and meaningful. ⚠ A live map that exists but lacks this name is NOT
        //    "written with a zero" — that would be the regression, not the fix.
        // ⭐⭐⭐ Batch 94 (94e) — the OBJECT ARM IS CHOSEN BY THE PROVIDER, NOT BY THIS FRAME'S MAP.
        //    🔴 It used to be `live != null && live.ContainsKey(v.Name)`, i.e. a row for a variable the
        //    run had not yet written took the BYTE arm and got no live arms at all ⇒ once pinned it
        //    could never unpend — which is precisely the case "(pending)" exists for.
        // ⭐ A host that has a live-object provider gets an object-arm row unconditionally; whether
        //   the variable is written yet is then asked per read, not baked in.
        if (_liveObjects != null)
        {
            // ⭐⭐⭐ Batch 94 (94a) — A CAMERA, NOT A PHOTOGRAPH.
            //    🔴 This used to be `var value = live![v.Name]; … () => value` — a closure over THIS
            //    FRAME'S value. The arm was still invoked every frame, so Details looked live; what
            //    was actually live was the ROW REBUILD (VariableTableModel.Build() → GetRows()).
            //    ⛔ PinnedVariableRowSource never rebuilds ⇒ a pinned row froze at pin time for ever
            //    — measured in Batch 93, railed in APinnedRowIsASnapshotTests.
            // ⭐ Closing over the PROVIDER instead makes the row an accessor, which is what Q46 §2
            //    rule 1 specifies: "one row = one accessor".
            // ⚠ The NAME is hoisted deliberately: `v` is the loop's view model and capturing it
            //    would keep the whole schema entry alive per row.
            var name = v.Name;
            return NewRow(v, kind,
                readValue:   () => Array.Empty<byte>(),
                readObject:  () => _liveObjects?.Invoke() is { } m && m.TryGetValue(name, out var cur)
                                       ? cur
                                       : null,
                // ⭐⭐ THE HONESTY RULE, and guide row C9 depends on it: presence in the map ⇒ written;
                //    ABSENCE ⇒ (pending). A provider that could not project a variable simply omits
                //    it, so absence is free and meaningful. ⚠ A live map that exists but lacks this
                //    name is NOT "written with a zero" — that would be the regression, not the fix.
                written:     live != null && live.ContainsKey(name),
                writtenNow:  () => _liveObjects?.Invoke()?.ContainsKey(name) == true);
        }

        // ⭐ The byte arm, unchanged in shape. ⚠ Presence is now MEASURED — a reader that returns no
        //   bytes for this name has not written it, which is the same rule the object arm follows.
        var reader = _readRaw;
        if (reader == null)
            // ⛔ No arm here: with no reader at all nothing can ever say the variable was written,
            //   so a live arm would only re-answer `false` at a cost.
            return NewRow(v, kind, () => Array.Empty<byte>(), readObject: null, written: false);

        byte[] cached;
        try { cached = reader(v.Name) ?? Array.Empty<byte>(); }
        catch { cached = Array.Empty<byte>(); }   // ⭐ a monitor never takes the window down

        // ⭐⭐⭐ Batch 94 (94a) — the byte arm becomes a camera too. ⛔⛔ BOTH ARMS, NEVER ONE:
        //    📌 Q46 §4a — fixing only the object arm would make pinning work on Blueprint and
        //    silently freeze on BTree/HSM, which is exactly the split U-6 removed.
        // ⚠ `cached` is still read EAGERLY, and that is not redundant: `written` is decided now,
        //    per name, per frame (BP-338), while the ARM is what the row reads later.
        var readName = v.Name;
        return NewRow(v, kind,
            () => { try { return reader(readName) ?? Array.Empty<byte>(); }
                    catch { return Array.Empty<byte>(); } },
            readObject: null,
            written:    cached.Length > 0,
            writtenNow: () => { try { return (reader(readName)?.Length ?? 0) > 0; }
                                catch { return false; } });
    }

    private VariableRow NewRow(
        VariableViewModel v, VariableRowKind kind,
        ReadRawValue readValue, ReadObjectValue? readObject, bool written,
        ReadHasEverBeenWritten? writtenNow = null)
        => new(
            Origin:    new VariableRowOrigin(_assetId, _entity, _section, v.Name, _assetName),
            ShortName: v.Name,
            TypeText:  v.TypeName,
            ClrType:   v.FieldType,
            ReadValue: readValue,
            AssetTick: _assetTick,
            RowKind:   kind,
            IsStale:   false,
            // ⚠ NOT an unconditional true, and never has been. With nothing written the cell must read
            //   "(pending)" — ⛔ NOT "<unreadable>", which would send a designer hunting a decode bug
            //   that did not happen. Same rule as BlackboardSectionRowSource.
            HasEverBeenWritten: written,
            // ⭐ Row 58 — the INITIAL arm, from whatever the schema source knows.
            ReadInitialJson: () => v.DefaultValueJson,
            ReadValueObject: readObject,
            // ⭐⭐⭐ Batch 94 (94e) — the LIVE (pending) arm. 📌 Same rule as `written` above, asked
            //    again each time it is read: presence in this frame's live map, or non-empty bytes.
            //    ⇒ a variable the run starts writing after the row was PINNED stops saying (pending).
            ReadWritten: writtenNow);
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
