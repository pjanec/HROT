using System;
using System.Numerics;
using CarKinem.Convoy;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Spatial;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using Xunit;
using Xunit.Abstractions;

namespace CarKinem.Tests.Convoy
{
    /// <summary>
    /// ⭐⭐ <c>CE-9001</c> (S-2; 📄 docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md §3 C-4, C-6, §5) — the road convoy's COLUMN-FOLLOWER: a lead drives an
    /// L-shaped route under its own driving, three followers drive its TRAIL. The user's rule "the convoy never splits" as numbers: along the path every gap
    /// stays between the minimum and 1.5 × the spacing, nobody passes its predecessor, the followers stay ON the lead's path round the corner (they do not
    /// cut it), they all stop with the lead, and a slow tail makes the lead slow down. 0 B per steady frame for the two convoy systems (R-2084).
    /// 🔴 Red-proofs (compiling mutations, measured): the follower's speed without the gap term ⇒ the gap rail red; the trail search without its window
    /// (from the oldest point) ⇒ still green on this route (documented, not a proof); the lead without its cap ⇒ the stretch rail red.
    /// </summary>
    public sealed class ConvoyColumnTests
    {
        private readonly ITestOutputHelper _out;
        public ConvoyColumnTests(ITestOutputHelper output) => _out = output;

        private const float Dt = 1f / 30f;
        private static readonly Vector2[] Route = { new(100f, 0f), new(100f, 120f) };

        private sealed class World
        {
            public readonly EntityRepository Repo = new();
            public readonly SpatialHashSystem Hash = new();
            public readonly ConvoyLeadSystem Lead = new();
            public readonly ConvoyFollowSystem Follow = new();
            public readonly CarKinematicsSystem Kin = new(new TrajectoryPoolManager());
            public Entity LeadE;
            public readonly Entity[] F = new Entity[3];
            public int Leg;

            public World(float tailTop = 20f, float spread = 15f)
            {
                var r = Repo;
                r.RegisterComponent<VehicleState>(); r.RegisterComponent<SimTransform>(); r.RegisterComponent<SimVelocity>(); r.RegisterComponent<VehicleParams>();
                r.RegisterComponent<NavState>(); r.RegisterComponent<SpatialGridData>(); r.RegisterComponent<FormationTarget>();
                r.RegisterComponent<FormationController>(); r.RegisterComponent<FormationFollower>(); r.RegisterComponent<ConvoyTrail>(); r.RegisterComponent<ConvoyProgress>();
                r.SetSingletonUnmanaged(new GlobalTime { DeltaTime = Dt, TimeScale = 1.0f });
                LeadE = Vehicle(new Vector2(0f, 0f), 20f);
                r.AddComponent(LeadE, new FormationController { Type = FormationType.Column, Params = new FormationParams { Spacing = 15f } });
                ref var nav = ref r.GetComponentRW<NavState>(LeadE);
                nav.Mode = KinematicsMode.Direct; nav.TargetSpeed = 10f; nav.ArrivalRadius = 2f; nav.FinalDestination = new WorldPoint(Route[0].X, Route[0].Y, 0);
                for (int i = 0; i < 3; i++)
                {
                    F[i] = Vehicle(new Vector2(-spread * (i + 1), 0f), i == 2 ? tailTop : 20f);
                    r.AddComponent(F[i], new FormationFollower { Leader = LeadE, SlotIndex = (ushort)(i + 1), IsInFormation = 1, State = FormationMemberState.Rejoining });
                }
            }

            private Entity Vehicle(Vector2 at, float top)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new VehicleState());
                Repo.AddComponent(e, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.Identity });
                Repo.SetAuthority<SimTransform>(e, true);
                Repo.AddComponent(e, new SimVelocity());
                Repo.AddComponent(e, new VehicleParams
                {
                    WheelBase = 3.5f, MaxSpeedFwd = top, MaxAccel = 2.5f, MaxDecel = 6f, MaxSteerAngle = (Angle)0.6f,
                    LookaheadTimeMin = 1f, LookaheadTimeMax = 4f, AccelGain = 2f, AvoidanceRadius = 3f, Length = 6f, Width = 2.5f,
                });
                Repo.AddComponent(e, new NavState { Mode = KinematicsMode.None });
                return e;
            }

            public void Step(bool leadStops = false)
            {
                var p = Pos(LeadE);
                ref var nav = ref Repo.GetComponentRW<NavState>(LeadE);
                if (leadStops) { nav.TargetSpeed = 0f; }
                else if (Leg < Route.Length - 1 && Vector2.Distance(p, Route[Leg]) < 6f) { Leg++; nav.FinalDestination = new WorldPoint(Route[Leg].X, Route[Leg].Y, 0); nav.HasArrived = 0; }
                Hash.Execute(Repo, Dt);
                Lead.Execute(Repo, Dt);
                Follow.Execute(Repo, Dt);
                Kin.Execute(Repo, Dt);
            }

            public Vector2 Pos(Entity e) { var p = Repo.GetComponentRO<SimTransform>(e).Position; return new Vector2(p.X, p.Y); }
            public double Arc(Entity e) => e == LeadE ? Repo.GetComponentRO<ConvoyTrail>(LeadE).RankArc(0) : Repo.GetComponentRO<ConvoyProgress>(e).ArcS;
            public float Speed(Entity e) => Repo.GetComponentRO<VehicleState>(e).Speed;
        }

        /// <summary>Distance from <paramref name="p"/> to the route polyline (0,0)→(100,0)→(100,120) — the lead's own path within a metre or two.</summary>
        private static float OffRoute(Vector2 p)
        {
            static float Seg(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f); return Vector2.Distance(p, a + ab * t); }
            return MathF.Min(MathF.Min(Seg(p, new(-60, 0), new(0, 0)), Seg(p, new(0, 0), Route[0])), Seg(p, Route[0], Route[1]));
        }

        /// <summary>A follower's distance to the lead's ACTUAL path (its trail) — the rule is "drive where the lead drove", not the authored route.</summary>
        private static float OffTrail(World w, Entity e)
        {
            ref readonly var t = ref w.Repo.GetComponentRO<ConvoyTrail>(w.LeadE);
            if (w.Arc(e) < t.OldestS) return 0f;   // still behind the trail's start: driving onto it
            return t.Project(w.Pos(e), t.OldestS - 1, t.NewestS + 1, out _, out float d) ? d : float.MaxValue;
        }

        [Fact]
        public void TheColumn_NeverSplits_NeverOvertakes_AndDrivesTheLeadsPathRoundTheCorner()
        {
            var w = new World();
            float minGap = float.MaxValue, maxGap = 0f, worstOff = 0f;
            for (int i = 0; i < 30 * 40; i++)
            {
                w.Step();
                if (i < 30 * 8) continue;   // the start: the column rolls out
                double prev = w.Arc(w.LeadE);
                for (int k = 0; k < 3; k++)
                {
                    double a = w.Arc(w.F[k]);
                    float g = (float)(prev - a);
                    minGap = MathF.Min(minGap, g); maxGap = MathF.Max(maxGap, g);
                    worstOff = MathF.Max(worstOff, OffTrail(w, w.F[k]));
                    prev = a;
                }
            }
            _out.WriteLine($"gaps along the path {minGap:F1}…{maxGap:F1} m; worst follower off the lead's own path {worstOff:F2} m; lead at {w.Pos(w.LeadE)}");
            Assert.True(w.Leg == 1 && w.Pos(w.LeadE).Y > 40f, "the lead turned the corner and drove on");
            Assert.True(minGap >= 7f - 0.5f, $"a follower closed under the minimum gap ({minGap:F1} m) — overtaking territory");
            Assert.True(maxGap <= 15f * 1.5f, $"the column split: a gap of {maxGap:F1} m along the path");
            Assert.True(worstOff < 2.5f, $"a follower left the lead's path by {worstOff:F2} m (cut the corner)");
        }

        /// <summary>A column formed too loose (25 m apart instead of 15) closes up to its spacing along the path while it drives — the gap controller, not just
        /// the predecessor's speed fed forward. 🔴 Red-proof: drop the gap term ⇒ the extra 10 m per gap never closes ⇒ red.</summary>
        [Fact]
        public void ALooseColumn_ClosesUpToItsSpacing_WhileDriving()
        {
            var w = new World(spread: 25f);
            for (int i = 0; i < 30 * 25; i++) w.Step();
            double prev = w.Arc(w.LeadE);
            for (int k = 0; k < 3; k++)
            {
                double a = w.Arc(w.F[k]);
                _out.WriteLine($"follower {k + 1}: {prev - a:F1} m behind its predecessor along the path");
                Assert.InRange(prev - a, 15f - 3f, 15f + 3f);
                prev = a;
            }
        }

        [Fact]
        public void TheColumn_StopsWithTheLead_AndKeepsItsMinimumGap()
        {
            var w = new World();
            for (int i = 0; i < 30 * 12; i++) w.Step();
            Assert.True(w.Speed(w.F[2]) > 3f, "anti-vacuity: the tail was moving");
            int stoppedAt = -1;
            for (int i = 0; i < 30 * 10; i++)
            {
                w.Step(leadStops: true);
                bool all = MathF.Abs(w.Speed(w.LeadE)) < 0.2f && MathF.Abs(w.Speed(w.F[0])) < 0.2f && MathF.Abs(w.Speed(w.F[1])) < 0.2f && MathF.Abs(w.Speed(w.F[2])) < 0.2f;
                if (all && stoppedAt < 0) stoppedAt = i;
            }
            _out.WriteLine($"all four stopped {stoppedAt / 30f:F1} s after the lead was told to stop");
            Assert.InRange(stoppedAt, 0, 30 * 8);
            double prev = w.Arc(w.LeadE);
            for (int k = 0; k < 3; k++) { double a = w.Arc(w.F[k]); Assert.True(prev - a >= 7f - 0.5f, $"follower {k + 1} stopped {prev - a:F1} m behind its predecessor"); prev = a; }
        }

        [Fact]
        public void ASlowTail_MakesTheLeadSlowDown_SoTheColumnStaysTogether()
        {
            var w = new World(tailTop: 4f);   // the last vehicle tops out at 4 m/s
            float leadTop = 0f, worstLag = 0f, capMin = 1f;
            for (int i = 0; i < 30 * 30; i++)
            {
                w.Step();
                if (i < 30 * 10) continue;
                leadTop = MathF.Max(leadTop, w.Speed(w.LeadE));
                ref readonly var t = ref w.Repo.GetComponentRO<ConvoyTrail>(w.LeadE);
                worstLag = MathF.Max(worstLag, t.TailLag); capMin = MathF.Min(capMin, t.SpeedCapFactor);
            }
            _out.WriteLine($"lead top speed {leadTop:F1} m/s (wants 10), worst tail lag {worstLag:F1} m, lowest cap {capMin:F2}");
            Assert.True(capMin < 1f, "the cap engaged");
            Assert.True(leadTop < 7f, $"the lead kept its own speed ({leadTop:F1} m/s) and left the tail behind");
            Assert.True(worstLag < 3f * 15f, $"the tail fell {worstLag:F1} m behind its place — the column split");
        }

        /// <summary>📌 Found by <c>cv-offroad-join-leave</c> (the column knotted on a lead that backed out of an overshot corner): the trail records only
        /// FORWARD motion, and a follower stops when its predecessor is physically within the minimum gap even if the arc says it is far. 🔴 Red-proofs:
        /// drop the forward test ⇒ the reversing lead's points are appended ⇒ red; drop the metres guard ⇒ the follower drives into the backed-up lead ⇒ red.</summary>
        [Fact]
        public void ALeadThatBacksUp_LeavesNoTrailBehind_AndItsFollowerStopsShortOfIt()
        {
            var t = new ConvoyTrail();
            for (int x = 0; x <= 20; x += 2) t.Record(new Vector2(x, 0));
            int count = t.Count;
            t.Record(new Vector2(16, 0)); t.Record(new Vector2(12, 0));   // backing up
            Assert.Equal(count, t.Count);
            t.Record(new Vector2(23, 0));                                   // forward again
            Assert.Equal(count + 1, t.Count);

            var w = new World();
            for (int i = 0; i < 30 * 6; i++) w.Step();
            // the lead is put back (as a reversing hull ends up) right in front of its first follower
            var f = w.Pos(w.F[0]);
            w.Repo.GetComponentRW<SimTransform>(w.LeadE).Position = new Vector3(f + new Vector2(14f, 0f), 0f);
            ref var nav = ref w.Repo.GetComponentRW<NavState>(w.LeadE); nav.TargetSpeed = 0f;
            for (int i = 0; i < 30 * 3; i++) w.Step(leadStops: true);
            Assert.True(Vector2.Distance(w.Pos(w.F[0]), w.Pos(w.LeadE)) >= 4f, $"the follower drove into its backed-up lead ({Vector2.Distance(w.Pos(w.F[0]), w.Pos(w.LeadE)):F1} m)");
        }

        [Fact]
        public void TheConvoySystems_AllocateNothing_InASteadyFrame()
        {
            var w = new World();
            for (int i = 0; i < 30 * 6; i++) w.Step();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 60; i++) { w.Lead.Execute(w.Repo, Dt); w.Follow.Execute(w.Repo, Dt); }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, bytes);
        }

        [Fact]
        public void AVehicleThatLeavesTheConvoy_GoesBackToNoMode()
        {
            var w = new World();
            for (int i = 0; i < 30 * 5; i++) w.Step();
            Assert.Equal(KinematicsMode.Column, w.Repo.GetComponentRO<NavState>(w.F[1]).Mode);
            w.Repo.RemoveComponent<FormationFollower>(w.F[1]);
            w.Step();
            Assert.Equal(KinematicsMode.None, w.Repo.GetComponentRO<NavState>(w.F[1]).Mode);
            Assert.False(w.Repo.HasComponent<ConvoyProgress>(w.F[1]));
        }
    }
}
