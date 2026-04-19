using System.Numerics;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Tools;
using Hrot.Editor.Tools;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.Facades;
using Hrot.ScenarioEditor.Tools;
using Fdp.Toolkit.Replication;

namespace Hrot.Editor.Adapters
{
    /// <summary>
    /// Implements <see cref="ISpawnController"/> for the offline editor.
    /// Translates spawn requests into <see cref="MapCanvas"/> tool activations:
    /// <list type="bullet">
    ///   <item>Entity placement â†’ <see cref="CreationTool"/> pushed onto the canvas.</item>
    ///   <item>Area authoring â†’ <see cref="PointSequenceTool"/> pushed onto the canvas.</item>
    ///   <item>Route authoring â†’ <see cref="PointSequenceTool"/> pushed onto the canvas.</item>
    /// </list>
    /// No DDS or CycloneDDS references; all dispatch is done through the in-process
    /// <see cref="FdpEventBus"/>.
    /// </summary>
    public sealed class EditorSpawnAdapter : ISpawnController
    {
        private readonly MapCanvas          _canvas;
        private readonly FdpEventBus        _bus;
        private readonly JsonAttributeCompiler? _jsonCompiler;
        private readonly ITkbDatabase?      _tkbDb;

        /// <summary>
        /// The TKB entity type most recently passed to <see cref="StartPlacementMode"/>.
        /// Used by <see cref="StartPlacementModeWithLastType"/> to re-activate placement
        /// without needing a new type selection.
        /// </summary>
        public long LastSelectedTkbType { get; private set; } = TkbEntityTypes.Tank_M1Abrams;

        /// <param name="canvas">The map canvas that hosts the tool stack.</param>
        /// <param name="bus">The local FDP event bus used to route spawn commands.</param>
        /// <param name="jsonCompiler">
        /// The JSONâ†’ECS attribute compiler (from <c>AttributeCompilerFactory.Build</c>).
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
            MapCanvas canvas,
            FdpEventBus bus,
            JsonAttributeCompiler? jsonCompiler = null,
            ITkbDatabase? tkbDb = null)
        {
            _canvas       = canvas;
            _bus          = bus;
            _jsonCompiler = jsonCompiler;
            _tkbDb        = tkbDb;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Creates a <see cref="CreationTool"/> whose delegate seeds a baseline
        /// <see cref="EntityInfo"/> (so the entity always appears in the ORBAT tree)
        /// then uses the shared <see cref="JsonAttributeCompiler"/> to compile
        /// <c>InitialAttributesJson</c> overrides on top, then publishes the completed
        /// <see cref="SpawnEntityCommand"/> onto the local bus.
        /// </remarks>
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null)
        {
            LastSelectedTkbType = tkbType;

            var tool = new CreationTool(
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

                    _bus.PublishManaged(cmd);
                },
                tkbType:               tkbType,
                initialPropertiesJson: initialPropertiesJson,
                autoPopOnPlace:        true);

            _canvas.PushTool(tool);
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
        /// Pushes a <see cref="PointSequenceTool"/> requiring â‰Ą 3 points.
        /// On completion, emits a <see cref="SpawnEntityCommand"/> carrying an
        /// <see cref="EditablePolyline"/> and optional <see cref="MapOverlayStyle"/>.
        /// </remarks>
        public void StartAreaAuthoringMode(string styleOverrideJson = "")
        {
            if (_canvas.ActiveTool is PointSequenceTool)
                _canvas.PopTool();

            var styleJson = styleOverrideJson;
            var tool = new PointSequenceTool(points =>
            {
                if (points.Length < 3)
                {
                    _canvas.PopTool();
                    return;
                }

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
                _canvas.PopTool();
            });

            _canvas.PushTool(tool);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Pushes a <see cref="PointSequenceTool"/> requiring â‰Ą 2 points.
        /// On completion, emits a <see cref="SpawnEntityCommand"/> carrying a
        /// <see cref="RoutePlan"/>.
        /// </remarks>
        public void StartRouteAuthoringMode()
        {
            if (_canvas.ActiveTool is PointSequenceTool)
                _canvas.PopTool();

            var tool = new PointSequenceTool(points =>
            {
                if (points.Length < 2)
                {
                    _canvas.PopTool();
                    return;
                }

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
                _canvas.PopTool();
            });

            _canvas.PushTool(tool);
        }
    }
}

