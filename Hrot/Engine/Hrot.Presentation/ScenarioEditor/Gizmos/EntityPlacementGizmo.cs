using System;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// Stateful gizmo that translates a left-click on the canvas into a
    /// <see cref="SpawnEntityCommand"/> routed through the injected delegate,
    /// decoupling the gizmo from any specific network protocol.
    ///
    /// Replaces the deleted <c>CreationTool</c> (Phase 3 of the gizmo migration).
    /// Exercised via <see cref="GlobalGizmoManager"/> which routes ECS bus events
    /// events into this gizmo.
    ///
    /// Workflow:
    /// <list type="number">
    ///   <item>Caller constructs the gizmo and registers it with <c>GlobalGizmoManager</c>.</item>
    ///   <item>Operator sees a ghost preview circle at the cursor.</item>
    ///   <item>Left-click builds a <see cref="SpawnEntityCommand"/> and fires
    ///         the <see cref="_onEntityCreated"/> delegate. When <c>autoPopOnPlace</c>
    ///         is <c>true</c> (default) the gizmo calls <c>_onRemove()</c> immediately
    ///         (single-placement); otherwise it stays active for multi-placement until
    ///         right-click or ESC.</item>
    ///   <item>Right-click or ESC cancels placement; the gizmo calls <c>_onRemove()</c>
    ///         without firing the delegate.</item>
    /// </list>
    ///
    /// No allocations on the hover / draw hot path.
    /// </summary>
    public sealed class EntityPlacementGizmo : IEntityStatefulGizmo
    {
        // Constants copied from deleted CreationToolConstants
        private const long DefaultTkbType    = 101L;
        private const byte GhostAlpha        = 128;
        // ⭐ CE-1065 — half the size it was (user, 2026-10-14: "make the entity placement and entity picker gizmo 2 times smaller").
        //   ⚠ Despite the "Px" name these are WORLD METRES (DrawSphere's default SizeMode) — a 15 m translucent ball hid the scene in 3-D.
        // ⭐ CE-1337 — 6 m across, about one tank (user, 2026-10-25: "it should not be bigger than 2 tank lengths"); the ghost box shows the real size.
        private const byte FootprintFillAlpha = 100;
        private const float GhostLabelOffsetY = 10f;

        private readonly Action<SpawnEntityCommand> _onEntityCreated;
        private readonly long                       _tkbType;
        private readonly ForceId                    _affiliationForDisplay;
        private readonly string?                    _initialPropertiesJson;
        private readonly bool                       _autoPopOnPlace;
        private readonly Func<string>?              _nameResolver;
        private readonly Action                     _onRemove;
        private readonly string?                    _displayName;
        private readonly Func<Hrot.Common.Macros.MacroPreviewResult?>? _macroPreview;   // ⭐ CE-4244 (MA-10) — a MACRO placement draws its ghosts at the cursor

        /// <summary>
        /// ⭐ CE-1017 S2 (D6b) — the entity faces NORTH: yaw +90° about Z. 📐 The transform's yaw 0 is EAST
        /// (<c>SimTransform</c>: "yaw: 0=X axis direction (east), +90=Y axis direction (north)"), so the
        /// <c>Quaternion.Identity</c> used before faced every placed entity east.
        /// </summary>
        internal static readonly Quaternion FacingNorth = PlacementSession.FacingNorth;

        /// <summary>The hint shown under the ghost while the tool stays armed (always-multi, D5 rev 3).</summary>
        internal const string MultiPlacementHint = "click: place  ·  right-click/Esc: done";

        private Vector3 _cursorWorld;
        private Fdp.Toolkit.World.IWorldQuery? _world;

        // ⭐ CE-4309 (Map Interaction P3) — airborne by type, the 2-D level, the heading drag: decided here, built into the command below
        private readonly PlacementSession _session = new();
        private bool _typeKnown;
        private (float? Air, int Level, int Count) _hintKey = (null, -1, -1);
        private (string First, string? Second) _hint = (MultiPlacementHint, null);

        /// <summary>The placement's choices (rails).</summary>
        internal PlacementSession Session => _session;

        /// <summary>
        /// Raised after a <see cref="SpawnEntityCommand"/> has been constructed and passed
        /// to the <see cref="_onEntityCreated"/> delegate, so tests and integrators can
        /// observe the event without inspecting the delegate's capture list.
        /// </summary>
        public event Action<SpawnEntityCommand>? OnCommandPublished;

        /// <summary>
        /// Raised when the gizmo is about to exit (before <c>_onRemove()</c> is invoked).
        /// Allows external observers to detect gizmo lifecycle changes.
        /// </summary>
        public event Action? Exited;

        /// <inheritdoc/>
        public bool RequiresExclusiveFocus => true;
        public bool WantsRawInput => true;

        /// <inheritdoc/>
        public bool IsFocused { get; private set; }

        /// <inheritdoc/>
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        /// <param name="onEntityCreated">
        /// Delegate invoked with the fully-constructed <see cref="SpawnEntityCommand"/> when
        /// the operator left-clicks. Must not be <c>null</c>.
        /// </param>
        /// <param name="tkbType">
        /// TKB template type to request. Defaults to <see cref="DefaultTkbType"/> when zero is passed.
        /// </param>
        /// <param name="initialPropertiesJson">
        /// Optional JSON object with initial property overrides.
        /// Recognised fields: <c>name</c> (string); <c>affiliation</c> (string, e.g. <c>"FORCE_FRIENDLY"</c>).
        /// Unknown fields are silently ignored.
        /// </param>
        /// <param name="autoPopOnPlace">
        /// When <c>true</c> (default) the gizmo removes itself immediately after a successful
        /// left-click (single-placement mode). Set to <c>false</c> for continuous multi-placement.
        /// </param>
        /// <param name="nameResolver">
        /// Optional delegate invoked on each left-click to obtain the entity name.
        /// When provided it replaces the TKB-default name (⭐ <c>CE-259ax</c> G2: it is no longer dropped). ⚠ An explicit <c>name</c> in
        /// <paramref name="initialPropertiesJson"/> is compiled on top by the adapter and still wins.
        /// </param>
        /// <param name="onRemove">
        /// Callback invoked when the gizmo wants to exit. Typically calls
        /// <c>GlobalGizmoManager.Unregister</c> to remove the gizmo from the manager.
        /// </param>
        public EntityPlacementGizmo(
            Action<SpawnEntityCommand> onEntityCreated,
            long                       tkbType               = DefaultTkbType,
            string?                    initialPropertiesJson = null,
            bool                       autoPopOnPlace        = true,
            Func<string>?              nameResolver          = null,
            Action?                    onRemove              = null,
            string?                    displayName           = null,
            Func<Hrot.Common.Macros.MacroPreviewResult?>? macroPreview = null)
        {
            _macroPreview          = macroPreview;
            _displayName           = displayName;
            _onEntityCreated       = onEntityCreated ?? throw new ArgumentNullException(nameof(onEntityCreated));
            _tkbType               = tkbType == 0 ? DefaultTkbType : tkbType;
            _affiliationForDisplay = ParseAffiliationFromJson(initialPropertiesJson);
            _initialPropertiesJson = initialPropertiesJson;
            _autoPopOnPlace        = autoPopOnPlace;
            _nameResolver          = nameResolver;
            _onRemove              = onRemove ?? (() => { });
        }

        // IEntityStatefulGizmo — draw

        /// <inheritdoc/>
        /// <remarks>
        /// Draws a semi-transparent ghost circle at the current cursor world position,
        /// with the TKB type code as a label below it.
        /// </remarks>
        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            _world = Fdp.Toolkit.World.WorldQuery.Of(view);   // ⭐ CE-1033 S2 — for the clicked level (M20)
            EnsureType(view);
            var ghostColor = GetAffiliationColor(_affiliationForDisplay);
            ghostColor.A = GhostAlpha;

            // ⭐ U-8 (CE-4339) — while a heading drag is down the entity lands at the PRESS point, so the ghost stands there (it used to follow the cursor) and a direction line runs from it toward the cursor
            var (ghostAt, dragYaw) = _session.Pending(_cursorWorld);
            // ⭐ B-17 P7 ① — NO ball any more (the 3 px sphere). The ghost is the entity's FOOTPRINT, as the 2-D map will draw it (EntityFootprint's one rule, read from the TKB template),
            //   turned to the pending heading with a notch at the front; an aircraft stands at its altitude over a drop line and a ground ring (its shadow).
            float lift = _session.AirborneHeight ?? 0f;
            DrawFootprint(draw, ghostColor, ghostAt, dragYaw ?? (MathF.PI / 2f));   // north until a drag sets the heading (PlacementSession.FacingNorth)
            DrawGhostBox(view, draw, ghostColor, lift > 0f ? new Vector3(ghostAt.X, ghostAt.Y, ghostAt.Z + lift) : ghostAt, dragYaw ?? (MathF.PI / 2f));
            if (lift > 0f) DrawAircraftShadow(draw, ghostColor, ghostAt, lift);
            if (_macroPreview?.Invoke() is { } preview) MacroGhostDraw.Draw(draw, preview, ghostAt);   // ⭐ CE-4244 MP-2 (a) — what the click would create
            PlacementDirectionLine.Draw(draw, _session, _cursorWorld);
            // ⭐ CE-1017 S2: the type's NAME (it used to be the bare TKB number), and while the tool stays armed
            //   the hint that says how to finish.
            draw.DrawTextLong(
                _cursorWorld.X,
                _cursorWorld.Y - GhostLabelOffsetY,   // ⭐ CE-1040 (N5) — north-up: below the cursor is world −Y
                _label ??= string.IsNullOrWhiteSpace(_displayName) ? _tkbType.ToString() : _displayName!,   // ⭐ cached: a per-frame ToString() allocated
                Rgba32.White, style: GizmoTextStyle.Backdrop);
            var key = (_session.AirborneHeight, _session.Level, _session.LevelCount);
            if (key != _hintKey) { _hintKey = key; _hint = _session.Hint(_autoPopOnPlace ? "click: place" : MultiPlacementHint); }   // ⭐ CE-4309 — rebuilt only when a choice moves (0 B steady)
            if (!_autoPopOnPlace || _hint.Second != null)
            {
                Hrot.ScenarioEditor.Map.MapUiStates.ToolsOf(view as EntityRepository)?.ClaimCursorHint();   // ⭐ B-17 P1 — ONE hint at the cursor: this one
                draw.DrawTextLong(_cursorWorld.X, _cursorWorld.Y - 2 * GhostLabelOffsetY, _hint.First, Rgba32.White, style: GizmoTextStyle.Backdrop);
                if (_hint.Second != null) draw.DrawTextLong(_cursorWorld.X, _cursorWorld.Y - 3 * GhostLabelOffsetY, _hint.Second, Rgba32.White, style: GizmoTextStyle.Backdrop);
            }
        }

        /// <summary>⭐ CE-4309 — once: does this type fly? Its TKB flight descriptor's height (the A-10C 300 m, a helicopter 30 m), else a ground type.</summary>
        private void EnsureType(ISimulationView view)
        {
            if (_typeKnown || view is not Fdp.Core.EntityRepository repo || !repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()) return;
            var tkb = repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
            if (tkb == null || !tkb.TryGetByType(_tkbType, out var template) || template == null) return;
            var flight = template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.FlightParamsDto>();
            _session.SetType(flight?.HoverHeightM, flight is null ? 0f : (flight.CruiseSpeedMps > 0f ? flight.CruiseSpeedMps : flight.MaxSpeedMps));
            _bodyLook = Hrot.UI.Common.Map3D.EntityBodyLayer3D.Resolve(template);   // ⭐ once, with the footprint: the ghost's wire box is the same every frame (0 B steady)
            Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.Dimensions(template, out _footLength, out _footWidth);   // ⭐ B-17 P7 ① — once: the rule the 2-D symbol and the pick area use
            _typeKnown = true;
        }

        // ⭐ B-17 P7 ① — the footprint (metres) the spawned entity will have on the 2-D map; the rule's fallback until the TKB template is known.
        private string? _label;
        private Hrot.UI.Common.Map3D.EntityBodyLayer3D.TypeLook? _bodyLook;
        private float _footLength = Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.DefaultLengthMetres;
        private float _footWidth = Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.DefaultLengthMetres * Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.DefaultWidthFactor;

        /// <summary>⭐ B-17 P7 ① — a translucent box of the entity's footprint (world metres) turned to <paramref name="yawRad"/> (math yaw: the length runs along local +X), a dashed outline in the side colour,
        /// and a small solid notch triangle just beyond the front edge so the heading reads at a glance.</summary>
        private void DrawFootprint(IDebugDrawBuilder draw, Rgba32 colour, Vector3 at, float yawRad)
        {
            var outline = new Rgba32(colour.R, colour.G, colour.B, 230);
            var fill = new Rgba32(colour.R, colour.G, colour.B, FootprintFillAlpha);
            var centre = new Vector2(at.X, at.Y);
            draw.DrawBox2D(centre, new Vector2(_footLength * 0.5f, _footWidth * 0.5f), outline, yawRad * (180f / MathF.PI), 1.6f, SizeMode.WorldMeters, fillColor: fill, style: LineStyle.Dashed);   // metres for the box, a 1.6 px outline (outlines are pixels)
            var f = new Vector2(MathF.Cos(yawRad), MathF.Sin(yawRad));
            var side = new Vector2(-f.Y, f.X);
            float half = _footLength * 0.5f, notchHalf = _footWidth * 0.2f, notchLen = _footWidth * 0.35f;
            var front = centre + f * half;
            draw.DrawFilledTriangle(front + side * notchHalf, front - side * notchHalf, front + f * notchLen, outline);
        }

        /// <summary>⭐ B-17 P7 ① — an aircraft at its altitude: a dashed drop line from the ghost down to the ground under it, and a ground ring (its shadow) round the footprint.</summary>
        private void DrawAircraftShadow(IDebugDrawBuilder draw, Rgba32 colour, Vector3 groundAt, float lift)
        {
            var line = new Rgba32(colour.R, colour.G, colour.B, 230);
            draw.DrawLine(new Vector3(groundAt.X, groundAt.Y, groundAt.Z + lift), groundAt, line, 1.2f, SizeMode.ScreenPixels, style: LineStyle.Dashed);
            draw.DrawSphere(new Vector3(groundAt.X, groundAt.Y, 0f), MathF.Max(_footLength, _footWidth) * 0.5f, line, thickness: 1f);   // thickness ⇒ an outline ring only, draped on the ground in 3-D
        }

        /// <summary>
        /// ⭐ CE-1033 S3 (docs/DESIGN_Map_3D_Mode.md M20) — the placement GHOST: a wire box of the type's drawn size (the 3-D map's
        /// <see cref="Hrot.UI.Common.Map3D.EntityBodyLayer3D.Resolve"/>, one sizing rule) standing on the picked surface. In 3-D
        /// it shows the body where it will land (a roof, a slope); in 2-D its edges collapse onto the type's footprint.
        /// ⚠ A wire box, not the translucent shape kit the design names: a gizmo emits primitives, and a kit mesh is not one.
        /// </summary>
        private static Vector3 Corner(Vector3 centre, float cs, float sn, float lx, float ly, float z) => new(centre.X + lx * cs - ly * sn, centre.Y + lx * sn + ly * cs, z);
        private static void Edge(IDebugDrawBuilder draw, Vector3 a, Vector3 b, Rgba32 colour) => draw.DrawLine(a, b, colour, 1f, SizeMode.ScreenPixels, PipelineTarget.Viewport3D);

        private void DrawGhostBox(ISimulationView view, IDebugDrawBuilder draw, Rgba32 colour, Vector3 centre, float yawRad)
        {
            if (_bodyLook is not { } look) return;   // resolved once by EnsureType
            if (look.Family == Hrot.UI.Common.Map3D.VisualFamily.Unit) return;
            var h = look.Size / 2f;
            float z0 = centre.Z, z1 = centre.Z + look.Size.Z;
            // ⭐ B-17 P7-C — the 3-D view's box only (PipelineTarget.Viewport3D: the 2-D renderer skips a primitive without Map2D, DebugPrimitiveRenderer2D.cs:119), turned to the SAME heading as
            //   the footprint (the size's X runs along the heading). ONE outline per view: 2-D shows the footprint box alone.
            float cs = MathF.Cos(yawRad), sn = MathF.Sin(yawRad);
            // the 12 edges, with no span / array (a steady frame allocates nothing): the two rectangles (z0, z1) and the four verticals
            for (int level = 0; level < 2; level++)
            {
                float z = level == 0 ? z0 : z1;
                var a = Corner(centre, cs, sn, -h.X, -h.Y, z); var b = Corner(centre, cs, sn, h.X, -h.Y, z);
                var c = Corner(centre, cs, sn, h.X, h.Y, z); var d = Corner(centre, cs, sn, -h.X, h.Y, z);
                Edge(draw, a, b, colour); Edge(draw, b, c, colour); Edge(draw, c, d, colour); Edge(draw, d, a, colour);
            }
            for (int i = 0; i < 4; i++)
            {
                float lx = (i & 1) == 0 ? -h.X : h.X, ly = (i & 2) == 0 ? -h.Y : h.Y;
                Edge(draw, Corner(centre, cs, sn, lx, ly, z0), Corner(centre, cs, sn, lx, ly, z1), colour);
            }
        }

        // IEntityStatefulGizmo — interaction

        /// <inheritdoc/>
        public void OnDragUpdate(Vector3 worldPos)
        {
            _cursorWorld = worldPos;
            _session.Hover(_world, worldPos);   // ⭐ CE-4309 — the surfaces stacked under the cursor (2-D)
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Left released: build a <see cref="SpawnEntityCommand"/> and optionally remove self.
        /// Right pressed: cancel placement and remove self.
        /// </remarks>
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            // ⭐ CE-1017 S2: modifiers ride in the high bits (MapMouseButton.ShiftMask …); a Shift+click IS a
            //   left click. 📐 The exact compare used before made a modified click do nothing at all.
            bool shift = (button & MapMouseButton.ShiftMask) != 0;   // ⭐ CE-4309 — Shift places an aircraft parked on the ground
            button &= ~(MapMouseButton.ShiftMask | MapMouseButton.CtrlMask | MapMouseButton.AltMask);
            if (button == MapMouseButton.Left && isPressed) _session.Press(worldPos);   // ⭐ CE-4309 — a drag from here sets the heading
            if (button == MapMouseButton.Left && !isPressed)
            {
                BuildAndPublishSpawnCommand(worldPos, shift);
                if (_autoPopOnPlace)
                    Remove();
            }
            else if (button == MapMouseButton.Right && isPressed)
            {
                Remove();
            }
        }

        /// <inheritdoc/>
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (key == MapKeyboardKey.Escape && isPressed)
                Remove();
            else if (isPressed) _session.Key((int)key);   // ⭐ CE-4309 — PgUp / PgDn: the level under the cursor
        }

        // Unused IEntityStatefulGizmo methods — empty body (no interaction handle for placement)
        /// <inheritdoc/>
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        /// <inheritdoc/>
        public void OnCommit(Vector3 worldPos) { }
        /// <inheritdoc/>
        public void OnCancel() { }
        /// <inheritdoc/>
        public void OnMenuAction(int actionId) { }

        /// <inheritdoc/>
        public void Dispose() { }

        // Private helpers

        /// <summary>
        /// Fires <see cref="Exited"/> then calls <see cref="_onRemove"/>.
        /// The <see cref="Exited"/> event fires BEFORE <see cref="_onRemove"/> so observers
        /// that wire up to the event run before the bridge is popped off the canvas.
        /// </summary>
        private void Remove()
        {
            Exited?.Invoke();
            _onRemove();
        }

        private void BuildAndPublishSpawnCommand(Vector3 worldPos, bool shift = false)
        {
            // The canvas worldPos is in flat-earth Cartesian space (X = east meters, Y = north meters).
            // Store it verbatim as the InitialTransform. The ACL egress translator
            // (SpawnEntityCommandEgressTranslator) converts this position to geodetic lat/lon
            // via the IGeographicTransform when building the DDS CreateEntityRequest.
            //
            // ⭐ CE-4309 (P3) — the session decides the rest: a FLYING type is born in the air (AboveLevel + its height; Shift: on the ground),
            //   a 2-D click on a stacked place takes the chosen level, a 3-D click its own level (CE-1033 M20), a drag sets the heading.
            int? levelOf3D = worldPos.Z != 0f && _world != null ? Fdp.Toolkit.World.Levels.LevelOf(_world, worldPos) : null;
            var (position, height, rotation) = _session.Resolve(worldPos, shift, levelOf3D);

            var cmd = new SpawnEntityCommand
            {
                NetworkId         = 0,
                TkbType           = (Fdp.Core.TkbTypeId)(_tkbType),
                OwnerNodeId       = (Fdp.Core.NodeId)(0),
                InitType          = ReliableInitType.AllPeers,
                InitialTransform  = new SimTransform
                {
                    Position = position,
                    Rotation = rotation,
                },
                SpawnHeight           = height,
                InitialAttributesJson = _initialPropertiesJson,
                RequestId             = Guid.NewGuid(),
            };

            // ⭐ CE-259ax G2 — the resolver was accepted and DROPPED (`_ = _nameResolver`). Its name now rides on the command as the
            //   entity's EntityInfo, which the adapter keeps instead of seeding its TKB-default one; an explicit `name` in the properties
            //   JSON is compiled on top and still wins.
            var resolved = _nameResolver?.Invoke();
            if (!string.IsNullOrWhiteSpace(resolved))
                cmd.InitialComponents = new System.Collections.Generic.List<object>
                {
                    new EntityInfo { Name = new FixedString64(resolved), ForceId = ForceId.Neutral },
                };

            _onEntityCreated(cmd);
            OnCommandPublished?.Invoke(cmd);
        }

        /// <summary>
        /// Parses the force affiliation string from the JSON blob for ghost rendering colour.
        /// Handles both legacy lower-case keys (<c>"affiliation"</c>) and PascalCase (<c>"Affiliation"</c>).
        /// </summary>
        private static ForceId ParseAffiliationFromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return ForceId.Neutral;
            try
            {
                using var doc = JsonDocument.Parse(json);
                JsonElement affEl;
                if (!doc.RootElement.TryGetProperty("affiliation", out affEl) &&
                    !doc.RootElement.TryGetProperty("Affiliation",  out affEl))
                    return ForceId.Neutral;

                var raw = affEl.GetString() ?? string.Empty;
                return raw.ToUpperInvariant() switch
                {
                    "FORCE_FRIENDLY" => ForceId.Friend,
                    "FORCE_OPPOSING" => ForceId.Hostile,
                    "FORCE_NEUTRAL"  => ForceId.Neutral,
                    _                => ForceId.Neutral,
                };
            }
            catch { /* malformed JSON */ }
            return ForceId.Neutral;
        }

        /// <summary>⭐ CE-1163 (Symbology §3.2) — the second side palette folded into the one table: the ghost and the placed entity match.</summary>
        private static Rgba32 GetAffiliationColor(ForceId affiliation) => Hrot.Common.Diagnostics.Gizmos.AffiliationColors.Of(affiliation);
    }
}
