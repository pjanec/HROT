using System.Numerics;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.Facades;
using Hrot.ScenarioEditor.Gizmos;
using Fdp.Toolkit.Replication;
using Hrot.Core.Network;

namespace Hrot.Editor.Adapters
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
    public sealed class EditorSpawnAdapter : ISpawnController
    {
        private readonly FdpEventBus        _bus;
        private readonly JsonAttributeCompiler? _jsonCompiler;
        private readonly ITkbDatabase?      _tkbDb;
        private readonly ScenarioEntityCreationRequestSource? _requestSource;
        private readonly GlobalGizmoManager? _globalGizmoManager;
        private long?                        _activeSequenceId;

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
        public EditorSpawnAdapter(
            FdpEventBus bus,
            JsonAttributeCompiler? jsonCompiler = null,
            ITkbDatabase? tkbDb = null,
            ScenarioEntityCreationRequestSource? requestSource = null,
            GlobalGizmoManager? globalGizmoManager = null)
        {
            _bus          = bus;
            _jsonCompiler = jsonCompiler;
            _tkbDb        = tkbDb;
            _requestSource = requestSource;
            _globalGizmoManager = globalGizmoManager;
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
            LastSelectedTkbType = tkbType;

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
        }

        /// <summary>
        /// Activates entity placement using the last entity type that was passed to
        /// <see cref="StartPlacementMode"/>.  Useful when the toolbar's "Place Entity"
        /// button fires without an explicit type selection.
        /// </summary>
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
            if (_activeSequenceId.HasValue)
            {
                _globalGizmoManager!.Unregister(_activeSequenceId.Value);
                _activeSequenceId = null;
            }

            var styleJson = styleOverrideJson;
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
                onRemove: () => { _activeSequenceId = null; });

            _activeSequenceId = GlobalGizmoManager.NewId();
            _globalGizmoManager!.Register(_activeSequenceId.Value, gizmo);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Registers a <see cref="PointSequenceGizmo"/> requiring >= 2 points with
        /// <see cref="GlobalGizmoManager"/>. On completion, emits a
        /// <see cref="SpawnEntityCommand"/> carrying a <see cref="RoutePlan"/>.
        /// </remarks>
        public void StartRouteAuthoringMode()
        {
            if (_activeSequenceId.HasValue)
            {
                _globalGizmoManager!.Unregister(_activeSequenceId.Value);
                _activeSequenceId = null;
            }

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
                onRemove: () => { _activeSequenceId = null; });

            _activeSequenceId = GlobalGizmoManager.NewId();
            _globalGizmoManager!.Register(_activeSequenceId.Value, gizmo);
        }
    }
}

