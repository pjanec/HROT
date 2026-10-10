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
        private const int  GhostRadiusPx     = 15;
        private const int  GhostLabelOffsetY = 20;

        private readonly Action<SpawnEntityCommand> _onEntityCreated;
        private readonly long                       _tkbType;
        private readonly ForceId                    _affiliationForDisplay;
        private readonly string?                    _initialPropertiesJson;
        private readonly bool                       _autoPopOnPlace;
        private readonly Func<string>?              _nameResolver;
        private readonly Action                     _onRemove;
        private readonly string?                    _displayName;

        /// <summary>
        /// ⭐ CE-1017 S2 (D6b) — the entity faces NORTH: yaw +90° about Z. 📐 The transform's yaw 0 is EAST
        /// (<c>SimTransform</c>: "yaw: 0=X axis direction (east), +90=Y axis direction (north)"), so the
        /// <c>Quaternion.Identity</c> used before faced every placed entity east.
        /// </summary>
        internal static readonly Quaternion FacingNorth = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

        /// <summary>The hint shown under the ghost while the tool stays armed (always-multi, D5 rev 3).</summary>
        internal const string MultiPlacementHint = "click: place  ·  right-click/Esc: done";

        private Vector3 _cursorWorld;
        private Fdp.Toolkit.World.IWorldQuery? _world;

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
        /// When provided it takes priority over any <c>name</c> in <paramref name="initialPropertiesJson"/>.
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
            string?                    displayName           = null)
        {
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
            var ghostColor = GetAffiliationColor(_affiliationForDisplay);
            ghostColor.A = GhostAlpha;

            draw.DrawSphere(_cursorWorld, GhostRadiusPx, ghostColor);
            DrawGhostBox(view, draw, ghostColor);
            // ⭐ CE-1017 S2: the type's NAME (it used to be the bare TKB number), and while the tool stays armed
            //   the hint that says how to finish.
            draw.DrawTextLong(
                _cursorWorld.X,
                _cursorWorld.Y + GhostLabelOffsetY,
                string.IsNullOrWhiteSpace(_displayName) ? _tkbType.ToString() : _displayName!,
                Rgba32.White);
            if (!_autoPopOnPlace)
                draw.DrawTextLong(_cursorWorld.X, _cursorWorld.Y + 2 * GhostLabelOffsetY, MultiPlacementHint, Rgba32.White);
        }

        /// <summary>
        /// ⭐ CE-1033 S3 (docs/DESIGN_Map_3D_Mode.md M20) — the placement GHOST: a wire box of the type's drawn size (the 3-D map's
        /// <see cref="Hrot.UI.Common.Map3D.EntityBodyLayer3D.Resolve"/>, one sizing rule) standing on the picked surface. In 3-D
        /// it shows the body where it will land (a roof, a slope); in 2-D its edges collapse onto the type's footprint.
        /// ⚠ A wire box, not the translucent shape kit the design names: a gizmo emits primitives, and a kit mesh is not one.
        /// </summary>
        private void DrawGhostBox(ISimulationView view, IDebugDrawBuilder draw, Rgba32 colour)
        {
            if (view is not Fdp.Core.EntityRepository repo || !repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()) return;
            var tkb = repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
            if (tkb == null || !tkb.TryGetByType(_tkbType, out var template) || template == null) return;
            var look = Hrot.UI.Common.Map3D.EntityBodyLayer3D.Resolve(template);
            if (look.Family == Hrot.UI.Common.Map3D.VisualFamily.Unit) return;
            var h = look.Size / 2f;
            float z0 = _cursorWorld.Z, z1 = _cursorWorld.Z + look.Size.Z;
            Span<Vector3> c = stackalloc Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = new Vector3(_cursorWorld.X + ((i & 1) == 0 ? -h.X : h.X), _cursorWorld.Y + ((i & 2) == 0 ? -h.Y : h.Y),
                                   (i & 4) == 0 ? z0 : z1);
            ReadOnlySpan<int> edges = stackalloc int[] { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
            for (int k = 0; k < edges.Length; k += 2) draw.DrawLine(c[edges[k]], c[edges[k + 1]], colour, 1f);
        }

        // IEntityStatefulGizmo — interaction

        /// <inheritdoc/>
        public void OnDragUpdate(Vector3 worldPos)
        {
            _cursorWorld = worldPos;
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
            button &= ~(MapMouseButton.ShiftMask | MapMouseButton.CtrlMask | MapMouseButton.AltMask);
            if (button == MapMouseButton.Left && !isPressed)
            {
                BuildAndPublishSpawnCommand(worldPos);
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

        private void BuildAndPublishSpawnCommand(Vector3 worldPos)
        {
            // The canvas worldPos is in flat-earth Cartesian space (X = east meters, Y = north meters).
            // Store it verbatim as the InitialTransform. The ACL egress translator
            // (SpawnEntityCommandEgressTranslator) converts this position to geodetic lat/lon
            // via the IGeographicTransform when building the DDS CreateEntityRequest.
            // nameResolver is retained for future wiring (session-scoped sequential names).
            _ = _nameResolver; // retained for future use

            var cmd = new SpawnEntityCommand
            {
                NetworkId         = 0,
                TkbType           = _tkbType,
                OwnerNodeId       = 0,
                InitType          = ReliableInitType.AllPeers,
                InitialTransform  = new SimTransform
                {
                    Position = new Vector3(worldPos.X, worldPos.Y, 0f),
                    Rotation = FacingNorth,
                },
                // ⭐ CE-1017 S2 (D6b rev 5): stand on the GROUND (level 0) at this point — the creating node
                //   resolves the Z from its terrain (NetworkSpawningSystem), ignoring the 0 sent above.
                // ⭐ CE-1033 S2 (M20) — a 3-D click on a roof or an upper floor carries its height: spawn on THAT level. A 2-D click
                //   (Z = 0) stays on the ground, as before.
                SpawnHeight           = worldPos.Z != 0f && _world != null
                    ? new Fdp.Toolkit.NetworkSpawning.SpawnHeight(Fdp.Toolkit.NetworkSpawning.SpawnHeightMode.OnLevel,
                                                                 (short)Fdp.Toolkit.World.Levels.LevelOf(_world, worldPos))
                    : Fdp.Toolkit.NetworkSpawning.SpawnHeight.OnGround,
                InitialAttributesJson = _initialPropertiesJson,
                RequestId             = Guid.NewGuid(),
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

        private static Rgba32 GetAffiliationColor(ForceId affiliation) =>
            affiliation switch
            {
                ForceId.Friend  => new Rgba32(0, 0, 255, 255),
                ForceId.Hostile => Rgba32.Red,
                ForceId.Neutral => Rgba32.Green,
                _               => Rgba32.White,
            };
    }
}
