
How is exercise time control working? What dds messages are used? How does it combine with cluster operations? Isnt there a duplication or overlap of control messages between time master and cluster master?

**How Exercise Time Control Works** Exercise time control operates on a Master-Slave architecture that toggles between two primary modes: **Continuous** (real-time/scaled) and **Deterministic** (paused/stepped lockstep).

-   In Continuous mode, the `MasterTimeController` maintains the authoritative simulation time and publishes synchronisation pulses. Slaves run a `SlaveTimeController` that feeds these pulses into a Phase-Locked Loop (PLL) and jitter filter to smoothly sync their local virtual clocks with the master.-   Mode switching is handled by the `DistributedTimeCoordinator` on the master and the `SlaveTimeModeListener` on the slaves. When pausing the simulation, the master uses a **Future Barrier protocol**: it computes a future wall-clock tick (`TotalWallTicks`), broadcasts it, and all nodes execute the controller swap at that exact tick, ensuring frame-accurate synchronisation across varying hardware.

**DDS Messages Used for Time Control** Time control relies on a dedicated set of lightweight DDS messages, distinct from general cluster operations:

-   **TimePulse** (`TimePulseDescriptor`): Published by the master at 1Hz during Continuous mode. It carries the master's wall ticks, simulation time snapshot, and time scale, which the slaves use to adjust their PLL.-   **SwitchTimeModeEvent** (`SwitchTimeModeWireDto`): Broadcasts mode changes (e.g., switching to Deterministic/paused mode). Because CycloneDDS IDL cannot represent arbitrary C# enums, this is transmitted over the wire using `SwitchTimeModeWireDto` with an integer field for the target mode, alongside the future barrier wall-ticks and fixed delta.

**Combination with Cluster Operations** Time control integrates with the orchestrator, but the orchestrator treats time operations as a special fast-path case. When a user requests a time change (e.g., Pause, Resume, Step, SetTimeScale) via the UI, a `ClusterOpRequest` is sent to the `ClusterMaster`. The `ClusterMaster` intercepts these specific operation types and **explicitly bypasses the standard cluster Two-Phase Commit (2PC) process**.

Instead of fanning out a heavy transaction, it immediately fires a local `TimeControlRequested` event. The `OrchestratorSubsystem` listens for this event and directly commands the `DistributedTimeCoordinator` or `TimeController` to execute the change.

Furthermore, time control can be embedded into macroscopic state transitions. For example, a `ClusterOpRequest` to transition to `LoadingLive` can include `"TimeMode": "Deterministic"` in its JSON payload. The orchestrator extracts this hint and instructs the time coordinator to pause the simulation just before the cluster enters the `OperatingLive` state.

**Is there a duplication or overlap of control messages?** No, there is a strict separation of concerns, meaning **no duplication or overlap exists** between the Time Master and Cluster Master:

-   **Cluster Master (2PC Protocol):** Handles macroscopic, heavy state transitions (e.g., `LoadingEdit`, `OperatingLive`, `LoadingReplay`) and distributed tasks like saving scenarios or loading zones. It uses `NodeOpCommand` and `NodeOpStatus` to execute a reliable, multi-step Two-Phase Commit protocol ensuring all nodes succeed or fail together.-   **Time Master (Future Barrier / PLL):** Handles high-frequency, low-latency, frame-accurate execution. Because time changes (like pausing) must happen instantly and precisely across the cluster, they explicitly bypass the 2PC overhead. The time master uses its own independent DDS topics (`TimePulse`, `SwitchTimeModeEvent`) to coordinate time without clogging the orchestration command queues.

does this architecture allow nodes that are NOT part of the cluster 2PC sync to act as time slaves, just using the time-specific dds messages, not knowing about the 2PC cluster operations wgatsoever?

Yes, this architecture fully allows nodes to act as pure time slaves without participating in or even knowing about the cluster's Two-Phase Commit (2PC) operations.

Because the system strictly decouples macroscopic orchestration from high-frequency time synchronization, a standalone node can sync its clock to the master purely by listening to the time-specific DDS topics.

To implement a pure time slave outside the 2PC cluster, a node only needs to wire up the following components:

**1\. Time-Specific DDS Translators** The node must create a `DdsParticipant` and a local `FdpEventBus`, then register the two ingress translators that bridge time messages from DDS to the local bus:

-   **TimePulseIngressTranslator****:** Polls the `TimePulse` DDS topic and publishes `TimePulseDescriptor` events to the local bus.-   **SwitchTimeModeDescriptorTranslator****:** Polls the `SwitchTimeModeEvent` DDS topic (using the `SwitchTimeModeWireDto` to bypass enum limitations) and publishes `SwitchTimeModeEvent` to the local bus.

**2\. Time Controllers and Listeners** The node then simply instantiates the standard slave time controllers, which operate entirely off the local `FdpEventBus` and require no knowledge of cluster states or transactions:

-   **SlaveTimeController****:** Consumes the `TimePulseDescriptor` events to drive its Phase-Locked Loop (PLL) and smoothly synchronize its continuous virtual clock with the master.-   **SlaveTimeModeListener****:** Consumes the `SwitchTimeModeEvent` messages to seamlessly handle transitions to deterministic (paused/stepped) mode using the Future Barrier protocol.

Because these translators and controllers only depend on `DdsParticipant` and `FdpEventBus`, the node never needs to instantiate a `ClusterSlave`, nor does it need to subscribe to `NodeOpCommand` or publish `NodeOpStatus` messages. It will remain perfectly frame-synced with the simulation while ignoring all 2PC orchestration traffic.

can also nodes not having FDP ECS kernel act as time slaves?

Yes, nodes that do not have the FDP ECS kernel can absolutely act as time slaves or time observers.

Because the time architecture is strictly decoupled from both the ECS and the macroscopic orchestration layers, time synchronization data is broadcast using standard CycloneDDS topics: **TimePulse** (which carries `TimePulseDescriptor`) and **SwitchTimeModeEvent** (which carries the blittable `SwitchTimeModeWireDto` to bypass enum limitations).

A perfect example of this in the system is the **ExCon (Interactive Operations Station)** subsystem. The ExCon is explicitly designed as a "no-ECS subsystem" that has no `ModuleHostKernel` or `EntityRepository`, relying instead on a simple dictionary-based `DerRepo`.

To synchronize its time without the FDP ECS components, the ExCon node:

-   Instantiates dedicated DDS ingress handlers (`TimePulseIngressHandler` and `TimeModeIngressHandler`) to poll the time topics directly over the network.-   Uses simple delegates (`OnTimePulse` and `OnTimeMode`) to receive the raw DDS samples.-   Updates its own internal properties (`MasterSimTime`, `MasterWallTicks`, `MasterTimeScale`, and `IsPaused`) directly from the incoming network data.

If an external or non-ECS node needs to act as a **highly precise continuous time slave** (rather than just a passive UI observer), it simply needs to subscribe to those same two DDS topics and implement its own internal logic mirroring the FDP concepts:

-   **For Continuous Mode:** It can read the 1Hz `TimePulse` messages and feed the `MasterWallTicks` and `SimTimeSnapshot` into its own custom Phase-Locked Loop (PLL) to smoothly adjust its local virtual clock.-   **For Deterministic/Paused Mode:** It can read the `SwitchTimeModeWireDto` messages and execute an instant mode swap as soon as its local clock reaches the exact `BarrierWallTicks` specified in the message.

let's take the ExCon as an example. you mention the node needs FdpEventBus, what for? How could we wire the time slave to the ExCon?

**Why the** **FdpEventBus** **is needed**

If you want a node to act as a _true_ time slave (meaning it runs a local virtual clock smoothed by a Phase-Locked Loop to eliminate network jitter), it must use the `SlaveTimeController` and `SlaveTimeModeListener` classes. These built-in FDP time controllers are explicitly designed to consume their synchronization data (`TimePulseDescriptor` and `SwitchTimeModeEvent`) from a local `FdpEventBus`.

The event bus acts as a decoupled bridge: the DDS ingress translators write network packets to the bus, and the time controllers read from it, keeping the complex time-sync math entirely separated from the networking layer.

**How ExCon is wired currently (Passive Observer)**

Right now, the ExCon is actually wired as a **passive observer**, not a true time slave. Because it only needs to display the time on the UI and doesn't run physics or logic that requires a jitter-free continuous clock, it bypasses the `FdpEventBus` and the PLL entirely.

In `ExConSubsystem.cs`, it uses simplified, custom DDS handlers (`TimePulseIngressHandler` and `TimeModeIngressHandler`) that take a direct delegate. When a DDS packet arrives, it immediately fires `logic.OnTimePulse` or `logic.OnTimeMode` to overwrite properties like `MasterSimTime` and `IsPaused`.

**How to wire ExCon as a true Time Slave**

If you wanted to upgrade ExCon to be a highly precise time slave (for instance, if you added timeline-interpolated entity movement to the UI), you would replace its passive observer setup with the `FdpEventBus` pipeline:

-   **Create the Bus & Translators:** In `ExConSubsystem.cs`, instantiate an `FdpEventBus`. Replace the custom UI handlers with the standard FDP translators: `TimePulseIngressTranslator` and `SwitchTimeModeDescriptorTranslator`. These will poll DDS and publish events to the bus.-   **Instantiate the Time Controller:** Create a `SlaveTimeController`, passing it the `FdpEventBus`.-   **The Update Loop:** Inside `ExConSubsystem.Update()`, you must drive the pipeline every frame:
    -   Call `PollIngress()` on your translators to read DDS.-   Call `_eventBus.SwapBuffers()` so the ingress events are ready to read.-   Call `_slaveTimeController.Update()`. This allows the controller to consume the `TimePulseDescriptor` events and feed them into its Phase-Locked Loop (PLL) to advance its internal `TotalWallTicks` and `TotalTime` smoothly.-   **Handle Mode Switches:** To support pausing (switching to Deterministic mode via Future Barriers), you would typically use `SlaveTimeModeListener`. However, because that class currently demands a `ModuleHostKernel` to execute the swap, ExCon would need a lightweight custom listener that consumes `SwitchTimeModeEvent` from the event bus and swaps out the active `ITimeController` reference inside ExCon manually.

you sya that SlaveTimeModeListener currently demands a ModuleHostKernel to execute the swap. Is that the correct decoupled architecture?

You are spot on to question this. Strictly speaking, **no, this is not a perfectly decoupled architecture.**

While the _networking layer_ is beautifully decoupled from the time logic (using standard CycloneDDS topics that are translated into local `SwitchTimeModeEvent` messages on the `FdpEventBus`,), the _application_ of that time switch is tightly coupled to the ECS kernel.

Here is why this breaks strict decoupling:

**1\. The Direct Kernel Dependency** The `SlaveTimeModeListener` explicitly demands a `ModuleHostKernel` in its constructor,. When the listener detects that the local wall clock has crossed the target barrier, it directly invokes `_kernel.SwapTimeController(...)` to change the active time mode.

**2\. The Architectural Impact** Because the listener is hardcoded to depend on the heavyweight `ModuleHostKernel`, it assumes that the consumer of time events is always an ECS-driven simulation node (like `SimHost` or `IG`). This is exactly why a non-ECS node like the ExCon (which relies on a lightweight dictionary-based `DerRepo` instead of a `ModuleHostKernel`) cannot reuse this built-in listener to execute a Phase-Locked Loop (PLL) swap, forcing you to write a custom implementation.

**How a Truly Decoupled Architecture Would Look** In a strictly decoupled design, the `FDP.Toolkit.Time` library should not know what a `ModuleHostKernel` is. Instead, the `SlaveTimeModeListener` should depend on a lightweight interface—such as an `ITimeControllerHost`—or simply manage an internal `ITimeController` reference that the parent application polls.

This tight coupling is a pragmatic shortcut in the current codebase. Because the primary time-synchronized nodes in the cluster (`SimHost` and `IG`) are built around the `ModuleHostKernel`, the time toolkit was wired directly into the kernel's swapping mechanics, rather than through a generalized interface.

pls detail how to switch to truly decoupled architecture to that exev ExCon can reuse the SlaveTimeModeListener without needing ModuleHostKernel

To switch to a truly decoupled architecture where the `ExCon` node can reuse the `SlaveTimeModeListener` without any dependency on the `ModuleHostKernel`, you need to extract the time-swapping responsibilities into a lightweight interface.

Here are the specific steps and code changes required to decouple the architecture:

**1\. Create the** **ITimeControllerHost** **Interface** First, define an interface in `ModuleHost.Core.Time` that exposes the exact properties and methods the time listeners need to orchestrate mode swaps. Looking at `SlaveTimeModeListener`, it only requires three things: reading `CurrentTime`, getting the active controller, and swapping the controller.

Create a new interface `ITimeControllerHost.cs`:

```
using Fdp.Kernel;

namespace ModuleHost.Core.Time
{
    /// <summary>
    /// Decoupled host interface for managing active time controllers.
    /// </summary>
    public interface ITimeControllerHost
    {
        GlobalTime CurrentTime { get; }
        ITimeController GetTimeController();
        void SwapTimeController(ITimeController newController);
    }
}
```

**2\. Implement the Interface on** **ModuleHostKernel** The `ModuleHostKernel` already implements these exact three members natively. You simply need to append the interface to its class declaration:

```
public sealed class ModuleHostKernel : IDisposable, ITimeControllerHost
```

**3\. Decouple the Listeners and Coordinators** Update `SlaveTimeModeListener` (and you should also do this for `DistributedTimeCoordinator`) to depend entirely on `ITimeControllerHost` instead of the heavyweight `ModuleHostKernel`.

In `SlaveTimeModeListener.cs`:

```
// Replace _kernel with _timeHost
private readonly ITimeControllerHost _timeHost;

public SlaveTimeModeListener(FdpEventBus eventBus, ITimeControllerHost timeHost, TimeControllerConfig config)
{
    _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    _timeHost = timeHost ?? throw new ArgumentNullException(nameof(timeHost));
    _config = config ?? throw new ArgumentNullException(nameof(config));
    
    _eventBus.Register<SwitchTimeModeEvent>();
}
```

_Note: Replace all instances of_ _\_kernel._ _with_ _\_timeHost._ _throughout the_ _Update()_ _and_ _ExecuteSwapTo...()_ _methods__._

**4\. Create a Lightweight Host for ExCon** Now that the listener is decoupled, the `ExCon` node can use it. Since `ExConSubsystem` lacks an ECS kernel, you can create a minimal implementation of `ITimeControllerHost` inside the `ExCon` layer to hold its time controller.

For instance, you can implement this directly on `ExConLogic`, or create a dedicated lightweight wrapper:

```
public class ExConTimeHost : ITimeControllerHost
{
    private ITimeController _activeController;
    private readonly ExConLogic _logic;

    public GlobalTime CurrentTime => _activeController?.GetCurrentState() ?? default;

    public ExConTimeHost(ITimeController initialController, ExConLogic logic)
    {
        _activeController = initialController;
        _logic = logic;
    }

    public ITimeController GetTimeController() => _activeController;

    public void SwapTimeController(ITimeController newController)
    {
        _activeController?.Dispose();
        _activeController = newController;
        
        // Update ExCon's internal UI properties to reflect the newly swapped mode
        var state = _activeController.GetCurrentState();
        _logic.OnTimePulse(new TimePulseDescriptor { 
            SimTimeSnapshot = state.TotalTime, 
            MasterWallTicks = state.TotalWallTicks, 
            TimeScale = state.TimeScale 
        });
        
        // Update pause state (Deterministic = Paused)
        _logic.OnTimeMode(new SwitchTimeModeWireDto { 
            TargetModeInt = (int)_activeController.GetMode() 
        });
    }
}
```

**5\. Wire Up the ExCon Subsystem** Finally, in your `ExConSubsystem` (or wherever ExCon is bootstrapped), you can instantiate the standard time controllers and the newly decoupled listener using your lightweight host:

```
// 1. Create a local event bus for time sync
var eventBus = new FdpEventBus();

// 2. Wire the standard ingress translators to feed the bus
var timePulseTranslator = new TimePulseIngressTranslator(_participant, eventBus);
var timeModeTranslator = new SwitchTimeModeDescriptorTranslator(_participant, eventBus);

// 3. Create the Time Host and Controllers
var timeConfig = TimeConfig.Default;
var continuousController = new SlaveTimeController(eventBus, timeConfig);
var timeHost = new ExConTimeHost(continuousController, logic);

// 4. Instantiate the now-decoupled Listener!
var configWrapper = new TimeControllerConfig { SyncConfig = timeConfig, LocalNodeId = _nodeIdOverride };
var timeModeListener = new SlaveTimeModeListener(eventBus, timeHost, configWrapper);
```

During the `ExCon` `Update()` loop, simply call `timeModeListener.Update()`, `timeHost.GetTimeController().Update()`, and `eventBus.SwapBuffers()`. Your `ExCon` node will now seamlessly participate in Phase-Locked Loop (PLL) synchronization and Future Barrier mode swapping without ever allocating a `ModuleHostKernel` or `EntityRepository`.

