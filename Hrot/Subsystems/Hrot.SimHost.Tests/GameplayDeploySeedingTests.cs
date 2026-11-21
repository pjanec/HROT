using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Determinism;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Common;
using Hrot.Gameplay;
using Hrot.Map.Common.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-11027</c> S12-4 (Q114 J; <c>docs/DESIGN_Player_Join.md</c> §12.4) — a role's spawn POINT is seeded from the run seed, so one scenario puts its men at the same points every run (the feature scenarios of DD-023 D-16 are regression tests).
    /// The server runs over the editor's in-process hub, with NO injected <see cref="Random"/> (the pack passes none): the seed is the world's recorded <c>RunSeed</c> singleton, as the other deterministic systems read it.
    /// 🔴 Mutation: <c>DeployProcessorSystem.SeededFromRun</c> returning <c>new Random()</c> ⇒ the same-seed rails red. In the <c>SimRng</c> collection: <c>SimRngRails</c> flips <c>SimRng.Deterministic</c>, a process-wide static this reads.
    /// </summary>
    [Collection("SimRng statics")]
    public sealed class GameplayDeploySeedingTests
    {
        private const int Server = 5, Role = 300;

        /// <summary>One server world: a big Friend spawn area and a role; <paramref name="n"/> men are asked for, one per frame, from distinct stations; returns where each was placed. <paramref name="epochAfter"/> = a world load between two batches.</summary>
        private static List<Vector3> Spawns(uint seed, int n, int loads = 0)
        {
            using var w = new EntityRepository();
            var map = new NetworkEntityMap();
            w.RegisterComponent<Playable>(); w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<PossessedBy>(); w.RegisterComponent<Health>(); w.RegisterEvent<NodeDeparted>();
            w.RegisterComponent<DeployRole>(); w.RegisterComponent<SpawnArea>(); w.RegisterComponent<GameplaySettings>();
            w.RegisterComponent<EntityInfo>(); w.RegisterComponent<TkbIdentity>(); w.RegisterComponent<Locomotion>();
            w.RegisterComponent<SimTransform>(); w.RegisterManagedComponent<EditablePolyline>();
            w.SetSingleton(new GlobalTime { TotalTime = 100 });
            w.SetSingleton(new Fdp.Toolkit.Aggregate.RunSeed { Value = seed });

            var area = w.CreateEntity();
            w.AddComponent(area, new SimTransform { Position = new Vector3(100f, 200f, 0f), Rotation = Quaternion.Identity });
            w.AddComponent(area, new SpawnArea { Side = ForceId.Friend, FacingDeg = 0f });
            w.SetManagedComponent(area, new EditablePolyline { Points = new[] { new Vector2(-400, -400), new Vector2(400, -400), new Vector2(400, 400), new Vector2(-400, 400) } });
            var role = w.CreateEntity();
            w.AddComponent(role, new NetworkIdentity { Value = Role });
            w.AddComponent(role, new DeployRole { Side = ForceId.Friend, PersonTkbType = (Fdp.Core.TkbTypeId)(2002), Name = new FixedString32("Rifleman"), MaxCount = 200 });
            map.Register(Role, role);

            var hub = LocalGameplayHub.Of(w);
            var calls = new List<Vector3>();
            long ms = 1_000;
            var proc = new DeployProcessorSystem(hub.ServerEnd(), map, Server, election: null, nowMs: () => ms,
                spawner: (tkb, at, comps) => { calls.Add(at.Position); return 900 + calls.Count; });   // ⭐ no rng: the pack passes none
            var client = hub.ClientEnd();
            void Frame() { ms += 1_100; w.Bus.SwapBuffers(); proc.Execute(w, 0.016f); }
            Frame();

            var all = new List<Vector3>();
            int station = 20;
            for (int load = 0; load <= loads; load++)
            {
                calls.Clear();
                for (int i = 0; i < n; i++)
                {
                    client.RequestWriter!.Write(new DeployRequestTopic { RequestId = Guid.NewGuid().ToString("N"), StationNodeId = station++, Side = (byte)ForceId.Friend, UnitNetId = Role });
                    for (int f = 0; f < 20 && calls.Count <= i; f++) Frame();
                    Assert.True(calls.Count > i, "the spawn request " + i);
                }
                all.AddRange(calls);
                if (load < loads) { WorldEpoch.Advance(w); Frame(); }   // a scenario load: the next world's choice starts from ITS seed again
            }
            return all;
        }

        [Fact]
        public void TheSameSeedTwice_GivesTheSameSpawnPoints_AnotherSeedOthers()
        {
            var a = Spawns(seed: 12345, n: 4);
            var b = Spawns(seed: 12345, n: 4);
            Assert.Equal(a, b);
            Assert.Equal(4, new HashSet<Vector3>(a).Count);   // the points are not one fixed spot
            Assert.NotEqual(a, Spawns(seed: 777, n: 4));
        }

        [Fact]
        public void ALoad_StartsTheNextWorldsChoiceFromItsSeedAgain()
        {
            var twice = Spawns(seed: 4242, n: 3, loads: 1);   // batch 1, a load, batch 2 — the same scenario twice in one process
            Assert.Equal(6, twice.Count);
            Assert.Equal(twice.GetRange(0, 3), twice.GetRange(3, 3));
        }
    }
}
