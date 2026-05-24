// GZH-001 through GZH-008: Gizmos-2 Headless infrastructure tests.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Hub;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Modules;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Diagnostics.Gizmos.UI;
using GizmoMap.Network;
using StructEdit.Core;
using StructEdit.Json;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ==========================================================================
    // Mock / stub helpers
    // ==========================================================================

    // Tracks OnCancel calls in addition to base MockGlobalGizmo.
    internal sealed class TrackingGlobalGizmo : IEntityStatefulGizmo
    {
        public bool RequiresExclusiveFocus { get; set; } = true;
        public bool IsFocused { get; private set; }
        public bool OnCancelCalled  { get; private set; }
        public int  DisposeCount    { get; private set; }

        public void SetFocus(bool f) { IsFocused = f; }
        public void UpdateAndDraw(ISimulationView view, float dt, IDebugDrawBuilder b) { }
        public void OnInteractionStarted(GizmoPickToken t, Vector3 w) { }
        public void OnDragUpdate(Vector3 w)                            { }
        public void OnCommit(Vector3 w)                                { }
        public void OnCancel()          { OnCancelCalled = true; }
        public void OnMenuAction(int id)                               { }
        public void OnMouseEvent(MapMouseButton b, bool p, Vector3 w)  { }
        public void OnKeyEvent(MapKeyboardKey k, bool p)               { }
        public void Dispose()           { DisposeCount++; }
    }

    // Permanent gizmo: never needs exclusive focus.
    internal sealed class PermanentMockGlobalGizmo : IEntityStatefulGizmo
    {
        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public bool Disposed { get; private set; }

        public void SetFocus(bool f) { IsFocused = f; }
        public void UpdateAndDraw(ISimulationView view, float dt, IDebugDrawBuilder b) { }
        public void OnInteractionStarted(GizmoPickToken t, Vector3 w) { }
        public void OnDragUpdate(Vector3 w)                            { }
        public void OnCommit(Vector3 w)                                { }
        public void OnCancel()                                         { }
        public void OnMenuAction(int id)                               { }
        public void OnMouseEvent(MapMouseButton b, bool p, Vector3 w)  { }
        public void OnKeyEvent(MapKeyboardKey k, bool p)               { }
        public void Dispose() { Disposed = true; }
    }

    // On-demand mock with cancel tracking (for GZH005 DataDriven tests).
    internal sealed class TrackingInjectedGizmo : IEntityStatefulGizmo
    {
        public bool RequiresExclusiveFocus { get; set; } = true;
        public bool IsFocused { get; private set; }
        public bool OnCancelCalled { get; private set; }
        public bool Disposed       { get; private set; }

        public void SetFocus(bool f) { IsFocused = f; }
        public void UpdateAndDraw(ISimulationView view, float dt, IDebugDrawBuilder b) { }
        public void OnInteractionStarted(GizmoPickToken t, Vector3 w) { }
        public void OnDragUpdate(Vector3 w)                            { }
        public void OnCommit(Vector3 w)                                { }
        public void OnCancel()  { OnCancelCalled = true; }
        public void OnMenuAction(int id)                               { }
        public void OnMouseEvent(MapMouseButton b, bool p, Vector3 w)  { }
        public void OnKeyEvent(MapKeyboardKey k, bool p)               { }
        public void Dispose()   { Disposed = true; }
    }

    // IEcsModuleSystem stub that counts Execute calls.
    internal sealed class CountingSystem : IEcsModuleSystem
    {
        public int ExecuteCount { get; private set; }
        public void Execute(ISimulationView view, float deltaTime) { ExecuteCount++; }
    }

    // IGizmoUiStatePublisher stub that records all Publish calls.
    internal sealed class RecordingPublisher : IGizmoUiStatePublisher
    {
        public List<GizmoUiState> Published { get; } = new();
        public void Publish(GizmoUiState state) { Published.Add(state); }
    }

    // A simple DTO that StructEdit.Reflection can serialize.
    public class SimpleHeadlessDto
    {
        public int Value { get; set; }
    }

    // ==========================================================================
    // GZH-001: TerminalConnectedEvent / TerminalDisconnectedEvent
    // ==========================================================================

    public class GZH001_Tests
    {
        // GZH001_1: Both lifecycle events round-trip through FdpEventBus.
        [Fact]
        public void GZH001_1_EventBus_TerminalConnected_RoundTrips()
        {
            var bus = new FdpEventBus();
            const long expectedId = 42L;

            bus.PublishManaged(new TerminalConnectedEvent { TerminalId = expectedId });
            bus.SwapBuffers();

            var events = bus.ReadManaged<TerminalConnectedEvent>();
            Assert.NotNull(events);
            Assert.Single(events);
            Assert.Equal(expectedId, events[0].TerminalId);
        }

        // GZH001_2: TerminalDisconnectedEvent round-trips through FdpEventBus.
        [Fact]
        public void GZH001_2_EventBus_TerminalDisconnected_RoundTrips()
        {
            var bus = new FdpEventBus();
            const long expectedId = 77L;

            bus.PublishManaged(new TerminalDisconnectedEvent { TerminalId = expectedId });
            bus.SwapBuffers();

            var events = bus.ReadManaged<TerminalDisconnectedEvent>();
            Assert.NotNull(events);
            Assert.Single(events);
            Assert.Equal(expectedId, events[0].TerminalId);
        }
    }

    // ==========================================================================
    // GZH-002: GizmoExecutionController
    // ==========================================================================

    public class GZH002_Tests
    {
        private static DebugPrimitiveBuffer MakeBuffer() => new DebugPrimitiveBuffer();

        // GZH002_1: Reference counting enables/disables the group at the right transitions.
        [Fact]
        public void GZH002_1_ListenerCount_EnablesDisablesGroup()
        {
            var sys     = new CountingSystem();
            var group   = new TogglablePostSimulationGroup("GizmoExecution", sys);
            group.Enabled = false;

            var buf     = MakeBuffer();
            var globalMgr = new GlobalGizmoManager(buf);
            var registry  = new GizmoRegistry();
            var ddSystem  = new DataDrivenGizmoSystem(registry, buf);

            var ctrl = new GizmoExecutionController(group, globalMgr, ddSystem);

            Assert.False(group.Enabled);
            Assert.Equal(0, ctrl.ListenerCount);

            ctrl.AddListener();
            Assert.True(group.Enabled);
            Assert.Equal(1, ctrl.ListenerCount);

            ctrl.AddListener();
            Assert.True(group.Enabled);
            Assert.Equal(2, ctrl.ListenerCount);

            ctrl.RemoveListener();
            Assert.True(group.Enabled);
            Assert.Equal(1, ctrl.ListenerCount);

            ctrl.RemoveListener();
            Assert.False(group.Enabled);
            Assert.Equal(0, ctrl.ListenerCount);
        }

        // GZH002_2: RemoveListener to 0 calls OnCancel on the focused global gizmo.
        [Fact]
        public void GZH002_2_RemoveListener_ToZero_CallsCancelOnFocusedGizmo()
        {
            var buf   = MakeBuffer();
            var globalMgr = new GlobalGizmoManager(buf);
            var registry  = new GizmoRegistry();
            var ddSys     = new DataDrivenGizmoSystem(registry, buf);

            var gizmo = new TrackingGlobalGizmo { RequiresExclusiveFocus = true };
            long id   = GlobalGizmoManager.NewId();
            globalMgr.Register(id, gizmo);

            var group = new TogglablePostSimulationGroup("GizmoExecution");
            group.Enabled = false;
            var ctrl  = new GizmoExecutionController(group, globalMgr, ddSys);

            ctrl.AddListener();
            ctrl.RemoveListener();

            Assert.True(gizmo.OnCancelCalled);
            Assert.Equal(0, ctrl.ListenerCount);
            Assert.False(group.Enabled);
        }
    }

    // ==========================================================================
    // GZH-003: TogglablePostSimulationGroup disabled/enabled integration
    // ==========================================================================

    public class GZH003_Tests
    {
        // GZH003_1: When group is disabled, inner systems are never called.
        [Fact]
        public void GZH003_1_GroupDisabled_InnerSystemsNotCalled()
        {
            var sys   = new CountingSystem();
            var group = new TogglablePostSimulationGroup("GizmoExecution", sys);
            group.Enabled = false;

            // Simulate several ticks (null view is safe since CountingSystem ignores it).
            group.Execute(null!, 0f);
            group.Execute(null!, 0f);
            group.Execute(null!, 0f);

            Assert.Equal(0, sys.ExecuteCount);
        }

        // GZH003_2: AddListener enables the group; subsequent ticks invoke inner systems.
        [Fact]
        public void GZH003_2_AddListener_EnablesGroup_SystemsAreCalled()
        {
            var sys   = new CountingSystem();
            var group = new TogglablePostSimulationGroup("GizmoExecution", sys);
            group.Enabled = false;

            var buf       = new DebugPrimitiveBuffer();
            var globalMgr = new GlobalGizmoManager(buf);
            var registry  = new GizmoRegistry();
            var ddSys     = new DataDrivenGizmoSystem(registry, buf);
            var ctrl      = new GizmoExecutionController(group, globalMgr, ddSys);

            ctrl.AddListener();
            Assert.True(group.Enabled);

            group.Execute(null!, 0f);
            group.Execute(null!, 0f);

            Assert.Equal(2, sys.ExecuteCount);
        }
    }

    // ==========================================================================
    // GZH-004: GlobalGizmoManager.CancelInteractiveTools
    // ==========================================================================

    public class GZH004_Tests
    {
        private static DebugPrimitiveBuffer MakeBuffer() => new DebugPrimitiveBuffer();

        // GZH004_1: Permanent gizmo survives; on-demand gizmo is cancelled and removed.
        [Fact]
        public void GZH004_1_CancelInteractiveTools_RemovesOnDemand_KeepsPermanent()
        {
            var buf     = MakeBuffer();
            var manager = new GlobalGizmoManager(buf);

            var permanent = new PermanentMockGlobalGizmo();
            var onDemand  = new TrackingGlobalGizmo { RequiresExclusiveFocus = true };

            long permId = GlobalGizmoManager.NewId();
            long demId  = GlobalGizmoManager.NewId();

            manager.Register(permId, permanent);
            manager.Register(demId,  onDemand);

            manager.CancelInteractiveTools();

            Assert.True(onDemand.OnCancelCalled);
            Assert.Equal(1, manager.ActiveCount);
            Assert.False(permanent.Disposed);
        }
    }

    // ==========================================================================
    // GZH-005: DataDrivenGizmoSystem.CancelInteractiveTools
    // ==========================================================================

    public class GZH005_Tests
    {
        // GZH005_1: Injected gizmo gets OnCancel then is cleared from the system.
        [Fact]
        public void GZH005_1_CancelInteractiveTools_CallsOnCancelAndClears()
        {
            var buf      = new DebugPrimitiveBuffer();
            var registry = new GizmoRegistry();
            var system   = new DataDrivenGizmoSystem(registry, buf);

            var entity = new Entity(1, 1);
            var gizmo  = new TrackingInjectedGizmo();

            system.ActivateGizmo(entity, gizmo);
            Assert.True(system.HasInjectedGizmo(entity));

            system.CancelInteractiveTools();

            Assert.True(gizmo.OnCancelCalled);
            Assert.True(gizmo.Disposed);
            Assert.False(system.HasInjectedGizmo(entity));
        }
    }

    // ==========================================================================
    // GZH-006: StructInspectorProjector<T>
    // ==========================================================================

    public class GZH006_Tests
    {
        private static IComponentEditService MakeService()
            => new ComponentEditServiceBuilder().Build();

        private static DebugPrimitiveBuffer MakeBuffer() => new DebugPrimitiveBuffer();

        // GZH006_1: Same DTO state called twice → publisher gets exactly 1 Publish.
        [Fact]
        public void GZH006_1_SameDtoTwice_OnlyOnePublish()
        {
            var service   = MakeService();
            var publisher = new RecordingPublisher();
            var projector = new StructInspectorProjector<SimpleHeadlessDto>(service, publisher);
            var draw      = MakeBuffer();
            var dto       = new SimpleHeadlessDto { Value = 7 };

            projector.EmitAndSync(draw, 100L, 1u, dto);
            projector.EmitAndSync(draw, 100L, 1u, dto);

            Assert.Single(publisher.Published);
        }

        // GZH006_2: Second call with a mutated DTO triggers another Publish.
        [Fact]
        public void GZH006_2_MutatedDto_SecondPublish()
        {
            var service   = MakeService();
            var publisher = new RecordingPublisher();
            var projector = new StructInspectorProjector<SimpleHeadlessDto>(service, publisher);
            var draw      = MakeBuffer();
            var dto       = new SimpleHeadlessDto { Value = 7 };

            projector.EmitAndSync(draw, 100L, 1u, dto);

            dto.Value = 99;
            projector.EmitAndSync(draw, 100L, 1u, dto);

            Assert.Equal(2, publisher.Published.Count);
        }

        // GZH006_3: After ApplyUpdate, next EmitAndSync with same DTO does NOT echo back.
        [Fact]
        public void GZH006_3_AfterApplyUpdate_NoEchoOnNextEmit()
        {
            var service   = MakeService();
            var publisher = new RecordingPublisher();
            var projector = new StructInspectorProjector<SimpleHeadlessDto>(service, publisher);
            var draw      = MakeBuffer();
            var dto       = new SimpleHeadlessDto { Value = 7 };

            // First emit publishes the initial state.
            projector.EmitAndSync(draw, 100L, 1u, dto);
            int countAfterFirst = publisher.Published.Count;

            // Build JSON payload by round-tripping through the edit service.
            using var session = service.Open(dto, typeof(SimpleHeadlessDto));
            var payloadJson = session.ToJson();

            // Apply that JSON — cache is set to canonical form.
            projector.ApplyUpdate(payloadJson, ref dto);

            // Emit again with the same (unchanged) DTO — should NOT publish.
            projector.EmitAndSync(draw, 100L, 1u, dto);

            Assert.Equal(countAfterFirst, publisher.Published.Count);
        }

        // GZH006_4: With null publisher, no exception is thrown; draw builder still gets the prim.
        [Fact]
        public void GZH006_4_NullPublisher_NoException_PrimEmitted()
        {
            var service   = MakeService();
            var projector = new StructInspectorProjector<SimpleHeadlessDto>(service, uiPublisher: null);
            var draw      = MakeBuffer();
            var dto       = new SimpleHeadlessDto { Value = 3 };

            var countBefore = draw.Count;
            projector.EmitAndSync(draw, 100L, 1u, dto);

            Assert.True(draw.Count > countBefore);
        }
    }

    // ==========================================================================
    // GZH-007: GizmoUiStateHub
    // ==========================================================================

    public class GZH007_Tests
    {
        private static GizmoUiState MakeState(uint id = 1u, string json = "{}") =>
            new GizmoUiState { GizmoInstanceId = id, EditDocumentJson = json };

        // GZH007_1: Publish to a hub with zero endpoints does not throw.
        [Fact]
        public void GZH007_1_ZeroEndpoints_NoThrow()
        {
            var hub = new GizmoUiStateHub();
            hub.Publish(MakeState()); // must not throw
        }

        // GZH007_2: Two registered endpoints both receive each Publish call.
        [Fact]
        public void GZH007_2_TwoEndpoints_BothReceive()
        {
            var hub = new GizmoUiStateHub();
            var ep1 = new RecordingPublisher();
            var ep2 = new RecordingPublisher();

            hub.AddEndpoint(ep1);
            hub.AddEndpoint(ep2);

            hub.Publish(MakeState(id: 42u));

            Assert.Single(ep1.Published);
            Assert.Single(ep2.Published);
            Assert.Equal(42u, ep1.Published[0].GizmoInstanceId);
            Assert.Equal(42u, ep2.Published[0].GizmoInstanceId);
        }

        // GZH007_3: After RemoveEndpoint, removed endpoint receives no further calls.
        [Fact]
        public void GZH007_3_RemoveEndpoint_NoFurtherCalls()
        {
            var hub = new GizmoUiStateHub();
            var ep  = new RecordingPublisher();

            hub.AddEndpoint(ep);
            hub.Publish(MakeState());
            hub.RemoveEndpoint(ep);
            hub.Publish(MakeState());

            Assert.Single(ep.Published);
        }

        // GZH007_4: Concurrent AddEndpoint during Publish does not throw InvalidOperationException.
        [Fact]
        public void GZH007_4_ConcurrentAdd_NoInvalidOperationException()
        {
            var hub   = new GizmoUiStateHub();
            var ep1   = new RecordingPublisher();
            var ep2   = new RecordingPublisher();
            hub.AddEndpoint(ep1);

            // ep2 gets added from a different thread while Publish is being called.
            Exception? caughtEx = null;
            var addThread = new Thread(() =>
            {
                try { hub.AddEndpoint(ep2); }
                catch (Exception ex) { caughtEx = ex; }
            });

            addThread.Start();
            hub.Publish(MakeState());  // must not throw
            addThread.Join();

            Assert.Null(caughtEx);
        }
    }

    // ==========================================================================
    // GZH-008: LocalGizmoUiStateTransport
    // ==========================================================================

    public class GZH008_Tests
    {
        private static GizmoUiState MakeState(uint id, string json = "{}") =>
            new GizmoUiState { GizmoInstanceId = id, EditDocumentJson = json };

        // GZH008_1: Publishing same ID twice overwrites; PollAndApply delivers only the last.
        [Fact]
        public void GZH008_1_SameId_LastWriteWins()
        {
            var transport = new LocalGizmoUiStateTransport();
            transport.Publish(MakeState(1u, "first"));
            transport.Publish(MakeState(1u, "second"));

            var delivered = new List<GizmoUiState>();
            transport.PollAndApply(s => delivered.Add(s));

            Assert.Single(delivered);
            Assert.Equal("second", delivered[0].EditDocumentJson);
        }

        // GZH008_2: Two distinct IDs are both delivered by PollAndApply.
        [Fact]
        public void GZH008_2_DistinctIds_BothDelivered()
        {
            var transport = new LocalGizmoUiStateTransport();
            transport.Publish(MakeState(1u, "a"));
            transport.Publish(MakeState(2u, "b"));

            var delivered = new List<GizmoUiState>();
            transport.PollAndApply(s => delivered.Add(s));

            Assert.Equal(2, delivered.Count);
        }

        // GZH008_3: After PollAndApply, the transport is empty (no double-delivery).
        [Fact]
        public void GZH008_3_AfterPoll_IsEmpty()
        {
            var transport = new LocalGizmoUiStateTransport();
            transport.Publish(MakeState(1u));

            var firstPoll  = new List<GizmoUiState>();
            var secondPoll = new List<GizmoUiState>();

            transport.PollAndApply(s => firstPoll.Add(s));
            transport.PollAndApply(s => secondPoll.Add(s));

            Assert.Single(firstPoll);
            Assert.Empty(secondPoll);
        }
    }

    // ==========================================================================
    // Helpers shared by GZH-009 / GZH-010 / GZH-015 tests
    // ==========================================================================

    // Stub IGizmoNetworkFactory with no live DDS participant.
    // All factory methods return null / empty so the module can be constructed in unit tests.
    internal sealed class StubNetworkFactory : IGizmoNetworkFactory
    {
        public DdsParticipant? Participant => null;
        public IEcsModuleSystem? CreateGizmoPublisherSystem(DebugPrimitiveBuffer buffer, long localNodeId) => null;
        public IReadOnlyList<INetworkTranslator> CreateGizmoTranslators(FdpEventBus interactionBus, long localNodeId, bool headless)
            => Array.Empty<INetworkTranslator>();
    }

    // Factory method shared by GZH-009 / GZH-010 / GZH-015 to build a controller and hub.
    internal static class GizmoTestHelpers
    {
        public static (GizmoExecutionController ctrl, GizmoUiStateHub hub) MakeControllerAndHub()
        {
            var buf       = new DebugPrimitiveBuffer();
            var globalMgr = new GlobalGizmoManager(buf);
            var registry  = new GizmoRegistry();
            var ddSys     = new DataDrivenGizmoSystem(registry, buf);
            var group     = new TogglablePostSimulationGroup("GizmoExecution");
            group.Enabled = false;
            var ctrl      = new GizmoExecutionController(group, globalMgr, ddSys);
            var hub       = new GizmoUiStateHub();
            return (ctrl, hub);
        }
    }

    // ==========================================================================
    // GZH-009: LocalTerminalModule
    // ==========================================================================

    public class GZH009_Tests
    {
        // GZH009_1: Constructing LocalTerminalModule increments ListenerCount;
        //           Dispose decrements it back to zero.
        [Fact]
        public void GZH009_1_Constructor_IncrementsListenerCount_Dispose_Decrements()
        {
            var (ctrl, hub) = GizmoTestHelpers.MakeControllerAndHub();

            Assert.Equal(0, ctrl.ListenerCount);

            using var module = new LocalTerminalModule(ctrl, hub);

            Assert.Equal(1, ctrl.ListenerCount);

            module.Dispose();

            Assert.Equal(0, ctrl.ListenerCount);
        }

        // GZH009_2: After construction, publishing to the hub reaches LocalUiTransport;
        //           after Dispose, subsequent publishes are NOT received.
        [Fact]
        public void GZH009_2_HubPublish_ReachesTransport_StopsAfterDispose()
        {
            var (ctrl, hub) = GizmoTestHelpers.MakeControllerAndHub();
            var module = new LocalTerminalModule(ctrl, hub);

            var state = new GizmoUiState { GizmoInstanceId = 7u, EditDocumentJson = "{}" };
            hub.Publish(state);

            var received = new List<GizmoUiState>();
            module.LocalUiTransport.PollAndApply(s => received.Add(s));
            Assert.Single(received);

            // After Dispose, the transport endpoint is removed.
            module.Dispose();

            hub.Publish(state);
            var afterDispose = new List<GizmoUiState>();
            module.LocalUiTransport.PollAndApply(s => afterDispose.Add(s));
            Assert.Empty(afterDispose);
        }
    }

    // ==========================================================================
    // GZH-010: GizmoNetworkTransportModule (stub factory, no DDS)
    // ==========================================================================

    public class GZH010_Tests
    {
        // GZH010_1: Constructing GizmoNetworkTransportModule with a null-participant factory
        //           leaves ListenerCount at zero (tracker does NOT call AddListener on construction).
        //           Dispose leaves ListenerCount at zero.
        [Fact]
        public void GZH010_1_Constructor_DoesNotIncrementListenerCount()
        {
            var (ctrl, hub) = GizmoTestHelpers.MakeControllerAndHub();
            var factory     = new StubNetworkFactory();
            var buffer      = new DebugPrimitiveBuffer();
            var bus         = new FdpEventBus();

            Assert.Equal(0, ctrl.ListenerCount);

            using var module = new GizmoNetworkTransportModule(ctrl, hub, factory, buffer, 1L, bus);

            Assert.Equal(0, ctrl.ListenerCount);

            module.Dispose();

            Assert.Equal(0, ctrl.ListenerCount);
        }

        // GZH010_2: Tracker.OnSample drives ListenerCount correctly across connects/disconnects.
        [Fact]
        public void GZH010_2_TrackerOnSample_DrivesListenerCount()
        {
            var (ctrl, hub) = GizmoTestHelpers.MakeControllerAndHub();
            var factory     = new StubNetworkFactory();
            var buffer      = new DebugPrimitiveBuffer();
            var bus         = new FdpEventBus();

            using var module = new GizmoNetworkTransportModule(ctrl, hub, factory, buffer, 1L, bus);

            module.Tracker.OnSample(1L, isAlive: true);
            Assert.Equal(1, ctrl.ListenerCount);

            module.Tracker.OnSample(2L, isAlive: true);
            Assert.Equal(2, ctrl.ListenerCount);

            module.Tracker.OnSample(1L, isAlive: false);
            Assert.Equal(1, ctrl.ListenerCount);
        }
    }

    // ==========================================================================
    // GZH-015: GizmoCapabilitiesTracker via Tracker field
    // ==========================================================================

    public class GZH015_Tests
    {
        private static (GizmoNetworkTransportModule module, GizmoExecutionController ctrl, FdpEventBus bus) MakeModule()
        {
            var (ctrl, hub) = GizmoTestHelpers.MakeControllerAndHub();
            var bus         = new FdpEventBus();
            var module      = new GizmoNetworkTransportModule(ctrl, hub, new StubNetworkFactory(), new DebugPrimitiveBuffer(), 99L, bus);
            return (module, ctrl, bus);
        }

        // GZH015_1: OnSample(id, true) for a new node publishes TerminalConnectedEvent and increments count.
        [Fact]
        public void GZH015_1_OnSample_NewAliveNode_PublishesConnectedEvent_IncrementsCount()
        {
            var (module, ctrl, bus) = MakeModule();
            using (module)
            {
                module.Tracker.OnSample(42L, isAlive: true);
                bus.SwapBuffers();

                var events = bus.ReadManaged<TerminalConnectedEvent>();
                Assert.Single(events);
                Assert.Equal(42L, events[0].TerminalId);
                Assert.Equal(1, ctrl.ListenerCount);
            }
        }

        // GZH015_2: OnSample(id, false) for a known node publishes TerminalDisconnectedEvent and decrements count.
        [Fact]
        public void GZH015_2_OnSample_KnownNodeDisconnects_PublishesDisconnectedEvent_DeprecatesCount()
        {
            var (module, ctrl, bus) = MakeModule();
            using (module)
            {
                module.Tracker.OnSample(42L, isAlive: true);
                bus.SwapBuffers();
                bus.ReadManaged<TerminalConnectedEvent>(); // drain

                module.Tracker.OnSample(42L, isAlive: false);
                bus.SwapBuffers();

                var events = bus.ReadManaged<TerminalDisconnectedEvent>();
                Assert.Single(events);
                Assert.Equal(42L, events[0].TerminalId);
                Assert.Equal(0, ctrl.ListenerCount);
            }
        }

        // GZH015_3: OnSample(id, false) for an unknown node does NOT publish TerminalDisconnectedEvent.
        [Fact]
        public void GZH015_3_OnSample_UnknownNodeDisconnects_NoEvent()
        {
            var (module, ctrl, bus) = MakeModule();
            using (module)
            {
                module.Tracker.OnSample(999L, isAlive: false);
                bus.SwapBuffers();

                var events = bus.ReadManaged<TerminalDisconnectedEvent>();
                Assert.Empty(events);
                Assert.Equal(0, ctrl.ListenerCount);
            }
        }

        // GZH015_4: OnSample(id, true) called twice for the same node is idempotent (count stays 1).
        [Fact]
        public void GZH015_4_OnSample_SameNodeAlive_Idempotent()
        {
            var (module, ctrl, bus) = MakeModule();
            using (module)
            {
                module.Tracker.OnSample(99L, isAlive: true);
                module.Tracker.OnSample(99L, isAlive: true);

                Assert.Equal(1, ctrl.ListenerCount);
            }
        }
    }
}
