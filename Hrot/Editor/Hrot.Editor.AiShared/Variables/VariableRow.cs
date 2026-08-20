using System;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>Reads a row's raw value bytes. ⭐ RAW, because §4a diffs bytes, not formatted text.</summary>
/// <remarks>
/// ⚠ A plain <c>Func&lt;ReadOnlySpan&lt;byte&gt;&gt;</c> does not compile — a <c>Span</c> cannot be a
/// generic type argument. Hence a named delegate rather than the design's shorthand.
/// </remarks>
public delegate ReadOnlySpan<byte> ReadRawValue();

/// <summary>
/// ⭐⭐⭐ <b>Batch 90 (<c>90a</c>) — reads a row's value as an ALREADY-DECODED CLR object.</b>
///
/// <para>📐 <b>Why an arm at all.</b> The Details table's live seam is <see cref="ReadRawValue"/>, and
/// <b>no production site supplied one</b> ⇒ every Value cell read <c>(pending)</c>. ⚠ Blueprint cannot
/// fill it: its live source is <c>BlueprintStateSnapshot.FieldValues</c>, an
/// <c>IReadOnlyDictionary&lt;string, object&gt;</c> of values the debug session <b>already decoded</b>.
/// ⛔ Re-encoding them to bytes so the byte arm can decode them again is the absurdity
/// <c>REPORT_Batch88</c> §2.2 rejected as option <c>(a)</c>.</para>
///
/// <para>⭐⭐ <b>Why <c>object</c> and not <c>string</c>.</b> The pipeline is
/// <b>bytes → decoder → object → <see cref="VariableValueFormatter"/> → text</b>. An object arm enters
/// it exactly one step in, so the formatter keeps ownership of <b>notation · elision ·
/// <c>&lt;unreadable&gt;</c> · the struct tooltip</b>. ⛔⛔ A <b>string</b> arm would hand notation to
/// the provider ⇒ <b>two notations for one value</b> — 📌 precisely the class of defect
/// <c>BP-01</c>/<c>C8</c> closed *(raw hex where a value belongs)*.</para>
///
/// <para>⚠ <b>The honest cost, stated rather than hidden:</b> §4a's change highlight diffs BYTES, so a
/// row fed through this arm has an <b>INERT</b> highlight. ⭐ That is the safe direction and this
/// codebase already chose it once — see <see cref="ReadAssetTick"/>: <i>"a row with no tick source has
/// an INERT highlight (never lights) rather than a WRONG one."</i> ⛔ <b>Do not fake bytes to light
/// it.</b></para>
///
/// <para>⭐ Returning <c>null</c> means <i>"no value this frame"</i>, which the formatter renders as
/// <c>&lt;unreadable&gt;</c> — ⛔ NOT as <c>(pending)</c>. <b><c>(pending)</c> is decided earlier</b>,
/// by <see cref="VariableRow.HasEverBeenWritten"/>, because <i>"the run has not written this"</i> and
/// <i>"the value would not decode"</i> are different facts.</para>
/// </summary>
public delegate object? ReadObjectValue();

/// <summary>
/// ⭐⭐⭐ <b>Batch 94 (<c>94e</c>) — reads whether the run has written this variable, <b>as of now</b>.</b>
///
/// <para>📌 <c>BP-338</c> made <see cref="VariableRow.HasEverBeenWritten"/> a per-name, per-frame
/// MEASUREMENT — ⚠ but it is a <c>bool</c> on an immutable record, decided when the row is built.
/// Details rebuilds every frame so it stays honest; ⛔ a PINNED row never rebuilds, so a variable the
/// run starts writing AFTER the pin reads <c>(pending)</c> for ever.</para>
///
/// <para>⭐ Supplying this arm makes the answer live, on exactly the same terms as
/// <see cref="ReadObjectValue"/>: optional, trailing, and preferred when present.</para>
/// </summary>
public delegate bool ReadHasEverBeenWritten();

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
/// ⭐⭐⭐ <b>Batch 95 (<c>95a</c>) — reads the AUTHORED DECLARATION this row stands for.</b>
///
/// <para>🔴🔴 <b>Why it exists.</b> The edit gestures resolved a row to its declaration by asking the
/// selection store: <c>if (store.ActiveAsset is not IBlackboardManagedAsset asset) return null;</c>.
/// 📐 <b>Measured:</b> <c>IBlackboardManagedAsset</c> is implemented by <c>HsmAsset</c> and
/// <c>BehaviorTreeAsset</c> and by <b>nothing else</b> — <c>BlueprintAsset</c> implements none of it —
/// ⇒ <b>"Edit value…" and "Properties…" could never open on the Blueprint perspective</b>, on any of
/// its three sections.</para>
///
/// <para>⭐⭐ <b>Why the ROW carries it and not the composition root.</b> 📐 Measured: Blueprint's rows
/// are built by <c>BlueprintMyBlueprintWindow</c>, which resolves <c>Local Variables</c> against
/// <c>_currentGraph()</c> <b>at call time</b> and is registered as an EXTRA window, long after
/// <c>PerspectiveWorkspaceServices.CreateRegistrar</c> has returned. ⇒ a resolver supplied at the
/// composition root could answer the two asset-scoped sections and <b>not</b> the graph-scoped one.
/// ⭐ The source that built the row already holds the schema that declares it, so nothing new reaches
/// a call site that could forget it (📌 <c>R-67</c>).</para>
///
/// <para>📐 <b>What the consumer actually needs, measured before choosing:</b>
/// <c>VariableEditLauncher.Open</c> → <c>DefaultValueAuthoring.OpenSession</c> reads exactly
/// <c>FieldType</c> and <c>DefaultValueJson</c> — <b>nothing else</b>. ⇒ a synthesised entry is fully
/// substitutable for an authored one, which is what made this shape available at all.</para>
///
/// <para>⚠ <c>null</c> means <i>"this source cannot say"</i>, and the resolver then falls back to the
/// active-asset lookup. ⛔ It is NOT <i>"the variable is gone"</i> — that is still the fallback's
/// fail-closed <c>null</c>.</para>
/// </summary>
public delegate BlackboardVariableEntry? ReadVariableDeclaration();

/// <summary>
/// ⭐⭐⭐ <b>Batch 98 (<c>98a</c>) — WRITES this row's authored initial value back to its declaration.</b>
///
/// <para>🔴🔴 <b>The defect.</b> 📐 Measured: <c>VariableEditCommit.CommitInitialValue</c> resolved its
/// write target through <c>PerspectiveWorkspaceRegistrar.DeclarationOwnerOf</c>, which type-tests
/// <c>store.ActiveAsset is IBlackboardManagedAsset</c> — ⛔ and <c>BlueprintAsset</c> is not one. ⇒ in
/// <b>PLANNING</b>, the ordinary authoring state, <c>TargetFor</c> chooses the initial value, the owner
/// was <b>always <c>null</c> on Blueprint</b>, and <b>OK refused on every Blueprint variable, every
/// time.</b> 📌 <c>BP-355</c> named this exact asymmetry — <i>"the same vocabulary mismatch <c>95a</c>
/// fixed for READING, unfixed for WRITING"</i> — and it was never given to anyone as an item.</para>
///
/// <para>⭐⭐ <b>Why the ROW carries it, mirroring <see cref="ReadVariableDeclaration"/>.</b> 📐 Measured
/// before choosing: Blueprint's <c>IVariablesSchemaSource</c>s are constructed <b>inside
/// <c>BlueprintMyBlueprintWindow</c>, per outline selection</b> — the asset-scoped one at <c>:416</c>,
/// the graph-scoped one at <c>:223</c> — ⛔ <b>long after <c>CreateRegistrar</c> has returned</b>, and
/// the graph-scoped one follows the canvas by delegate. ⇒ a seam supplied at the composition root
/// could answer for the two asset-scoped sections and <b>not</b> for Local Variables. ⚠ <b>That is the
/// same measurement <c>95a</c> made</b>, and it is why the read arm lives here too.</para>
///
/// <para>⭐ <b>The source that BUILT the row already holds the writable object</b> —
/// <c>SectionVariableRowSource</c> holds an <c>IVariablesSchemaSource</c>,
/// <c>BlackboardSectionRowSource</c> an <c>IBlackboardManagedAsset</c> — so nothing new reaches a call
/// site that could forget it *(📌 <c>R-67</c>)*, and a <b>pinned</b> Watch row keeps its write-back
/// because a pin copies the row.</para>
///
/// <para>⚠ <b>Returns <c>false</c> for <i>"this source cannot write"</i></b>, and the commit then falls
/// back to the asset arm — ⛔ it is NOT <i>"the write failed"</i>. ⭐ <c>null</c> as the argument
/// CLEARS the authored default, exactly as <c>UpdateVariableDefaultValueJson</c> defines it.</para>
/// </summary>
public delegate bool WriteVariableDefault(string? defaultValueJson);

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
    bool              HasEverBeenWritten = true,
    // ⭐⭐ Row 58 — the INITIAL arm of the ONE Value column (Q32 ruling 3: "initial when not
    //    running, current when running or paused"). The declaration's persisted default, as JSON.
    // ⛔ NOT a second Value column — ruling 3 overrules that explicitly. It is the same column
    //    read through the other arm, and VariableValue.ModeFor picks which.
    // ⚠ Null means "this source cannot say what the initial value is", which is NOT the same as
    //   "there is no default" (a null JSON string with a known ClrType ⇒ zero-initialised, BP-247).
    Func<string?>?    ReadInitialJson = null,
    // ⭐⭐⭐ Batch 90 (90a) — the OBJECT arm of the CURRENT value. null by default, so every existing
    //    construction site is unchanged and every existing row still reads its bytes.
    // ⭐ Preferred over ReadValue when present (VariableValueFormatter.Decode), because a host that
    //   already HAS the decoded value must not be made to re-encode it — REPORT_Batch88 §2.2, option (a).
    // ⚠ It does NOT need ClrType: an object carries its own type. The byte arm does, to decode.
    ReadObjectValue?  ReadValueObject = null,
    // ⭐⭐⭐ Batch 94 (94e) — the LIVE arm of HasEverBeenWritten. null by default and PREFERRED when
    //    present: the exact shape Batch 90 established for ReadValueObject just above.
    // 🔴 Why: HasEverBeenWritten is a bool decided when the row is BUILT. Details rebuilds every
    //    frame so it stays true; a PINNED row never rebuilds ⇒ a variable the run starts writing
    //    AFTER you pinned it read "(pending)" for ever, while Details showed its value.
    //    📄 Q46 §1 "the same bug, second face"; ⚠ guide row C9 is about the opposite error.
    // ⛔ The bool was NOT widened into a delegate — 3 production and ~28 test sites name it, and an
    //   optional arm changes ZERO of them (Q46 §4e, ruling 9: one precedent, not a new idiom).
    ReadHasEverBeenWritten? ReadWritten = null,
    // ⭐⭐⭐ Batch 95 (95a) — the row's own AUTHORED DECLARATION. null by default and preferred when
    //    present: the exact shape Batch 90 established for ReadValueObject and Batch 94 for
    //    ReadWritten (📌 ruling 9 — one precedent, not a new idiom).
    // 🔴 Why: the edit gestures resolved a row by type-testing store.ActiveAsset against
    //    IBlackboardManagedAsset, which BlueprintAsset does not implement ⇒ the dialog could never
    //    open on Blueprint. See ReadVariableDeclaration for the full measurement.
    ReadVariableDeclaration? ReadDeclaration = null,
    // ⭐⭐⭐ Batch 98 (98a) — the WRITE half of ReadDeclaration, and the reason OK refused on every
    //    Blueprint variable while PLANNING. Same optional-and-preferred shape as every arm above
    //    (📌 ruling 9 — one precedent, not a new idiom). See WriteVariableDefault for the measurement.
    WriteVariableDefault? WriteDefault = null)
{
    /// <summary>
    /// ⭐⭐ <b>Has this variable ever been written, as of NOW.</b> ⭐ Prefers <see cref="ReadWritten"/>
    /// when the source supplied one, and falls back to the value decided at build time.
    /// ⛔ Every reader must ask THIS, never the raw <see cref="HasEverBeenWritten"/> field — otherwise
    /// a pinned row keeps reporting its pin-time answer.
    /// </summary>
    public bool WrittenNow => ReadWritten?.Invoke() ?? HasEverBeenWritten;

    /// <summary>§5 — <i>"editability = run state ∧ row kind"</i>. ⛔ 🔒 and node-owned rows never get a
    /// writable dialog, in either mode; a stale row gets no dialog at all.</summary>
    public bool CanEverBeWritten => RowKind == VariableRowKind.Normal && !IsStale;

    /// <summary>
    /// ⭐⭐ <b>THE row-kind rule, in one place.</b> Row kind is MEASURED, never passed in:
    /// <c>IsAutoManaged</c> is the editor-owned (node-owned) marker and <c>IsReadOnly</c> the
    /// passthrough one, and neither is a property the designer sets.
    ///
    /// <para>⛔ Every source that builds rows calls this. Two sources spelling the precedence
    /// themselves is how the same rule drifts — the shape <c>BP-306</c> was.</para>
    /// </summary>
    public static VariableRowKind KindOf(bool isAutoManaged, bool isReadOnly)
        => isAutoManaged ? VariableRowKind.NodeOwned
         : isReadOnly    ? VariableRowKind.ReadOnlyPassthrough
         :                 VariableRowKind.Normal;
}
