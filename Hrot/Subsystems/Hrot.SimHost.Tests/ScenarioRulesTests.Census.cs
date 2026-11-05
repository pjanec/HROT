using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication;
using Hrot.AI.Behaviors.Brains.Objectives;
using Hrot.CGF.Configuration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public sealed partial class ScenarioRulesTests
    {
        /// <summary>
        /// ⭐ CE-2416 — the objective census tests only the bodies inside the area's bounding circle (the shared frame grid) instead of every
        /// positioned entity per objective per side (~30 % of the brain tick on the CE-2408 stress world); its answer — the count AND the men,
        /// in entity order (the capture event names them) — must be the scan's: two sides and neutrals, men down, circles and turned
        /// rectangles, a man created within the frame, men moved a little within it. 🔴 Red-proof: the circle taken without the bounding
        /// radius (the centre only) ⇒ the counts differ.
        /// </summary>
        [Fact]
        public void CE2416_TheObjectiveCensus_OverTheGrid_CountsAndNamesTheMenTheScanDid()
        {
            using var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            CognitiveComponentRegistry.RegisterAll(w);
            var rng = new Random(2416);
            var men = new List<Entity>();
            Entity Man(float x, float y, ForceId side, bool down = false)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
                w.AddComponent(e, new EntityInfo { ForceId = side });
                w.AddComponent(e, new Health { Current = down ? 0f : 100f, Max = 100f });
                men.Add(e);
                return e;
            }
            for (int i = 0; i < 600; i++)
                Man(rng.NextSingle() * 1200f - 600f, rng.NextSingle() * 1200f - 600f, (ForceId)(i % 3), down: i % 13 == 0);
            var areas = new List<AreaLink>();
            for (int a = 0; a < 20; a++)
            {
                var c = new WorldPoint(rng.NextSingle() * 1000f - 500f, rng.NextSingle() * 1000f - 500f, 0f);
                areas.Add(a % 2 == 0 ? AreaLink.Circle(c, 20f + rng.NextSingle() * 150f)
                                     : AreaLink.Rectangle(c, 30f + rng.NextSingle() * 200f, 30f + rng.NextSingle() * 200f, rng.NextSingle() * 360f));
            }
            void Compare(string when)
            {
                foreach (var area in areas)
                    foreach (var side in new[] { ForceId.Friend, ForceId.Hostile })
                    {
                        var byGrid = new List<Entity>();
                        int n = ObjectiveCensus.MenOfSideInside(w, side, in area, byGrid);
                        ObjectiveCensus.GridOn = false;
                        var byScan = new List<Entity>();
                        int m;
                        try { m = ObjectiveCensus.MenOfSideInside(w, side, in area, byScan); }
                        finally { ObjectiveCensus.GridOn = true; }
                        Assert.Equal(m, n);
                        Assert.Equal(byScan, byGrid);
                    }
            }
            w.Tick();
            Compare("a fresh frame");
            Man(areas[0].Center.X, areas[0].Center.Y, ForceId.Friend);              // created within the frame, inside an area
            foreach (var e in men)                                                    // moved a little within the frame
                w.GetComponentRW<SimTransform>(e).Position += new Vector3(rng.NextSingle() * 6f - 3f, rng.NextSingle() * 6f - 3f, 0f);
            Compare("changed within the frame");
            int total = 0;
            foreach (var area in areas) total += ObjectiveCensus.MenOfSideInside(w, ForceId.Friend, in area);
            Assert.True(total > 20, $"the premise: men inside the areas ({total})");
        }
    }
}
