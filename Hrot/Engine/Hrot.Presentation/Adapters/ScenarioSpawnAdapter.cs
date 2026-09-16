using System.Numerics;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
// ⭐ `Hrot.IG.Components` is a NAMESPACE IN Hrot.Core, ⛔ not in the Hrot.IG assembly — measured
//   2026-08-27 after I wrongly called this using stale and the compiler corrected me
//   (EditablePolyline + MapOverlayStyle are declared under it in Hrot.Core/Components/Map).
//   ⇒ it carries NO Hrot.IG reference, which is why this adapter can live here at all.
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.Facades;
using Hrot.ScenarioEditor.Gizmos;
using Fdp.Toolkit.Replication;
using Hrot.Core.Network;

namespace Hrot.UI.Common.Adapters
{
    /// <summary>
    /// Implements <see cref="ISpawnController"/> for the offline editor.
    /// Translates spawn requests into <see cref="MapCanvas"/> tool activations:
    /// <list type="bullet">
    ///   <item>Entity placement → <see cref="GlobalGizmoManager"/> (wrapping <see cref="EntityPlacementGizmo"/>).</item>
    ///   <item>Area authoring → <see cref="PointSequenceGizmo"/> registered with <see cref="GlobalGizmoManager"/>.</item>
    ///   <item>Route authoring → <see cref="PointSequenceGizmo"/> registered with <see cref="GlobalGizmoManager"/>.</item>
    /// </list>
    /// No DDS or CycloneDDS references; all dispatch is done through the in-process
    /// <see cref="FdpEventBus"/>.
    /// </summary>
    public sealed class ScenarioSpawnAdapter : ISpawnController
    {
        private readonly FdpEventBus        _bus;
        private readonly JsonAttributeCompiler? _jsonCompiler;
        private readonly ITkbDatabase?      _tkbDb;
        private readonly ScenarioEntityCreationRequestSource? _requestSource;
        private readonly GlobalGizmoManager? _globalGizmoManager;
        private readonly Hrot.ScenarioEditor.Tools.ToolController? _tools;
        private long?                        _activeSequenceId;

        // ⚠⚠ The per-invocation parameters, held between Activate() and the arm body — the same shape
        //    EditorZoneAdapter and MapCommandController use, because ToolActivation takes only an Entity.
        // ⭐⭐ _pendingPropertiesJson is CONSUMED by the arm (read-then-clear), and that is load-bearing:
        //    it is what keeps the toolbar's behaviour IDENTICAL to before this split. 📐 Before, the
        //    toolbar path was StartPlacementModeWithLastType() => StartPlacementMode(last, json: null),
        //    i.e. the toolbar always cleared the properties. If the field merely PERSISTED, a toolbar
        //    press after an ORBAT create would silently re-apply that unit's affiliation JSON.
        private string? _pendingPropertiesJson;
        private string  _pendingStyleJson = string.Empty;

        /// <summary>
        /// The TKB entity type most recently passed to <see cref="StartPlacementMode"/>.
        /// Used by <see cref="StartPlacementModeWithLastType"/> to re-activate placement
        /// without needing a new type selection.
        /// </summary>
        public long LastSelectedTkbType { get; private set; } = TkbEntityTypes.Tank_M1Abrams;

        /// <param name="bus">The local FDP event bus used to route spawn commands.</param>
        /// <param name="jsonCompiler">
        /// The JSON->ECS attribute compiler (from <c>AttributeCompilerFactory.Build</c>).
        /// Used to parse <c>InitialAttributesJson</c> into concrete ECS component objects
        /// so the offline spawning pipeline attaches <see cref="EntityInfo"/> (name,
        /// affiliation) without going through the DDS pipeline.
        /// </param>
        /// <param name="tkbDb">
        /// Optional TKB database used to look up the default entity name for the
        /// baseline <see cref="EntityInfo"/> component. When <c>null</c>, the
        /// name defaults to <c>"New Unit"</c>.
        /// </param>
        /// <param name="tools">
        /// 🔒 <b>The host's arbiter</b> — <c>MapInteraction.Tools</c>. ⛔ Optional so tests and
        /// unconverted hosts still work, but a production caller that HAS it must PASS it.
        ///
        /// <para>⚠⚠ <b>This adapter registers only <c>PlaceArea</c> and <c>PlaceRoute</c> — NOT
        /// <c>Spawn</c>.</b> 📐 <c>Spawn</c> is registered by <c>MapInteractionPack.Build</c> (its arm calls
        /// the pack's <c>StartPlacementMode</c> delegate), and the duplicate-id guard is strict, so
        /// registering it here too would throw at composition. ⇒ placement REACHES the arbiter by
        /// <c>Activate(Spawn)</c>, while area/route bring their own ids.</para>
        /// </param>
        public ScenarioSpawnAdapter(
            FdpEventBus bus,
            JsonAttributeCompiler? jsonCompiler = null,
            ITkbDatabase? tkbDb = null,
            ScenarioEntityCreationRequestSource? requestSource = null,
            GlobalGizmoManager? globalGizmoManager = null,
            Hrot.ScenarioEditor.Tools.ToolController? tools = null)
        {
            _bus          = bus;
            _jsonCompiler = jsonCompiler;
            _tkbDb        = tkbDb;
            _requestSource = requestSource;
            _globalGizmoManager = globalGizmoManager;
            _tools        = tools;

            // ⭐ Registered ONCE, here — ⛔ not per activation (the G4 duplicate-id lesson).
            _tools?.Register(
                new Hrot.ScenarioEditor.Tools.ToolDescriptor(
                    Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceArea,
                    "Draw Area",
                    Hrot.ScenarioEditor.Tools.ToolModality.Modal,
                    Hrot.ScenarioEditor.Tools.ToolArbiter.Global),
                _ => ArmAreaAuthoring());

            _tools?.Register(
                new Hrot.ScenarioEditor.Tools.ToolDescriptor(
                    Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceRoute,
                    "Draw Route",
                    Hrot.ScenarioEditor.Tools.ToolModality.Modal,
                    Hrot.ScenarioEditor.Tools.ToolArbiter.Global),
                _ => ArmRouteAuthoring());
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Creates an <see cref="EntityPlacementGizmo"/> registered with <see cref="GlobalGizmoManager"/>.
        /// The gizmo delegate seeds a baseline <see cref="EntityInfo"/> (so the entity always appears
        /// in the ORBAT tree) then uses the shared <see cref="JsonAttributeCompiler"/> to compile
        /// <c>InitialAttributesJson</c> overrides on top, then publishes the completed
        /// <see cref="SpawnEntityCommand"/> onto the local bus.
        /// </remarks>
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null)
        {
            LastSelectedTkbType    = tkbType;
            _pendingPropertiesJson = initialPropertiesJson;

            // ⭐⭐⭐ UXI-07 step 4a — ACTIVATE through the arbiter instead of arming directly.
            //   🔴 This used to register an EntityPlacementGizmo straight on GlobalGizmoManager, which is
            //   §4.8's bypass: the gizmo took focus while ToolController still believed a tool held it.
            //   📐 Reached from ScenarioOrbatAdapter.CreateUnit and SpawnerPanel.HandleActivatePlacementTool
            //   — both shared surfaces, so both were bypassing on every host that composes this adapter.
            if (_tools != null)
            {
                _tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.Spawn);
                return;
            }

            // ⚠ No arbiter wired ⇒ arm anyway and SAY SO. 🔒 R-137: refusing would cost a capability on a
            //   host that simply has not been converted; silence would hide the bypass this step removes.
            Hrot.ScenarioEditor.Tools.ToolReport.Say(null,
                "entity placement armed WITHOUT an arbiter — ScenarioSpawnAdapter was constructed with no "
              + "ToolController, so this modal cannot displace another (UXI-07 step 4a).");
            ArmPlacement();
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The arm body — and splitting it out is what BREAKS THE RECURSION.</b>
        ///
        /// <para>📐 The loop this avoids, measured: the pack's <c>Spawn</c> arm calls its
        /// <c>StartPlacementMode</c> delegate; the hosts used to point that delegate at
        /// <see cref="StartPlacementModeWithLastType"/>. ⇒ had <see cref="StartPlacementMode"/> simply
        /// called <c>Activate(Spawn)</c>, the chain would be
        /// <c>Activate → arm → StartPlacementModeWithLastType → StartPlacementMode → Activate → …</c>.
        /// ⭐ The hosts now pass THIS body instead, so the cycle cannot form.</para>
        ///
        /// <para>⚠ Reached either through <c>ToolController.Activate</c> (production) or directly when no
        /// arbiter was wired. It CONSUMES <c>_pendingPropertiesJson</c> — see the field's remarks.</para>
        /// </summary>
        public Hrot.ScenarioEditor.Tools.ToolActivationOutcome ArmPlacement()
        {
            if (_globalGizmoManager == null)
                return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Unserviceable;

            var tkbType = LastSelectedTkbType;

            // ⭐⭐ CONSUMED, not merely read — the toolbar path must see null, exactly as it did before.
            var initialPropertiesJson = _pendingPropertiesJson;
            _pendingPropertiesJson    = null;

            var id = GlobalGizmoManager.NewId();
            var gizmo = new EntityPlacementGizmo(
                onEntityCreated: cmd =>
                {
                    cmd.InitialComponents ??= new System.Collections.Generic.List<object>();

                    // Seed a baseline EntityInfo so the entity is guaranteed to appear
                    // in the ORBAT tree even when no Name property is supplied in JSON.
                    string defaultName = "New Unit";
                    if (_tkbDb != null
                     && _tkbDb.TryGetByType(cmd.TkbType, out var template)
                     && !string.IsNullOrWhiteSpace(template.Name))
                    {
                        defaultName = template.Name;
                    }

                    cmd.InitialComponents.Add(new EntityInfo
                    {
                        Name    = new Fdp.Core.FixedString64(defaultName),
                        ForceId = ForceId.Neutral,
                    });

                    // Apply JSON overrides (e.g. Affiliation) on top of the baseline.
                    if (!string.IsNullOrEmpty(cmd.InitialAttributesJson) && _jsonCompiler != null)
                    {
                        var ctx = new ListPatchContext(cmd.InitialComponents);
                        _jsonCompiler.Compile(cmd.InitialAttributesJson, ctx);
                        cmd.InitialComponents = ctx.FlushComponents();
                    }


                    // Preserve explicit unmanaged fields from the tool.
                    // EntityCreationRequest expects all spatial overrides to be inside the generic list.
                    if (cmd.InitialTransform.HasValue)
                    {
                        cmd.InitialComponents.Add(cmd.InitialTransform.Value);
                    }
                    if (cmd.InitialVelocity.HasValue)
                    {
                        cmd.InitialComponents.Add(cmd.InitialVelocity.Value);
                    }


                    if (_requestSource != null)
                    {
                        _requestSource.Enqueue(new EntityCreationRequest
                        {
                            RequestId             = cmd.RequestId,
                            OwnerAppInstanceId    = cmd.OwnerNodeId,
                            TkbType               = cmd.TkbType,
                            DisType               = cmd.DisType,
                            InitialComponents     = cmd.InitialComponents,
                            InitialAttributesJson = cmd.InitialAttributesJson
                        });
                    }
                    else
                    {
                        _bus.PublishManaged(cmd);
                    }
                },
                tkbType:               tkbType,
                initialPropertiesJson: initialPropertiesJson,
                autoPopOnPlace:        true,
                onRemove:              () => _globalGizmoManager!.Unregister(id));
            _globalGizmoManager!.Register(id, gizmo);
            return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed;
        }

        /// <summary>
        /// Activates entity placement using the last entity type that was passed to
        /// <see cref="StartPlacementMode"/>.  Useful when the toolbar's "Place Entity"
        /// button fires without an explicit type selection.
        /// </summary>
        /// <remarks>
        /// ⚠⚠ <b>Hosts must NOT pass this as the pack's <c>StartPlacementMode</c> delegate</b> — pass
        /// <see cref="ArmPlacement"/> instead, or the cycle described there re-forms.
        /// </remarks>
        public void StartPlacementModeWithLastType()
            => StartPlacementMode(LastSelectedTkbType);

        /// <inheritdoc/>
        /// <remarks>
        /// Registers a <see cref="PointSequenceGizmo"/> requiring >= 3 points with
        /// <see cref="GlobalGizmoManager"/>. On completion, emits a
        /// <see cref="SpawnEntityCommand"/> carrying an <see cref="EditablePolyline"/>
        /// and optional <see cref="MapOverlayStyle"/>.
        /// </remarks>
        public void StartAreaAuthoringMode(string styleOverrideJson = "")
        {
            _pendingStyleJson = styleOverrideJson;

            // ⭐⭐⭐ UXI-07 step 4a — see StartPlacementMode. ⭐ Area/route bring their OWN tool ids
            //   (PlaceArea/PlaceRoute) because nothing else registers them, and §4.9 ④ measured that
            //   neither has the recursion Spawn has: no host wires them into the pack.
            if (_tools != null)
            {
                _tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceArea);
                return;
            }

            ArmAreaAuthoring();
        }

        /// <summary>The area arm body — reached through <c>Activate</c>, or directly with no arbiter.</summary>
        public Hrot.ScenarioEditor.Tools.ToolActivationOutcome ArmAreaAuthoring()
        {
            if (_globalGizmoManager == null)
                return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Unserviceable;

            var styleOverrideJson = _pendingStyleJson;

            if (_activeSequenceId.HasValue)
            {
                _globalGizmoManager!.Unregister(_activeSequenceId.Value);
                _activeSequenceId = null;
            }

            var styleJson = styleOverrideJson;
            var areaId = GlobalGizmoManager.NewId();
            var gizmo = new PointSequenceGizmo(
                onFinish: points =>
                {
                    if (points.Length < 3)
                        return;

                    // Build entity-relative geometry (centroid-based anchor).
                    float sumX = 0f, sumY = 0f;
                    for (int i = 0; i < points.Length; i++) { sumX += points[i].X; sumY += points[i].Y; }
                    var anchor = new Vector2(sumX / points.Length, sumY / points.Length);

                    var relPoints = new System.Collections.Generic.List<Vector2>(points.Length);
                    for (int i = 0; i < points.Length; i++)
                        relPoints.Add(points[i] - anchor);

                    var polyline = new EditablePolyline { Points = relPoints };
                    var style    = MapOverlayStyle.FromJson(styleJson);

                    var cmd = new SpawnEntityCommand
                    {
                        NetworkId         = 0,
                        TkbType           = TkbEntityTypes.TacGraphic_Area,
                        OwnerNodeId       = 0,
                        InitType          = ReliableInitType.AllPeers,
                        RequestId         = System.Guid.NewGuid(),
                        InitialTransform  = new SimTransform { Position = new System.Numerics.Vector3(anchor.X, anchor.Y, 0f) },
                        InitialComponents = new System.Collections.Generic.List<object> { polyline, style },
                    };

                    _bus.PublishManaged(cmd);
                },
                onRemove: () => { _globalGizmoManager!.Unregister(areaId); _activeSequenceId = null; });

            _activeSequenceId = areaId;
            _globalGizmoManager!.Register(areaId, gizmo);
            return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed;
        }

        /// <remarks>
        /// Registers a <see cref="PointSequenceGizmo"/> requiring >= 2 points with
        /// <see cref="GlobalGizmoManager"/>. On completion, emits a
        /// <see cref="SpawnEntityCommand"/> carrying a <see cref="RoutePlan"/>.
        /// </remarks>
        public void StartRouteAuthoringMode()
        {
            // ⭐⭐⭐ UXI-07 step 4a — see StartPlacementMode.
            if (_tools != null)
            {
                _tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceRoute);
                return;
            }

            ArmRouteAuthoring();
        }

        /// <summary>The route arm body — reached through <c>Activate</c>, or directly with no arbiter.</summary>
        public Hrot.ScenarioEditor.Tools.ToolActivationOutcome ArmRouteAuthoring()
        {
            if (_globalGizmoManager == null)
                return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Unserviceable;

            if (_activeSequenceId.HasValue)
            {
                _globalGizmoManager!.Unregister(_activeSequenceId.Value);
                _activeSequenceId = null;
            }

            var routeId = GlobalGizmoManager.NewId();
            var gizmo = new PointSequenceGizmo(
                onFinish: points =>
                {
                    if (points.Length < 2)
                        return;

                    var routePlan = new RoutePlan { IsLoop = false };
                    routePlan.Mutate(wps =>
                    {
                        for (int i = 0; i < points.Length; i++)
                        {
                            wps.Add(new RouteWaypoint
                            {
                                Position    = new System.Numerics.Vector3(points[i].X, points[i].Y, 0f),
                                TargetSpeed = 0f,
                            });
                        }
                    });

                    var anchor = routePlan.Waypoints.Count > 0
                        ? routePlan.Waypoints[0].Position
                        : default;

                    var cmd = new SpawnEntityCommand
                    {
                        NetworkId         = 0,
                        TkbType           = TkbEntityTypes.TacGraphic_Route,
                        OwnerNodeId       = 0,
                        InitType          = ReliableInitType.AllPeers,
                        RequestId         = System.Guid.NewGuid(),
                        InitialTransform  = new SimTransform { Position = anchor },
                        InitialComponents = new System.Collections.Generic.List<object> { routePlan },
                    };

                    _bus.PublishManaged(cmd);
                },
                onRemove: () => { _globalGizmoManager!.Unregister(routeId); _activeSequenceId = null; });

            _activeSequenceId = routeId;
            _globalGizmoManager!.Register(routeId, gizmo);
            return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed;
        }
    }
}

