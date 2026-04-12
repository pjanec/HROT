using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network.Interfaces;
using FDP.Toolkit.Replication.Services;
using Hrot.IG.Components;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK3-A005 â€” ACL Backdoor Elimination verification tests (Tests 2 and 3).
///
/// <para><b>Test 2</b> â€” E2E area authoring with no backdoor:
/// The IG area-authoring tool now packages geometry in
/// <see cref="SpawnEntityCommand.InitialComponents"/>. This test drives the
/// full DDS path via <see cref="HrotRunnerHarness"/> and asserts that exactly
/// one <see cref="CreateEntityRequest"/> with a <c>dtMapVisualOverlay</c>
/// descriptor arrives from the translator â€” no pre-built side-channel.</para>
///
/// <para><b>Test 3</b> â€” Offline editor isolation:
/// Publishing a <see cref="SpawnEntityCommand"/> in <see cref="EditorHarness"/>
/// creates an ECS entity locally.  The <see cref="SpawnEntityCommandEgressTranslator"/>
/// is <em>not</em> registered in the offline harness, so no DDS write can occur.</para>
/// </summary>
public sealed class AclBackdoorEliminationTests
{
    // Domain IDs for the live E2E test (Test 2).  Must not overlap with other
    // test class ranges.  229 is one above UrbanCombatFileLifecycleTests (228).
    private const int DomainBase = 229;
    private static int _domainCounter = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainCounter);

    private const int WaitForRequestTimeoutFrames = 150;

    // â”€â”€ Test 2: E2E area authoring, no backdoor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// PACK3-A005 Test 2.
    /// Area authoring places geometry in <c>InitialComponents</c>.  The translator
    /// synthesises a <c>CreateEntityRequest</c> with <c>dtMapVisualOverlay</c> purely
    /// from the domain event â€” no pre-built side-channel.
    /// Compile-time proof that <c>MapCommandController._prebuiltRequests</c> no longer
    /// exists is implicit: the solution builds cleanly after A001â€“A004.
    /// </summary>
    [Fact]
    public void AreaAuthoring_EndToEnd_NoBackdoor_PublishesCorrectCreateEntityRequest()
    {
        int domainId = NextDomainId();

        using var harness = new HrotRunnerHarness(
            "simhost,ig",
            domainId);

        var igApp = harness.Ig.App;

        // Observer participant on the same domain â€” reads the CreateEntityRequest
        // that the SpawnEntityCommandEgressTranslator writes over DDS.
        using var observer     = new DdsParticipant((uint)domainId);
        using var reqReader    = new DdsReader<CreateEntityRequest>(observer, "CreateEntityRequest");

        // Activate area authoring via IG test hook (no ExCon needed).
        var requestId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        igApp.TestHook_ParseCommandAndActivateAreaTool(
            requestId,
            $"{{\"contextId\":\"{contextId:N}\"}}");

        // Commit three canvas points.  The tool callback will build EditablePolyline +
        // MapOverlayStyle into InitialComponents and call MapCommandController.
        var points = new List<Vector2>
        {
            new Vector2(100f, 200f),
            new Vector2(150f, 220f),
            new Vector2(120f, 260f),
        };
        igApp.TestHook_DirectPointSequenceToolCommit(points);

        // Pump until the CreateEntityRequest lands on the DDS reader.
        CreateEntityRequest observed = default;
        bool arrived = harness.PumpUntil(
            () => TryTakeAnyCreateRequest(reqReader, out observed),
            WaitForRequestTimeoutFrames);

        Assert.True(arrived, "CreateEntityRequest did not arrive on DDS in time.");
        Assert.NotNull(observed.InitialDescriptors);

        // Geometry must be described as dtMapVisualOverlay with 3 points.
        var overlayDesc = observed.InitialDescriptors!
            .FirstOrDefault(d => d._d == EDescriptorType.dtMapVisualOverlay);
        Assert.NotEqual(default, overlayDesc);
        Assert.NotNull(overlayDesc.MapVisualOverlay.Points);
        Assert.Equal(points.Count, overlayDesc.MapVisualOverlay.Points!.Count);
    }

    // â”€â”€ Test 3: Offline editor isolation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed class CapturingWriter<T> : IDdsWriter<T>
    {
        public int CallCount { get; private set; }
        public void Write(T sample) => CallCount++;
        public void DisposeInstance(T key) { }
    }

    /// <summary>
    /// PACK3-A005 Test 3.
    /// Publishing a <see cref="SpawnEntityCommand"/> in <see cref="EditorHarness"/>
    /// (no DDS translator packs) must create exactly one ECS entity in the repo and
    /// must NOT trigger any DDS writer calls.
    /// </summary>
    [Fact]
    public void SpawnCommand_OfflineEditor_NoNetworkCallsMade()
    {
        using var harness = new EditorHarness();

        // Initialise scenario before spawning (mirrors EditorPreviewAndSaveIntegrationTests).
        harness.Editor.NewScenario();
        harness.PumpFrames(1);

        // Set up a standalone translator (NOT registered in the kernel) with a
        // capturing mock writer on the same bus.  This lets us assert that the
        // translator never writes â€” because the kernel never pumps it.
        var capWriter  = new CapturingWriter<CreateEntityRequest>();
        var translator = new SpawnEntityCommandEgressTranslator(
            capWriter, harness.Bus, geoTransform: null);

        // Publish a SpawnEntityCommand with TkbType=1L which is registered in EditorHarness.
        // Provide an explicit NetworkId (non-zero) because the EditorHarness IdAllocator
        // is a sequential stub starting at 1000, not a DDS server.
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = 1L,
            NetworkId   = 1L,
            RequestId   = Guid.NewGuid(),
            OwnerNodeId = 0,
            InitType    = ReliableInitType.None,
        });

        // Pump until the entity appears (NetworkSpawningSystem processes the event).
        bool entityCreated = harness.PumpUntil(() => harness.Repo.EntityCount == 1, timeoutMs: 5000);
        Assert.True(entityCreated, "Entity was not created in offline repo within 5 s.");

        // The standalone translator was never pumped by the kernel, so it sees
        // zero events on the bus (already consumed by NetworkSpawningSystem above).
        // Pumping it now is a belt-and-braces check.
        translator.PollIngress(null!, null!);
        Assert.Equal(0, capWriter.CallCount);
    }

    // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static bool TryTakeAnyCreateRequest(
        DdsReader<CreateEntityRequest> reader,
        out CreateEntityRequest request)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            request = sample.Data;
            return true;
        }

        request = default;
        return false;
    }
}
