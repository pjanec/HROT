using System;
using Fdp.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>Reads a row's raw value bytes. ⭐ RAW, because §4a diffs bytes, not formatted text.</summary>
/// <remarks>
/// ⚠ A plain <c>Func&lt;ReadOnlySpan&lt;byte&gt;&gt;</c> does not compile — a <c>Span</c> cannot be a
/// generic type argument. Hence a named delegate rather than the design's shorthand.
/// </remarks>
public delegate ReadOnlySpan<byte> ReadRawValue();

/// <summary>
/// ⭐⭐⭐ Reads <b>THIS row's</b> asset tick (§4a).
///
/// <para>
/// 🔴🔴 <b>Nullable, and that is the whole point.</b> <c>null</c> means <i>"this row has no asset-tick
/// source"</i> — not <i>"tick zero"</i>. ⛔ A sentinel <c>uint</c> could not express it: <c>0</c> is a
/// legal tick. ⭐ A row with no tick source has an <b>INERT</b> highlight (never lights) rather than a
/// <b>WRONG</b> one, which is the only safe direction — see <see cref="VariableChangeMonitor"/>.
/// </para>
/// </remarks>
public delegate uint? ReadAssetTick();

/// <summary>
/// ⭐⭐ Row identity (§1a). <b><see cref="Entity"/> is PART of it</b> — the same asset on two entities
/// has two different values, so the key is <c>(AssetId, Entity, VariablePath)</c>, ⛔ never
/// <c>(asset, variable)</c>.
/// </summary>
/// <param name="AssetName">
/// ⚠ <b>Display text, not identity.</b> Added beyond the design's four-tuple because §1a's
/// qualification rule renders <c>PlatoonHillAttack2.Health</c> and a <see cref="Guid"/> cannot be shown
/// to a designer. ⛔ Equality deliberately still runs on the whole record, but every lookup key in this
/// namespace is built from <see cref="AssetId"/>/<see cref="Entity"/>/<see cref="VariablePath"/> only.
/// </param>
public readonly record struct VariableRowOrigin(
    Guid   AssetId,
    Entity Entity,
    string Section,
    string VariablePath,
    string AssetName = "")
{
    /// <summary>⭐ The identity triple §4a keys the highlight cache by. ⛔ <see cref="AssetName"/> and
    /// <see cref="Section"/> are excluded — a row does not change identity because it was regrouped.</summary>
    public (Guid, Entity, string) Key => (AssetId, Entity, VariablePath);
}

/// <summary>§1a / §5 — the two kinds that can never get a writable dialog.</summary>
public enum VariableRowKind
{
    Normal,
    /// <summary>🔒 read-only passthrough.</summary>
    ReadOnlyPassthrough,
    /// <summary><c>IsAutoManaged</c> — the editor owns it.</summary>
    NodeOwned,
}

/// <summary>
/// ⭐⭐⭐ <b>One row of the variable table — SELF-DESCRIBING (§1a).</b>
///
/// <para>
/// ⛔⛔ <b>The table is NOT "the view of one asset".</b> It renders an
/// <c>IReadOnlyList&lt;VariableRow&gt;</c> and knows nothing about where the rows came from, because
/// Watch mixes rows from arbitrary assets and entities — <i>"the watch window must allow for selected
/// variables from different assets"</i>. ⇒ ⭐ <b>the row carries its own identity and its own
/// accessors; the panel never reaches back to "the asset", because in Watch there is no single one.</b>
/// </para>
///
/// <para>
/// ⚠ <b><see cref="ShortName"/> is SHORT on purpose.</b> §1a was corrected: an earlier draft put
/// qualification in the source's display name, so every row would repeat <c>Asset.Var</c>. ⇒ the source
/// supplies the short name plus the origin facets, and ⭐ <b>the CONTROL qualifies only what grouping
/// has not already hoisted</b> — because only the control knows the active <c>GroupBy</c>.
/// </para>
/// </summary>
public sealed record VariableRow(
    VariableRowOrigin Origin,
    string            ShortName,
    string            TypeText,
    Type?             ClrType,
    ReadRawValue      ReadValue,
    ReadAssetTick?    AssetTick    = null,
    VariableRowKind   RowKind      = VariableRowKind.Normal,
    bool              IsStale      = false,
    bool              HasEverBeenWritten = true)
{
    /// <summary>§5 — <i>"editability = run state ∧ row kind"</i>. ⛔ 🔒 and node-owned rows never get a
    /// writable dialog, in either mode; a stale row gets no dialog at all.</summary>
    public bool CanEverBeWritten => RowKind == VariableRowKind.Normal && !IsStale;
}
