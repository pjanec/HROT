using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — this shell's OWN decision, dumped.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>
/// §Example.
///
/// <para>⚠ <b>Deliberately narrower than the chosen view's full paint</b> — the same limit
/// <see cref="DetailsViewWindow"/>'s conversion states: the content is delegated to an
/// <c>IDetailsViewInstance.Draw</c> with no returned model, a separate conversion (group 2). ⭐ What IS
/// this shell's own decision — which view is chosen, the whole offer set, the empty-state reason, the
/// toolbar affordances' visibility, and the resolved context's summary — is captured whole.</para>
/// </summary>
public sealed record DetailsWindowPanelViewModel(
    string PanelId,
    string PanelKind,
    string? ChosenViewId,
    IReadOnlyList<string> OfferedViewIds,
    string? EmptyState,
    bool ShowsViewSwitch,
    bool ShowsFloatAndPin,
    Guid? AssetId,
    string? AssetName,
    string Perspective,
    string Mode,
    string Focus,
    int SelectionCount,
    int EntityCount,
    string Layout = "Tabs") : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>⭐ <c>BP-515</c> DS-1 (DESIGN_Details_Panel_View_Switching.md §8) — how the shell lays out its offered views.</summary>
public enum DetailsLayout
{
    /// <summary>One tab per view; the selected tab is the chosen view.</summary>
    Tabs,
    /// <summary>Every view in its own collapsing header, one under another.</summary>
    Stacked,
}

/// <summary>
/// ⭐⭐⭐ <b><c>L2.1</c> — THE DETAILS SHELL: one window, N views, chosen by a predicate.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §4 *(<i>"⭐⭐⭐ grow this into
/// <c>DetailsWindow</c>"</i> — the measured verdict among three candidate shells)* · §2's
/// <c>classDiagram</c> *(<c>DetailsWindow o-- DetailsViewRegistry</c> · <c>o-- IDetailsContextSource</c>
/// · <c>*-- "0..1" IDetailsViewInstance</c>)* · §2b's four sequences · §6 <c>L2</c>.
///
/// <para>⭐⭐ <b>Was <c>AiDetailsWindow</c>.</b> 📌 §6 <c>L2.1</c> names the rename, and the old name is
/// now false: this is the shell for EVERY perspective, not an <i>"AI"</i> one. ⚠ The ImGui <b>id</b>
/// is unchanged *(<c>ai_details_&lt;suffix&gt;</c>)* — ⛔ it is persisted in saved layouts, and §5 is
/// explicit that a bare key rename <i>"silently resets layouts"</i>. ⇒ ⭐ the TYPE renames, the KEY does
/// not.</para>
///
/// <para>⭐⭐⭐ <b>What actually changed under the rename:</b> the window no longer decides WHAT to draw.
/// It asks a <see cref="DetailsViewRegistry"/> which views claim this frame's context, and a
/// <see cref="DetailsViewSelector"/> which of those to show. ⛔ 📌 <c>R-112</c> — <b>no
/// <c>AssetKind</c> switch</b>, which is the mistake §4 dissolves <c>RuntimeInspectorWindow</c> for.</para>
///
/// <para>⭐⭐ <b>It reads no store</b> — 📌 §2: <i>"only the workspace builds a context."</i> The context
/// arrives through <see cref="IDetailsContextSource"/>, which is also the ONLY thing that will
/// distinguish this window from <c>L4</c>'s float and pin *(<c>R-119</c>)*.</para>
///
/// <para>⭐⭐ <b>Both collaborators are CONSTRUCTOR arguments, not attach-later fields.</b> 📌 the
/// <c>2026-08-16</c> rule — <i>"a production caller that HAS a dependency must PASS it"</i>: the
/// registrar holds the registry, the store and the run-state source at the line where it builds this
/// window. ⛔ An <c>AttachShell(…)</c> setter would be a tenth silent default waiting to happen.</para>
///
/// <para>⚠ <b>The variables section is still hosted directly</b> *(<see cref="Variables"/>,
/// <see cref="ShowVariables"/>)* because the outline routes INTO it — ⭐ re-pointing that at the context
/// is <c>L3</c>'s job. ⛔ It is not a second draw path: the section is drawn only through the registry,
/// as <c>VariablesDetailsView</c>.</para>
/// </summary>
public sealed class DetailsWindow
    : ManagedWindow, IVariableDetailsHost, Variables.IVariableTableHost, Shell.IDetailsViewSource,
      Variables.IVariablePropertiesFormHost
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. ⛔ Local literal, not a <c>PanelIds</c> constant — adding a
    /// cross-host constant is a <c>PanelIds.cs</c> change, which the sweep's STOP-AND-REPORT list names
    /// explicitly; flagged in the final report rather than done unilaterally.</summary>
    internal const string Kind = Fdp.Diagnostics.Contracts.Panels.PanelIds.Details;

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // ⭐⭐⭐ S1 — THE "PROPERTIES…" FORM, AS AN INJECTED DELEGATE.
    // ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The host's custom Properties form, or <see langword="null"/> when this perspective has
    /// none.</b> 📌 <c>R-109</c>: Properties is a CUSTOM form, ⛔ not a StructEdit session, so the
    /// gesture binder cannot open one and the host that HAS one must.
    ///
    /// <para>⛔⛔ <b>A DELEGATE, and that is a reference-wall fact rather than a preference.</b>
    /// 📐 <c>VariablePropertiesModal</c> lives in <c>Hrot.Blueprints.Editor</c> and depends on
    /// <c>Hrot.Blueprints.Core.Compiler.Catalogs</c> — <b>ABOVE</b> this assembly *(§3's reference
    /// wall)*. ⇒ ⭐ the host supplies the form; this shell never learns what a blueprint type is.
    /// 📌 Exactly the shape <c>W4</c>'s <c>ResolveStagedField</c> and <c>L6.5</c>'s brain signal use,
    /// for the same reason.</para>
    ///
    /// <para>⚠ <b>Retired with <c>BlueprintDetailsWindow</c>:</b> that class implemented
    /// <see cref="Variables.IVariablePropertiesFormHost"/> directly and OWNED the modal. ⭐ The form
    /// itself did not move — only who holds the reference.</para>
    /// </summary>
    private Func<Variables.VariableRow, bool, bool>? _propertiesForm;

    /// <summary>
    /// ⭐⭐⭐ <b>Install this perspective's Properties form.</b> ⚠ Called even with <see langword="null"/>
    /// is <b>not</b> the contract — ⛔ a host with no form simply never calls this, and
    /// <see cref="HasPropertiesForm"/> is then honestly <c>false</c>.
    /// </summary>
    public void SetPropertiesForm(Func<Variables.VariableRow, bool, bool> form)
        => _propertiesForm = form ?? throw new ArgumentNullException(nameof(form));

    /// <summary>
    /// ⭐⭐⭐ <b>Does this shell actually have a form?</b> ⛔ Asked of the CONSTRUCTED object, which is the
    /// control the <c>2026-08-16</c> silent-default rule prescribes.
    ///
    /// <para>⚠⚠ <b>This replaces a TYPE test.</b> 📌 <c>TheDialogOpensOnEveryHostTests</c> used to ask
    /// <c>window is IVariablePropertiesFormHost</c>, which worked only while Blueprint had a
    /// <b>different window class</b>. ⭐ One shell for four perspectives makes the type test meaningless
    /// and this one strictly better: it reads what the composition root actually wired.</para>
    /// </summary>
    public bool HasPropertiesForm => _propertiesForm is not null;

    /// <inheritdoc/>
    /// <remarks>⭐ <c>false</c> when no form was installed — 📌 the interface's own contract: <i>"no host
    /// ⇒ the gesture opens nothing, and that is honest"</i>. ⛔ Never a dialog that does nothing.</remarks>
    public bool OpenVariableProperties(Variables.VariableRow row, bool editable)
        => _propertiesForm is { } form && form(row, editable);

    /// <summary>
    /// ⭐⭐ <b>Batch 100 (<c>100f</c>) — the row gestures this surface offers.</b>
    /// ⭐ An AUTHORING surface — the Details panel is where a designer edits a declaration.
    /// <para>⛔ Answered explicitly because <c>IVariableTableHost.Gestures</c> has
    /// <b>no default body</b> — 📌 <c>U-5</c>/<c>BP-230</c>: <i>"a default body is the
    /// interface volunteering to lie on an implementer's behalf."</i></para>
    /// </summary>
    public Hrot.Editor.AiShared.Variables.VariableTableGestures Gestures => Hrot.Editor.AiShared.Variables.VariableTableGestures.Default;

    private readonly VariableDetailsSection _variables;
    private readonly string                 _drawId;
    private readonly DetailsViewRegistry    _views;
    private readonly IDetailsContextSource  _context;
    private readonly DetailsViewSelector    _selector = new();

    /// <summary>⭐ <c>BP-515</c> DS-4 — the live instance of each DRAWN view: one in tabs, one per open header when stacked.</summary>
    private readonly Dictionary<string, IDetailsViewInstance> _instances = new(StringComparer.Ordinal);
    private string?               _instanceId;

    // ⭐ BP-515 DS-6 — a request for a view, pending until the view is offered; then the view to REVEAL on the next draw.
    private string? _requestedViewId;
    private int     _requestFramesLeft;
    private string? _revealViewId;
    // ⭐ BP-515 DS-2 — the tab ImGui last showed, so a choice changed from outside is pushed to the tab bar.
    private string? _shownTabId;

    /// <param name="id">Unique ImGui window id (e.g. <c>"ai_details_btree"</c>). ⚠ Persisted — see the
    /// class remarks on the rename.</param>
    /// <param name="owningPerspective">Perspective key — <c>"BTree"</c> or <c>"HSM"</c>.</param>
    /// <param name="formatter">
    /// ⭐ <b>The one value formatter</b>, shared with the standalone table and the Watch. ⛔ Building a
    /// second one here would be a second place to fix a rendering rule — 📌 <c>C8</c>/<c>BP-01</c>.
    /// </param>
    /// <param name="views">
    /// ⭐⭐ <b>This perspective's view catalogue</b> — §2's <c>DetailsWindow o-- DetailsViewRegistry</c>.
    /// ⚠ The SAME instance this window contributes its own descriptor to *(<c>L1.2</c>'s claim chain)*,
    /// so registration order does not matter: the registry is read at DRAW time, not here.
    /// </param>
    /// <param name="context">
    /// ⭐⭐ <b>Where this frame's context comes from</b> — <c>Live</c> for a docked shell.
    /// 📌 <c>R-119</c>: this argument is the whole difference between docked, float and pin.
    /// </param>
    /// <param name="columns">
    /// Defaults to <see cref="VariableTableColumns.Details"/>, the same set Blueprint's Details uses.
    /// </param>
    public DetailsWindow(
        string id,
        string owningPerspective,
        VariableValueFormatter formatter,
        DetailsViewRegistry views,
        IDetailsContextSource context,
        VariableTableColumns? columns = null)
        : base(id, "Details", owningPerspective, WindowScope.PerspectiveBound)
    {
        if (formatter is null) throw new ArgumentNullException(nameof(formatter));

        _views     = views   ?? throw new ArgumentNullException(nameof(views));
        _context   = context ?? throw new ArgumentNullException(nameof(context));
        _variables = new VariableDetailsSection(formatter, columns);
        _drawId    = $"{id}_variables";
        IsOpen     = false;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>
    /// ⭐ The hosted list. Exposed so a rail can assert on the CONSTRUCTED object rather than on
    /// whatever wired it — 📌 <c>R-67</c>.
    /// </summary>
    public VariableDetailsSection Variables => _variables;

    /// <summary>
    /// ⭐⭐⭐ <b><c>L1.3</c>/<c>L1.2</c> — this window CONTRIBUTES the variables view to its
    /// perspective's catalogue.</b> 📄 §6 <c>L1.3</c>
    /// *("<c>VariableDetailsSection</c> becomes the first descriptor")*.
    ///
    /// <para>⭐⭐ <b>The window declares it; the registrar's claim chain collects it</b> — 📌 §6
    /// <c>L1.2</c>: <i>"registration through the existing claim chain, ⛔ no new root argument"</i>
    /// *(<c>R-67</c>)*. ⇒ ⛔ <c>EditorSubsystem</c> gains nothing to forget.</para>
    ///
    /// <para>⚠ Read ONCE at registration, so this is a fresh descriptor over the SAME section — ⭐ the
    /// section stays the one the registrar wires with run-state, gestures and the live projection.</para>
    /// </summary>
    public IEnumerable<Shell.DetailsViewDescriptor> DetailsViews
    {
        get { yield return Shell.VariablesDetailsViewDescriptor.For(_variables); }
    }

    /// <summary>⭐ What the panel is currently showing, or null. A rail surface.</summary>
    public string? Heading => _variables.Heading;

    /// <summary>⭐ True when a list is shown; false means the empty arm draws instead.</summary>
    public bool ShowingVariables => _variables.HasContent;

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ Forwarded from the hosted section, so the registrar's ONE attach loop reaches this table
    /// without knowing that a Details WINDOW hosts a Details SECTION — the same forwarding
    /// <c>BlueprintDetailsWindow</c> does. ⛔ Not a second table.
    /// </remarks>
    VariableTableControl? Variables.IVariableTableHost.VariableTable
        => ((Variables.IVariableTableHost)_variables).VariableTable;

    /// <inheritdoc/>
    /// <remarks>⭐ <c>W4</c> — forwarded for the same reason and by the same route as the table above.
    /// ⛔ Not a second model.</remarks>
    Variables.VariableTableModel? Variables.IVariableTableHost.TableModel
        => ((Variables.IVariableTableHost)_variables).TableModel;

    /// <inheritdoc/>
    /// <remarks>⭐ Row 58 — forwarded to the hosted list, which is what renders the Value column.</remarks>
    public void SetRunStateSource(Func<VariableRunState> runState)
        => _variables.SetRunStateSource(runState);

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ 📌 <c>Q32</c> ruling 2 — <i>"selection routes"</i>. ⛔ A selection with no rows CLEARS the
    /// list rather than leaving a stale one beside an unrelated selection, exactly as on Blueprint.
    /// </remarks>
    public void ShowVariables(VariableOutlineSelection selection)
    {
        if (selection.HasRows) _variables.Show(selection);
        else                   _variables.Clear();
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // ⭐⭐⭐ L2.1 — THE FRAME. Everything the shell decides, as a VALUE.
    // ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>What this window would draw right now</b> — context, chosen view, and the grey line if
    /// there is nothing to show.
    ///
    /// <para>⭐⭐ <b>This exists so <c>L2</c> is railable at all.</b> 📌 §6: <i>"every task's rail asserts
    /// on a store or a returned MODEL — the draw is unrailed by construction"</i> *(<c>R-21</c>/
    /// <c>R-62</c>)*. ⇒ <see cref="DrawClientArea"/> is a thin renderer over this, so a rail and the
    /// pixels cannot disagree about WHICH view is showing.</para>
    /// </summary>
    /// <param name="Context">⭐ This frame's context, from the source. ⛔ Never null.</param>
    /// <param name="Choice">⭐ §2b's state plus the whole <c>Rank</c>-ordered offer set.</param>
    /// <param name="EmptyState">
    ///   ⭐ The grey line, or <see langword="null"/> when a view is showing. ⚠ Non-null <b>exactly</b>
    ///   when <c>Choice.State == EmptyOffer</c> — 📌 <c>R-117</c>, and the reason it is a STRING here
    ///   rather than a <c>TextDisabled</c> call three frames away.
    /// </param>
    public readonly record struct DetailsFrame(
        DetailsContext            Context,
        DetailsViewSelector.Choice Choice,
        string?                   EmptyState);

    /// <summary>
    /// ⭐⭐ Resolve this frame. ⚠ Builds the context ONCE and asks the selector ONCE — ⛔ a second
    /// <c>Current()</c> inside the draw could see a different selection mid-frame and put a toolbar
    /// above a view it does not belong to.
    /// </summary>
    public DetailsFrame Frame()
    {
        var ctx    = _context.Current();
        var choice = _selector.Resolve(_views, ctx);
        return new DetailsFrame(
            ctx,
            choice,
            choice.State == DetailsViewSelector.Mode.EmptyOffer ? DetailsEmptyState.For(ctx) : null);
    }

    /// <summary>
    /// ⭐⭐ <b><c>L2.2</c> — the designer picked a view from the toolbar.</b> Exposed so the gesture is
    /// railable without ImGui *(the button is a draw; the CONSEQUENCE is this)*.
    /// </summary>
    public void Pick(DetailsContext context, string viewId) => _selector.Pick(context, viewId);

    /// <summary>⭐ <c>L2.2</c> — forget this context's pick and go back to the <c>Rank</c> default.</summary>
    public void ClearPick(DetailsContext context) => _selector.ClearPick(context);

    /// <summary>
    /// ⭐ Which view is instantiated right now, or null before the first draw. ⚠ A rail surface for
    /// §2's <c>*-- "0..1" IDetailsViewInstance</c> — ⛔ the multiplicity is a claim about lifetime, and
    /// this is what makes it checkable.
    /// </summary>
    public string? InstantiatedViewId => _instanceId;

    /// <summary>
    /// ⭐⭐ The live instance for a descriptor, created on first need and REUSED while the choice holds.
    ///
    /// <para>⚠⚠ <b>A different view DISPOSES the old instance; an empty offer does NOT.</b> ⭐ An empty
    /// offer is usually transient *(a marquee, a click on blank canvas)*, and throwing the instance away
    /// would lose the designer's scroll position every time they deselect. ⛔ Switching views is a
    /// deliberate act and the old view has no business surviving it — 📌 <c>R-120</c>: a view owns no
    /// state anyone else can see.</para>
    /// </summary>
    private IDetailsViewInstance InstanceFor(DetailsViewDescriptor descriptor)
    {
        // ⭐ BP-515 DS-4 — one view drawn (tabs): every OTHER instance is disposed, as the single slot always did
        foreach (var id in _instances.Keys.Where(k => !string.Equals(k, descriptor.Id, StringComparison.Ordinal)).ToList())
        {
            _instances[id].Dispose();
            _instances.Remove(id);
        }
        _instanceId = descriptor.Id;
        return StackedInstanceFor(descriptor);
    }

    /// <summary>⭐ <c>BP-515</c> DS-4 — the instance of one of SEVERAL drawn views (stacked): created on first need, the others kept.</summary>
    private IDetailsViewInstance StackedInstanceFor(DetailsViewDescriptor descriptor)
    {
        if (_instances.TryGetValue(descriptor.Id, out var live)) return live;
        var created = descriptor.Create()
                      ?? throw new InvalidOperationException(
                          $"Details view '{descriptor.Id}' returned a null instance from its factory.");
        _instances[descriptor.Id] = created;
        return created;
    }

    /// <summary>⭐ <c>BP-515</c> DS-4 — disposes the instances of views no longer offered. ⚠ An EMPTY offer disposes nothing (as before).</summary>
    private void PruneInstances(IReadOnlyList<DetailsViewDescriptor> offered)
    {
        if (offered.Count == 0) return;
        foreach (var id in _instances.Keys.Where(k => !offered.Any(d => string.Equals(d.Id, k, StringComparison.Ordinal))).ToList())
        {
            _instances[id].Dispose();
            _instances.Remove(id);
            if (string.Equals(_instanceId, id, StringComparison.Ordinal)) _instanceId = null;
        }
    }

    /// <summary>⭐ <c>BP-515</c> — every view instantiated right now (a rail surface: one in tabs, one per open header when stacked).</summary>
    public IReadOnlyCollection<string> InstantiatedViewIds => _instances.Keys;

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // ⭐⭐⭐ BP-515 — THE LAYOUT AND THE REQUEST FOR A VIEW (DESIGN_Details_Panel_View_Switching.md §8)
    // ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ <c>BP-515</c> DS-1 — tabs (one per view) or stacked collapsing headers; tabs by default. Remembered for the session.</summary>
    public DetailsLayout Layout { get; set; } = DetailsLayout.Tabs;

    /// <summary>⭐ <c>BP-515</c> DS-6 — how many frames a request waits for its view to be offered (the selection lands a frame later).</summary>
    public const int RequestFrames = 120;

    /// <summary>
    /// ⭐ <c>BP-515</c> DS-6 — ask this shell to SHOW <paramref name="viewId"/>: pending until the view is offered (at most
    /// <see cref="RequestFrames"/>), then picked and revealed — its tab selected, or its header opened and scrolled into view.
    /// 🔒 User, 2026-10-22: <i>"'Properties…' … should open the 'Details' panel"</i> — the Editor's Properties handler asks for the
    /// Components view through this. ⚠ Not a <see cref="Pick"/>: the selector drops a pick for a view not offered yet.
    /// </summary>
    public void RequestView(string viewId)
    {
        _requestedViewId   = viewId ?? throw new ArgumentNullException(nameof(viewId));
        _requestFramesLeft = RequestFrames;
    }

    /// <summary>⭐ The view a request is still waiting for, or null (a rail surface).</summary>
    public string? PendingViewRequest => _requestedViewId;

    /// <summary>⭐ The view the next draw reveals, or null (a rail surface).</summary>
    public string? RevealViewId => _revealViewId;

    /// <summary>⭐ <c>BP-515</c> DS-6 — this frame, with a pending request applied once its view is offered (re-resolved after the pick).</summary>
    public DetailsFrame FrameForDraw()
    {
        var frame = Frame();
        if (_requestedViewId is not { } wanted) return frame;
        foreach (var d in frame.Choice.Offered)
        {
            if (!string.Equals(d.Id, wanted, StringComparison.Ordinal)) continue;
            Pick(frame.Context, wanted);
            _revealViewId    = wanted;
            _requestedViewId = null;
            return Frame();
        }
        if (--_requestFramesLeft <= 0) _requestedViewId = null;
        return frame;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>U-obs-5: BUILD · CAPTURE.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
    /// ⛔⛔ No ImGui — <see cref="Frame"/>, <see cref="ShowsViewSwitch"/> and <see cref="ShowsFloatAndPin"/>
    /// were already pure, published before any render call.
    /// </summary>
    private DetailsWindowPanelViewModel BuildAndPublish(in DetailsFrame frame)
    {
        var ctx = frame.Context;
        var vm = new DetailsWindowPanelViewModel(
            Id, Kind,
            frame.Choice.View?.Id,
            frame.Choice.Offered.Select(d => d.Id).ToList(),
            frame.EmptyState,
            ShowsViewSwitch(frame),
            ShowsFloatAndPin,
            ctx.Asset?.AssetId, ctx.Asset?.Name, ctx.Perspective,
            ctx.Mode.ToString(), ctx.Focus.ToString(),
            ctx.Selection.Count, ctx.Entities.Count,
            Layout.ToString());

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>
    /// ⭐⭐ <c>BP-484</c> — the id scope this shell hands its hosted views, so a reader can COMPOSE a
    /// view's address instead of guessing it. ⛔ Exposed rather than duplicated: the composition rule
    /// (<c>idScope + "/" + ViewId</c>) has one owner.
    /// </summary>
    public string VariablesIdScope => _drawId;

    /// <summary>
    /// ⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.
    ///
    /// <para>⚠⚠ <b><c>BP-484</c> — it now drives the CHOSEN VIEW's publish too, and that was a real
    /// gap.</b> 📐 Before this it published only the SHELL's model, so a headless reader saw which view
    /// was chosen and never what that view SHOWS — ⛔ the hosted view's own <c>BuildAndPublish</c> runs
    /// inside <see cref="IDetailsViewInstance.Draw"/>, which only <see cref="DrawClientArea"/> called.
    /// ⇒ the hook claimed to be "the BUILD + CAPTURE portion" while capturing half of it.</para>
    ///
    /// <para>⭐ Safe headless: every view in this family publishes BEFORE its own ImGui guard, which is
    /// the AS-BUILT deviation ① the whole contract rests on.</para>
    /// </summary>
    internal DetailsWindowPanelViewModel SimulateDrawClientArea()
    {
        var frame = FrameForDraw();
        var vm    = BuildAndPublish(frame);

        // ⛔ An empty shell hosts no view — there is nothing further to publish, and inventing an entry
        //   would claim a panel the user cannot see.
        if (frame.EmptyState == null && frame.Choice.View != null)
        {
            _revealViewId = null;   // ⭐ BP-515 — what a draw consumes, the headless draw consumes too
            InstanceFor(frame.Choice.View).Draw(frame.Context, _drawId);
        }

        return vm;
    }

    /// <remarks>
    /// ⭐⭐⭐ <b>A thin renderer over <see cref="Frame"/>.</b> ⛔ It decides nothing — every branch below
    /// is a rendering of a value a rail can build itself.
    /// </remarks>
    protected override void DrawClientArea()
    {
        var frame = FrameForDraw();
        BuildAndPublish(frame);

        if (frame.EmptyState != null)
        {
            // ⭐ L2.3 — 📌 R-117: "a blank panel is a defect". This replaces the old
            //   "No variable selected.", which named ONE view's precondition as if it were the
            //   panel's only reason to be empty.
            ImGuiNET.ImGui.TextDisabled(frame.EmptyState);
            return;
        }

        // ⭐⭐ BP-515 — the switch is a TAB BAR or STACKED HEADERS (DS-1…DS-5); ⛔ the radio buttons are gone
        if (Layout == DetailsLayout.Stacked) DrawStacked(frame);
        else                                 DrawTabs(frame);
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // ⭐⭐⭐ L4.2 / L4.3 / L4.4 — FLOAT AND PIN. The shell is where both gestures start.
    // ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>L4.2</c> — the CONTEXTUAL FLOAT id, and it is STABLE.</b>
    /// 📄 §6 <c>L4.2</c>: <i>"contextual float — live context, <c>IsVolatile = false</c>, stable
    /// id."</i>
    /// <para>⚠ <b>Stable means it does NOT include the selection</b> — ⛔ an id that moved with the
    /// selection could never be restored from a saved layout, which is the whole point of
    /// <c>IsVolatile = false</c>.</para>
    /// </summary>
    public static string FloatIdFor(string perspective, string viewId)
        => $"details_float_{perspective}_{viewId}".ToLowerInvariant();

    /// <summary>
    /// ⭐⭐⭐ <b><c>L4.3</c> — the PIN id: §2b's <c>viewId + assetId + selectionKey</c>.</b>
    /// ⭐ The selection key is <see cref="DetailsViewSelector.KeyOf"/> — 📌 <b>the same key the toolbar
    /// remembers picks by</b>, ⛔ not a second key-builder (ruling 9). ⚠ It already carries the
    /// perspective and the asset id, so the id is exactly §2b's triple.
    /// </summary>
    public static string PinIdFor(DetailsContext context, string viewId)
        => $"details_pin_{viewId}_{DetailsViewSelector.KeyOf(context)}";

    /// <summary>
    /// ⭐⭐⭐ <b><c>L4.2</c> — open the CURRENT view in its own window</b>, following the live context.
    /// 📄 §2b's float sequence.
    ///
    /// <para>⭐ Returns the existing window if one is already open for this view — ⚠ the same
    /// <c>TryGetWindow</c>-then-focus shape §2b gives the pin, because a second identical float is
    /// never what the designer meant.</para>
    ///
    /// <para>⛔ Returns <see langword="null"/> when nothing is showing — ⚠ there is no view to float,
    /// and 📌 <c>R-117</c>'s grey line is already the shell's answer.</para>
    /// </summary>
    /// <summary>
    /// ⭐⭐ <b><c>VC-1</c> — the descriptor this shell is showing right now, or <see langword="null"/>.</b>
    /// ⭐ Extracted so the View-menu items can be greyed with a REASON rather than acting and failing
    /// silently — 📌 the user's <c>2026-08-17</c> tooltip ruling. ⛔ It is the SAME question
    /// <see cref="OpenFloat"/> asks, so there is one answer, not two *(<c>R-13</c>)*.
    /// </summary>
    public DetailsViewDescriptor? CurrentDescriptor() => Frame().Choice.View;

    /// <summary>
    /// ⭐⭐⭐ <b><c>VC-1</c> — DOES THE TOOLBAR DRAW THE VIEW SWITCH?</b> ⭐ More than one view applies.
    /// ⚠ With a single offer the row would be one permanently-pressed button that does nothing, and the
    /// view's own heading already names it. ⛔ Nothing is lost — the SELECTOR still runs, so the moment
    /// a second view claims the context the switch appears.
    /// </summary>
    public bool ShowsViewSwitch(in DetailsFrame frame) => frame.Choice.Offered.Count >= 2;

    /// <summary>
    /// ⭐⭐⭐ <b><c>VC-1</c> — DOES THE TOOLBAR DRAW FLOAT AND PIN?</b> A view is showing and this shell
    /// has a manager to register the new window with.
    ///
    /// <para>⛔⛔ <b>These two questions used to be ONE.</b> 📐 <c>DrawToolbar</c> opened with
    /// <c>if (offered.Count &lt; 2) return;</c> — written for the SWITCH — and the float/pin block sat
    /// below it, so a context offering exactly one view had neither affordance. ⚠ That is the user's
    /// <c>VC-1</c> finding on any single-view context, and §6 <c>L4.4</c>'s reason for entry points
    /// *("so a float is reachable")* says nothing about how many views apply.</para>
    ///
    /// <para>⭐⭐ <b>Why a PROPERTY and not just fixed control flow:</b> 📌 <c>R-21</c>/<c>R-62</c> — the
    /// draw itself cannot be railed, so a fix living only in the draw is a fix a probe cannot redden
    /// *(<c>BP-402</c> ①: my first attempt at this rail was exactly that, and it stayed green through
    /// the reverted bug)*. ⭐ Naming the decision moves it into the MODEL, where re-fusing it with
    /// <see cref="ShowsViewSwitch"/> turns red.</para>
    ///
    /// <para>⚠ <b>Stated limit:</b> this proves the DECISION, ⛔ not that a button appears on screen —
    /// that half stays with the visual check.</para>
    /// </summary>
    public bool ShowsFloatAndPin => _windowManager is not null && CurrentDescriptor() is not null;

    public DetailsViewWindow? OpenFloat(WindowManager windowManager)
    {
        ArgumentNullException.ThrowIfNull(windowManager);
        return Frame().Choice.View is { } descriptor ? OpenFloat(windowManager, descriptor) : null;
    }

    /// <summary>⭐ <c>BP-515</c> DS-5 — float THIS view (a stacked header's own button), not necessarily the chosen one.</summary>
    public DetailsViewWindow? OpenFloat(WindowManager windowManager, DetailsViewDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(windowManager);
        ArgumentNullException.ThrowIfNull(descriptor);

        var id = FloatIdFor(OwningPerspective, descriptor.Id);
        if (windowManager.TryGetWindow(id, out var existing))
        {
            windowManager.FocusWindow(id);
            return existing as DetailsViewWindow;
        }

        var window = new DetailsViewWindow(
            id:                id,
            title:             descriptor.Title,
            owningPerspective: OwningPerspective,
            descriptor:        descriptor,
            // ⭐⭐ LIVE — 📌 R-119: the source is the only thing that makes this a float and not a pin.
            context:           _context,
            isVolatile:        false);

        windowManager.RegisterWindow(window);
        return window;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>L4.3</c> — PIN the current view at the CURRENT context.</b> 📄 §2b's pin sequence ·
    /// 📌 <c>R-100</c>: <i>"a pin is one titled, volatile instance; a duplicate FOCUSES."</i>
    ///
    /// <para>⭐⭐ <b>The snapshot is taken HERE, once</b> — <c>D-&gt;&gt;D: snapshot = current ctx</c> —
    /// and handed to a <see cref="FrozenContextSource"/>. ⛔ The window itself has no idea it is
    /// pinned.</para>
    ///
    /// <para>⭐ <b>Pinning the same view at the same context FOCUSES the existing pin</b> rather than
    /// stacking a second identical window — ⚠ which is what the id's selection key is FOR.</para>
    /// </summary>
    public DetailsViewWindow? Pin(WindowManager windowManager)
    {
        ArgumentNullException.ThrowIfNull(windowManager);
        return Frame().Choice.View is { } descriptor ? Pin(windowManager, descriptor) : null;
    }

    /// <summary>⭐ <c>BP-515</c> DS-5 — pin THIS view at the current context (a stacked header's own button).</summary>
    public DetailsViewWindow? Pin(WindowManager windowManager, DetailsViewDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(windowManager);
        ArgumentNullException.ThrowIfNull(descriptor);

        var frame = Frame();
        var id = PinIdFor(frame.Context, descriptor.Id);
        if (windowManager.TryGetWindow(id, out var existing))
        {
            windowManager.FocusWindow(id);
            return existing as DetailsViewWindow;
        }

        var window = new DetailsViewWindow(
            id:                id,
            // ⚠ TITLED, so two pins of one view are told apart — 📌 R-100's "one titled instance".
            title:             $"{descriptor.Title} (pinned)",
            owningPerspective: OwningPerspective,
            descriptor:        descriptor,
            // ⭐⭐ FROZEN at this moment. That is the entire difference from OpenFloat above.
            context:           new FrozenContextSource(frame.Context),
            isVolatile:        true);

        windowManager.RegisterWindow(window);
        return window;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-515</c> DS-2 — the TABBED layout.</b> 📌 <c>R-98</c>: <i>"the toolbar is a panel switch"</i> — still: the
    /// selected tab IS the selector's choice, and a click on a tab is <see cref="Pick"/>. One tab per offered view, in <c>Rank</c>
    /// order, drawn even for one view: the bar carries the layout, float and pin buttons (DS-5, trailing).
    ///
    /// <para>⚠ ImGui keeps its own selected tab. ⇒ when the CHOICE changed from outside (a <c>Rank</c> fallback, a request) the
    /// chosen tab is pushed with <c>SetSelected</c>; a frame where ImGui shows a different tab WITHOUT a push is the designer's
    /// click.</para>
    /// <para>⛔ The radio buttons' "click the active one to clear the pick" is gone — a tab does not toggle; the <c>Rank</c> default
    /// still returns when the pick stops applying (unchanged).</para>
    /// </summary>
    private void DrawTabs(in DetailsFrame frame)
    {
        var chosen = frame.Choice.View!;
        bool push = _revealViewId != null || !string.Equals(_shownTabId, chosen.Id, StringComparison.Ordinal);
        _revealViewId = null;

        DetailsViewDescriptor? shown = null;
        float barLeft = ImGuiNET.ImGui.GetCursorScreenPos().X, barTop = ImGuiNET.ImGui.GetCursorScreenPos().Y;
        float barWidth = ImGuiNET.ImGui.GetContentRegionAvail().X;
        if (ImGuiNET.ImGui.BeginTabBar($"##{Id}_tabs", ImGuiNET.ImGuiTabBarFlags.FittingPolicyScroll))
        {
            foreach (var d in frame.Choice.Offered)
            {
                var flags = push && ReferenceEquals(d, chosen) ? ImGuiNET.ImGuiTabItemFlags.SetSelected : ImGuiNET.ImGuiTabItemFlags.None;
                if (BeginTab($"{d.Title}##{Id}_tab_{d.Id}", flags))
                {
                    shown = d;
                    ImGuiNET.ImGui.EndTabItem();
                }
            }
            ImGuiNET.ImGui.EndTabBar();
        }

        // ⭐ DS-5 — the buttons RIGHT-ALIGNED on the tab bar's row (ImGui's "trailing" tab buttons would sit right after the tabs):
        //   the layout switch, then float and pin of the shown tab's view. ⚠ Many wide tabs can run under them — four views fit.
        var target = shown ?? chosen;
        var after = ImGuiNET.ImGui.GetCursorScreenPos();
        var labels = new System.Collections.Generic.List<string> { Fdp.Presentation.Fonts.IconsFontAwesome6.LayerGroup };
        bool floatPin = ShowsFloatAndPin && _windowManager is not null;
        if (floatPin) { labels.Add(Fdp.Presentation.Fonts.IconsFontAwesome6.UpRightFromSquare); labels.Add(Fdp.Presentation.Fonts.IconsFontAwesome6.Thumbtack); }
        float pad = ImGuiNET.ImGui.GetStyle().FramePadding.X * 2f, spacing = ImGuiNET.ImGui.GetStyle().ItemSpacing.X;
        float width = labels.Sum(l => ImGuiNET.ImGui.CalcTextSize(l).X + pad) + spacing * (labels.Count - 1);
        ImGuiNET.ImGui.SetCursorScreenPos(new System.Numerics.Vector2(barLeft + barWidth - width, barTop));
        if (ImGuiNET.ImGui.SmallButton($"{Fdp.Presentation.Fonts.IconsFontAwesome6.LayerGroup}##{Id}_layout")) Layout = DetailsLayout.Stacked;
        Tooltip("Show the views stacked, each in its own header");
        if (floatPin && _windowManager is { } wm)
        {
            ImGuiNET.ImGui.SameLine();
            if (ImGuiNET.ImGui.SmallButton($"{Fdp.Presentation.Fonts.IconsFontAwesome6.UpRightFromSquare}##{Id}_float")) OpenFloat(wm, target);
            Tooltip($"Float {target.Title} in its own window (follows the selection)");
            ImGuiNET.ImGui.SameLine();
            if (ImGuiNET.ImGui.SmallButton($"{Fdp.Presentation.Fonts.IconsFontAwesome6.Thumbtack}##{Id}_pin")) Pin(wm, target);
            Tooltip($"Pin {target.Title} at this selection");
        }
        ImGuiNET.ImGui.SetCursorScreenPos(after);

        var draw = target;
        if (!push && !ReferenceEquals(draw, chosen)) Pick(frame.Context, draw.Id);   // ⭐ the designer clicked a tab
        _shownTabId = draw.Id;
        InstanceFor(draw).Draw(frame.Context, _drawId);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-515</c> DS-3 — the STACKED layout:</b> every offered view in its own collapsing header, <c>Rank</c> order. On
    /// first sight only the chosen view's header is open (ImGui keeps each header's state after); OPENING a header is
    /// <see cref="Pick"/>; a requested view's header is opened and scrolled into view (DS-6). Float and pin are right-aligned on
    /// EACH header and act on THAT view (DS-5, allow-overlap).
    /// </summary>
    private void DrawStacked(in DetailsFrame frame)
    {
        var reveal = _revealViewId;
        _revealViewId = null;
        PruneInstances(frame.Choice.Offered);
        float right = ImGuiNET.ImGui.GetCursorPosX() + ImGuiNET.ImGui.GetContentRegionAvail().X;   // the content's right edge (window-local)

        // the layout switch, right-aligned above the headers
        float glyph = ImGuiNET.ImGui.CalcTextSize(Fdp.Presentation.Fonts.IconsFontAwesome6.TableColumns).X
                      + ImGuiNET.ImGui.GetStyle().FramePadding.X * 2f;
        ImGuiNET.ImGui.SetCursorPosX(MathF.Max(ImGuiNET.ImGui.GetCursorPosX(), right - glyph));
        if (ImGuiNET.ImGui.SmallButton($"{Fdp.Presentation.Fonts.IconsFontAwesome6.TableColumns}##{Id}_layout")) Layout = DetailsLayout.Tabs;
        Tooltip("Show the views as tabs");

        bool buttons = ShowsFloatAndPin && _windowManager is not null;
        float spacing = ImGuiNET.ImGui.GetStyle().ItemSpacing.X;
        foreach (var d in frame.Choice.Offered)
        {
            if (string.Equals(reveal, d.Id, StringComparison.Ordinal)) ImGuiNET.ImGui.SetNextItemOpen(true, ImGuiNET.ImGuiCond.Always);
            else ImGuiNET.ImGui.SetNextItemOpen(ReferenceEquals(d, frame.Choice.View), ImGuiNET.ImGuiCond.Once);
            ImGuiNET.ImGui.SetNextItemAllowOverlap();
            bool open = ImGuiNET.ImGui.CollapsingHeader($"{d.Title}##{Id}_hdr_{d.Id}", ImGuiNET.ImGuiTreeNodeFlags.AllowOverlap);
            bool opened = open && ImGuiNET.ImGui.IsItemToggledOpen();
            if (string.Equals(reveal, d.Id, StringComparison.Ordinal)) ImGuiNET.ImGui.SetScrollHereY(0f);
            if (buttons && _windowManager is { } wm)
            {
                float w = ImGuiNET.ImGui.CalcTextSize(Fdp.Presentation.Fonts.IconsFontAwesome6.Thumbtack).X
                          + ImGuiNET.ImGui.CalcTextSize(Fdp.Presentation.Fonts.IconsFontAwesome6.UpRightFromSquare).X
                          + ImGuiNET.ImGui.GetStyle().FramePadding.X * 4f + spacing;
                ImGuiNET.ImGui.SameLine(right - w);
                if (ImGuiNET.ImGui.SmallButton($"{Fdp.Presentation.Fonts.IconsFontAwesome6.UpRightFromSquare}##{Id}_float_{d.Id}")) OpenFloat(wm, d);
                Tooltip($"Float {d.Title} in its own window (follows the selection)");
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.SmallButton($"{Fdp.Presentation.Fonts.IconsFontAwesome6.Thumbtack}##{Id}_pin_{d.Id}")) Pin(wm, d);
                Tooltip($"Pin {d.Title} at this selection");
            }
            if (opened) Pick(frame.Context, d.Id);
            if (!open) continue;
            ImGuiNET.ImGui.PushID(d.Id);
            StackedInstanceFor(d).Draw(frame.Context, _drawId);
            ImGuiNET.ImGui.PopID();
        }
    }

    /// <summary>A tab with flags and NO close button — ImGui.NET's managed overload with flags takes a <c>p_open</c>, which would add one.</summary>
    private static unsafe bool BeginTab(string label, ImGuiNET.ImGuiTabItemFlags flags)
    {
        int count = System.Text.Encoding.UTF8.GetByteCount(label);
        byte* utf8 = stackalloc byte[count + 1];
        fixed (char* chars = label) System.Text.Encoding.UTF8.GetBytes(chars, label.Length, utf8, count);
        utf8[count] = 0;
        return ImGuiNET.ImGuiNative.igBeginTabItem(utf8, null, flags) != 0;
    }

    /// <summary>⭐ <c>BP-515</c> — the real draw, for a frame rail inside a live ImGui frame (the window manager is not needed).</summary>
    internal void DrawClientAreaForRail() => DrawClientArea();

    private static void Tooltip(string text)
    {
        if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip(text);
    }

    /// <summary>
    /// ⭐⭐ <b><c>L4.4</c> — the window manager the float/pin affordances act on.</b>
    /// ⭐ Set by <see cref="OnRegistered"/>, so <b>every</b> registration path supplies it.
    /// ⛔ Still nullable: a hand-built <c>DetailsWindow</c> in a rail is never registered, and the
    /// gestures themselves take the manager as an argument and are railed without this.
    /// </summary>
    private WindowManager? _windowManager;

    /// <summary>
    /// ⭐⭐⭐ <b><c>VC-1</c> — the shell SELF-WIRES on registration.</b>
    ///
    /// <para>🔴 <b>The defect this replaces, measured <c>2026-08-22</c>:</b> the manager arrived through
    /// an explicit <c>AttachWindowManager</c> call with <b>exactly one caller</b>
    /// *(<c>PerspectiveWorkspaceRegistrar</c>)*. ⛔ The Scenario Details host is built at the composition
    /// root instead, so it never got one ⇒ <b>its float/pin buttons could not draw at all</b> — the
    /// user's <c>VC-1</c> finding. ⚠ That is the <c>2026-08-16</c> silent-default shape again *(the
    /// caller HELD the manager and did not pass it)*, and it was MY omission in <c>L6.1c</c>.</para>
    ///
    /// <para>⭐⭐ <b>Registration is the one event both roots already perform</b>, so hanging the wiring
    /// off it makes forgetting unrepresentable — 📌 <c>R-126</c>'s PULL argument, one floor up.</para>
    ///
    /// <para>⭐ <c>CE-1150</c> (canvas-context-menu-design §12 HA-7) — ⛔ SUPERSEDED: it also registered the View ▸ Details menu items
    /// here (<c>BP-403</c>). 🔒 User, 2026-10-22: <i>"Windows themselves should not answer, they should have no idea there is any
    /// 'Properties...' menu or some global ui actions"</i> ⇒ the items are actions registered by <c>EntityDetailsBundle</c>, which acts on
    /// this shell's public <see cref="OpenFloat(WindowManager)"/> / <see cref="Pin(WindowManager)"/>.</para>
    /// </summary>
    public override void OnRegistered(WindowManager manager)
        => _windowManager = manager ?? throw new ArgumentNullException(nameof(manager));
}
