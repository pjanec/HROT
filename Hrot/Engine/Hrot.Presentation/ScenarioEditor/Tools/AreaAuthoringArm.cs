using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Vis2D.Gizmos;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>
    /// ⭐⭐⭐ <c>E5</c> — <b>THE ONE area-authoring mechanism.</b> Registers a
    /// <see cref="PointSequenceGizmo"/>, turns the committed point sequence into an anchor +
    /// entity-relative polyline, and hands a finished <see cref="SpawnEntityCommand"/> to whatever
    /// the host does with it.
    ///
    /// <para>⛔⛔ <b>Why this class exists — it is a MOVE, not a new layer.</b> The same mechanism was
    /// written twice: <c>ScenarioSpawnAdapter.ArmAreaAuthoring</c> (editor/CGF, ~35 lines) and
    /// <c>IgApplication.ActivateAreaAuthoringTool</c> (IG, ~135 lines). ⭐ Ruling 9 — *"no keeping two
    /// implementations for the same concept"* — and 🔒 the user's <c>U6</c> ruling, verbatim: *"area
    /// authoring should be part of unified <b>Map2d role</b> features, as well as authoring the tactical
    /// drawings, <b>nothing of it should be IG host only</b>."*</para>
    ///
    /// <para>⭐⭐ <b>The precedent this follows exactly.</b> <c>UXI-07</c> step <c>3b</c> already did this
    /// for the EDIT half: <c>ActivateAreaEditingTool</c> used to carry verbatim copies of the
    /// <c>VertexEditGizmo</c>/<c>RouteWaypointGizmo</c> arms and now just calls
    /// <c>ToolController.Activate(ScenarioToolIds.Edit)</c> on the tools <c>MapInteractionPack</c>
    /// registers. ⛔ The CREATE half was left behind because <c>PlaceArea</c>/<c>PlaceRoute</c> are
    /// registered by <c>ScenarioSpawnAdapter</c>'s constructor — 📐 measured: those are the ONLY
    /// registrations of either id — so a host that does not compose that adapter (IG does not) had
    /// nowhere to route the arm and grew its own copy. ⇒ this class is the arm the ids point at,
    /// composable without the adapter.</para>
    ///
    /// <para>⭐⭐ <b>The two host differences are DATA AVAILABILITY and a COMMIT ROUTE, never set
    /// membership</b> (🔒 <c>Q27</c>'s standing ruling, quoted at <c>ActivateAreaEditingTool</c>'s own
    /// IG-only guard):</para>
    /// <list type="number">
    ///   <item>⭐ <b>geo</b> — IG holds a <see cref="IGeographicTransform"/>, the editor does not. 📐 With
    ///   a null transform this class reduces EXACTLY to the editor's arithmetic canvas centroid
    ///   (<c>lat = Y</c>, <c>lon = X</c>, <c>anchor = (mean X, mean Y, 0)</c>), so neither host's
    ///   geometry changes — verified against both prior bodies before the move.</item>
    ///   <item>⭐ <b>commit</b> — the editor publishes the command on its local bus; IG hands it to
    ///   <c>MapCommandController.OnAreaEntityCreated</c> so the <c>CMD_START_AUTHORING</c> session can
    ///   ACK it, or to a test sink. ⇒ <see cref="AreaAuthoringRequest.OnCommit"/>.</item>
    /// </list>
    ///
    /// <para>⚠ <b>What is deliberately NOT here:</b> the session bookkeeping
    /// (<c>BeginAreaAuthoringSession</c>), the keep-last context de-duplication, and the
    /// <c>_networkEnabled</c> gate stay at IG's call site — they are host rules about WHEN to arm, not
    /// part of the arm. ⛔ Pulling them in would make the editor answer to a DDS session it has not got.
    /// </para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1, §2.2 (points are RELATIVE);
    ///    docs/blueprints/PLAN_Terrain_Zones_Build.md <c>E5</c> / <c>§3-U6</c>;
    ///    docs/UX/UX_Feature_Tool_Model.md §4.9 (the arbiter contract this returns into).
    /// </summary>
    public sealed class AreaAuthoringArm
    {
        private readonly GlobalGizmoManager? _gizmos;
        private readonly IGeographicTransform? _geo;

        /// <param name="gizmos">
        /// The host's gizmo manager. ⚠ <b>Nullable on purpose</b>, and a null one still ARMS: IG's prior
        /// body used <c>_globalGizmoManager?.Register</c> and left the gizmo reachable through its own
        /// field, which is how its headless rails drive a commit. ⛔ Hardening this to non-null would
        /// have reddened those 11 rails for no gain. ⭐ A caller that wants the stricter contract keeps
        /// its own pre-check — <c>ScenarioSpawnAdapter</c> still returns <c>Unserviceable</c> first.
        /// </param>
        /// <param name="geo">
        /// The host's geographic transform, or <c>null</c> for a host that authors in canvas space.
        /// ⭐ See the class remarks: null reduces to the editor's exact prior arithmetic.
        /// </param>
        public AreaAuthoringArm(GlobalGizmoManager? gizmos, IGeographicTransform? geo = null)
        {
            _gizmos = gizmos;
            _geo    = geo;
        }

        /// <summary>The id this arm registered, or <c>null</c> when nothing is armed.</summary>
        public long? ActiveGizmoId { get; private set; }

        /// <summary>
        /// The gizmo this arm registered, or <c>null</c> when nothing is armed.
        /// ⭐ Exposed because IG mirrors it into <c>_activeSequenceGizmo</c>, a field SHARED by its
        /// placement, area and route tools — so "one sequence tool at a time" stays a single fact on
        /// that host. ⛔ Hiding the gizmo would have forced IG to keep its own construction.
        /// </summary>
        public PointSequenceGizmo? ActiveGizmo { get; private set; }

        /// <summary>
        /// Arm the tool. Any sequence this arm already holds is unregistered first.
        /// </summary>
        public ToolActivationOutcome Arm(in AreaAuthoringRequest request)
        {
            if (request.OnCommit == null)
                throw new ArgumentException("An area-authoring request must carry a commit sink.", nameof(request));

            Disarm();

            var minPoints  = request.MinPoints;
            var tkbType    = request.TkbType;
            var styleJson  = request.StyleJson ?? string.Empty;
            var onCommit   = request.OnCommit;
            var onCancel   = request.OnCancelled;
            var onDisarmed = request.OnDisarmed;
            var id         = GlobalGizmoManager.NewId();

            var gizmo = new PointSequenceGizmo(
                onFinish: points =>
                {
                    if (points == null || points.Length < minPoints)
                    {
                        // ⚠ Does NOT unregister: the prior bodies both left the gizmo alive here, and the
                        //   gizmo's own onRemove is what tears it down. Changing that would make a
                        //   too-short shape silently kill the tool mid-draw.
                        onCancel?.Invoke();
                        return;
                    }

                    var anchor   = ComputeAnchor(points, out var relative);
                    var polyline = new EditablePolyline { Points = relative };
                    var style    = MapOverlayStyle.FromJson(styleJson);

                    onCommit(new SpawnEntityCommand
                    {
                        NetworkId         = 0,
                        TkbType           = tkbType,
                        OwnerNodeId       = 0,
                        InitType          = ReliableInitType.AllPeers,
                        RequestId         = Guid.NewGuid(),
                        InitialTransform  = new SimTransform { Position = anchor },
                        InitialComponents = new List<object> { polyline, style },
                    });
                },
                onRemove: () =>
                {
                    _gizmos?.Unregister(id);

                    // ⚠⚠ The guard matters: a LATER Arm() may already own the canvas, and clearing
                    //   unconditionally would null the live sequence. ⛔ Both prior bodies cleared without
                    //   it — a latent bug the move removes.
                    if (ActiveGizmoId != id) return;

                    ActiveGizmoId = null;
                    ActiveGizmo   = null;

                    // 🔴 REQUIRED, and it is the regression the move first shipped without: a host that
                    //   MIRRORS the armed gizmo in its own field (IG does — `_activeSequenceGizmo` is shared
                    //   by its placement, area and route tools) has no other way to learn the tool tore
                    //   itself down. 📐 Measured: without this, IG`s
                    //   `AreaAuthoringTests.AreaTool_AfterCommit_ToolIsPopped` goes RED — the mirror still
                    //   reported a live sequence tool after a commit, so the next arm believed one was held.
                    onDisarmed?.Invoke();
                });

            ActiveGizmoId = id;
            ActiveGizmo   = gizmo;
            _gizmos?.Register(id, gizmo);
            return ToolActivationOutcome.Armed;
        }

        /// <summary>Drop whatever this arm holds. ⭐ Idempotent.</summary>
        public void Disarm()
        {
            if (!ActiveGizmoId.HasValue) return;
            _gizmos?.Unregister(ActiveGizmoId.Value);
            ActiveGizmoId = null;
            ActiveGizmo   = null;
        }

        /// <summary>
        /// ⭐⭐ <b>The geometry, in ONE place.</b> Anchor = the centroid; the polyline's points are the
        /// vertices RELATIVE to it (📄 design §2.2 — <c>EditablePolyline</c> is entity-relative, and
        /// <c>A1</c> made <c>TacticalAreaGizmo</c> add the <c>SimTransform</c> back when drawing).
        ///
        /// <para>⭐ With a <see cref="IGeographicTransform"/> the mean is taken in GEODETIC space and
        /// projected back, which is what IG did; without one it is the arithmetic mean of the canvas
        /// points, which is what the editor did. ⚠ The accumulators are <c>double</c> — IG's were, the
        /// editor's were <c>float</c> — so the shared path is never less precise than either.</para>
        /// </summary>
        private Vector3 ComputeAnchor(Vector2[] points, out List<Vector2> relative)
        {
            if (_geo == null)
            {
                double sx = 0.0, sy = 0.0;
                for (int i = 0; i < points.Length; i++) { sx += points[i].X; sy += points[i].Y; }
                var anchor2 = new Vector2((float)(sx / points.Length), (float)(sy / points.Length));

                relative = new List<Vector2>(points.Length);
                for (int i = 0; i < points.Length; i++)
                    relative.Add(points[i] - anchor2);

                return new Vector3(anchor2.X, anchor2.Y, 0f);
            }

            // Canvas is XY: canvas Y = world Y (North, ENU). Altitude is 0 for authoring.
            var abs = new (double Lat, double Lon, double Alt)[points.Length];
            double refLat = 0.0, refLon = 0.0, refAlt = 0.0;
            for (int i = 0; i < points.Length; i++)
            {
                abs[i] = _geo.ToGeodetic(new Vector3(points[i].X, points[i].Y, 0f));
                refLat += abs[i].Lat;
                refLon += abs[i].Lon;
                refAlt += abs[i].Alt;
            }
            refLat /= points.Length;
            refLon /= points.Length;
            refAlt /= points.Length;

            var anchor = _geo.ToCartesian(refLat, refLon, refAlt);

            relative = new List<Vector2>(points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                var absCart = _geo.ToCartesian(abs[i].Lat, abs[i].Lon, 0.0);
                relative.Add(new Vector2(absCart.X - anchor.X, absCart.Y - anchor.Y));
            }

            return anchor;
        }
    }

    /// <summary>
    /// What a host wants authored. ⭐ <see cref="TkbType"/> is a parameter rather than a constant
    /// because a <b>terrain zone</b> is drawn by exactly this mechanism — 🔒 the <c>U6</c> ruling is
    /// *"so zones can be drawn wherever that role runs"*, and <c>B1</c> allocated
    /// <c>TkbEntityTypes.TerrainZone</c> for it. ⛔ Hard-coding <c>TacGraphic_Area</c> (as both prior
    /// bodies did) is what made a zone undrawable.
    /// </summary>
    /// <param name="TkbType">The TKB type the committed entity is born as.</param>
    /// <param name="StyleJson">Overlay style JSON; empty means the <c>MapOverlayStyle</c> default.</param>
    /// <param name="OnCommit">
    /// ⭐ Where the finished command goes — the host's difference. ⛔ Required: an arm with no sink is
    /// exactly the silently-no-op capability <c>R-133</c> forbids, so it throws rather than draws.
    /// </param>
    /// <param name="OnCancelled">
    /// Invoked when the operator commits fewer than <paramref name="MinPoints"/> points. ⚠ Optional
    /// because only a host running a request/ACK session has anything to tell.
    /// </param>
    /// <param name="MinPoints">
    /// ⭐ 3 for a closed area, and a parameter so the route arm can reuse this one day. ⚠ The gizmo
    /// itself does not enforce it — the check is here, as it was in both prior bodies.
    /// </param>
    /// <param name="OnDisarmed">
    /// 🔴 <b>Invoked when the tool tears itself down</b> (a commit, or the operator dismissing it).
    /// ⭐ Required by any host that MIRRORS the armed gizmo in its own state — IG keeps
    /// <c>_activeSequenceGizmo</c>/<c>_activeSequenceId</c> shared across its placement, area and route
    /// tools, and the editor keeps <c>_activeSequenceId</c>. ⛔ Omitting it leaves that mirror STALE, so
    /// the host believes a sequence tool is still held after it is gone. 📐 That is exactly what
    /// reddened <c>AreaTool_AfterCommit_ToolIsPopped</c> when this arm first landed.
    /// </param>
    public readonly record struct AreaAuthoringRequest(
        long TkbType,
        string StyleJson,
        Action<SpawnEntityCommand> OnCommit,
        Action? OnCancelled = null,
        int MinPoints = 3,
        Action? OnDisarmed = null);
}
