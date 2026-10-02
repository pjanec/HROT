using System;
using System.IO;
using System.Threading;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.ExCon.Observer;
using Xunit;

namespace Hrot.ExCon.Tests.Observer
{
    /// <summary>
    /// CE-280 — proves ExCon's LOAD-side handler restores the observer state from the foreign slice routed
    /// back to this node on load (§4c/§6b T-C, ExCon part). The round-trip is exercised through the REAL
    /// save handler (writes the foreign observer slice) → the file placed where the orchestrator's foreign
    /// routing lands it (per-node <c>scenarios/&lt;name&gt;/foreign/node_&lt;id&gt;.json</c>) → the load handler,
    /// invoked with the same <c>PrefetchFiles</c> intent the cluster fans out. The unique per-process marker
    /// is the value-level proof: a fresh state's marker becomes the SAVED one only if the file round-tripped.
    /// </summary>
    public sealed class ExConScenarioLoadHandlerTests : IDisposable
    {
        private const int NodeId = 500;
        private readonly string _name = "test-excon-load-" + Guid.NewGuid().ToString("N");

        public void Dispose()
        {
            try
            {
                var dir = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), _name);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch { /* best effort */ }
        }

        /// <summary>Writes ExCon's foreign observer slice via the real save handler, then places it where the
        /// orchestrator's foreign-routing lands it for this node.</summary>
        private void StageForeignSlice(ExConObserverState saved)
        {
            var saveHandler = new ExConScenarioSaveHandler(saved, NodeId);
            var saveIntent = new ExecuteNodeOpIntent
            {
                Operation     = NodeOpType.SerializeLocal,
                TransactionId = Guid.NewGuid(),
                DomainPayload = new ScenarioSaveHandlerPayload(_name),
            };
            saveHandler.PrepareAsync(saveIntent, CancellationToken.None).GetAwaiter().GetResult();

            // The save writes <node-scenarios>/<name>/scenario.json; the merge tags it foreign and the
            // load-side routing delivers it to <node-scenarios>/<name>/foreign/node_<id>.json.
            var scenarioDir = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), _name);
            var sliceFile   = Path.Combine(scenarioDir, "scenario.json");
            var foreignDir  = Path.Combine(scenarioDir, "foreign");
            Directory.CreateDirectory(foreignDir);
            File.Copy(sliceFile, Path.Combine(foreignDir, $"node_{NodeId}.json"), overwrite: true);
            File.Delete(sliceFile);   // ExCon has no ECS scenario.json — only the foreign slice returns.
        }

        private static ExecuteNodeOpIntent PrefetchIntent(string scenarioId) => new()
        {
            Operation     = NodeOpType.PrefetchFiles,
            TransactionId = Guid.NewGuid(),
            DomainPayload = new PrefetchHandlerPayload(scenarioId),
        };

        [Fact]
        public void PrepareAsync_RestoresObserverState_FromForeignSlice()
        {
            var saved = new ExConObserverState { CameraX = 42f, CameraY = 43f, CameraZ = 44f, InstanceMarker = "saved-01" };
            StageForeignSlice(saved);

            // A FRESH instance with different defaults — proves restore, not coincidence.
            var fresh = new ExConObserverState { CameraX = 1f, CameraY = 2f, CameraZ = 3f, InstanceMarker = "fresh-99" };
            Assert.False(fresh.RestoredFromScenario);

            var handler = new ExConScenarioLoadHandler(fresh, NodeId, OrchestrationConstants.ResolveStagingRoot());
            handler.PrepareAsync(PrefetchIntent(_name), CancellationToken.None).GetAwaiter().GetResult();

            Assert.True(fresh.RestoredFromScenario);           // flag-level proof
            Assert.Equal(42f, fresh.CameraX);
            Assert.Equal(43f, fresh.CameraY);
            Assert.Equal(44f, fresh.CameraZ);
            Assert.Equal("saved-01", fresh.InstanceMarker);    // value-level proof: the marker round-tripped
        }

        [Fact]
        public void Dump_ThroughPanel_ShowsRestoredState()
        {
            var saved = new ExConObserverState { CameraX = 5f, CameraY = 6f, CameraZ = 7f, InstanceMarker = "panel-xyz" };
            StageForeignSlice(saved);

            var fresh   = new ExConObserverState();
            var handler = new ExConScenarioLoadHandler(fresh, NodeId, OrchestrationConstants.ResolveStagingRoot());
            handler.PrepareAsync(PrefetchIntent(_name), CancellationToken.None).GetAwaiter().GetResult();

            // The panel is exactly what GET /panels/excon_observer dumps — the T-C proof surface.
            var panel = new ExConObserverPanelViewModel(fresh).Dump().AsObject();
            Assert.True((bool)panel["RestoredFromScenario"]!);
            Assert.Equal(5f, (float)panel["CameraX"]!);
            Assert.Equal("panel-xyz", (string)panel["InstanceMarker"]!);
        }

        [Fact]
        public void PrepareAsync_NoForeignSlice_IsNoOp_AndDoesNotRestore()
        {
            // A load with no foreign slice for this node (the common case) must not flip the flag or throw.
            var fresh   = new ExConObserverState { InstanceMarker = "untouched" };
            var handler = new ExConScenarioLoadHandler(fresh, NodeId, OrchestrationConstants.ResolveStagingRoot());

            handler.PrepareAsync(PrefetchIntent(_name), CancellationToken.None).GetAwaiter().GetResult();

            Assert.False(fresh.RestoredFromScenario);
            Assert.Equal("untouched", fresh.InstanceMarker);
        }
    }
}
