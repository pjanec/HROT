using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>
/// Stateful gizmo for collecting a sequence of 2D world points (a path, an area outline). Replaces the deleted <c>PointSequenceTool</c>.
///
/// <para>⭐⭐ B-17 P2 (📄 docs/UX/DESIGN_Map_Interaction.md §2.5 — D11): the convention lives in ONE place, <see cref="ShapeEditSession"/>; this class keeps
/// only the drawing and its own result (the point array).</para>
/// <list type="number">
///   <item>Left-click (release) appends a world point — or, on a CLOSED shape, a click on the first point finishes it; a click on the on-map ✓ finishes.</item>
///   <item><b>Enter</b>, the ✓ or the menu's <i>Finish</i> call <c>onFinish</c> with the points, then <c>onRemove</c> — ⭐ with too few points they do NOT: the
///         reason is shown and the tool stays armed.</item>
///   <item><b>Esc</b> or the menu's <i>Cancel</i> calls <c>onCancelled</c> (if any) and <c>onRemove</c>; <b>Backspace</b> removes the last point.</item>
///   <item><b>Right-click</b> is the MENU (<see cref="ToolMenuJson"/>) — it finishes and cancels NOTHING.</item>
/// </list>
/// </summary>
public sealed class PointSequenceGizmo : IEntityStatefulGizmo
{
    private readonly Action<Vector2[]> _onFinish;
    private readonly Action            _onRemove;
    private readonly Action?           _onCancelled;
    private Vector3                    _currentPos;
    private Fdp.Toolkit.World.IWorldQuery? _world;

    /// <summary>
    /// ⭐ CE-1033 S3 (docs/DESIGN_Map_3D_Mode.md M22 step 1) — the height of the level the sequence is drawn on: the FIRST point's
    /// surface height when it was picked on a roof or an upper floor (a 3-D click, <see cref="Fdp.Toolkit.World.Levels"/> ≥ 1),
    /// else 0 = on the ground, which is DRAPED (M18) so a ground area follows relief instead of floating at one height (R-248).
    /// A 2-D click has no height, so in 2-D this is always 0 — byte-identical to before.
    /// </summary>
    public float AreaHeight => Session.Heights.Count > 0 ? Session.Heights[0] : 0f;

    /// <summary>⭐ B-17 P2 — THE drawing core this gizmo is a view of (the points, the finish / cancel convention, the menu the host attached).</summary>
    public ShapeEditSession Session { get; }

    /// <summary>
    /// ⭐ <c>CE-4592</c> (P-5 2b) — drawn FIRST each frame, under the shape, with the points placed so far: what the shape will do to the world it is drawn on (a ground
    /// shape's tint of the height change). Null = nothing (every other tool). ⛔ It runs every frame — the callback must not allocate (R-2084).
    /// </summary>
    public Action<IDebugDrawBuilder, IReadOnlyList<Vector2>>? Underlay { get; set; }
    /// <summary>⭐ CE-5006 TE-2 (docs/DESIGN_Terrain_Editor_UX.md §5) — after a finish the tool stays armed for the next shape (a terrain line / area); Esc then drops the
    /// shape in progress first (<see cref="ShapeEditSession.EscDropsShapeFirst"/>). False = the D11 tool: a finish ends it.</summary>
    public bool Repeat { get => _repeat; set { _repeat = value; Session.EscDropsShapeFirst = value; } }
    private bool _repeat;
    /// <summary>⭐ TE-2 — called after each repeated finish (the host rolls a new seed for the next shape).</summary>
    public Action? OnRepeat { get; set; }

    /// <summary>⭐ CE-5009 TE-6 (R-5033) — the host's snap: a point and the reach (metres) in, what it snapped to out. Null = no snapping (every non-terrain-line
    /// tool). Held Ctrl frees the next point; held Shift steps the segment's direction by 15° from the previous point. ⛔ It runs on every cursor move — it must
    /// not allocate (R-2084).</summary>
    public Func<Vector2, float, SnapHit>? Snap { get; set; }
    /// <summary>The snap reach in metres (fixed: at the zooms a line is drawn at, 3 m is a few pixels to a few dozen).</summary>
    public const float SnapReach = 3f;
    /// <summary>What the cursor snapped to now (for the mark and the hint).</summary>
    public SnapHit LastSnap { get; private set; }
    /// <summary>The snap mark's colour (the map's accent blue).</summary>
    public static readonly Rgba32 SnapColour = new(0x42, 0x96, 0xFA, 255);
    private bool _snapSuspended, _angleStep;

    /// <summary>The point a cursor / click at <paramref name="raw"/> becomes: the 15° step (Shift) from the previous point, then the host's snap (unless Ctrl).</summary>
    public Vector2 SnapPoint(Vector2 raw)
    {
        var p = raw;
        var pts = Session.Points;
        if (_angleStep && pts.Count > 0)
        {
            var prev = pts[^1]; var d = p - prev; float len = d.Length();
            if (len > 1e-3f)
            {
                const float step = MathF.PI / 12f;   // 15°
                float a = MathF.Round(MathF.Atan2(d.Y, d.X) / step) * step;
                p = prev + new Vector2(MathF.Cos(a), MathF.Sin(a)) * len;
            }
        }
        LastSnap = Snap != null && !_snapSuspended ? Snap(p, SnapReach) : SnapHit.None;
        return LastSnap.Hit ? LastSnap.Point : p;
    }

    /// <inheritdoc/>
    // ⭐ CE-1969 — the menu (Finish · Remove last · Cancel · Stop drawing) only while there is something to finish: with no point placed yet (a fresh tool,
    //   or the one re-armed after a Finish) only the way out (⭐ CE-1977 — Stop drawing). 🔒 User, 2026-11-17: "I would expect the finish to appear only
    //   when I before clicked the points on the map, so there is something to finish actually".
    public string? ToolMenuJson => Session.Points.Count > 0 ? Session.Menu?.Json : Session.Menu?.EmptyJson;



    /// <inheritdoc/>
    public bool RequiresExclusiveFocus => true;

    /// <summary>
    /// ⭐⭐⭐ <b>TRUE, and it is not optional for an exclusive-focus gizmo.</b> 📄
    /// <c>docs/designs/gizmos-1/gizmo-input-focus-design.md</c> §5.1 · <c>§6.2c</c> · <c>CE-259k</c>.
    ///
    /// <para>🔴 <b>The defect this closes, measured <c>2026-09-09</c> by running the editor:</b> this gizmo
    /// implements its ENTIRE behaviour in <see cref="OnMouseEvent"/> / <see cref="OnKeyEvent"/> — the RAW
    /// handlers — while <c>OnInteractionStarted</c> / <c>OnCommit</c> are empty stubs. With
    /// <c>WantsRawInput</c> left at its <see langword="false"/> default the terminal is told the opposite,
    /// and the gizmo is DEAF ON BOTH PATHS: <c>DebugGizmoLayer.cs:126</c> withholds raw events, and
    /// <c>:428</c> suppresses spatial hit-testing for everything not anchored to the capture token because
    /// <c>RequiresExclusiveFocus</c> is set. ⇒ it DRAWS and ignores every click, and Escape too.</para>
    ///
    /// <para>🔒 <b>Design basis:</b> §5.1 defines <c>InputCaptureBinding</c> as <i>"a meta-primitive
    /// declaring that the bound token wants raw hardware events streamed to it"</i>, with ONE flag —
    /// <c>ConditionMask: 1 = Exclusive, 0 = Shared</c>. ⛔ There is no <c>wantsRawInput</c> bit in the
    /// design at all; emitting the binding WAS the request. The second bit is an as-built divergence, and
    /// reconciling it is <c>CE-259l</c> — until then every exclusive gizmo must say this explicitly.</para>
    /// </summary>
    public bool WantsRawInput => true;

    /// <inheritdoc/>
    public bool IsFocused { get; private set; }

    /// <inheritdoc/>
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    /// <param name="onFinish">
    /// Invoked with the collected point array when the operator finishes (Enter, the ✓, the menu) with at least <paramref name="minPoints"/> points.
    /// Called before <paramref name="onRemove"/>.
    /// </param>
    /// <param name="onRemove">
    /// Invoked when the gizmo wants to exit — after <paramref name="onFinish"/>, or on Esc / Cancel. Typically calls <c>GlobalGizmoManager.Unregister</c>.
    /// </param>
    /// <param name="minPoints">The fewest points a finish needs (an area 3, a route 2); fewer ⇒ the tool says why and stays armed.</param>
    /// <param name="closed">Whether the shape closes (clicking the first point finishes it).</param>
    /// <param name="noun">What is drawn, with its article, for the too-few reason ("an area").</param>
    /// <param name="onCancelled">Invoked on Esc / Cancel, before <paramref name="onRemove"/> (optional).</param>
    public PointSequenceGizmo(Action<Vector2[]> onFinish, Action onRemove, int minPoints = 1, bool closed = false, string noun = "a shape", Action? onCancelled = null)
    {
        _onFinish = onFinish ?? throw new ArgumentNullException(nameof(onFinish));
        _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
        _onCancelled = onCancelled;
        Session = new ShapeEditSession(minPoints, closed, noun);
    }

    // ---- IEntityStatefulGizmo — draw ----------------------------------------

    /// <inheritdoc/>
    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        _world = Fdp.Toolkit.World.WorldQuery.Of(view);
        var points = Session.Points;
        var heights = Session.Heights;
        Underlay?.Invoke(draw, points);
        // ⭐ B-17 P7 ② — the ONE drawing look (GizmoEditStyle): kind colour, small screen-pixel squares, the first point white, placed segments solid, the next dashed,
        //   the closing one (areas) dotted, a faint fill once an area is convex. ⛔ The private blues, the round spheres and the cursor disc are gone.
        var kind = Session.Closed ? ShapeKind.Area : ShapeKind.Route;
        int n = points.Count;
        if (n > 0)
        {
            if (Session.Closed) GizmoEditStyle.FillConvex(draw, points, kind);
            for (int i = 0; i < n - 1; i++)
                GizmoEditStyle.Segment(draw, new Vector3(points[i].X, points[i].Y, heights[i]),
                    new Vector3(points[i + 1].X, points[i + 1].Y, heights[i + 1]), kind, SegmentKind.Placed);

            GizmoEditStyle.Segment(draw, new Vector3(points[n - 1].X, points[n - 1].Y, heights[n - 1]), _currentPos, kind, SegmentKind.Next);
            if (Session.Closed && n >= 2)
                GizmoEditStyle.Segment(draw, _currentPos, new Vector3(points[0].X, points[0].Y, heights[0]), kind, SegmentKind.Closing);

            for (int i = 0; i < n; i++)
            {
                GizmoEditStyle.Point(draw, points[i], kind, first: i == 0);
                GizmoEditStyle.Stem(draw, points[i], heights[i], kind);
            }

            ShapeDoneMark.Draw(draw, Session);   // the on-map Done button
        }
        // ⭐ CE-5009 — the snap mark: a ring at the snapped point and the target dashed (a wall / road edge segment; an end is the ring alone)
        if (LastSnap.Hit)
        {
            draw.DrawSphere(new Vector3(LastSnap.Point, 0f), 6f, SnapColour, thickness: 2f, sizeMode: SizeMode.ScreenPixels);
            if (LastSnap.A != LastSnap.B)
                draw.DrawLine(new Vector3(LastSnap.A, 0f), new Vector3(LastSnap.B, 0f), SnapColour, 1.5f, style: LineStyle.Dashed);
        }
    }

    // ---- IEntityStatefulGizmo — interaction ---------------------------------

    /// <inheritdoc/>
    public void OnDragUpdate(Vector3 worldPos)
    {
        if (Snap == null && !_angleStep) { _currentPos = CursorAt(worldPos); return; }
        var p = SnapPoint(new Vector2(worldPos.X, worldPos.Y));   // ⭐ CE-5009 — the cursor shows where the click will land
        _currentPos = CursorAt(new Vector3(p.X, p.Y, worldPos.Z));
    }

    /// <summary>
    /// ⭐ CE-4594 (the 3-D acceptance rail) — the cursor's height the way a CLICK keeps it: a point on a ROOF keeps the roof's height, one on the ground keeps 0 (the 3-D drawer drapes a z = 0 shape on the relief). 🔴 The cursor used to carry the terrain pick's own height while the placed points
    /// had 0, so the rubber band from the last point to the cursor was a slanted line from z = 0 up to the ground height — it cut through the hill and was not drawn on a slope (measured: 9.8 m under the relief, the "next segment" invisible).
    /// </summary>
    private Vector3 CursorAt(Vector3 worldPos)
    {
        bool onLevel = worldPos.Z != 0f && _world != null && Fdp.Toolkit.World.Levels.LevelOf(_world, worldPos) >= 1;
        return onLevel ? worldPos : new Vector3(worldPos.X, worldPos.Y, 0f);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Left released: the ✓ finishes; on a closed shape the first point finishes it; else the point is appended.
    /// ⭐ B-17 P2 — right-click does nothing here: it is the menu.
    /// </remarks>
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        if (button != MapMouseButton.Left || isPressed) return;
        var at = new Vector2(worldPos.X, worldPos.Y);
        if (Snap != null || _angleStep)
        {
            // ⭐ CE-5009 — the click lands where the snap put it (the ✓ and the first point are hit-tested on the raw click: a snap never steals them)
            if (!Session.HitDone(at) && !Session.HitsFirstPoint(at)) { var s = SnapPoint(at); worldPos = new Vector3(s.X, s.Y, worldPos.Z); at = s; }
        }
        if (Session.HitDone(at) || Session.HitsFirstPoint(at)) { Resolve(Session.Finish()); return; }

        // ⭐ CE-1033 M22 — a point picked on a roof keeps the roof's height; one on the ground keeps 0 (draped).
        bool onLevel = worldPos.Z != 0f && _world != null && Fdp.Toolkit.World.Levels.LevelOf(_world, worldPos) >= 1;
        Session.Add(at, onLevel ? worldPos.Z : 0f);
    }

    /// <inheritdoc/>
    /// <remarks>Enter finishes · Backspace removes the last point · Esc cancels (all <see cref="ShapeEditSession.OnKey"/>).</remarks>
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        // ⭐ CE-5009 — the snap modifiers are HELD keys (a modifier is no keymap chord): Ctrl frees the next point, Shift steps the angle
        var code = (MapKeyboardKey)((int)key & 0xFFFF);
        if (code is MapKeyboardKey.LeftControl or MapKeyboardKey.RightControl) { _snapSuspended = isPressed; return; }
        if (code is MapKeyboardKey.LeftShift or MapKeyboardKey.RightShift) { _angleStep = isPressed; return; }
        Resolve(Session.OnKey(key, isPressed));
    }

    /// <inheritdoc/>
    /// <remarks>The drawing menu's rows (Finish · Remove last point · Cancel) — the SAME commands as the keys and the ✓.</remarks>
    public void OnMenuAction(int actionId) => Resolve(Session.ApplyMenuAction(actionId));

    // ⭐ ONE tail for all three finish doors and both cancel doors.
    private void Resolve(ShapeOutcome outcome)
    {
        if (outcome == ShapeOutcome.Finished)
        {
            _onFinish(Session.ToArray());
            // ⭐ CE-5006 TE-2 — a REPEATING tool stays armed: the next shape starts at once (the host renews what the next one is born with)
            if (Repeat) { Session.Restart(); OnRepeat?.Invoke(); }
            else _onRemove();
        }
        else if (outcome == ShapeOutcome.Cancelled)
        {
            _onCancelled?.Invoke();
            _onRemove();
        }
        // TooFew / None: stays armed (the reason is on the session's hint line).
    }

    // ---- Unused IGizmoInteractionHandler methods -----------------------------

    /// <inheritdoc/>
    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    /// <inheritdoc/>
    public void OnCommit(Vector3 worldPos) { }
    /// <inheritdoc/>
    public void OnCancel() { }

    /// <inheritdoc/>
    public void Dispose() { }
}
