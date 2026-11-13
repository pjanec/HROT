using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Scenario.Tests;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Federation.Tests
{
    /// <summary>
    /// ⭐ CE-4495 R-1 (docs/DESIGN_Threaded_Review_Source.md §1.1, §2) — the review read model: what the builder publishes equals what the panels read from the manager today,
    /// for Merged and a part; an unchanged state returns the SAME model and allocates nothing; a step / an offset / a new request moves it.
    /// </summary>
    public sealed class ReviewModelBuilderTests : IDisposable
    {
        private readonly string _dir;
        private readonly ScenarioSerializer _serializer;
        private readonly Guid _exercise = Guid.NewGuid();

        public ReviewModelBuilderTests()
        {
            ComponentTypeRegistry.Clear();
            _dir = Path.Combine(Path.GetTempPath(), $"ReviewModel_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
            using var prime = new EntityRepository();
            prime.RegisterComponent<NetworkIdentity>();
            prime.RegisterComponent<NetworkAuthority>();
            prime.RegisterComponent<DummyPosition>();
            _serializer = new ScenarioSerializerBuilder("TestSubsystem").Build();
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        /// <summary>One file for a node: unit <c>net</c> at x = node + step, a keyframe per step at wall tick (step+1) × 1 000 000.</summary>
        private string Record(int nodeId, int net, bool owns, int steps)
        {
            var path = Path.Combine(_dir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
            var meta = new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)_exercise, NodeId = (Fdp.Core.NodeId)nodeId };
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<DummyPosition>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new NetworkIdentity { Value = net });
            repo.AddComponent(e, new DummyPosition { X = nodeId });
            if (owns) { repo.SetAuthority<NetworkIdentity>(e, true); repo.SetAuthority<DummyPosition>(e, true); }
            using (var rec = new AsyncRecorder(path, meta))
                for (int i = 0; i < steps; i++)
                {
                    repo.GetComponentRW<DummyPosition>(e).X = nodeId * 100 + i;
                    rec.CaptureKeyframe(repo, (i + 1) * 1_000_000L, blocking: true, eventBus: repo.Bus);
                }
            File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
            return path;
        }

        /// <summary>A host over a real manager: the facts a test sets, the manager's operations.</summary>
        private sealed class Host : IReviewHost
        {
            public FederatedReplayManager? Manager { get; set; }
            public ReviewHostFacts Facts { get; set; }
            public string NameOf(int node) => "Node " + node;
            public string RoleKeyOf(int node) => "node/other";
            public string PartLabel(int node) => "Node " + node + "'s part";
            public void SelectMerged() { }
            public void SelectPart(int node) { }
            public void SeekFrame(int frame) { }
            public void StepFrame(int delta) { if (delta > 0) Manager!.StepForwardAll(); else Manager!.StepBackwardAll(); }
            public void SetNodeOffsetTicks(int node, long t) => Manager!.SetNodeOffset(node, t);
            public void SetBaseWallTicks(long t) => Manager!.SetBaseWallTicks(t);
            public void SetLocalEntitiesShown(int node, bool s) => Manager!.SetLocalEntitiesShown(node, s);
            public void SetLocalEntitiesProvider(int node) => Manager!.SetLocalEntitiesProvider(node);
        }

        private static ReviewHostFacts Facts(ReviewKind kind, FederatedReplayManager m, int partNode = 0)
        {
            var pb = m.Contexts[m.LocalEntitiesProviderNodeId];
            return new ReviewHostFacts(kind, true, false, null, m.ExerciseId.Value, partNode, pb.CurrentFrame, pb.Playback?.TotalFrames ?? 0, false,
                kind == ReviewKind.Merged ? "Merged · 2 participants" : "Part: Node " + partNode, null, null, 0);
        }

        private ReplicaViews Views() => new(new OwnershipSource(), _serializer);

        [Fact]
        public void TheMergedModel_IsWhatThePanelsReadFromTheManager()
        {
            using var m = FederatedReplayManager.LoadGroup(new[] { Record(1, 42, owns: true, 3), Record(2, 42, owns: false, 3) });
            m.SetBaseWallTicks(1_000_000L);
            m.SetNodeOffset(2, 5 * TimeSpan.TicksPerMillisecond);
            m.SetLocalEntitiesShown(2, false);
            var host = new Host { Manager = m };
            host.Facts = Facts(ReviewKind.Merged, m);
            var views = Views();
            var request = new ReviewViewRequest(1, new[] { (42L, typeof(DummyPosition)) }, Array.Empty<ReviewWatchKey>());

            var model = new ReviewModelBuilder(views).Build(host, request, null);

            Assert.True(model.IsMerged);
            Assert.Equal(m.NodeCount, model.Participants.Count);
            for (int i = 0; i < m.NodeCount; i++)
            {
                int node = m.NodeIdAt(i);
                var p = model.Participant(node)!;
                Assert.Equal(m.NodeOffsets.TryGetValue(node, out long o) ? o : 0L, p.OffsetTicks);   // what FederationPanel's slider reads
                Assert.Equal(m.IsLocalEntitiesShown(node), p.LocalShown);                            // what its locals checkbox reads
                Assert.Equal(m.ContextAt(i).CurrentFdpPath, p.Path);                                 // what the search / watch scans read
                Assert.Equal("Node " + node, p.Name);
            }
            Assert.Equal((int)m.LocalEntitiesProviderNodeId, model.PrimaryNode);
            Assert.True(model.HasNonZeroOffset);
            Assert.Equal(m.BaseWallTicks, model.BaseWallTicks);
            Assert.Equal(ReplicaViews.StateKey(m), model.ReplicaStateKey);
            Assert.True(model.TryByNode(42, typeof(DummyPosition), out var byNode));
            var direct = views.Compute(m, 42, typeof(DummyPosition));                               // what ByNodeSection computes today
            Assert.NotNull(byNode);
            Assert.Equal(direct!.Rows, byNode!.Rows);
            Assert.Equal(direct.OwnerNode, byNode.OwnerNode);
        }

        [Fact]
        public void APartModel_CarriesThePartsFacts_AndNoReplicaViews()
        {
            using var m = FederatedReplayManager.LoadGroup(new[] { Record(1, 42, owns: true, 3), Record(2, 42, owns: false, 3) });
            m.SetBaseWallTicks(1_000_000L);
            var host = new Host { Manager = m };
            host.Facts = Facts(ReviewKind.Part, m, partNode: 2);
            var request = new ReviewViewRequest(1, new[] { (42L, typeof(DummyPosition)) }, Array.Empty<ReviewWatchKey>());

            var model = new ReviewModelBuilder(Views()).Build(host, request, null);

            Assert.Equal(ReviewKind.Part, model.Kind);
            Assert.Equal(2, model.Facts.PartNode);
            Assert.Equal("Part: Node 2", model.Facts.StatusText);
            Assert.False(model.IsMerged);
            Assert.True(model.TryByNode(42, typeof(DummyPosition), out var byNode));
            Assert.Null(byNode);   // By node is the merged view's lens only
            Assert.Equal(2, model.Participants.Count);
        }

        [Fact]
        public void AnUnchangedState_ReturnsTheSameModel_AndAllocatesNothing()
        {
            using var m = FederatedReplayManager.LoadGroup(new[] { Record(1, 42, owns: true, 3), Record(2, 42, owns: false, 3) });
            m.SetBaseWallTicks(1_000_000L);
            var host = new Host { Manager = m };
            host.Facts = Facts(ReviewKind.Merged, m);
            var builder = new ReviewModelBuilder(Views());
            var request = new ReviewViewRequest(1, new[] { (42L, typeof(DummyPosition)) }, Array.Empty<ReviewWatchKey>());
            var first = builder.Build(host, request, null);
            for (int i = 0; i < 3; i++) builder.Build(host, request, first);   // warm (JIT)

            long before = GC.GetAllocatedBytesForCurrentThread();
            ReviewReadModel again = first;
            for (int i = 0; i < 100; i++) again = builder.Build(host, request, again);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Same(first, again);
            Assert.Equal(1, builder.Builds);
            Assert.Equal(0, allocated);
        }

        [Fact]
        public void AStep_AnOffset_AndANewRequest_EachMoveTheModel()
        {
            using var m = FederatedReplayManager.LoadGroup(new[] { Record(1, 42, owns: true, 3), Record(2, 42, owns: false, 3) });
            m.SetBaseWallTicks(1_000_000L);
            var host = new Host { Manager = m };
            host.Facts = Facts(ReviewKind.Merged, m);
            var builder = new ReviewModelBuilder(Views());
            var request = new ReviewViewRequest(1, new[] { (42L, typeof(DummyPosition)) }, Array.Empty<ReviewWatchKey>());
            var a = builder.Build(host, request, null);

            host.StepFrame(+1);
            host.Facts = Facts(ReviewKind.Merged, m);
            var b = builder.Build(host, request, a);
            Assert.NotSame(a, b);
            Assert.NotEqual(a.ReplicaStateKey, b.ReplicaStateKey);

            host.SetNodeOffsetTicks(2, 7 * TimeSpan.TicksPerMillisecond);
            var c = builder.Build(host, request, b);
            Assert.NotSame(b, c);
            Assert.Equal(7d, c.Participant(2)!.OffsetMs, 3);

            var wider = new ReviewViewRequest(2, new[] { (42L, typeof(DummyPosition)), (42L, typeof(NetworkIdentity)) }, Array.Empty<ReviewWatchKey>());
            var d = builder.Build(host, wider, c);
            Assert.NotSame(c, d);
            Assert.True(d.TryByNode(42, typeof(NetworkIdentity), out _));
        }
    }
}
