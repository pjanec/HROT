using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Terrain;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B2 (Minefields MF-13 ②③, MR-7; docs/designs/minefields/DESIGN_Minefields.md §11) — the SHIPPED <c>ReconMinefield</c>
    /// through the real ingress and brain tick, on a 40 × 40 m anti-personnel field (4 m spacing ⇒ every 10 m knowledge cell holds mines).
    /// </summary>
    public sealed unsafe class ReconMinefieldBehaviorTests
    {
        private const int FieldNet = 7301;

        private static Entity Field(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<Minefield>()) repo.RegisterComponent<Minefield>();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            if (!repo.TryGetTable(typeof(Hrot.Map.Common.Components.EditablePolyline), out _)) repo.RegisterManagedComponent<Hrot.Map.Common.Components.EditablePolyline>();
            if (!repo.Bus.IsRegistered<MinefieldCellsSwept>()) repo.Bus.Register<MinefieldCellsSwept>();
            if (!repo.Bus.IsRegistered<MinefieldMarked>()) repo.Bus.Register<MinefieldMarked>();
            var f = repo.CreateEntity();
            repo.AddComponent(f, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            repo.AddComponent(f, new Minefield { Type = MineType.AntiPersonnel, SpacingM = 4f, OrientationDeg = 0f });
            repo.AddComponent(f, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = FieldNet });
            repo.SetManagedComponent(f, new Hrot.Map.Common.Components.EditablePolyline
            {
                Points = new[] { new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 40f), new Vector2(0f, 40f) },
            });
            return f;
        }

        private static Vector3 Destination(EntityRepository repo, Entity e)
        {
            var ch = repo.GetComponentRO<LocomotionChannel>(e);
            return ch.ActiveAction == Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo ? ((Fdp.Toolkit.Navigation.MoveToParams*)ch.Params)->Destination : new Vector3(float.NaN);
        }

        /// <summary>
        /// ⭐⭐ MR-7 — from 25 m west of the field he sweeps toward its centre; the first mined cell starts the trace, the mine kept on his LEFT; he walks
        /// round the whole field on cells he swept clear — ⭐ NEVER stepping into a mined cell — back to the trace's start, and MARKS the field for his
        /// side (once). Every swept cell is reported with the truth (the 12 edge cells mined; the 4 inside never reached). ✅ Red-proofs: the never-enter guard dropped
        /// (he walks into the probed cell whatever it holds) ⇒ red; the left-hand order reversed ⇒ the trace never closes ⇒ red; the closure test
        /// dropped ⇒ no mark ⇒ red.
        /// </summary>
        [Fact]
        public void CE4624_MS_B2_TheEngineer_TracesTheFieldsEdge_OnSweptClearCellsOnly_AndMarksIt()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            var field = Field(repo);
            repo.GetComponentRW<SimTransform>(w.Man).Position = new Vector3(-25f, 15f, 0f);
            if (!repo.HasComponent<EntityInfo>(w.Man)) repo.AddComponent(w.Man, new EntityInfo { ForceId = ForceId.Friend });
            else repo.GetComponentRW<EntityInfo>(w.Man).ForceId = ForceId.Friend;
            w.Start(BehaviorNames.ReconMinefield, "{\"FieldNetId\":" + FieldNet + ",\"At\":[20,20,0],\"Pace\":5}");
            var swept = new Dictionary<(int, int), byte>();
            int marks = 0;
            byte side = 0;
            var stood = new List<Vector3>();
            for (int i = 0; i < 6000 && w.Finished == null; i++)
            {
                w.Tick();
                foreach (var e in repo.Bus.Read<MinefieldCellsSwept>())
                    for (int k = 0; k < e.Count; k++) swept[(e.Col[k], e.Row[k])] = e.Found[k];
                foreach (var m in repo.Bus.Read<MinefieldMarked>()) { marks++; side = m.SidePlus1; Assert.Equal(field, m.Field); }
                var to = Destination(repo, w.Man);
                if (!float.IsNaN(to.X)) { repo.GetComponentRW<SimTransform>(w.Man).Position = to; stood.Add(to); }   // he walks where he is sent
            }
            Assert.Equal(NodeStatus.Success, w.Finished);
            Assert.Equal(1, marks);                                                        // closed ⇒ marked, once
            Assert.Equal((byte)((int)ForceId.Friend + 1), side);
            foreach (var p in stood)
                Assert.False(p.X > 0.5f && p.X < 39.5f && p.Y > 0.5f && p.Y < 39.5f, $"he never stands inside the mined field: {p}");
            Assert.Equal(12, swept.Count);                                                 // the field's EDGE ring of the 4 × 4 grid was probed (the inner 4 never) …
            foreach (var kv in swept) Assert.Equal((byte)MineCellKnowledge.KnownMined, kv.Value);   // … and found mined
            Assert.True(stood.Count >= 16, $"he walked round it: {stood.Count} steps");
        }
    }
}
