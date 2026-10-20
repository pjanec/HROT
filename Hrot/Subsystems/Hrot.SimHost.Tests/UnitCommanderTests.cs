using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.Map.Common;
using Hrot.IG.Components;
using Hrot.Map.Definitions.Tkb;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2125</c> (R-272) — a unit's commander is a BODY-LESS entity: a TKB composite type stamps <see cref="UnitEntity"/>,
    /// and an EQS area search never finds it. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.
    /// </summary>
    public sealed class UnitCommanderTests
    {
        private static (EntityRepository world, ITkbDatabase tkb) World()
        {
            var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);
            return (world, HrotEnvironment.CreateTkb());
        }

        /// <summary>⭐ C-1 — the infantry-squad composite (302) is a unit with the default 6 s command lull; a soldier type is not.</summary>
        [Fact]
        public void CE2125_ACompositeType_IsAUnit_ASoldierIsNot()
        {
            var (world, tkb) = World();
            Assert.True(tkb.TryGetByType(TkbEntityTypes.Unit_InfantrySquad, out var squad));
            Assert.True(tkb.TryGetByType(TkbEntityTypes.Infantry_Rifleman, out var rifleman));
            var unit = world.CreateEntity();
            var soldier = world.CreateEntity();
            new CompositeUnitTkbTranslator().Inject(world, unit, squad!);
            new CompositeUnitTkbTranslator().Inject(world, soldier, rifleman!);

            Assert.True(world.HasComponent<UnitEntity>(unit));
            Assert.Equal(6f, world.GetComponentRO<UnitEntity>(unit).CommandDisruptionSeconds);
            Assert.False(world.HasComponent<UnitEntity>(soldier));
            Assert.Contains(Hrot.Core.Tkb.TkbTranslatorSet.Base(), t => t is CompositeUnitTkbTranslator);
        }

        /// <summary>⭐ <c>CE-2324</c> (Entity Interactions §7.2c) — every MAP GRAPHIC type (route, area, fire line, weather region, stone
        /// source) is stamped <see cref="Fdp.Toolkit.Perception.MapGraphic"/> and so hidden; a soldier, a door and an obstacle are not
        /// (a door and an obstacle are physical: they block sight and fire). 🔴 Red before: no type carried the mark — the insurgents of
        /// <c>uc-urban-battle</c> shot at the route "Avenue E north".</summary>
        [Fact]
        public void CE2324_EveryMapGraphicType_IsNotABody_ASoldierADoorAnObstacleAre()
        {
            var (world, tkb) = World();
            var translator = new MapGraphicTkbTranslator();
            bool Stamped(long type)
            {
                Assert.True(tkb.TryGetByType(type, out var t), $"type {type} has a template");
                var e = world.CreateEntity();
                translator.Inject(world, e, t!);
                Assert.Equal(world.HasComponent<Fdp.Toolkit.Perception.MapGraphic>(e), Fdp.Toolkit.Perception.PhysicalPresence.IsHidden(world, e));
                return world.HasComponent<Fdp.Toolkit.Perception.MapGraphic>(e);
            }
            foreach (var graphic in new[] { TkbEntityTypes.TacGraphic_Route, TkbEntityTypes.TacGraphic_Area, TkbEntityTypes.TacGraphic_FireLine,
                                            TkbEntityTypes.WeatherRegion, TkbEntityTypes.StoneSource })
                Assert.True(Stamped(graphic), $"type {graphic} is a map graphic");
            foreach (var body in new[] { TkbEntityTypes.Infantry_Rifleman, TkbEntityTypes.Door, TkbEntityTypes.Obstacle_ConcreteBlock })
                Assert.False(Stamped(body), $"type {body} is a body");
            Assert.Contains(Hrot.Core.Tkb.TkbTranslatorSet.Base(), t => t is MapGraphicTkbTranslator);
        }

        /// <summary>⭐ <c>CE-2324</c> — an EQS area search over a polygon holding a soldier and a ROUTE finds only the soldier (the same
        /// door the perception grid and the collider hash go through).</summary>
        [Fact]
        public void CE2324_AnAreaSearch_NeverFindsARoute()
        {
            var (world, _) = World();
            var area = world.CreateEntity();
            world.AddComponent(area, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.SetManagedComponent(area, new EditablePolyline { Points = new List<Vector2> { new(-50, -50), new(50, -50), new(50, 50), new(-50, 50) } });
            var soldier = world.CreateEntity();
            world.AddComponent(soldier, new SimTransform { Position = new Vector3(5, 5, 0), Rotation = Quaternion.Identity });
            var route = world.CreateEntity();
            world.AddComponent(route, new SimTransform { Position = new Vector3(6, 6, 0), Rotation = Quaternion.Identity });
            world.AddComponent(route, new Fdp.Toolkit.Perception.MapGraphic());

            var sensor = new EqsSensor { ContextSlot1 = area };
            var results = new EqsResult[16];
            int n = new EntitiesInAreaGenerator().Generate(Entity.Null, ref sensor, world, results);

            var found = new List<long>();
            for (int i = 0; i < n; i++) found.Add(results[i].EntityId);
            Assert.Contains((long)soldier.PackedValue, found);
            Assert.DoesNotContain((long)route.PackedValue, found);
        }

        /// <summary>⭐ C-2 — an EQS area search over a polygon holding a soldier and a unit's commander finds only the soldier.
        /// ✅ Red-proof: drop <c>ExcludeHidden</c> from the generator ⇒ red.</summary>
        [Fact]
        public void CE2125_AnAreaSearch_NeverFindsAUnitsCommander()
        {
            var (world, _) = World();
            var area = world.CreateEntity();
            world.AddComponent(area, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.SetManagedComponent(area, new EditablePolyline { Points = new List<Vector2> { new(-50, -50), new(50, -50), new(50, 50), new(-50, 50) } });
            var soldier = world.CreateEntity();
            world.AddComponent(soldier, new SimTransform { Position = new Vector3(5, 5, 0), Rotation = Quaternion.Identity });
            var unit = world.CreateEntity();
            world.AddComponent(unit, new SimTransform { Position = new Vector3(6, 6, 0), Rotation = Quaternion.Identity });
            world.AddComponent(unit, new UnitEntity { CommandDisruptionSeconds = 6f });

            var sensor = new EqsSensor { ContextSlot1 = area };
            var results = new EqsResult[16];
            int n = new EntitiesInAreaGenerator().Generate(Entity.Null, ref sensor, world, results);

            var found = new List<long>();
            for (int i = 0; i < n; i++) found.Add(results[i].EntityId);
            Assert.Contains((long)soldier.PackedValue, found);
            Assert.DoesNotContain((long)unit.PackedValue, found);
        }

        /// <summary>⭐ <c>CE-3110</c> I-2c (R-271) — the same area search never finds a passenger inside an ENCLOSED vehicle, and does find one
        /// riding an open cabin (the one hidden rule, <c>PhysicalPresence.IsHidden</c>). 📄 DESIGN_Entity_Interactions §7.2a.</summary>
        [Fact]
        public void CE3110_I2c_AnAreaSearch_NeverFindsAnEnclosedPassenger()
        {
            var (world, _) = World();
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>())
                world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var area = world.CreateEntity();
            world.AddComponent(area, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.SetManagedComponent(area, new EditablePolyline { Points = new List<Vector2> { new(-50, -50), new(50, -50), new(50, 50), new(-50, 50) } });
            Entity At(float x) { var e = world.CreateEntity(); world.AddComponent(e, new SimTransform { Position = new Vector3(x, 5, 0), Rotation = Quaternion.Identity }); return e; }
            var enclosed = At(5);
            world.AddComponent(enclosed, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { Exposed = 0 });
            var exposed = At(6);
            world.AddComponent(exposed, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { Exposed = 1 });

            var sensor = new EqsSensor { ContextSlot1 = area };
            var results = new EqsResult[16];
            int n = new EntitiesInAreaGenerator().Generate(Entity.Null, ref sensor, world, results);

            var found = new List<long>();
            for (int i = 0; i < n; i++) found.Add(results[i].EntityId);
            Assert.Contains((long)exposed.PackedValue, found);
            Assert.DoesNotContain((long)enclosed.PackedValue, found);
        }
    }
}
