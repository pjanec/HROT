using System;
using System.Collections.Generic;
using Fdp.Presentation.Editing;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b>The services every perspective shares, held ONCE so the three perspectives cannot
/// disagree about them.</b>
///
/// <para>🔴🔴 <b>Why this type exists — <c>R-67</c>, the FOURTH instance.</b>
/// <c>EditorSubsystem.RegisterWindows</c> built three <see cref="PerspectiveWorkspaceRegistrar"/>s by
/// hand, 40 lines apart, each with its own 14-argument list. 📐 <b>Measured <c>2026-08-18</c>:</b>
/// <c>facetEditService</c> was passed to the BTree registrar (<c>:2134</c>) and the HSM one
/// (<c>:2158</c>) and <b>omitted from the Blueprint one</b> (<c>:2162</c>) — so
/// <c>if (facetEditService != null)</c> was false there, <c>EditGestures</c> was null, and
/// ⛔ <b>"Edit value…" and "Properties…" did nothing on the perspective the user was looking at.</b>
/// ⚠ The same list also silently dropped <c>expressionTargetFieldAccessor</c>,
/// <c>aggregatorService</c> and <c>liveValueProvider</c> there.</para>
///
/// <para>📌 <b><c>CLAUDE.md</c>'s silent-default pattern, verbatim:</b> <i>"a production caller that HAS
/// a dependency must PASS it."</i> ⭐ Batches 80, 82 and 83 each fixed one instance by passing one more
/// argument. ⛔ <b>That does not compose</b> — the next shared service is one more thing three call
/// sites must remember, and the third one has now forgotten three times.</para>
///
/// <para>⭐⭐⭐ <b>The move is ruling 9's:</b> ONE construction path instead of three lists that must
/// agree. ⇒ <b>divergence on a shared service becomes impossible by construction</b>, not merely
/// gated. ⭐ What stays per-perspective is what genuinely differs — the name, the selection store, the
/// validators, the live-value provider — and each of those is REQUIRED at
/// <see cref="CreateRegistrar"/>, so it cannot be silently dropped either.</para>
///
/// <para>⛔⛔ <b><see cref="FacetEditService"/> and the two clock signals are REQUIRED, and throw.</b>
/// ⭐ That is deliberate and is the strongest half of this fix: the omission is no longer a thing a
/// caller can express. ⚠ A rail can only catch a defect that is expressible.</para>
/// </summary>
public sealed class PerspectiveWorkspaceServices
{
    // ── Required ──────────────────────────────────────────────────────────────

    /// <summary>The shared asset catalog.</summary>
    public IAssetCatalog Catalog { get; }

    /// <summary>The shared refactor service.</summary>
    public IRefactorService RefactorService { get; }

    /// <summary>
    /// The shared debug-session registry. ⚠ <b>Not a clock</b> — 📌 <c>R-66</c>: its
    /// <c>ActiveSession</c> answers <i>"which document's session is active"</i>. Run state comes from
    /// <see cref="IsSimUp"/> / <see cref="IsFrozen"/>.
    /// </summary>
    public IDebugSessionRegistry DebugRegistry { get; }

    /// <summary>
    /// ⭐⭐⭐ The StructEdit edit service. ⛔ <b>REQUIRED</b> — 📌 <c>R-67</c>: this is the argument the
    /// Blueprint perspective was missing, and the whole variable edit dialog hangs off it.
    /// </summary>
    public IComponentEditService FacetEditService { get; }

    /// <summary>
    /// ⭐⭐ Is the simulation running at all? ⛔ <b>REQUIRED</b> — 📌 <c>R-66</c>: leaving it optional is
    /// how the editor came to answer this question with <i>"is a document open?"</i>.
    /// </summary>
    public Func<bool> IsSimUp { get; }

    /// <summary>
    /// ⭐⭐ Is time held by the debugger — a breakpoint pause OR deterministic stepping?
    /// ⛔ <b>REQUIRED</b>. 📌 Ruling 15 names both arms and the write surface depends on this one.
    /// </summary>
    public Func<bool> IsFrozen { get; }

    // ── Optional, but shared when present ─────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>L0.4</c> — where the Details context gets its selected ENTITIES</b>
    /// *(<c>R-122</c>: "entity selection is on the entity")*. ⭐ Production passes a
    /// <c>WorldEntitySelectionSource</c> over the kernel's live world.
    ///
    /// <para>⛔⛔ <b>It belongs in THIS bag and nowhere else.</b> 📌 This class exists because
    /// <i>"the next shared service is one more thing three call sites must remember, and the third one
    /// has now forgotten three times"</i> — ⚠ a per-perspective entity source would be exactly that
    /// mistake, and the entity is <b>one fact about the world</b>, not a per-perspective one.</para>
    ///
    /// <para>⚠ Optional, because headless hosts have no World; ⛔ but a production caller that HAS one
    /// must pass it, and <c>TheEntityContextReadsTheWorldTests</c> asserts that on the CONSTRUCTED
    /// object rather than on this declaration.</para>
    /// </summary>
    public Shell.IEntitySelectionSource? EntitySelection { get; init; }

    /// <summary>
    /// ⭐⭐⭐ <b><c>W4</c> — the ONE staged-write query every variable surface reads.</b>
    /// 📄 <c>DESIGN_Staged_Live_Write.md</c> §4 fork A, §7 *(<c>R-120</c>)*.
    ///
    /// <para>⛔⛔ <b>In THIS bag for the same reason <see cref="EntitySelection"/> is:</b> what is
    /// staged is <b>one fact about the editor</b>, not a per-perspective one. ⚠ A per-perspective
    /// staged set would let a Blueprint Details panel and a Blueprint Watch — built by the same
    /// registrar — still agree, while the same variable on another perspective's surface disagreed;
    /// 📌 §7 is explicit that the surfaces <b>do NOT diverge</b>.</para>
    ///
    /// <para>⚠ Optional, because a headless host has no <c>DataBreakpointManager</c> and nothing can be
    /// staged. ⛔ But a production caller that HAS one must pass it *(the <c>2026-08-16</c> rule)*, and
    /// the rail asserts that on the CONSTRUCTED models — Details and Watch holding the SAME
    /// instance — ⛔ never on this declaration.</para>
    /// </summary>
    public Variables.StagedWriteView? StagedWrites { get; init; }

    /// <summary>Shared breakpoint manager; drives the per-perspective Watch + Breakpoints windows.</summary>
    public IDataBreakpointManager? BreakpointManager { get; init; }

    /// <summary>Comparison sanitizer registry (AIE-050).</summary>
    public SanitizerRegistry? SanitizerRegistry { get; init; }

    /// <summary>Comparison export builder (AIE-050).</summary>
    public ComparisonExportBuilder? ExportBuilder { get; init; }

    /// <summary>Comparison session registry (AIE-050).</summary>
    public ComparisonSessionRegistry? SessionRegistry { get; init; }

    /// <summary>Blackboard aggregator service (AIE-052).</summary>
    public BlackboardAggregatorService? AggregatorService { get; init; }

    /// <summary>Action schema exporter (AIE-053 / DEBT-AIB-009).</summary>
    public IActionSchemaExporter? SchemaExporter { get; init; }

    /// <summary>Attribute-dispatched picker drawers for the Inspector (SE1).</summary>
    public IReadOnlyDictionary<Type, IImGuiFieldDrawer>? FacetCustomDrawers { get; init; }

    /// <summary>Extracts <c>ExpressionTargetField</c> from a boxed facet struct (B-3).</summary>
    public Func<object?, string?>? ExpressionTargetFieldAccessor { get; init; }

    /// <summary>Decoder for raw blackboard bytes; shared by the table and the Watch formatter.</summary>
    public DecodeRawValue? ValueDecoder { get; init; }

    /// <summary>
    /// ⭐⭐ <b><c>AQ55</c> — the host's "point at an entity" capability</b>, handed to every
    /// perspective's Watch window. 📄 <c>Architect_Question_55_Watch_Concrete_Entity_Picker.md</c>.
    ///
    /// <para>⚠ Optional: a headless host and a shell with no map have nothing to pick with, and the
    /// menu entry is then ABSENT rather than dead. ⛔ But a composition root that HAS a map-pick
    /// service must pass it *(the <c>2026-08-16</c> rule)*, and the rail asserts that on the
    /// CONSTRUCTED window's <c>HasEntityPicker</c> — ⛔ never on this declaration.</para>
    /// </summary>
    public Variables.WatchEntityPicker? EntityPicker { get; init; }

    /// <param name="facetEditService">
    ///   ⛔ <b>Throws when null.</b> 📌 <c>R-67</c> — the omission this type exists to make impossible.
    /// </param>
    /// <param name="isSimUp">⛔ <b>Throws when null.</b> 📌 <c>R-66</c>.</param>
    /// <param name="isFrozen">⛔ <b>Throws when null.</b> 📌 ruling 15.</param>
    public PerspectiveWorkspaceServices(
        IAssetCatalog         catalog,
        IRefactorService      refactorService,
        IDebugSessionRegistry debugRegistry,
        IComponentEditService facetEditService,
        Func<bool>            isSimUp,
        Func<bool>            isFrozen)
    {
        Catalog          = catalog          ?? throw new ArgumentNullException(nameof(catalog));
        RefactorService  = refactorService  ?? throw new ArgumentNullException(nameof(refactorService));
        DebugRegistry    = debugRegistry    ?? throw new ArgumentNullException(nameof(debugRegistry));
        FacetEditService = facetEditService ?? throw new ArgumentNullException(nameof(facetEditService));
        IsSimUp          = isSimUp          ?? throw new ArgumentNullException(nameof(isSimUp));
        IsFrozen         = isFrozen         ?? throw new ArgumentNullException(nameof(isFrozen));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The ONE way a perspective's registrar is built in production.</b>
    ///
    /// <para>⭐ Every argument here is something that genuinely DIFFERS per perspective. ⛔ Everything
    /// shared comes from this object, so no call site can pass a different value or forget one.</para>
    /// </summary>
    /// <param name="perspectiveName">⭐ <c>"BTree"</c> / <c>"HSM"</c> / <c>"Blueprint"</c>.</param>
    /// <param name="selectionStore">⭐ The perspective's own store — never shared.</param>
    /// <param name="validators">
    ///   ⭐ Host-specific validators. ⛔ Required rather than defaulted: a perspective with no
    ///   validators must SAY so, because "I forgot" and "there are none" looked identical before.
    /// </param>
    /// <param name="liveValueProvider">
    ///   ⭐ The perspective's live-value provider, or null where none exists yet (Blueprint).
    /// </param>
    /// <param name="writeLive">
    ///   ⭐⭐ The perspective's LIVE blackboard writer, or null where none exists.
    ///   ⚠ <b>Genuinely per-perspective</b>, exactly like <paramref name="liveValueProvider"/>: 📐 only
    ///   Blueprint has a live write path *(<c>IBlueprintDebugSession</c>)*; BTree/HSM have none, and
    ///   ⛔ their <c>LiveWriteUnavailable</c> refusal is the honest answer, not a gap to paper over.
    /// </param>
    /// <param name="hostKind">
    ///   ⭐ Override only. ⛔ Normally null — 📌 Batch 80 made the registrar DERIVE it from the
    ///   perspective name precisely because a caller forgot it for two perspectives.
    /// </param>
    public PerspectiveWorkspaceRegistrar CreateRegistrar(
        string                        perspectiveName,
        EditorSelectionStore          selectionStore,
        IReadOnlyList<IAssetValidator> validators,
        ILiveBlackboardValueProvider? liveValueProvider = null,
        BlackboardHostKind?           hostKind          = null,
        WriteLiveValue?               writeLive         = null)
        => new PerspectiveWorkspaceRegistrar(
            perspectiveName, selectionStore, Catalog, RefactorService, DebugRegistry,
            validators:                    validators ?? throw new ArgumentNullException(nameof(validators)),
            breakpointManager:             BreakpointManager,
            sanitizerRegistry:             SanitizerRegistry,
            exportBuilder:                 ExportBuilder,
            sessionRegistry:               SessionRegistry,
            aggregatorService:             AggregatorService,
            schemaExporter:                SchemaExporter,
            facetEditService:              FacetEditService,
            facetCustomDrawers:            FacetCustomDrawers,
            expressionTargetFieldAccessor: ExpressionTargetFieldAccessor,
            liveValueProvider:             liveValueProvider,
            hostKind:                      hostKind,
            valueDecoder:                  ValueDecoder,
            isSimUp:                       IsSimUp,
            isFrozen:                      IsFrozen,
            writeLive:                     writeLive,
            entitySelection:               EntitySelection,
            stagedWrites:                  StagedWrites,
            entityPicker:                  EntityPicker);
}
