using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.ExCon.Observer;
using Xunit;

namespace Hrot.ExCon.Tests.Observer
{
    /// <summary>
    /// CE-277(c3) — proves ExCon's save handler writes an INTENTIONALLY-INCOMPATIBLE slice
    /// (<c>$meta.docType = "ExCon.Observer"</c>) carrying the observer state, and reports a manifest tagged
    /// with that foreign docType so the orchestrator merge routes it to <c>foreign/</c> instead of merging it.
    /// </summary>
    public sealed class ExConScenarioSaveHandlerTests : IDisposable
    {
        private const int NodeId = 500;
        private readonly string _name = "test-excon-" + Guid.NewGuid().ToString("N");

        public void Dispose()
        {
            try
            {
                var dir = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), _name);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch { /* best effort */ }
        }

        [Fact]
        public void PrepareAsync_WritesForeignObserverSlice_AndTagsTheManifest()
        {
            var state = new ExConObserverState { CameraX = 7f, CameraY = 8f, CameraZ = 9f, InstanceMarker = "abc123" };
            var handler = new ExConScenarioSaveHandler(state, NodeId);

            var intent = new ExecuteNodeOpIntent
            {
                Operation     = NodeOpType.SerializeLocal,
                TransactionId = Guid.NewGuid(),
                DomainPayload = new ScenarioSaveHandlerPayload(_name),
            };

            var result = handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();

            // Manifest tagged FOREIGN so the merge routes (never parses) it.
            var manifest = Assert.IsType<FileManifestResult[]>(result);
            Assert.Single(manifest);
            Assert.Equal("ExCon.Observer", manifest[0].DocType);
            Assert.Contains($"node_{NodeId}", manifest[0].RelativeDest);

            // The file is a non-Hrot.Scenario document carrying the observer state.
            var file = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), _name, "scenario.json");
            Assert.True(File.Exists(file));
            var dom = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            Assert.Equal("ExCon.Observer", JsonEnvelope.Read(dom).DocType);
            var obs = dom["Observer"]!.AsObject();
            Assert.Equal(7f, (float)obs["CameraX"]!);
            Assert.Equal("abc123", (string)obs["InstanceMarker"]!);
        }

        [Fact]
        public void PrepareAsync_IgnoresNonScenarioPayload()
        {
            var handler = new ExConScenarioSaveHandler(new ExConObserverState(), NodeId);
            var intent = new ExecuteNodeOpIntent
            {
                Operation     = NodeOpType.SerializeLocal,
                TransactionId = Guid.NewGuid(),
                DomainPayload = new ArchiveHandlerPayload(Guid.NewGuid()),
            };
            var result = handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Null(result);
        }
    }
}
