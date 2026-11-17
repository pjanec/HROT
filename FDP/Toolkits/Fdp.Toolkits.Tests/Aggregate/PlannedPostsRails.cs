using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8303</c> (D-P3 DP3-C, SV-5, R-4286; 📄 docs/designs/aggregate-units/DESIGN_DP3_Planned_Posts.md §6) — <see cref="PlannedPosts"/>: id 138
    /// (agg-behaviors' block), unmanaged and ≤ 1024 B (RV-11), recorded (a split replays on its planned posts), NoScenario, and its reader
    /// <see cref="PlannedPosts.TryGet"/> (what the manager's Materialise calls, DP3-D ①).
    /// </summary>
    public sealed class PlannedPostsRails
    {
        /// <summary>✅ Red-proof: a <c>string</c> field (a managed one) ⇒ IsReferenceOrContainsReferences ⇒ red; the id moved ⇒ red.</summary>
        [Fact]
        public void PlannedPosts_Is138_Unmanaged_AndFitsOneCommandBufferRecord_AndIsNeverSaved()
        {
            Assert.Equal(138, GlobalComponentIds.PlannedPosts);
            int size = Unsafe.SizeOf<PlannedPosts>();
            Assert.True(size <= 1024, $"PlannedPosts is {size} B — over EntityCommandBuffer's 1024 B (RV-11)");
            Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PlannedPosts>());
            var policy = (DataPolicyAttribute?)Attribute.GetCustomAttribute(typeof(PlannedPosts), typeof(DataPolicyAttribute));
            Assert.NotNull(policy);
            Assert.True((policy!.Policy & DataPolicy.NoScenario) != 0, "process state: never saved with a scenario");
            Assert.True((policy.Policy & DataPolicy.NoReplay) == 0, "recorded");
            Assert.True(PlannedPosts.Capacity >= UnitSlotTable.CapacityFor(0), "one entry per squad slot");
        }

        /// <summary>⭐ DP3-D ① — the reader: a posted entry comes back with its point, watch and flags; one without a post, one past
        /// <see cref="PlannedPosts.Count"/>, or any before <see cref="PlannedPosts.Ready"/> does not. ✅ Red-proof: the Ready guard dropped ⇒ red.</summary>
        [Fact]
        public void TryGet_GivesAPostedEntry_AndNothingElse()
        {
            var p = new PlannedPosts { Count = 3, Stage = PlannedPosts.StagePlanning };
            p.Set(0, new Vector3(1, 2, 3), Vector3.Zero, PlannedPosts.PostedFlag | PlannedPosts.WindowFlag);
            p.Set(2, new Vector3(7, 8, 9), new Vector3(0, 50, 0), PlannedPosts.PostedFlag | PlannedPosts.RoofFlag | PlannedPosts.GunFlag);
            Assert.False(p.TryGet(0, out _), "not before Ready");
            p.Ready = 1; p.Stage = PlannedPosts.StageReady;
            Assert.True(p.TryGet(0, out var a));
            Assert.Equal((new Vector3(1, 2, 3), true, false), (a.Point, a.Window, a.Roof));
            Assert.False(p.TryGet(1, out _), "no post ⇒ the fallback point");
            Assert.True(p.TryGet(2, out var c));
            Assert.Equal((new Vector3(7, 8, 9), new Vector3(0, 50, 0), true, true), (c.Point, c.Watch, c.Roof, c.Gun));
            Assert.False(p.TryGet(3, out _));
            Assert.False(p.TryGet(-1, out _));
        }

        /// <summary>✅ Red-proof: <see cref="PlannedPosts"/> marked <c>NoReplay</c> ⇒ the replayed unit has no posts ⇒ red.</summary>
        [Fact]
        public void ARecording_GivesBackThePlannedPosts()
        {
            using var harness = new Fdp.Toolkit.ReplayBrowser.Support.FdpRecordingHarness();
            var repo = harness.Repository;
            repo.RegisterComponent<AggregateUnit>(); repo.RegisterComponent<PlannedPosts>();
            var unit = repo.CreateEntity();
            repo.AddComponent(unit, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Splitting = SplitStage.Planning });
            repo.AddComponent(unit, new PlannedPosts { Stage = PlannedPosts.StagePlanning, PlannedAt = (SimInstant)12.5 });
            harness.Tick().RecordKeyframe(100_000L);
            var ready = new PlannedPosts { Stage = PlannedPosts.StageReady, Ready = 1, Count = 2, PlannedAt = (SimInstant)12.5 };
            ready.Set(0, new Vector3(4, 5, 6), Vector3.Zero, PlannedPosts.PostedFlag | PlannedPosts.WindowFlag | PlannedPosts.FrameFlag);
            ready.Set(1, new Vector3(-4, 5, 9), Vector3.Zero, PlannedPosts.PostedFlag | PlannedPosts.RoofFlag);
            repo.SetComponent(unit, ready);
            harness.Tick().RecordDelta(200_000L);
            harness.BuildToTempFile(out var path);

            using var sandbox = new EntityRepository();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessPosition>();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessVelocity>();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessTransform>();
            sandbox.RegisterComponent<Fdp.Toolkit.ReplayBrowser.Support.HarnessEntityInfo>();
            sandbox.RegisterComponent<AggregateUnit>(); sandbox.RegisterComponent<PlannedPosts>();
            using var playback = new Fdp.Core.FlightRecorder.PlaybackController(path);
            Assert.True(playback.StepForward(sandbox));
            Assert.Equal(PlannedPosts.StagePlanning, sandbox.GetComponentRO<PlannedPosts>(unit).Stage);
            Assert.Equal(12.5, (double)sandbox.GetComponentRO<PlannedPosts>(unit).PlannedAt);
            Assert.True(playback.StepForward(sandbox));
            var back = sandbox.GetComponentRO<PlannedPosts>(unit);
            Assert.Equal((byte)2, back.Count);
            Assert.True(back.TryGet(0, out var a));
            Assert.Equal((new Vector3(4, 5, 6), true, true), (a.Point, a.Window, a.Frame));
            Assert.True(back.TryGet(1, out var b));
            Assert.Equal((new Vector3(-4, 5, 9), true), (b.Point, b.Roof));
        }
    }
}
