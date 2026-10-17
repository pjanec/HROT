using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.IG.Gizmos;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.IG.Tests.Eqs
{
    // T-VIS1 through T-VIS5: unit tests for EQS visualizer classes (TASK-EQS-022).
    public sealed class EqsVisualizersTests
    {
        // T-VIS1: EqsSensorGizmo must carry the [GizmoProjector] attribute keyed on EqsSensor.
        // ⭐ CE-3143 — and NOT on SimTransform: a unit's sensors are child entities with no SimTransform (EqsChildSensor.Ensure), so
        //   the old (SimTransform, EqsSensor) key — which this rail used to pin — matched none of them and the gizmo drew nothing.
        [Fact]
        public void EqsSensorGizmo_HasGizmoProjectorAttribute_KeyedOnTheSensorAlone()
        {
            var attr = typeof(EqsSensorGizmo)
                .GetCustomAttribute<GizmoProjectorAttribute>();
            Assert.NotNull(attr);
            Assert.Contains(typeof(EqsSensor), attr.RequiredComponents);
            Assert.DoesNotContain(typeof(SimTransform), attr.RequiredComponents);
        }

        // T-VIS2: EqsCognitiveBufferRenderer must carry [ImGuiRenderer] targeting
        // EqsCognitiveBuffer so the registry auto-discovers it.
        [Fact]
        public void EqsCognitiveBufferRenderer_HasImGuiRendererAttribute_ForCognitiveBuffer()
        {
            var attrs = typeof(EqsCognitiveBufferRenderer)
                .GetCustomAttributes<ImGuiRendererAttribute>();
            Assert.Contains(attrs, a => a.TargetType == typeof(EqsCognitiveBuffer));
        }

        // T-VIS3: GetSummary returns a "Ready" string containing the candidate count
        // when LastUpdateTick > 0 (IsReady == true).
        [Fact]
        public void EqsCognitiveBufferRenderer_GetSummary_ReadyBuffer_ReturnsCorrectString()
        {
            var renderer = new EqsCognitiveBufferRenderer();
            // IsReady is a computed property: LastUpdateTick > 0.
            var buffer   = new EqsCognitiveBuffer { Count = 3, LastUpdateTick = 1 };
            var summary  = renderer.GetSummary(buffer);
            Assert.NotNull(summary);
            Assert.Contains("3",     summary);
            Assert.Contains("Ready", summary);
        }

        // T-VIS4: GetSummary returns an "Awaiting" string when the buffer is not ready
        // (LastUpdateTick == 0).
        [Fact]
        public void EqsCognitiveBufferRenderer_GetSummary_NotReady_ReturnsAwaitingString()
        {
            var renderer = new EqsCognitiveBufferRenderer();
            // LastUpdateTick == 0 means IsReady == false.
            var buffer   = new EqsCognitiveBuffer { Count = 0, LastUpdateTick = 0 };
            var summary  = renderer.GetSummary(buffer);
            Assert.NotNull(summary);
            Assert.Contains("Awaiting", summary);
        }

        // T-VIS5: The three EqsGizmoSettings key strings must produce distinct FNV-1a hashes
        // so settings lookups never collide.
        [Fact]
        public void EqsGizmoSettings_KeyHashes_AreDistinct()
        {
            uint h1 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowRadius);
            uint h2 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowCandidates);
            uint h3 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowScores);
            uint h4 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowVerdict);
            Assert.Equal(4, new[] { h1, h2, h3, h4 }.Distinct().Count());
        }

        private sealed class OneTemplate : IEqsTemplateRegistry
        {
            private readonly EqsQueryTemplate _t;
            public OneTemplate(EqsQueryTemplate t) => _t = t;
            public bool TryGetTemplate(uint id, out EqsQueryTemplate t) { t = _t; return id == _t.BlueprintId; }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3143</c> — THE VERDICT: a CHILD cover sensor of a unit, a parked car between the unit and its threat. The gizmo draws
        /// from the unit (the child has no position) and runs the template's own filters (<see cref="EqsFilters.Run"/>): the two points
        /// behind the car (south, away from the threat) are GREEN — hidden — and the four on its other sides RED — seen. The car's 4.5 m
        /// sides carry 2 points each (rounded up, CE-3143). 🔴 Red-proof: key the projector on SimTransform again (nothing draws), or
        /// drop the vehicle boxes from <c>EqsTerrainSight.Sight</c> (all six red).
        /// </summary>
        [Fact]
        public void EqsSensorGizmo_DrawsTheFiltersVerdict_ForAChildCoverSensor_GreenBehindTheCar()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<SimVelocity>();
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            w.RegisterComponent<EqsSensor>();
            w.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            w.RegisterComponent<Fdp.Toolkit.Terrain.StaticObstacle>();
            w.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            w.RegisterComponent<global::CarKinem.Core.VehicleState>();
            w.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            w.RegisterManagedComponent<ICoverProvider>();
            w.RegisterManagedComponent<IEqsTemplateRegistry>();
            var flat = new Fdp.Toolkit.Terrain.TerrainWorld { BoundsMin = new Vector2(0, 0), BoundsMax = new Vector2(60, 60) };
            w.SetSingletonManaged(flat);
            w.SetSingletonManaged<ICoverProvider>(TerrainCoverProvider.Build(flat));
            var template = new EqsQueryTemplate
            {
                BlueprintId = 77, Generator = new CoverPointsGenerator(),
                FilterCheap = new IEqsTest[] { new CheapLineOfSightTest() }, MaxCandidates = 64,
            };
            w.SetSingletonManaged<IEqsTemplateRegistry>(new OneTemplate(template));

            Entity At(float x, float y) { var e = w.CreateEntity(); w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity }); return e; }
            var unit = At(30, 22);
            var threat = At(30, 45);
            var car = At(30, 30);
            w.AddComponent(car, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 2.4f, Height = 1.5f, CollisionLayer = 1 });
            w.AddComponent(car, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.PersonalCar, Length = 4.5f, Width = 1.8f });
            w.AddComponent(car, new global::CarKinem.Core.VehicleState { Speed = 0f });
            var sensor = w.CreateEntity();
            w.AddComponent(sensor, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = unit, InstanceId = 1 });
            w.AddComponent(sensor, new EqsSensor { BlueprintId = 77, SearchRadius = 15f, ContextSlot1 = threat });

            var draw = new DebugPrimitiveBuffer();
            new EqsSensorGizmo(new GizmoSettingsRegistry()).Draw(w, sensor, draw);

            var dots = draw.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Sphere).ToArray();
            var green = dots.Where(d => d.Color.Equals(EqsSensorGizmo.KeptColor)).ToArray();
            var red   = dots.Where(d => d.Color.Equals(EqsSensorGizmo.RejectedColor)).ToArray();
            Assert.Equal(2, green.Length);
            Assert.Equal(4, red.Length);
            Assert.All(green, d => Assert.True(d.SphereCenter.Y < 29f, $"a kept point {d.SphereCenter} is not behind the car"));
        }

        /// <summary>
        /// ⭐⭐ CE-1160 (gizmos-1 §11.11) — the verdict reruns the AI's filters (sight against the terrain) on the DRAW thread, so it is
        /// CACHED per sensor: drawn every frame from the cache, recomputed only when <see cref="EqsSensorGizmo.VerdictRefreshSeconds"/> has
        /// passed (and no sooner than 10× its own cost). 🔒 User, 2026-10-16: selecting a unit froze the 3-D map — "1 frame drawn in few
        /// seconds", with these very dots appearing. 🔴 Red-proof: recompute every frame (the count rises on the second draw).
        /// </summary>
        [Fact]
        public void CE1160_TheVerdict_IsDrawnEveryFrame_ButRecomputedOnlyOnItsBudget()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<EqsSensor>();
            w.RegisterManagedComponent<IEqsTemplateRegistry>();
            var template = new EqsQueryTemplate { BlueprintId = 5, Generator = new Ring(), MaxCandidates = 8 };
            w.SetSingletonManaged<IEqsTemplateRegistry>(new OneTemplate(template));
            var unit = w.CreateEntity();
            w.AddComponent(unit, new SimTransform { Position = new Vector3(10, 10, 0), Rotation = Quaternion.Identity });
            w.AddComponent(unit, new EqsSensor { BlueprintId = 5, SearchRadius = 5f });

            long now = 1_000;
            var gizmo = new EqsSensorGizmo(new GizmoSettingsRegistry()) { Clock = () => now };
            int Dots() { var d = new DebugPrimitiveBuffer(); gizmo.Draw(w, unit, d); return d.GetFrame().ToArray().Count(p => p.Shape == DebugPrimitiveShape.Sphere && p.Color.Equals(EqsSensorGizmo.KeptColor)); }

            Assert.Equal(4, Dots());
            Assert.Equal(4, Dots());                                   // the next frame: drawn from the cache
            Assert.Equal(1, gizmo.VerdictsComputed);
            now += (long)(EqsSensorGizmo.VerdictRefreshSeconds * System.Diagnostics.Stopwatch.Frequency) + 1;
            Assert.Equal(4, Dots());
            Assert.Equal(2, gizmo.VerdictsComputed);                    // its budget passed: recomputed
        }

        /// <summary>Four points round the observer, nothing filters them.</summary>
        private sealed class Ring : IEqsGenerator
        {
            public int Generate(Entity observer, ref EqsSensor sensor, Fdp.ModuleHost.Abstractions.ISimulationView view, System.Span<EqsResult> candidates)
            {
                for (int i = 0; i < 4; i++) candidates[i] = new EqsResult { PositionX = 10 + i, PositionY = 10, PositionZ = 0 };
                return 4;
            }
        }
    }
}
