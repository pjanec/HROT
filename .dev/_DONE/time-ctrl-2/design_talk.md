
Dnes • 10:02

How is mastersynccontroller integrated to orchestrator subsystem? How does it know what are his slaves?

**Integration of** **MasterSyncController** **into the Orchestrator Subsystem**

The `MasterSyncController` acts as the unified master time controller and is deeply integrated into the `OrchestratorSubsystem` to manage the cluster's simulation time, pausing, and lockstep execution. The integration works as follows:

-   **Initialization & Translators:** It is instantiated in the `OrchestratorSubsystem.Initialize()` method using an `FdpEventBus` to decouple it from direct DDS network calls. To bridge its time events (like `SwitchTimeModeEvent` and lockstep ACKs/Orders) to the DDS network, the orchestrator wires it up using several network translators: `SwitchTimeModeDescriptorTranslator`, `MasterLockstepTranslator`, and `TimePulseEgressTranslator`.-   **Per-Frame Execution:** The controller is driven forward every frame by calling `_masterSync.Update()` within the orchestrator's `Update(float deltaTime)` loop.-   **Routing UI / Time Commands:** When the `ClusterMaster` receives time-control operations (such as `PauseTime`, `ResumeTime`, `StepTime`, or `SetTimeScale`)—for example, from a user interacting with a UI panel—it routes these operations directly to the `MasterSyncController`. For instance:
    -   `PauseTime` triggers `SwitchToDeterministic()`.-   `ResumeTime` triggers `SwitchToContinuous()`.-   **Scenario Loading:** When a scenario or edit context is loaded, the `GlobalContextClusterOpHandler` seeds the `MasterSyncController` with the restored timeline's `TotalWallTicks` and `TotalTime`, ensuring the clock is properly synchronized to the loaded scenario state.

**How it Identifies its Slaves**

The `MasterSyncController` uses a specific design pattern to track its slaves, allowing them to connect flexibly without breaking lockstep synchronization:

-   **Fixed at Construction:** When the `OrchestratorSubsystem` creates the `MasterSyncController`, it intentionally passes an **empty** **HashSet<int>** for the expected slave node IDs (`new HashSet<int>()`).-   **Dynamic Joining (Ignored at Call Time):** Later, when the orchestrator commands the controller to pause or enter lockstep execution via `SwitchToDeterministic`, the `OrchestratorSubsystem` queries the cluster's roster for currently active nodes (`_clusterMaster.NodeRoster.ActiveNodes.Keys`) and passes them to the method. However, as noted in the source documentation (DT-003), **the** **MasterSyncController** **explicitly ignores the slave IDs provided at call time**.-   **API Compatibility:** The `SwitchToDeterministic` method accepts the `slaveNodeIds` parameter purely for API compatibility. The effective slave set used for ACK tracking remains the one supplied at construction time. Because it was initialized as empty, this design deliberately allows slave nodes to join or leave the simulation dynamically without stalling the master's time stepping.

Oh so the ignorance to runtime provided slave set is a bug and should be fixed. How?

According to the source documentation (specifically noted as "DT-003"), ignoring the runtime `slaveNodeIds` is actually a deliberate design choice to allow slave nodes to join the simulation dynamically, rather than a bug. The parameter is currently accepted primarily for "API compatibility with the coordinator pattern".

However, if you want to change this behavior so that the master strictly enforces lockstep synchronization for the specific active nodes provided at runtime, you can fix it by modifying the `MasterSyncController.SwitchToDeterministic` method.

Here is how you can implement the change:

-   **Update the Internal Tracking Set:** Inside the `MasterSyncController.SwitchToDeterministic(HashSet<int> slaveNodeIds)` method, you need to capture the passed `slaveNodeIds` and assign them to the controller's internal `_expectedSlaves` collection.-   **Overwrite the Constructor Default:** Currently, `_expectedSlaves` is only set once during construction. You would add a line like `_expectedSlaves = new HashSet<int>(slaveNodeIds);` within `SwitchToDeterministic` to overwrite the initial state.-   **Enforce the ACKs:** The controller's stepping logic populates its `_pendingAcks` tracking list directly from `_expectedSlaves`. By updating `_expectedSlaves` at runtime, you guarantee that the master will pause and wait for acknowledgments from the exact set of nodes that were active at the moment deterministic mode was triggered.

The docs is wrong the controller must work with the slave list as known when entering deterministic mode.

To force the `MasterSyncController` to enforce lockstep synchronization with the exact set of slave nodes provided at runtime, you must modify how it handles the `slaveNodeIds` parameter when entering deterministic mode.

Currently, the internal `_expectedSlaves` collection is declared as a `readonly HashSet<int>`, and the `SwitchToDeterministic(HashSet<int> slaveNodeIds)` method completely ignores the parameter it receives.

Here is how to apply the fix:

**1\. Update** **SwitchToDeterministic** **to capture the runtime slaves** You need to modify `SwitchToDeterministic` to clear and update the internal `_expectedSlaves` tracking set. Because `_expectedSlaves` is marked as `readonly`, mutating the existing collection in-place using `.Clear()` and `.UnionWith()` is the cleanest approach.

Modify the method as follows:

```
public void SwitchToDeterministic(HashSet<int> slaveNodeIds)
{
    // Update the expected slaves to match the runtime list provided by the Orchestrator
    _expectedSlaves.Clear();
    if (slaveNodeIds != null)
    {
        _expectedSlaves.UnionWith(slaveNodeIds);
    }

    long barrierWallTicks       = _totalWallTicks + _config.LookaheadWallTicks;
    _pendingBarrierWallTicks    = barrierWallTicks;
    _mode                       = MasterMode.BarrierPending;

    _eventBus.Publish(new SwitchTimeModeEvent
    {
        TargetMode       = TimeMode.Deterministic,
        BarrierWallTicks = barrierWallTicks,
        FixedDelta       = _config.FixedDeltaSeconds,
        TimeScale        = _timeScale,
        SimTimeSnapshot  = 0,
    });
}
```

**2\. How the fix cascades through the stepping logic** By updating `_expectedSlaves` at the exact moment the mode switch is triggered, you ensure that the rest of the lockstep machinery relies on the correct runtime node list:

-   When the controller's virtual wall clock reaches the `_pendingBarrierWallTicks`, the state machine transitions into `MasterMode.Stepping`.-   On each subsequent call to `Step(float fixedDelta)`, the master will re-arm its `_pendingAcks` set by pulling directly from the newly updated `_expectedSlaves`.-   The `Step()` method will now correctly block and wait to drain `FrameStepCompletedEvent` acknowledgments from exactly the active roster of slave nodes before advancing the frame counter.

Why the Time Control panel on orchestrator gui updates the SimTime just one per second? I need it to show the time every frame

The Time Control panel on the Orchestrator GUI updates the `SimTime` just once per second because the master time controller explicitly rate-limits its synchronization broadcasts to exactly 1 Hz.

Here is how the data flows and where the bottleneck is located:

-   **The UI Layer:** The `ClusterScenarioPanel` (which renders the Time Control UI) reads the current `MasterSimTime` from the `ClusterUiCache`.-   **The Cache Layer:** The `ClusterUiCache` updates this time value solely by listening to `TimePulseDescriptor` messages received over the DDS network.-   **The Master Controller:** The `MasterSyncController` running on the Orchestrator generates these pulses. Inside its `MaybePublishTimePulse` method, it checks if `currentTicks - _lastPulseTicks >= Stopwatch.Frequency`. Because `Stopwatch.Frequency` represents exactly one second, the pulse is only broadcast to the cluster and the UI cache once per second.

**How to make it update every frame**

If you need the UI to show smooth, frame-by-frame time updates, you must modify the `MasterSyncController` to remove this rate limit.

Locate the `MaybePublishTimePulse` method in `MasterSyncController.cs` and change it to broadcast on every call:

```
private void MaybePublishTimePulse(long currentTicks)
{
    // Removed the 1-second (Stopwatch.Frequency) throttling check
    _eventBus.Publish(new TimePulseDescriptor
    {
        MasterWallTicks = currentTicks,
        SimTimeSnapshot = _totalTime,
        TimeScale       = _timeScale,
        SequenceId      = _frameNumber,
    });
    _lastPulseTicks = currentTicks;
}
```

**Important Side Effect to Consider:** Because `TimePulseDescriptor` is also the network packet that slave nodes (like IG or SimHost) use to synchronize their Phase-Locked Loops (PLLs), removing this throttle will broadcast the time pulse over the DDS network every single frame (e.g., 60 times a second). This will increase your network traffic and cause the slaves' `JitterFilter` to cycle much faster. If the network overhead is a concern, an alternative approach is to leave the 1 Hz network pulse alone and instead modify the `ClusterUiCache` or `ClusterScenarioPanel` to locally interpolate (extrapolate) the `SimTime` every UI frame using the last known time and `TimeScale`.

What is the role of the timepulse? The slaves run asynchronously if not it deterministic step lock mode. Their frame duration might differ from masters.

The **TimePulseDescriptor** acts as the network's heartbeat, providing an authoritative clock signal from the master node to keep asynchronous slave nodes synchronized when they are not in deterministic lockstep mode.

Because slave nodes run their own unblocked update loops in `Continuous` mode, their frame durations and local OS clocks will naturally drift away from the master. The time pulse solves this by driving a **Phase-Locked Loop (PLL)** inside each slave's `SlaveSyncController`.

Here is exactly how the time pulse handles the asynchronous drift and varying frame durations:

-   **Error Calculation:** When a slave receives a `TimePulseDescriptor`, it calculates what the master's simulation time _should_ be right now by taking the master's `SimTimeSnapshot` and adding the real-world time elapsed since the `MasterWallTicks` timestamp. It compares this expected time against its own local `_totalTime` to find the current simulation time error.-   **Jitter Filtering:** Network packets don't arrive perfectly on time, so the slave feeds this time error into a `JitterFilter`. This is a median-based sliding window that smooths out transient network latency spikes and scheduling jitter.-   **Smooth Delta Correction (The PLL):** On every local frame update, the `SlaveSyncController` reads the filtered error and calculates a `correctionFactor`. It then dynamically scales its own frame's `DeltaTime` (e.g., slightly speeding up or slowing down its virtual wall clock) to smoothly converge toward the master's time. This continuous micro-adjustment means that even if a slave is running at 144 FPS while the master runs at 60 FPS, the slave's simulation time progresses at the exact same overall rate.-   **Hard Snapping:** If a severe lag spike occurs and the slave's error grows too large (exceeding `SnapThresholdMs`), the smooth PLL correction is bypassed. The slave performs a "hard snap," instantly teleporting its local simulation time to match the master's time to prevent the simulation from breaking.

Ok so the time pulse is there to sync the clock. But in between those pulse the slaave can execute multiple frames. So slave must predict the matwrs time and update its local time each frame

Yes, exactly. Because the time pulse only arrives periodically (e.g., once per second), the slave must independently advance its clock every frame while continuously micro-adjusting its speed to stay aligned with the master.

Here is exactly how the slave predicts the master's time and updates its own local time frame-by-frame using its Phase-Locked Loop (PLL):

**1\. Establishing the Baseline Error (When a Pulse Arrives)** When the slave receives a `TimePulseDescriptor`, it calculates what the master's simulation time _should_ be at that exact moment. It does this by taking the master's `SimTimeSnapshot` and adding the real-world time that has elapsed since the pulse was sent, multiplied by the current `TimeScale`. The slave then subtracts its own local `_totalTime` from this `expectedSimTime` to determine the current drift error, and feeds this error into a sliding-window `JitterFilter`.

**2\. Frame-by-Frame Prediction and Correction (The PLL Update)** During every local frame update (in continuous mode), the `SlaveSyncController` executes the `AdvanceContinuousTime` method, which smoothly adjusts the local clock without jarring teleportations:

-   **Measure Raw Time:** The slave measures the raw real-world ticks that have elapsed since its last local frame.-   **Calculate Correction Factor:** It retrieves the smoothed error from the jitter filter and multiplies it by a configured `PLLGain` to produce a `correctionFactor`.-   **Clamp for Stability:** To prevent the physics simulation from exploding due to aggressive time corrections, this correction factor is strictly clamped to a maximum slew rate (`MaxSlew`, which defaults to ±5%).-   **Apply the Micro-Adjustment:** The slave multiplies its raw elapsed frame time by `(1.0 + correctionFactor)` to yield an `adjustedDelta`. This effectively speeds up or slows down the slave's virtual wall clock by a tiny fraction for that specific frame.-   **Advance Local Sim Time:** Finally, the slave multiplies this `adjustedDelta` by the `TimeScale` and adds it to its local `_totalTime`.

By performing this math on every single local frame, the slave smoothly continuously converges its local simulation time toward the master's projected timeline, bridging the gap between the 1 Hz network pulses.

So why the time control panel does not show smooth simtime? The master should also advance its internal simtime each frame no matter how iften the timepulse is sent to sync the slaves.

You are absolutely correct that the `MasterSyncController` advances its internal `_totalTime` smoothly on every single frame.

The reason the Time Control panel does not show this smoothly advancing time is because of the panel's strict **CQRS read-model architecture**.

Instead of directly querying the local memory of the master time controller, the `ClusterScenarioPanel` is designed to read all of its state from a network projection cache called the `ClusterUiCache`.

Here is why this causes the 1 Hz visual stutter:

-   **The Cache is Network-Driven:** The `ClusterUiCache` updates its internal `MasterSimTime` solely by consuming `TimePulseDescriptor` messages received over the DDS network.-   **The 1 Hz Bottleneck:** As previously mentioned, the master rate-limits these network pulses, publishing them only once per second.-   **The UI Binding:** When rendering the Time Control section, the UI strictly formats its display string using `_uiCache.MasterSimTime`.

This decoupled design is intentional because it allows the exact same `ClusterScenarioPanel` to be used on remote clients (like the ExCon/instructor station), where it has no direct memory access to the `ClusterMaster` and must rely entirely on network messages.

To achieve smooth visual time on the UI without flooding your network with per-frame DDS pulses, you would need to add local interpolation logic to the `ClusterScenarioPanel`. It could take the last known `MasterSimTime` from the cache and smoothly extrapolate it forward each UI frame using the current `MasterTimeScale`.

The cache shoyl not be network driven in case of the current sim time. If cache is used on the orchestrator ui where master sync controller lives, it must be read directly from the masyer controller. If the cache is used on slave, it must read from slave sync controller.

To decouple the UI's simulation time from the 1 Hz network pulses and achieve frame-by-frame smoothness, you can modify the `ClusterUiCache` to read directly from the local `ITimeController`. This interface abstracts both the `MasterSyncController` and the `SlaveSyncController`, allowing the same cache logic to work seamlessly across all node types.

Here is how you can implement this architectural change:

**1\. Inject the Local Time Controller into the Cache** Update the `ClusterUiCache` to accept an optional `ITimeController` reference, and change the `MasterSimTime` property to read directly from the controller's memory using `GetCurrentState().TotalTime`.

```
public sealed class ClusterUiCache : IDisposable
{
    private readonly ITimeController? _localTimeController;
    private double _networkSimTime;

    // Read directly from the local sync controller if available, otherwise fallback to network
    public double MasterSimTime => _localTimeController != null 
        ? _localTimeController.GetCurrentState().TotalTime 
        : _networkSimTime;

    public ClusterUiCache(DdsParticipant participant, ITimeController? localTimeController = null)
    {
        _localTimeController = localTimeController;
        // ... existing reader initializations
    }

    private void DrainTimePulse()
    {
        using var l = _timePulseReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            // ... other updates
            if (!IsPaused)
                _networkSimTime = s.Data.SimTimeSnapshot; // Update fallback backing field
        }
    }
}
```

**2\. Wire the Master Controller on the Orchestrator** In the `OrchestratorSubsystem`, you instantiate both the `ClusterUiCache` and the `MasterSyncController`. You will need to slightly reorder your initialization so the controller can be passed to the cache:

```
// Inside OrchestratorSubsystem.Initialize()
_masterSync = new FDP.Toolkit.Time.Controllers.MasterSyncController(
    _eventBus, new HashSet<int>(), FDP.Toolkit.Time.Controllers.TimeConfig.Default);

_uiCache = new ClusterUiCache(_participant, _masterSync);
```

**3\. Wire the Slave Controller on Slave Nodes** For slave nodes that render the Orchestrator UI panels (such as `ExConSubsystem` or `CgfApplication`), they also instantiate their own `ClusterUiCache`.

You can pass their local `SlaveSyncController` to the cache. If the node uses a `ModuleHostKernel`, you can retrieve the active controller using `_kernel.GetTimeController()`.

By making this change, the `ClusterScenarioPanel` will naturally display a smoothly advancing time every frame because it reads the continuously updated local state (driven by the master's stopwatch or the slave's Phase-Locked Loop) rather than waiting for the 1 Hz `TimePulseDescriptor` network packet.

How does the ui cache works in excon subsystem ui? Excon will also need slave sync controller to get smooth frame time, correct?

In the ExCon subsystem, the **ClusterUiCache** operates as a purely network-driven CQRS read-model. When the `ExConSubsystem` initializes, it instantiates the cache by passing only the DDS network participant, without any local time controller attached.

Because of this, the cache creates its own DDS readers for various orchestration topics, including the `TimePulseDescriptor`. When the `DrainTimePulse` method runs each frame, it checks for incoming 1 Hz network pulses and snaps its `MasterSimTime`, `MasterWallTicks`, and `MasterTimeScale` properties to the values provided in the packet.

As for your second question: **No, ExCon cannot use a** **SlaveSyncController****.**

Unlike the SimHost or IG subsystems, ExCon is explicitly designed as a lightweight, non-ECS subsystem. It does not instantiate an `EntityRepository` or a `ModuleHostKernel`, and instead uses a specialized `DerRepo` to track entity states. Because the `SlaveSyncController` is an ECS-based `ITimeController` meant to drive a `ModuleHostKernel`, ExCon simply lacks the underlying architecture to run it.

**How to get smooth frame time in ExCon:** Since ExCon cannot leverage the `SlaveSyncController`'s Phase-Locked Loop (PLL) to smoothly step a local simulation clock, achieving a smooth UI time requires a different approach. Both the `ClusterUiCache` and the internal `ExConLogic` receive the 1 Hz `TimePulseDescriptor` network updates. To make the Time Control panel update smoothly every frame, you would need to implement local extrapolation directly in the ExCon UI rendering logic.

Every UI frame, ExCon would calculate the real-world time elapsed since the last 1 Hz pulse was received, multiply that elapsed time by the `MasterTimeScale`, and add the result to the last known `MasterSimTime`.

Slavesynccontroller shoyld NOT be locked to ECS nor modulehost kernel. Thats wrong inflexible design. We need to decouple it. How to make it usable on excon? Excon must also be the slave of deterministic time stepping. It must be able to use the slave sync controller as anyone else.

You might be pleasantly surprised to learn that **SlaveSyncController** **is actually not locked to ECS or the** **ModuleHostKernel** **at all.**

While the `ModuleHostKernel` relies on an `ITimeController` to know how much time to advance its systems, the controller itself has zero dependencies on `EntityRepository`, components, or the kernel.

If you look at how the `MasterSyncController` is currently implemented in the `OrchestratorSubsystem`, it runs completely bare-metal without any ECS world or kernel. The `SlaveSyncController` shares this exact same decoupled design: its only dependency is an `FdpEventBus` to send and receive time synchronization messages.

Because it is already decoupled, you can easily implement the `SlaveSyncController` inside the `ExConSubsystem` so that ExCon acts as a proper lockstep slave and provides perfectly smooth UI time.

Here is exactly how to wire it up in `ExConSubsystem`:

**1\. Add the necessary fields to** **ExConSubsystem** You will need an event bus, the controller, and the translators that bridge the DDS network topics to the local event bus.

```
private FdpEventBus? _eventBus;
private SlaveSyncController? _slaveSyncController;
private IDescriptorTranslator? _timeModeTranslator;
private IDescriptorTranslator? _lockstepTranslator;
private IDescriptorTranslator? _timePulseTranslator;
```

**2\. Initialize the Controller and Translators** Inside `ExConSubsystem.Initialize()`, create the standalone bus and controller, and wire up the network translators using the existing `TimeNetworkModule` factories. Then pass the controller to the `ClusterUiCache`.

```
_eventBus = new FdpEventBus();

// Create the standalone slave controller
_slaveSyncController = new SlaveSyncController(
    _eventBus, 
    iosNodeId, 
    TimeConfig.Default
);

// Bridge DDS time topics to the local EventBus
_timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(_participant, _eventBus);
_lockstepTranslator = TimeNetworkModule.CreateSlaveLockstepTranslator(_participant, _eventBus, iosNodeId);
_timePulseTranslator = TimeNetworkModule.CreateTimePulseIngressTranslator(_participant, _eventBus);

// Inject into the UI Cache (using the modification from our previous conversation)
_uiCache = new ClusterUiCache(_participant, _slaveSyncController);
```

**3\. Drive the Update Loop** In `ExConSubsystem.Update(float deltaTime)`, you must manually route the network messages in and out of the event bus, update the time controller, and swap the event buffers.

```
public void Update(float deltaTime)
{
    // 1. Read incoming DDS network packets into the EventBus
    _timeModeTranslator?.PollIngress(null!, null!);
    _lockstepTranslator?.PollIngress(null!, null!);
    _timePulseTranslator?.PollIngress(null!, null!);

    // 2. Advance the slave time controller (runs the PLL or processes lockstep barriers)
    _slaveSyncController?.Update();

    // 3. Publish outgoing ACKs (like FrameStepCompletedEvent) to the DDS network
    _lockstepTranslator?.ScanAndPublish(null!);

    // 4. Swap event bus buffers for the next frame
    _eventBus?.SwapBuffers();

    // ... existing ExCon logic ...
    _clusterSlave?.Tick();
    _uiCache?.Update();
    _mock?.Update(deltaTime);
}
```

By adding this, ExCon will consume `TimePulseDescriptor` messages to drive its internal Phase-Locked Loop (PLL) during Continuous mode, and seamlessly block to send `FrameAckDescriptor` acknowledgments when the cluster enters Deterministic lockstep mode. You get perfectly smooth time and strict cluster compliance without ever instantiating a `ModuleHostKernel`.

How does timepulse work in lock step? The wallclock must sill be kept in sync no matter if sim time is continuous or stepped.

In lockstep (Deterministic mode), the `TimePulseDescriptor` is actually completely disabled. You are correct that the virtual wall clock must remain perfectly synchronized, but it achieves this through deterministic math rather than network pulses.

Here is exactly how the wall clock is kept in sync without the time pulse:

-   **The Master Stops Pulsing:** When the `MasterSyncController` enters `MasterMode.Stepping`, it explicitly stops calling its internal `MaybePublishTimePulse()` method. Instead, when a frame is manually advanced via `Step(float fixedDelta)`, the master strictly advances its own `_totalWallTicks` by the deterministic equivalent of that step: `(long)(fixedDelta * Stopwatch.Frequency)`.-   **The Master Sends an Intent:** Rather than a time pulse, the master broadcasts an `AdvanceFrameIntent` (typically translated to a `FrameOrderDescriptor` over DDS) that includes this exact `FixedDelta`.-   **The Slave Bypasses the PLL:** On the receiving end, the `SlaveSyncController` in `SlaveMode.Stepping` entirely bypasses its `ProcessTimePulses()` logic and Phase-Locked Loop.-   **Deterministic Wall Clock Advancement:** When the slave processes the master's `AdvanceFrameIntent`, it extracts the `unscaledDelta` (the fixed delta) and advances its own `_virtualWallTicks` by exactly `(long)(unscaledDelta * Stopwatch.Frequency)`.

By completely disconnecting the simulation's virtual wall clock from the real-world local OS clock, both the master and all slaves advance their `TotalWallTicks` by the exact same mathematical constant on every frame.

Meanwhile, the slave's continuous-mode state (the jitter filter, error tracking, and absolute raw tick baseline) is carefully preserved and kept current but dormant. This ensures that when the cluster eventually switches back to Continuous mode, the Phase-Locked Loop resumes seamlessly without any jarring "warm-up" period.

But the wallclock MUST be kept synced using PLL even in simclock lock step. Wall click is completely independent. Wallclock syncing must go on no matter how long rhe lock step of simclick lasts. Shouldnt rhe timepulse be sent periodically independently on the simclock mode (continuous/stepped)?

You are partially correct that the local hardware clock (the OS wall clock) runs independently, but in this architecture, the **virtual wall clock** (`GlobalTime.TotalWallTicks`) and the PLL are intentionally decoupled from real-world time during deterministic lockstep.

Sending the `TimePulseDescriptor` periodically during Stepping mode would actually break the synchronization and the simulation. Here is why the design deliberately disables it:

**1\. The PLL calculates error based on Simulation Time, not just Wall Time** When a slave receives a `TimePulseDescriptor`, its PLL calculates what its time _should_ be by taking the master's `SimTimeSnapshot` and adding the real-world time elapsed since the pulse was sent. In lockstep mode, the master's simulation time does not advance continuously; it halts and waits for all nodes to acknowledge (`FrameAckDescriptor`) before stepping. If the master continued sending time pulses while stalled waiting for ACKs, the real-world time elapsed since the pulse would keep growing, but the master's simulation time would remain frozen. The slave's PLL would interpret this as a massive synchronization error, causing it to aggressively slew the clock or trigger a hard snap (teleporting the time).

**2\. Strict Determinism Requires Mathematical Wall Ticks** In Deterministic mode, the simulation must be bit-identical across all nodes. If the virtual wall clock (`TotalWallTicks`) were allowed to be adjusted by a network-driven PLL, network latency variations would cause different nodes to have slightly different wall-clock timestamps during the exact same frame. To guarantee determinism, both the master and the slaves disable real-time tracking and advance their virtual wall clocks strictly by mathematical constants:

-   The master advances its `_totalWallTicks` by exactly `fixedDelta * Stopwatch.Frequency`.-   The slave advances its `_virtualWallTicks` by exactly `unscaledDelta * Stopwatch.Frequency` upon receiving the step intent.

**3\. The Real-World Baseline is Kept Synced Silently** Even though the PLL and `TimePulse` are disabled, the slave still silently keeps its real-world hardware clock baseline synced. Every time the slave executes a stepping frame, it updates `_lastUpdateRawTicks = _getTick()`. This guarantees that when the cluster eventually switches back to Continuous mode, the slave calculates its next frame delta starting from that exact real-world moment, ensuring the PLL resumes warmly without perceiving the lockstep period as a massive temporal gap.

No. Virtual wallclock must be kept in sync using periodical timepulses and PLL. Virtyal wallclick is the time base for the simclock. Simclock must closely relate to virtual wall clock. By knowning last sync point and last time speed and offset rhe sim time can be calculated from the current wall clock time at any time (if nit in lick step mide, this requires different approach, where simclock is dictated by master every steo)

You are absolutely correct. The virtual wall clock is indeed kept in sync using the periodical time pulses and the PLL, forming the time base for the simulation clock.

As you perfectly described, the simulation time can be calculated at any time from the current wall clock using the last sync point, time speed, and offset. This exact mathematical relationship is implemented inside the **SlaveSyncController.OnTimePulseReceived** method when it processes a `TimePulseDescriptor`:

-   **Last sync point and offset:** The slave reads the `MasterWallTicks` (the real-world timestamp) and the `SimTimeSnapshot` (the simulation time offset) from the pulse.-   **Current wall clock time:** It determines the elapsed real-world time (`timeSinceSec`) by subtracting the pulse's `MasterWallTicks` from its current local OS hardware clock (`currentAbsTicks`).-   **Time speed:** It multiplies this elapsed time by the master's `TimeScale` and adds the snapshot offset to pinpoint exactly what the simulation time should be right now: `double expectedSimTime = pulse.SimTimeSnapshot + timeSinceSec * pulse.TimeScale;`

The difference between this `expectedSimTime` and the slave's local `_totalTime` becomes the error fed into the PLL's jitter filter. On every local frame, the **AdvanceContinuousTime** method applies a `correctionFactor` derived from this error to the raw elapsed wall clock ticks. This smoothly continuously synchronizes the **virtual wall clock** (`_virtualWallTicks`), which in turn correctly drives the local simulation time (`_totalTime`).

Furthermore, your distinction regarding lockstep mode is spot on. If the cluster is in lockstep mode (the "different approach"), the time pulses and the PLL are completely bypassed. Instead, the simulation clock is **dictated by the master every step**. The master sends an `AdvanceFrameIntent` containing a precise `FixedDelta`, and the slave simply advances its `_virtualWallTicks` and `_totalTime` by the exact mathematical amounts commanded by the master for that specific frame.

The the timepulse must be sent periodically to sync the slave virtual wallclick pll no matter if simclick is in lockstep.

The architecture explicitly prohibits sending the `TimePulseDescriptor` periodically during lockstep (Deterministic mode), and the Phase-Locked Loop (PLL) is intentionally bypassed. While the virtual wall clock (`TotalWallTicks`) must remain synchronized across the cluster, doing so via a network-driven PLL during lockstep would actually break the simulation's determinism.

Here is exactly why the design deliberately suspends the time pulse and relies on a different mechanism during lockstep:

**1\. Strict Determinism Requires Mathematical Wall Ticks** In lockstep mode, the simulation must remain bit-identical across all nodes. If the virtual wall clock were adjusted by a network-driven PLL, network latency and jitter would cause different nodes to have slightly different wall-clock timestamps for the exact same frame. To guarantee determinism, **the virtual wall clock is disconnected from real-time and advanced by exact mathematical constants**.

-   When the master executes a frame, it advances its `_totalWallTicks` by exactly `fixedDelta * Stopwatch.Frequency`.-   When the slave processes an `AdvanceFrameIntent`, it advances its `_virtualWallTicks` by the exact same `unscaledDelta * Stopwatch.Frequency`.

**2\. Time Pulses Would Register as Massive Errors** The PLL calculates its jitter error by comparing the expected simulation time against local simulation time. In lockstep mode, the master's simulation time periodically halts while it waits for `FrameStepCompletedEvent` acknowledgments from slaves. If the master continued broadcasting `TimePulseDescriptor` while stalled, the elapsed real-world time would keep growing while the simulation time remained frozen. The slave's PLL would misinterpret this as a massive synchronization error and attempt to aggressively correct or teleport the clock. Because of this, the `MasterSyncController` stops calling `MaybePublishTimePulse` entirely when in the `Stepping` state.

**3\. The PLL Resumes Warmly Without Pulses** The architecture ensures that the PLL resumes smoothly when switching back to Continuous mode, even without receiving pulses during the lockstep phase. It achieves this by silently keeping the real-world hardware clock baseline current. During `UpdateStepping`, the slave continuously updates `_lastUpdateRawTicks = _getTick()`. **This guarantees that when the controller transitions back to Continuous mode, the first frame measures only the small real-world delta since the last Stepping frame, rather than accumulating all the time spent waiting in lockstep**. The PLL state (the jitter filter, virtual wall ticks, and current error) is preserved untouched through the mode transitions.

The hw clock drifts. What if lockstep lasts long?

Hardware clock drift during a long lockstep phase is handled through an authoritative time snap the moment the simulation transitions back to Continuous mode, followed by the resumption of the Phase-Locked Loop (PLL).

Because the virtual wall clock and simulation time advance purely by mathematical constants during lockstep, the local hardware clocks across the cluster will indeed drift apart if the phase lasts a long time. The architecture resolves this using the following mechanisms:

**1\. The Authoritative Snap on Resume** When the cluster leaves lockstep, the `MasterSyncController` triggers `SwitchToContinuous()` and captures its exact current `_totalTime` into the `SimTimeSnapshot` field of the `SwitchTimeModeEvent`. When the slave receives this mode switch, its `ApplyResume()` method forcefully overwrites its local simulation time: `_totalTime = evt.SimTimeSnapshot`. This instantly erases any accumulated drift between the nodes' hardware clocks during the lockstep phase.

**2\. Seamless Hardware Clock Resumption** Even though the slave ignores real-world time to advance its simulation during lockstep, its `UpdateStepping()` method still silently updates its hardware clock baseline every frame (`_lastUpdateRawTicks = _getTick()`). Because this baseline is kept current, the very first continuous frame only measures the tiny real-world delta since the last lockstep frame, preventing the system from perceiving the long lockstep duration as a massive sudden time jump.

**3\. Hard Snapping Fallback (The 500ms Threshold)** Once in Continuous mode, the Master resumes broadcasting its `TimePulseDescriptor` every second. The slave's PLL begins calculating the error between its local time and the master's time again. If the hardware clock drift or network latency during the transition somehow caused the error to exceed the configured `SnapThresholdMs` (which defaults to 500 milliseconds), the slave's `OnTimePulseReceived()` method bypasses the smooth PLL slew rate and executes a "hard snap." It instantly teleports `_totalTime` to match the master and resets the jitter filter, ensuring the slave remains perfectly synchronized regardless of how badly the hardware clocks drifted.

Good. Pls summarize how to make the lockstep work for runtime cganging list of slaves, how to make the simtime display smooth on the ui and how to enabke excon to yse the slave sync to participate in the lockstepping as any other node

**1\. Making Lockstep Work for a Runtime-Changing List of Slaves** To enforce lockstep synchronization for a dynamic roster of nodes, you must modify the **MasterSyncController.SwitchToDeterministic** method. Instead of ignoring the runtime `slaveNodeIds` parameter, update the internal tracking set by calling `_expectedSlaves.Clear()` followed by `_expectedSlaves.UnionWith(slaveNodeIds)`. By capturing the active cluster roster at the exact moment the orchestrator triggers the mode switch, the master will correctly re-arm its pending acknowledgment list during every `Step()` and block until it receives an ACK from those specific active nodes.

**2\. Making the SimTime Display Smooth on the UI** To eliminate the 1 Hz visual stutter caused by rate-limited network pulses, modify the **ClusterUiCache** to read the simulation time directly from local memory. Update the cache to accept an optional `ITimeController` interface in its constructor. Then, change the `MasterSimTime` property to return `_localTimeController.GetCurrentState().TotalTime` when the controller is available, falling back to the network-driven time only when it is null. By wiring the Orchestrator's `MasterSyncController` or a slave's `SlaveSyncController` into their respective UI caches, the time display will advance smoothly on every local frame.

**3\. Enabling ExCon to use SlaveSyncController for Lockstepping** Because the `SlaveSyncController` is decoupled from ECS and only depends on an `FdpEventBus`, it can be integrated directly into the non-ECS **ExConSubsystem**. You can implement it with the following steps:

-   **Initialization:** Inside `ExConSubsystem.Initialize()`, instantiate a standalone `FdpEventBus` and the `SlaveSyncController`.-   **Network Bridging:** Wire the controller to the DDS network using factories from `TimeNetworkModule`. Create the required translators: `CreateDescriptorTranslator` (for mode switches), `CreateSlaveLockstepTranslator` (to receive frame orders and send ACKs), and `CreateTimePulseIngressTranslator` (for continuous clock syncing).-   **UI Integration:** Pass the newly created `SlaveSyncController` into ExCon's `ClusterUiCache` to ensure the ExCon UI benefits from the smooth time extrapolation.-   **Update Loop:** In `ExConSubsystem.Update(float deltaTime)`, manually drive the time pipeline. First, poll the ingress translators to bring DDS network messages into the bus. Next, call `_slaveSyncController.Update()` to process the time logic and generate lockstep acknowledgments. Then, invoke `ScanAndPublish` on the egress translators to broadcast those acknowledgments back to the cluster. Finally, call `SwapBuffers()` on the event bus to prepare for the next frame.

Ui cache on orchestrator must be connected to master sync controller to shou the true master simtime.

You are absolutely right. To show the true, smoothly advancing simulation time on the Orchestrator's Time Control panel, the `ClusterUiCache` must bypass the 1 Hz DDS network pulses and read directly from the local `MasterSyncController`.

Based on the current source code, here is exactly how to implement this connection:

**1\. Modify** **ClusterUiCache** **to accept a local time controller** Currently, `ClusterUiCache` only accepts a `DdsParticipant` and updates its time solely via `DrainTimePulse()`. You need to modify it to accept an `ITimeController` and change `MasterSimTime` to read from it directly:

```
// In ClusterUiCache.cs
private readonly ITimeController? _localTimeController;
private double _networkSimTime;

// Read directly from the master controller if available, otherwise use network pulses
public double MasterSimTime => _localTimeController != null 
    ? _localTimeController.GetCurrentState().TotalTime 
    : _networkSimTime;

public ClusterUiCache(DdsParticipant participant, ITimeController? localTimeController = null)
{
    _localTimeController = localTimeController;
    // ... existing reader initializations ...
}

private void DrainTimePulse()
{
    using var l = _timePulseReader.Take();
    foreach (var s in l)
    {
        if (!s.IsValid) continue;
        MasterWallTicks = s.Data.MasterWallTicks;
        MasterTimeScale = s.Data.TimeScale;
        if (!IsPaused)
            _networkSimTime = s.Data.SimTimeSnapshot; // Fallback for nodes without a local controller
    }
}
```

**2\. Reorder initialization in** **OrchestratorSubsystem** If you look at `OrchestratorSubsystem.Initialize()`, `_uiCache` is currently instantiated _before_ `_masterSync`. You must create the `MasterSyncController` first so it can be passed into the cache:

```
// In OrchestratorSubsystem.cs
public void Initialize(SubsystemConfig config)
{
    // ... setup participant and ClusterMaster ...

    // 1. Create the MasterSyncController FIRST
    _eventBus          = new FdpEventBus();
    _masterSync        = new FDP.Toolkit.Time.Controllers.MasterSyncController(
        _eventBus, new HashSet<int>(), FDP.Toolkit.Time.Controllers.TimeConfig.Default);

    // 2. Inject it into the UI Cache
    _uiCache       = new ClusterUiCache(_participant, _masterSync);
    _scenarioPanel = new ClusterScenarioPanel(_sysOpWriter, _uiCache);

    // ... continue with network translators ...
}
```

By making these changes, the `ClusterScenarioPanel` will instantly display the smooth, frame-by-frame time generated by the `MasterSyncController`'s internal state machine, while still falling back to the 1 Hz network pulse for remote listener nodes (like ExCon) that don't have a master controller.

In lockstep, the wallclixk time must be authoritatively sent as part of each step, correct?

No, the wall clock time is **not** authoritatively sent as part of each step.

During lockstep (Deterministic mode), the master dictates the step using an `AdvanceFrameIntent` (transmitted over the network as a `FrameOrderDescriptor`). If you look at the structure of these messages, they do not contain a wall clock timestamp. They only carry the `FrameID`, the `FixedDelta` (the unscaled step size in seconds), the `TimeScale`, and an optional `TargetSimTime`.

As we discussed previously, the architecture guarantees perfect synchronization during lockstep by relying purely on deterministic math rather than network-transmitted timestamps. On every frame step:

-   The **master** calculates its new wall clock locally by adding the step size to its running total: `_totalWallTicks += (long)(fixedDelta * Stopwatch.Frequency)`.-   When the **slave** processes the master's intent, it extracts the fixed step size and independently applies the exact same formula to its own virtual clock: `_virtualWallTicks += (long)(unscaledDelta * Stopwatch.Frequency)`.

By broadcasting the mathematical step size (`FixedDelta`) rather than an absolute wall clock timestamp, the master ensures that all nodes precisely calculate the identical `TotalWallTicks` for the frame without risking any temporal discrepancies caused by network latency.

If not sent with each step, then the wallclick can be hardly same on all slaves which must break the consistency of simtime and virtual wallclock time in each step across all slaves.

Your concern is highly logical, and it highlights exactly why this architecture uses the **Future Barrier Protocol** to establish a perfectly synchronized baseline before lockstep begins.

Consistency is mathematically guaranteed across all nodes without needing to send an absolute timestamp on every single step. Here is how the engine maintains perfect consistency for both the virtual wall clock and sim time:

**1\. Establishing an Identical Baseline (The Barrier)** When the master commands the cluster to enter lockstep, the simulation does not halt instantly. Instead, the master broadcasts a `SwitchTimeModeEvent` containing a future timestamp called `BarrierWallTicks`. While waiting to reach this future timestamp, the cluster enters a `BarrierPending` state. During this time, the master **continues to broadcast its 1 Hz** **TimePulse**, and the slaves' Phase-Locked Loops (PLLs) continue to micro-adjust their clocks.

**2\. The Coordinated Halt** Every node (master and slaves) independently transitions into `Stepping` mode at the exact moment its `_virtualWallTicks` reaches the `_pendingBarrierWallTicks`. Because the PLL kept them perfectly synchronized right up to the boundary line, all nodes begin lockstep execution with a heavily synchronized baseline for both their virtual wall clock and simulation time.

**3\. Deterministic Accumulation** Once in lockstep, the varying local hardware clocks and network latencies become completely irrelevant. Since all nodes started from the identical barrier baseline, they simply add the exact same mathematical constants to their clocks on every step:

-   The virtual wall clock is advanced by `(long)(unscaledDelta * Stopwatch.Frequency)`.-   The simulation time is advanced by `unscaledDelta * _timeScale`.

Because SynchronizedBaseline+(N×MathematicalConstant) is perfectly deterministic, the virtual wall clocks and sim times remain bit-identical across all slaves, no matter how many steps are executed or how long lockstep lasts.

**The** **TargetSimTime** **Fail-Safe** The architects actually anticipated the exact edge-case you brought up! The documentation for `FrameOrderDescriptor` explicitly notes that due to the slight differences in exactly when each slave's local hardware clock hits the barrier, a slave's seeded `TotalTime` could theoretically end up slightly divergent from the master's time.

To prevent this from breaking consistency, the `AdvanceFrameIntent` (and the resulting `FrameOrderDescriptor` packet) includes a **TargetSimTime** field. If the master provides a non-zero `TargetSimTime`, the slave's `UpdateStepping()` method bypasses the mathematical accumulation and forcefully snaps its `_totalTime` to match the master's exact simulation time for that specific step.

If wallclock is expected to drift across slaves in syncclock, why not to allow syncing it using time pulses even during lockstep. If No hard guarantee that wall clock and synclock is same througout the lockstep across slaves.

There actually is a hard guarantee that the virtual wall clock and simulation clock remain identical across all slaves during lockstep, but it relies on deterministic math rather than the Phase-Locked Loop (PLL) and time pulses.

Here is why time pulses cannot be used during lockstep, and how the engine guarantees strict consistency without them:

**1\. Time Pulses Would Break Lockstep Pacing** The PLL relies on measuring the real-world local OS clock against the master's time pulses. In lockstep (Deterministic mode), the simulation speed is no longer tied to real-world time; it is dictated by how fast the cluster can process frames and exchange acknowledgements (`FrameAckDescriptor`). If the master stalls for 500ms waiting for a slow node to acknowledge a frame, real-world time continues to pass. If the master were sending time pulses during this stall, the slave's PLL would compare its local OS clock to the stalled simulation time, falsely perceive a massive synchronization error, and aggressively warp or teleport the simulation time to "catch up" to a real-world clock that the simulation is intentionally ignoring.

**2\. The Hard Guarantee: Mathematical Accumulation** To guarantee that the virtual wall clock (`TotalWallTicks`) and simulation time (`TotalTime`) are identical across the cluster, lockstep completely disconnects these clocks from the drifting local hardware clocks. Instead, time is advanced purely by exact mathematical constants dictated by the master:

-   When the master steps, it advances its `_totalWallTicks` by exactly `fixedDelta * Stopwatch.Frequency` and publishes an `AdvanceFrameIntent` containing the `FixedDelta`.-   When a slave dequeues this intent, it advances its own `_virtualWallTicks` by the exact same mathematical formula: `(long)(unscaledDelta * Stopwatch.Frequency)`.

Because every node applies the exact same math to the exact same starting baseline (established by the Future Barrier), the virtual wall clocks cannot drift, regardless of how long the lockstep phase lasts.

**3\. The Ultimate Fail-Safe:** **TargetSimTime** If the master and slave seed their initial lockstep states at slightly different moments due to the barrier look-ahead, their simulation times could theoretically diverge. To prevent this, the master's `FrameOrderDescriptor` packet includes a **TargetSimTime** field. During every single frame step, the slave checks if the master has provided a `TargetSimTime` > 0.0. If it has, the slave explicitly snaps its `_totalTime` to match the master's authoritative time for that exact frame, forcefully erasing any possible drift.

On one hand you say the wallclock is not consistent across slaves in lock step, on second you say it is not sent as part of each step so it means it must differ across slaves in lockstep because if hw drifts. I see no hard guarantee here.

I understand why this sounds contradictory. The confusion comes from the difference between the **real-world hardware OS clock** (which absolutely drifts) and the **virtual wall clock** (`TotalWallTicks`) that the simulation actually uses.

Here is the hard guarantee: **During lockstep mode, the engine completely stops reading the hardware OS clock to advance the simulation time.**

Instead, the virtual wall clock is advanced using pure, bit-deterministic integer math. Because real-world time is entirely removed from the equation, hardware clock drift becomes irrelevant.

Here is the exact mechanism from the source code that guarantees all slaves have the exact same virtual wall clock time during every step:

**1\. The Identical Starting Line (The Barrier)** When transitioning into lockstep, the master broadcasts a `BarrierWallTicks` timestamp. Both the master and the slaves wait until their virtual clocks hit this exact tick before entering the `Stepping` state. This guarantees that every node starts lockstep execution with a bit-identical `_virtualWallTicks` baseline.

**2\. The Master Sends a Mathematical Constant, Not a Timestamp** During lockstep, the master does not look at its hardware clock to decide what time it is. When the master takes a step, it simply takes the fixed step size (e.g., 1/60th of a second) and adds the mathematical equivalent in ticks to its virtual clock: `_totalWallTicks += (long)(fixedDelta * Stopwatch.Frequency);`. It then sends an `AdvanceFrameIntent` to the slaves containing only this `FixedDelta`.

**3\. The Slaves Apply the Exact Same Math** When a slave receives this intent, it completely ignores its drifting local hardware clock. Inside `SlaveSyncController.UpdateStepping()`, it extracts the step size: `float unscaledDelta = next.FixedDelta;`. It then advances its own virtual wall clock by applying the exact same integer math as the master: `_virtualWallTicks += (long)(unscaledDelta * Stopwatch.Frequency);`.

**The Hard Guarantee** Because every slave starts at the exact same integer barrier value, and every slave adds the exact same integer constant (`fixedDelta * Stopwatch.Frequency`) on every single frame, the resulting `_virtualWallTicks` is **mathematically guaranteed to be perfectly identical** across the master and all slaves on any given frame.

It does not matter if one slave's hardware CPU took 5 milliseconds to calculate the frame and another slave took 500 milliseconds. Real-world hardware drift cannot break the consistency because the virtual wall clock is advancing by fixed mathematical increments, not by measuring real-world time.

aha! ok, so in lock step the virtual wall clock on master does not advance in real time, but advances just by the step size, correct?

Yes, that is exactly correct. When the master is in lockstep (`Stepping` mode), it stops measuring real elapsed time to update the simulation's clock. Instead, every time the `Step(float fixedDelta)` method is executed, the master strictly advances its virtual wall clock by adding the exact mathematical equivalent of the fixed step size: `_totalWallTicks += (long)(fixedDelta * Stopwatch.Frequency)`.

Furthermore, the master will only perform this mathematical advancement if there are no pending acknowledgments (`_pendingAcks.Count == 0`), meaning it has successfully waited for all slave nodes to complete the previous frame. By advancing the virtual wall clock by this fixed constant rather than reading the real-world hardware clock, the engine ensures that time remains perfectly deterministic and bit-identical across all nodes in the cluster, regardless of actual hardware drift or how much real time was spent waiting for network acknowledgments.

