using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Windows;

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
    : ManagedWindow, IVariableDetailsHost, Variables.IVariableTableHost, Shell.IDetailsViewSource
{
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

    private IDetailsViewInstance? _instance;
    private string?               _instanceId;

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
        if (_instance != null && string.Equals(_instanceId, descriptor.Id, StringComparison.Ordinal))
            return _instance;

        _instance?.Dispose();
        _instance   = descriptor.Create()
                      ?? throw new InvalidOperationException(
                          $"Details view '{descriptor.Id}' returned a null instance from its factory.");
        _instanceId = descriptor.Id;
        return _instance;
    }

    /// <remarks>
    /// ⭐⭐⭐ <b>A thin renderer over <see cref="Frame"/>.</b> ⛔ It decides nothing — every branch below
    /// is a rendering of a value a rail can build itself.
    /// </remarks>
    protected override void DrawClientArea()
    {
        var frame = Frame();

        if (frame.EmptyState != null)
        {
            // ⭐ L2.3 — 📌 R-117: "a blank panel is a defect". This replaces the old
            //   "No variable selected.", which named ONE view's precondition as if it were the
            //   panel's only reason to be empty.
            ImGuiNET.ImGui.TextDisabled(frame.EmptyState);
            return;
        }

        DrawToolbar(frame);
        InstanceFor(frame.Choice.View!).Draw(frame.Context, _drawId);
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
    public DetailsViewWindow? OpenFloat(WindowManager windowManager)
    {
        ArgumentNullException.ThrowIfNull(windowManager);

        var frame = Frame();
        if (frame.Choice.View is not { } descriptor) return null;

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

        var frame = Frame();
        if (frame.Choice.View is not { } descriptor) return null;

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
    /// ⭐⭐ <b><c>L2.2</c> — the toolbar.</b> 📌 <c>R-98</c>: <i>"the toolbar is a panel switch."</i>
    ///
    /// <para>⚠ <b>Drawn only when more than one view applies</b> — ⭐ a judgement call, stated: with a
    /// single offer the row would be one permanently-pressed button that does nothing, and the view's
    /// own heading already names it. ⛔ Nothing is lost — the SELECTOR still runs, so the moment a
    /// second view claims the context the switch appears.</para>
    /// </summary>
    private void DrawToolbar(in DetailsFrame frame)
    {
        var offered = frame.Choice.Offered;
        if (offered.Count < 2) return;

        for (int i = 0; i < offered.Count; i++)
        {
            var d = offered[i];
            if (i > 0) ImGuiNET.ImGui.SameLine();

            bool active = ReferenceEquals(d, frame.Choice.View);
            // ⭐ A radio-shaped toggle: clicking the ACTIVE one clears the pick, so the designer can
            //   get back to the Rank default without hunting for a "reset" — ⛔ and without a second
            //   affordance to explain.
            if (ImGuiNET.ImGui.RadioButton($"{d.Title}##{Id}_tab_{d.Id}", active))
            {
                if (active) ClearPick(frame.Context);
                else        Pick(frame.Context, d.Id);
            }
        }

        // ⭐⭐ L4.4 — the float/pin affordances live on the toolbar, at the right edge.
        //   ⚠ A DRAW, and unrailed by construction (R-21/R-62) — everything it DOES is OpenFloat/Pin,
        //     which are railed directly.
        if (_windowManager != null)
        {
            ImGuiNET.ImGui.SameLine();
            if (ImGuiNET.ImGui.SmallButton($"float##{Id}_float")) OpenFloat(_windowManager);
            ImGuiNET.ImGui.SameLine();
            if (ImGuiNET.ImGui.SmallButton($"pin##{Id}_pin"))     Pin(_windowManager);
        }

        ImGuiNET.ImGui.Separator();
    }

    /// <summary>
    /// ⭐⭐ <b><c>L4.4</c> — the window manager the float/pin affordances act on.</b>
    /// ⚠ Set by the registrar when it registers this window; ⛔ null in the standalone constructions
    /// that predate <c>L4</c>, in which case the toolbar simply offers no float button — ⭐ the
    /// gestures themselves take the manager as an argument and are railed without this.
    /// </summary>
    private WindowManager? _windowManager;

    /// <summary>⭐ Called by the registrar at registration — ⛔ not a service the root must remember.</summary>
    public void AttachWindowManager(WindowManager windowManager)
        => _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
}
