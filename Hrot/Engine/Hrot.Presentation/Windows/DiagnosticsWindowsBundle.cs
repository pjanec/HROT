using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.ModuleHost;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Toolkit.Runner;
using Hrot.Presentation.Facades;

namespace Hrot.Presentation.Windows;

/// <summary>
/// ⭐⭐⭐ <b>The four diagnostics windows every visual host offers, registered ONCE.</b>
///
/// <para>📐 <b>Measured `2026-08-27`:</b> the entity inspector, the event browser, the architecture
/// diagnostics window, the system profiler and the "Inspect…" context-menu wiring were instantiated at
/// <b>20 sites across 4 hosts</b> — IG, SimHost, CGF and the Editor — as five near-identical blocks of
/// copy-paste. ⛔ 20 sites is 20 chances to drift, and three had already drifted *(see the
/// <c>DiagnosticsHostServices</c> remarks)</para>.
///
/// <para>⚠⚠ <b>NOT five hosts.</b> ReplayBrowser registers
/// <c>Fdp.Presentation.Windows.ReplayBrowser.FdpEntityInspectorWindow</c> — a <b>different type in a
/// different assembly</b> — and has no profiler or architecture window at all. ⇒ ⛔ it cannot join this
/// bundle, and an earlier plan that counted it as a fifth adopter was wrong.</para>
///
/// <para>⭐⭐ <b>The ids and titles are DERIVED from two strings</b>, because measurement showed all four
/// hosts already followed one scheme exactly: <c>{IdPrefix}fdp_inspector</c> titled
/// <c>"{TitlePrefix} Entity Inspector"</c>, and so on. ⛔ A host that wanted a different id would be
/// changing users' saved layouts, so there is deliberately no way to override one.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.7 *(the
/// <c>classDiagram</c>/<c>sequenceDiagram</c> this type is drawn in, and decisions
/// <c>F1</c>–<c>F6</c>)</para>.
/// </summary>
public sealed class DiagnosticsWindowsBundle : IUiBundle
{
    private readonly DiagnosticsHostServices _host;

    public DiagnosticsWindowsBundle(DiagnosticsHostServices host)
        => _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <inheritdoc/>
    public string Name => "diagnostics-windows";

    /// <inheritdoc/>
    public void RegisterInto(UiBundleContext ctx)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        var h = _host;

        ctx.Windows.RegisterWindow(new FdpEntityInspectorWindow(
            InspectorId(h.IdPrefix), $"{h.TitlePrefix} Entity Inspector", h.Perspective,
            h.Inspector,
            h.RepoAdapter,
            h.InspectorState,
            h.TitleBarColor));

        // ⭐ The component-editor reflector + the "Inspect…" context menu.
        // ⚠ This registers NOTHING eagerly — the helper's own RegisterWindow sits inside the
        //   "Inspect…" click handler — so its position relative to the window above cannot change the
        //   registered set. 📐 That is measured, and it is why CGF's old call site (which ran BEFORE its
        //   inspector window) can move here safely.
        // ⛔ It takes its OWN colour: two hosts genuinely pass a different shade here (see the record).
        FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu(
            h.Inspector,
            ctx.Windows,
            h.Perspective,
            h.RepoAdapter,
            h.PickBridge,
            h.InspectContextMenuTitleBarColor ?? h.TitleBarColor);

        ctx.Windows.RegisterWindow(new FdpEventBrowserWindow(
            EventsId(h.IdPrefix), $"{h.TitlePrefix} Event Browser", h.Perspective,
            h.EventBrowser,
            h.TitleBarColor));

        // ⭐⭐ RULING 49 — absent-and-explained beats present-and-broken. A host that cannot service
        //    these two does not register them, which is exactly what the editor's `if (_kernel != null)`
        //    guard said before this bundle existed. ⛔ Registering them unconditionally would have moved
        //    the editor's window set and therefore the ui-baseline golden (design §5c.7.2 G1).
        if (h.ArchitecturePanel != null)
            ctx.Windows.RegisterWindow(new ArchitectureDiagnosticsWindow(
                ArchitectureId(h.IdPrefix), $"{h.TitlePrefix} Architecture Diagnostics", h.Perspective,
                h.ArchitecturePanel,
                h.TitleBarColor));

        // BP-327 — the module/system execution-stats profiler.
        if (h.ExecutionStats != null)
            ctx.Windows.RegisterWindow(new SystemProfilerWindow(
                ProfilerId(h.IdPrefix), $"{h.TitlePrefix} System Profiler", h.Perspective,
                h.ExecutionStats,
                h.TitleBarColor));
    }

    // ⭐ The id scheme, in one place and PUBLIC — so the equivalence rail asserts against the same
    //   expressions the bundle uses, and so a host can never spell one differently by hand.
    public static string InspectorId(string idPrefix)    => $"{idPrefix}fdp_inspector";
    public static string EventsId(string idPrefix)       => $"{idPrefix}fdp_events";
    public static string ArchitectureId(string idPrefix) => $"{idPrefix}architecture_diagnostics";
    public static string ProfilerId(string idPrefix)     => $"{idPrefix}system_profiler";
}

/// <summary>
/// ⭐⭐ <b>Everything the four hosts actually differ by</b> — the ctor-arg bag for
/// <see cref="DiagnosticsWindowsBundle"/>.
///
/// <para>⭐ Services arrive as CONSTRUCTOR ARGUMENTS, never through <c>UiBundleContext</c> (design
/// <c>D1</c>): the context deliberately hands out no kernel, no bus and no module registry, and widening
/// it would breach that.</para>
///
/// <para>⚠⚠ <b>THREE MEASURED DRIFTS ARE PRESERVED HERE, NOT TIDIED AWAY</b> — 📐 all three measured
/// `2026-08-27`, all three recorded in design §5c.7.2:</para>
/// <list type="number">
///   <item><b><c>G1</c> — the kernel.</b> The editor guarded its architecture + profiler windows on
///     <c>if (_kernel != null)</c> and bound the kernel EAGERLY; the other three bound it lazily and did
///     not guard. ⇒ ⭐ this record takes the <b>already-built</b>
///     <see cref="ArchitectureDiagnosticsPanel"/> and a stats delegate, so each host keeps its own
///     construction verbatim and <b>nothing about that split changes</b>. ⛔ Passing null is how the
///     editor's guard survives.</item>
///   <item><b><c>G2</c> — two colours.</b> IG and SimHost pass a genuinely DIFFERENT title-bar colour to
///     the "Inspect…" helper than to their windows *(IG <c>(0.07,0.30,0.07)</c> vs
///     <c>(0.08,0.40,0.08)</c>; SimHost <c>(0.50,0.10,0.10)</c> vs <c>(0.40,0.08,0.08)</c>)</item>;
///     Editor and CGF pass one value to both. ⇒ <see cref="InspectContextMenuTitleBarColor"/> exists so
///     that difference is explicit rather than silently recoloured. ⚠ It looks like latent drift — ⛔ but
///     changing a colour is not a unification slice's business.
///   <item><b><c>G3</c> — the reflector block is NOT here.</b> The ~30-line
///     <c>AddBufferViewProvider</c>/<c>EditContextFactory</c> setup is duplicated verbatim between CGF
///     and the editor and is <b>absent on IG/SimHost</b> ⇒ ⛔ putting it in this bundle would hand two
///     hosts a capability they do not have. ⭐ It lives in
///     <see cref="BlackboardReflection"/> with exactly two callers.</item>
/// </list>
/// </summary>
/// <param name="IdPrefix">e.g. <c>"ig_"</c> — ⚠ include the trailing underscore.</param>
/// <param name="TitlePrefix">e.g. <c>"IG"</c> — the window titles read <c>"IG Entity Inspector"</c>.</param>
/// <param name="Perspective">
/// ⚠ The PERSPECTIVE, not a display name. It is also the "Inspect…" watch windows' id prefix
/// (lower-cased by the helper), so <c>"Scenario"</c> on CGF/Editor and the host name on IG/SimHost.
/// </param>
/// <param name="ArchitecturePanel">⛔ null ⇒ the window is NOT registered (ruling 49, and <c>G1</c>).</param>
/// <param name="ExecutionStats">⛔ null ⇒ the profiler is NOT registered.</param>
/// <param name="InspectContextMenuTitleBarColor">
/// ⭐ Defaults to <paramref name="TitleBarColor"/>. Pass it only when the host really does use a different
/// shade for spawned watch windows — see <c>G2</c>.
/// </param>
public sealed record DiagnosticsHostServices(
    string IdPrefix,
    string TitlePrefix,
    string Perspective,
    EntityInspectorPanel Inspector,
    Func<RepositoryAdapter?> RepoAdapter,
    Func<InspectorState> InspectorState,
    EventBrowserPanel EventBrowser,
    Vector4? TitleBarColor,
    ArchitectureDiagnosticsPanel? ArchitecturePanel = null,
    Func<List<ModuleStats>?>? ExecutionStats = null,
    MapPickServiceBridge? PickBridge = null,
    Vector4? InspectContextMenuTitleBarColor = null);
