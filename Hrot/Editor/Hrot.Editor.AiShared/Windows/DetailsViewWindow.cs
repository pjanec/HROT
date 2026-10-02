using System;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Shell;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — this window's OWN state, dumped.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>
/// §Example.
///
/// <para>⚠ <b>Deliberately narrower than the hosted view's full paint.</b> The content is delegated to
/// an <c>IDetailsViewInstance.Draw</c> with no returned model — that instance is one of the
/// <c>*DetailsView</c> family (group 2 of the sweep), a separate conversion. ⭐ What IS this shell's own
/// state — which view, whether its predicate applies, the resolved context's summary — is captured
/// whole.</para>
///
/// <para>⭐ <b><see cref="PanelKind"/> is the view id itself</b> — one <see cref="DetailsViewWindow"/>
/// hosts exactly one view for its whole life (multiplicity "1"), so the descriptor's id IS the stable
/// logical name conformance would group by.</para>
/// </summary>
public sealed record DetailsViewWindowPanelViewModel(
    string PanelId,
    string PanelKind,
    bool Applies,
    string? EmptyState,
    Guid? AssetId,
    string? AssetName,
    string Perspective,
    string Mode,
    string Focus,
    int SelectionCount,
    int EntityCount) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐⭐ <b><c>L4.1</c> — ONE VIEW, IN ITS OWN WINDOW. Float and pin are the same class.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2's <c>classDiagram</c>
/// *(<c>DetailsViewWindow o-- DetailsViewDescriptor</c> · <c>o-- IDetailsContextSource</c> ·
/// <c>*-- "1" IDetailsViewInstance</c>)* · §2b's float and pin sequences · §6 <c>L4.1</c>–<c>L4.3</c> ·
/// 📌 <c>R-119</c> · <c>R-100</c> · <c>R-117</c> · <c>R-120</c>.
///
/// <para>⭐⭐⭐ <b>§2, verbatim: <i>"the two window classes differ ONLY in
/// <c>IDetailsContextSource</c>."</i></b> ⇒ ⛔ there is <b>no</b> <c>isPinned</c> flag and no second
/// class: a <b>contextual float</b> holds a <see cref="LiveContextSource"/>, a <b>pin</b> holds a
/// <see cref="FrozenContextSource"/>. ⚠ Everything else — chrome, instance lifetime, the grey line —
/// is identical, which is what stops the two drifting apart.</para>
///
/// <para>⭐⭐ <b>Multiplicity <c>"1"</c>, not <c>"0..1"</c>: it composes its OWN instance at
/// construction</b> *(📌 <c>R-120</c> — a view owns no shared state, so two windows showing one view
/// get two instances and there is nothing to arbitrate)*. ⛔ Unlike the docked shell, this window shows
/// exactly one view for its whole life.</para>
///
/// <para>⛔⛔ <b>IT HOLDS NO REFERENCE CAPTURED AT OPEN TIME</b> — 📄 §6 <c>L4</c>, verbatim.
/// ⚠ A float is restored from the layout into contexts that reject it, and §2's hosting table says it
/// <b>stays open with a grey line</b> rather than closing — ⭐ so it must re-ask its source and its
/// predicate every frame, ⛔ never remember what was true when the designer opened it.</para>
/// </summary>
public sealed class DetailsViewWindow : ManagedWindow, IDisposable
{
    private readonly DetailsViewDescriptor  _descriptor;
    private readonly IDetailsContextSource  _context;
    private readonly IDetailsViewInstance   _instance;

    /// <param name="id">
    /// ⭐⭐ The window id. ⚠ For a <b>contextual float</b> this is STABLE *(§6 <c>L4.2</c>)*, so the
    /// layout can restore it; for a <b>pin</b> it is <c>viewId + assetId + selectionKey</c>
    /// *(§2b's pin sequence)*, which is what makes <c>TryGetWindow</c> able to focus a duplicate.
    /// </param>
    /// <param name="isVolatile">
    /// ⭐⭐⭐ <b>The layout-save switch, and the ONLY other difference between the two modes.</b>
    /// 📄 §2's hosting table: a contextual float <b>persists</b> *(<c>false</c>)*; a pin is
    /// <c>IsVolatile</c> *(<c>true</c>)* and is <b>excluded from the layout save</b> — 📌 <c>R-100</c>:
    /// a pin is a transient comparison, ⛔ not a workspace the designer expects back tomorrow.
    /// </param>
    public DetailsViewWindow(
        string                id,
        string                title,
        string                owningPerspective,
        DetailsViewDescriptor descriptor,
        IDetailsContextSource context,
        bool                  isVolatile)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _context    = context    ?? throw new ArgumentNullException(nameof(context));
        _instance   = descriptor.Create()
                      ?? throw new InvalidOperationException(
                          $"Details view '{descriptor.Id}' returned a null instance from its factory.");

        IsVolatile = isVolatile;
        // ⚠ A pin is spawned on demand and must not clutter the persistent Windows menu; a contextual
        //   float is a placement the designer chose and SHOULD be listed there.
        ShowInMenu = !isVolatile;
        IsOpen           = true;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐ Which view this window shows. A rail surface.</summary>
    public string ViewId => _descriptor.Id;

    /// <summary>
    /// ⭐⭐⭐ <b>What this window would draw right now</b> — the same
    /// <i>"assert on a returned model"</i> shape as the shell's <c>Frame()</c>
    /// *(§6, <c>R-21</c>/<c>R-62</c>)*.
    /// </summary>
    /// <param name="Context">⭐ This frame's context, from the source. ⛔ Never null.</param>
    /// <param name="Applies">⭐ Whether the descriptor's own predicate claims it.</param>
    /// <param name="EmptyState">
    ///   ⭐ The grey line, or <see langword="null"/> when the view is showing. ⚠ Non-null <b>exactly</b>
    ///   when <paramref name="Applies"/> is false — 📌 <c>R-117</c>'s <b>second</b> site.
    /// </param>
    public readonly record struct FloatFrame(DetailsContext Context, bool Applies, string? EmptyState);

    /// <summary>
    /// ⭐⭐ Resolve this frame. ⚠ Asks the source ONCE and the predicate ONCE — ⛔ a second
    /// <c>Current()</c> could see a different selection mid-frame.
    /// </summary>
    public FloatFrame Frame()
    {
        var ctx     = _context.Current();
        var applies = _descriptor.AppliesTo(ctx);
        return new FloatFrame(
            ctx,
            applies,
            // ⭐ NAMES THE VIEW — 📌 R-117's row: "empty offer set · a float whose predicate is false".
            //   ⚠ A float stays OPEN when rejected (§2's hosting table), so it must say why or it
            //     reads as stuck.
            applies ? null : DetailsEmptyState.ForInapplicableFloat(Title));
    }

    /// <summary>⭐⭐⭐ BUILD · CAPTURE. ⛔⛔ No ImGui — <see cref="Frame"/> was already pure.</summary>
    private (FloatFrame Frame, DetailsViewWindowPanelViewModel Vm) BuildAndPublish()
    {
        var frame = Frame();
        var ctx   = frame.Context;

        var vm = new DetailsViewWindowPanelViewModel(
            Id, ViewId, frame.Applies, frame.EmptyState,
            ctx.Asset?.AssetId, ctx.Asset?.Name, ctx.Perspective,
            ctx.Mode.ToString(), ctx.Focus.ToString(),
            ctx.Selection.Count, ctx.Entities.Count);

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return (frame, vm);
    }

    /// <summary>
    /// ⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.
    ///
    /// <para>⚠⚠ <b><c>BP-485</c> — it drives the HOSTED VIEW's publish too.</b> 📐 Before this it
    /// published only this window's own model, so a headless reader learned WHICH view was floated and
    /// never what that view SHOWS — ⛔ the hosted view's <c>BuildAndPublish</c> runs inside
    /// <see cref="IDetailsViewInstance.Draw"/>, which only <see cref="DrawClientArea"/> called.
    /// 📌 The identical gap <c>BP-484</c> fixed in <c>DetailsWindow</c>; ⚠ <b>I fixed one of the two
    /// twins and this one sat unmeasured</b> — exactly what <c>PanelIds.Details</c>'s own remark warns
    /// about *("a SECOND CLASS must agree with it")*.</para>
    /// </summary>
    internal DetailsViewWindowPanelViewModel SimulateDrawClientArea()
    {
        var (frame, vm) = BuildAndPublish();

        // ⛔ An empty frame hosts nothing — inventing an entry would claim a panel nobody can see.
        if (frame.EmptyState == null)
            _instance.Draw(frame.Context, Id);

        return vm;
    }

    /// <remarks>⭐⭐ A thin renderer over <see cref="BuildAndPublish"/> — ⛔ it decides nothing.</remarks>
    protected override void DrawClientArea()
    {
        var (frame, _) = BuildAndPublish();

        if (frame.EmptyState != null)
        {
            ImGuiNET.ImGui.TextDisabled(frame.EmptyState);
            return;
        }

        _instance.Draw(frame.Context, Id);
    }

    /// <summary>
    /// ⭐⭐ <b>This window OWNS its instance</b> *(multiplicity <c>"1"</c>)*, so closing it disposes.
    /// ⚠ Whether that frees anything is the VIEW's business — <c>L1.3</c>'s variables view borrows a
    /// shared section and deliberately disposes nothing.
    /// </summary>
    public void Dispose() => _instance.Dispose();
}
