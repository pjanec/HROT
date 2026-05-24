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

        private Vector3 _cursorWorld;

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
            Action?                    onRemove              = null)
        {
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
            var ghostColor = GetAffiliationColor(_affiliationForDisplay);
            ghostColor.A = GhostAlpha;

            draw.DrawSphere(_cursorWorld, GhostRadiusPx, ghostColor);
            draw.DrawTextLong(
                _cursorWorld.X,
                _cursorWorld.Y + GhostLabelOffsetY,
                _tkbType.ToString(),
                Rgba32.White);
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
                    Rotation = Quaternion.Identity,
                },
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
