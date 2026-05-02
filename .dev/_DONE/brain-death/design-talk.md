

now i see that "Center on entity" menu item teleports the map center far from the entity (seems like in the opposite direction, maybe)

why is that? Happens on SimHost running standalone, works ok on IG running standalone

The reason this is happening comes down to how Raylib's 2D camera maps world coordinates to screen pixels, specifically the camera's **Offset** property.

In a Raylib `Camera2D`, the `Target` is the point in the 2D world you want to look at, and the `Offset` is the exact pixel on your screen where that `Target` should be drawn.

Here is why they behave differently:

**1\. The IG Standalone (Working Correctly)** When you run the IG standalone, `IgApplication.InitializeEmbedded` explicitly configures the camera offset to be exactly half of the window's width and height:

```
// Offset keeps the world origin centred in the window.
_camera.Offset = new Vector2(WindowWidth / 2f, WindowHeight / 2f); 
```

Because of this, when the IG focuses on an entity, Raylib draws that entity perfectly in the middle of your screen.

**2\. The SimHost Standalone (The Bug)** In `SimHostVisualization.Initialize`, you instantiate the map canvas like this:

```
_map       = new MapCanvas();
```

However, you never configure the camera's offset. If you look at the `MapCamera` constructor, its default offset is `Vector2.Zero`.

When you click "Center on entity", `SimHostVisualization.CenterCameraOnEntity` correctly extracts the entity's position and calls `_map?.Camera.FocusOn(...)`. But because the `Offset` is `(0, 0)`, Raylib draws the entity exactly at screen pixel `(0, 0)`—which is the **top-left corner of your window**.

This abruptly shifts the map up and to the left to pin the entity to the top-left corner, giving you the illusion that the center of the map teleported far away in the opposite direction.

The Fix

To fix this, simply apply the same center offset to the SimHost's camera right after creating the canvas in `Hrot.SimHost.SimHostVisualization.Initialize`:

```
_map = new MapCanvas();
_map.AddResource(trajectoryPool);

// Add this line to center the camera target in the middle of the 1280x720 window:
_map.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f);
```

\[BUG\] Entities created from "SimHost Controls" panel are not moving. It happened after splitting the direct Navigation component into NagivationIntent in the 'brain' and the kinematics in the 'muscle' to be able to run the brain on different machine than the SimHost 'muscle". To solve similar issue with IOS entity spawning, I had to send a new mission plan to the entities, containing a single MoveToLocation task. It looks like the entity behavior infrastructure might be constantly overwriting the navigation intent although there is no real behavior driving it.

The reason entities spawned from the "SimHost Controls" panel are not moving is not because the behavior infrastructure is overwriting their navigation intent. In fact, those specific entities don't have any behavior infrastructure attached to them at all.

The root cause is a missing network ownership component that causes the ground kinematics engine to completely ignore them.

Here is the exact breakdown of what is happening:

**1\. The Flawed Spawn Path (****SpawnEntityLocal****)** When you use the "SimHost Controls" panel to spawn entities (via the "Spawn", "Road users", or "Collision test" buttons), the `SimHostScenarioManager` uses a legacy internal helper method called `SpawnEntityLocal`. `SpawnEntityLocal` manually creates a bare ECS entity and attaches basic physical components like `SimTransform`, `SimVelocity`, and `NavState`. However, it **fails to add a** **NetworkAuthority** component or mark the entity as locally owned.

**2\. The CQRS Authority Filter** As part of the CQRS split you mentioned, the `CarKinematicsSystem` (the 'muscle') was updated to strictly enforce split-authority across the network. Its ECS query now specifically uses the `.WithOwned<SimTransform>()` filter. Because your `SpawnEntityLocal` entities lack `NetworkAuthority`, this filter silently excludes them from the physics loop. The kinematics system never processes them, leaving their speed permanently at 0.

**Why your IOS fix worked (and why this is different):** You correctly observed that for IOS-spawned entities, you had to send a `MoveToLocation` mission plan to stop the brain pipeline from overwriting the navigation intent. That diagnosis is perfectly accurate _for fully networked entities_. IOS entities are spawned via `SpawnEntityCommand`, which routes through the `NetworkSpawningSystem` and fully applies the TKB blueprints. If those blueprints include active default brains (like the `TrafficBrainSystem` for civilians), they will run every frame, emit a `NavigationIntent`, and the `NavigationIntentBridgeSystem` will constantly overwrite any manual `NavState` legacy commands. Sending a `CMD_REPLACE_MISSION` correctly preempts the brain.

However, `SpawnEntityLocal` bypasses the TKB entirely. It never attaches `BehaviorState`, `LocomotionChannel`, or `NavigationIntent` to the entities. Because `NavigationIntent` is completely absent, the `NavigationIntentBridgeSystem` skips them entirely. The behavior system is physically incapable of overwriting their intent—they just lack the ownership required to be simulated by the muscle layer.

**How to Fix This:** You need to update `SimHostScenarioManager` so that `SpawnRoamers`, `SpawnRoadUsers`, and `SpawnCollisionTest` stop using `SpawnEntityLocal`.

Change them to use the existing **SpawnVehicle** method instead (just as you already did for `SpawnFastOne`). `SpawnVehicle` properly publishes a `SpawnEntityCommand`. This will trigger the `NetworkSpawningSystem` to apply the TKB blueprint, attach the mandatory `NetworkAuthority` component, and allow the `CarKinematicsSystem` to properly claim and move the vehicles.

Even if spawned properly and commanded via the mission task, when on standalone SimHost i use rigth click to define a new destination point, the entity starts moving the that direction towards the new destination but it does NOT stop moving if the destination is reached, overshoots it and then keeps turning back and trying over and over again.

The reason your entities overshoot the destination and loop endlessly is due to a combination of three distinct bugs across your UI, Mission, and Behavior systems.

Here is exactly what is happening under the hood, and how to fix all three issues.

The Immediate Cause: The Overshoot Loop

Because the navigation intent is never cleanly deactivated, the `NavigationIntentBridgeSystem` runs every frame and continuously forces `NavState.HasArrived = 0`. When the vehicle reaches the destination, the `CarKinematicsSystem` applies the brakes, but physical inertia causes the vehicle to slide slightly outside the precise `ArrivalRadius`. Because `HasArrived` is forced to `0`, the kinematics system immediately accelerates back to 15 m/s to correct the overshoot, causing the infinite loop.

To stop the loop, the `NavigationIntent` must be cleared to `NavigationMode.None`. However, three bugs prevent this from happening:

Bug 1: The Endless Task (Missing Triggers)

In `SimHostVisualization.cs`, the right-click handler creates a `MissionTask` for `MoveToLocation` but leaves the `Triggers` list empty. Under your data model's rules, `MissionControlRequestSystem.ResolveTrigger` assigns a fallback of `TimerElapsed(float.MaxValue)` to any empty trigger list. Therefore, the task literally never completes, and the BTree stays stuck in `MoveToLocation` forever.

Bug 2: The Missing Terminal Idle State

Even if you fix the trigger, the right-click logic only sends a **single-task** mission plan. When the `MissionDirectorSystem` completes the final task in a plan, it simply stops evaluating. Because there is no next phase, the entity's `BehaviorState` remains stuck on `MoveToLocation_BT`, keeping the intent alive.

Bug 3: The Orphaned Intent (Channel Arbitration Bug)

If you append an `Idle` task to the mission plan, the behavior will change. When a behavior changes, `ChannelArbitrationSystem` correctly detects the mismatch and clears the channel using `channel = default;`. However, wiping the struct to `default` sets both `ActionInstanceId = 0` and `DispatchedInstanceId = 0`. On the very next tick, `LocomotionDispatcherSystem` evaluates `0 != 0` as `false`, which **completely bypasses the** **OnExit** **lifecycle method**. Because `MoveToExecutor.OnExit` is never called, the `NavigationIntent` is never reverted to `NavigationMode.None`. The muscle layer is permanently orphaned and continues executing the old command forever.

\--------------------------------------------------------------------------------

How to Fix It

**1\. Fix the Channel Arbitration Bug** Open `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs`. Instead of wiping the structs to `default`, you must explicitly zero the action but increment the instance ID so the dispatchers fire their `OnExit` cleanup routines.

Update the Locomotion, Weapon, and Interaction channel blocks to look like this:

```
if (channel.ActiveAction != 0 && channel.BehaviorInstanceId != behavior.InstanceId)
{
    channel.ActiveAction = 0;
    unchecked { channel.ActionInstanceId++; } // Forces dispatcher to call OnExit
}
```

**2\. Fix the Right-Click Mission Plan in** **SimHostVisualization** Open `Hrot.SimHost/SimHostVisualization.cs`. In the `OnWorldClick` handler where the right-click mission is generated, you need to add the `ReachedDestination` trigger and append an `Idle` task so the behavior cleanly resets.

Replace the task generation block with this:

```
var taskId = Guid.NewGuid();

// Task 1: Move to the clicked location
var task = new MissionTask
{
    TaskId          = taskId,
    ExecutingEngine = "CGFX",
    BehaviorId      = "MoveToLocation",
    BehaviorParams  = paramsJson,
    Triggers        = new List<Hrot.NED.Descriptors.MissionTrigger>
    {
        // Tell the mission director to advance when we arrive
        new Hrot.NED.Descriptors.MissionTrigger { Type = "ReachedDestination", Params = "" }
    },
    State           = eTaskState.TASK_PLANNED,
};

// Task 2: Idle at the destination
var idleTask = new MissionTask
{
    TaskId          = Guid.NewGuid(),
    ExecutingEngine = "CGFX",
    BehaviorId      = "Idle",
    BehaviorParams  = "{}",
    Triggers        = new List<Hrot.NED.Descriptors.MissionTrigger>(),
    State           = eTaskState.TASK_PLANNED,
};

var plan = new MissionPlan
{
    ActiveTaskId = taskId,
    Tasks        = new List<MissionTask> { task, idleTask },
};
```

With these fixes, the vehicle will reach the destination, trigger the phase advancement, switch to the `Idle` behavior, correctly invoke `MoveToExecutor.OnExit`, clear the `NavigationIntent`, and come to a smooth, permanent stop.

I do not want any explicit Idle task - i thinks it is a wrong solution to another problem. If the behavior (behavior) finishes, the task should be marked finished and if there is no other task started via trigger, the entity should have no specific behavior, meaning nothing is stimulating any channel. I also need a way how to clear the behavior on the entity, so it enters the no-behavior state, executing no behavior, stimulating no channels, basically doing nothing special (like in case of 'brain death'). If i need the entity to start executing some idle behavior, we can assign en entity-specific "Idle" behavior or something which mest be overwritten by each newly triggered task (each new incoming behavior request)

Your architectural reasoning is absolutely correct. Relying on an explicit "Idle" task is a workaround. A completed mission or an aborted plan should naturally drop the entity into a "no-behavior" state (represented by `BehaviorIds.None = 0`), which should cleanly shut down all active channels.

The reason the entity stays stuck executing its last behavior is due to an omission in how your mission and command systems handle the end-of-plan state.

Here is exactly how to fix the systems so the entity enters the "brain death" state automatically when a mission ends, and how to trigger it manually.

1\. Fix the End-of-Mission Transition (`MissionDirectorSystem`)

Currently, when `MissionDirectorSystem` detects that a trigger has fired and the mission is over (`CurrentPhase >= PhaseCount`), it simply skips assigning a new behavior. Because it does nothing, the `BehaviorState` permanently retains the _last_ executing behavior (e.g., `MoveToLocation`), keeping the muscle layer permanently stimulated.

To fix this, we need to explicitly assign the "no-behavior" state when the queue runs out.

Open `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` and update the phase transition block:

```
if (triggered)
{
    queue.CurrentPhase++;
    queue.PhaseElapsedSeconds = 0f;

    // Load the next phase's behavior if there is one.
    if (queue.CurrentPhase < queue.PhaseCount)
    {
        unchecked { behavior.InstanceId++; }
        behavior.ActiveBehaviorHash = phases[queue.CurrentPhase].BehaviorId;
    }
    else
    {
        // FIX: Mission is completely finished. Transition to "no behavior".
        unchecked { behavior.InstanceId++; }
        behavior.ActiveBehaviorHash = BehaviorIds.None; // 0
    }
}
```

2\. Fix Explicit Behavior Clearing (`MissionControlRequestSystem`)

When you need to manually clear the behavior (e.g., clicking "ABORT" from the IOS), the system sends a `CMD_ABORT_ALL` command. Currently, `MissionControlRequestSystem` wipes the `MissionPlanQueue` to 0, but it completely forgets to wipe the `BehaviorState`.

Open `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` and add the behavior wipe to the `CMD_ABORT_ALL` block:

```
case eMissionCommandType.CMD_ABORT_ALL:
{
    var abortQueue = new MissionPlanQueue
    {
        PhaseCount = 0,
        CurrentPhase = 0,
        PhaseElapsedSeconds = 0f
    };
    repo.SetComponent(entity, abortQueue);
    repo.RemoveComponent<Hrot.SimHost.Components.EntityMissionHolder>(entity);

    // FIX: Explicitly wipe the behavior state so the entity goes "brain dead"
    if (repo.HasComponent<BehaviorState>(entity))
    {
        ref var doc = ref repo.GetComponentRW<BehaviorState>(entity);
        unchecked { doc.InstanceId++; }
        doc.ActiveBehaviorHash = FDP.Toolkit.Behavior.BehaviorIds.None; // 0
    }

    _taskOrder[request.TargetEntityId] = new List<Guid>();

    currentVersion++;
    _missionVersions[request.TargetEntityId] = currentVersion;

    WriteAck(request.RequestId, SstErrorCode.Success, errorMessage: null, newVersion: currentVersion);
    return;
}
```

Why this works perfectly now

With these two fixes (and assuming you applied the `ChannelArbitrationSystem` fix from earlier), the lifecycle will now correctly resolve itself:

-   The `MissionDirectorSystem` (or the Abort command) sets `ActiveBehaviorHash` to `0` and bumps the `InstanceId`.-   The `ChannelArbitrationSystem` detects the mismatch, zeroes the `ActiveAction` in the `LocomotionChannel`, and bumps the `ActionInstanceId`.-   The `LocomotionDispatcherSystem` detects the action change and fires `OnExit` for the outgoing behavior.-   `MoveToExecutor.OnExit` runs, resetting the `NavigationIntent` to `NavigationMode.None`.-   The muscle layer sees `NavigationMode.None`, stops steering, and the vehicle comes to a complete, permanent stop.

No RVO avoidance . after implementing teh Navigation state split into Navigation Intent and the 'muscle', Vehicle on simhost entities no longer avoid each other. For example the "Collision test".

The reason your vehicles are no longer avoiding each other is that they are entirely missing from the spatial collision grid.

This is actually not caused by the CQRS `NavigationIntent` split, but rather by an optimization added to the `SpatialHashSystem`.

Here is the exact breakdown of what is breaking your RVO avoidance and how to fix it:

**1\. The Missing** **PhysicsCollider** **Requirement** Recently, the `SpatialHashSystem` was optimized to only insert entities into the spatial grid if they carry both a `SimTransform` and a `PhysicsCollider` component. This was done to prevent non-collidable entities (like waypoints or cameras) from clogging up the broadphase neighbor queries.

**2\. The Omission in the SimHost TKB Builder** However, the `SimHost` vehicle templates defined in `BdcTkbBuilder` (specifically in the `WithPhysics` method) were never updated to attach this required `PhysicsCollider` component. Furthermore, if you are still using the legacy `SpawnEntityLocal` method for your "Collision test", it also fails to attach a `PhysicsCollider`.

**3\. The Resulting Avoidance Failure** Because the spawned vehicles lack a `PhysicsCollider`, the `SpatialHashSystem` silently ignores them. Consequently, the `SpatialGridData` singleton is completely empty. When the `CarKinematicsSystem` runs `ApplyCollisionAvoidance` and queries the grid for nearby vehicles, the query returns exactly **0 neighbors**. Since the RVO algorithm sees no neighbors, it applies zero avoidance force, and the vehicles drive directly into each other.

How to Fix It

To restore RVO avoidance, you must ensure that all spawned vehicles are stamped with a `PhysicsCollider`.

**Fix 1: Update the TKB Builder (For networked/standard vehicles)** Open `Hrot.Map.Definitions.Tkb.BdcTkbBuilder.cs` and update the `WithPhysics` method to automatically attach the collider based on the vehicle's dimensions:

```
public BdcTkbBuilder WithPhysics(long tkbId, Action<SimVehicleDef> configure)
{
    var template = _db.GetByType(tkbId);
    if (template == null)
        throw new InvalidOperationException($"Template {tkbId} not found");

    var physicsDef = new SimVehicleDef();
    configure(physicsDef);

    template.AddComponent(BuildVehicleParams(physicsDef));
    
    // FIX: Add the missing PhysicsCollider required by SpatialHashSystem for RVO
    template.AddComponent(new FDP.Toolkit.Physics.Components.PhysicsCollider 
    { 
        Radius = Math.Max(physicsDef.Length, physicsDef.Width) / 2f,
        CollisionLayer = 1 // Standard entity collision layer
    });

    return this;
}
```

**Fix 2: Update the Legacy Spawner (For the Collision Test)** If your "Collision test" button still uses the internal `SpawnEntityLocal` helper instead of proper TKB blueprints, you must also patch it in `SimHostScenarioManager.cs`:

```
private Entity SpawnEntityLocal(Vector2 position, Vector2 heading, VehicleClass vehicleClass = VehicleClass.PersonalCar)
{
    // ... existing setup ...
    _repo.AddComponent(e, preset);
    _repo.AddComponent(e, new NavState());

    // FIX: Attach the collider so RVO can see it
    _repo.AddComponent(e, new FDP.Toolkit.Physics.Components.PhysicsCollider { 
        Radius = Math.Max(preset.Length, preset.Width) / 2f, 
        CollisionLayer = 1 
    });

    return e;
}
```

Once the `PhysicsCollider` is attached, the vehicles will be correctly inserted into the spatial hash, the neighbor queries will succeed, and the RVO logic will smoothly steer them around each other.

Shift+Right click in SimHost perspective (or standalone SimHost) no longer adds points to planned vehicle trajectory. It looks like the point is added and drawn on the map for the first frame, but then disappear and the vehicle seems to act as if just right click was used (no shift) - new mission with moveToLocation gets generated..

The behavior you are describing is a direct consequence of the CQRS Brain/Muscle split, specifically a conflict between the legacy **Muscle-level** trajectory system and the new **Brain-level** mission system.

Here is exactly what is happening frame-by-frame and how to fix it so that Shift+Right-click correctly queues waypoints.

The Cause: Brain vs. Muscle Conflict

When you do a normal Right-click, `SimHostVisualization.cs` sends a `CMD_REPLACE_MISSION` containing a `MoveToLocation` task. This gives the vehicle a **Brain**, which constantly outputs a `NavigationIntent` towards that destination.

When you hold Shift and Right-click, the code takes a legacy path:

```
if (shift)
{
    _scenario!.AddWaypoint(e, pos, _ui!.UIState.InterpolationMode);
}
```

Here is why this fails:

-   **The 1-Frame Draw:** `AddWaypoint` talks directly to the "Muscle". It updates the physics `NavState` to `KinematicsMode.CustomTrajectory` and builds a spline. Because the mode is updated, the trajectory draws on the map for exactly one frame.-   **The Overwrite:** Meanwhile, the vehicle's Brain is still active! It is still executing the old `MoveToLocation` mission. On the very next tick, the `NavigationIntentBridgeSystem` reads the Brain's intent and forcibly overwrites `NavState.Mode` back to `KinematicsMode.Direct`, pushing the old destination back into the physics state.-   **The Disappearance:** Because the mode is no longer `CustomTrajectory`, the visualizer drops the spline line. The vehicle ignores your new point and continues driving to the old destination. This gives you the illusion that the Shift key was ignored and a "new" right-click mission was generated.

The Fix: Append to the Mission Plan

To properly queue waypoints for an AI-driven entity in the new architecture, Shift+Right-click must not bypass the Brain. Instead, it should extract the current mission plan, append a new `MoveToLocation` task to the end of it, and send a `CMD_REPLACE_MISSION` update.

Open `Hrot.SimHost/SimHostVisualization.cs` and replace the `_interactionTool.OnWorldClick` lambda block with the following:

```
_interactionTool.OnWorldClick += (pos, btn, shift, ctrl, hitEntity) =>
{
    if (btn != MouseButton.Right) return;
    var entities = new List<Fdp.Kernel.Entity>(_selection.SelectedEntities);
    if (entities.Count == 0) return;

    foreach (var e in entities)
    {
        if (!repo.IsAlive(e)) continue;
        if (!repo.HasComponent<NetworkIdentity>(e)) continue;
        
        ref readonly var netId = ref repo.GetComponentRO<NetworkIdentity>(e);

        float speed = repo.HasComponent<VehicleParams>(e)
            ? repo.GetComponentRO<VehicleParams>(e).MaxSpeedFwd * 0.8f
            : 15f;

        var paramsJson = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{{\"X\":{0},\"Y\":{1},\"Speed\":{2},\"ArrivalRadius\":3.0}}",
            pos.X, pos.Y, speed);

        var taskId = Guid.NewGuid();
        var newTask = new MissionTask
        {
            TaskId          = taskId,
            ExecutingEngine = "CGFX",
            BehaviorId      = "MoveToLocation",
            BehaviorParams  = paramsJson,
            // Trigger is required so the BTree knows when to advance to the next queued waypoint
            Triggers        = new List<Hrot.NED.Descriptors.MissionTrigger>
            {
                new Hrot.NED.Descriptors.MissionTrigger { Type = "ReachedDestination", Params = "" }
            },
            State           = eTaskState.TASK_PLANNED,
        };

        MissionPlan plan;

        if (shift && repo.HasComponent<EntityMissionHolder>(e))
        {
            var holder = repo.GetComponent<EntityMissionHolder>(e);
            
            // Clone the existing plan to safely append to it
            plan = new MissionPlan
            {
                ActiveTaskId = holder.Mission.Plan.ActiveTaskId,
                Tasks = new List<MissionTask>(holder.Mission.Plan.Tasks ?? new List<MissionTask>())
            };

            // Ensure the previous task has a completion trigger, otherwise it will never advance to our new point!
            if (plan.Tasks.Count > 0)
            {
                var lastTask = plan.Tasks[^1];
                if (lastTask.Triggers == null || lastTask.Triggers.Count == 0)
                {
                    lastTask.Triggers = new List<Hrot.NED.Descriptors.MissionTrigger>
                    {
                        new Hrot.NED.Descriptors.MissionTrigger { Type = "ReachedDestination", Params = "" }
                    };
                    plan.Tasks[^1] = lastTask;
                }
            }
            plan.Tasks.Add(newTask);
        }
        else
        {
            // Standard click (no shift) -> Replace entirely with a single task
            plan = new MissionPlan
            {
                ActiveTaskId = taskId,
                Tasks        = new List<MissionTask> { newTask },
            };
        }

        _missionWriter!.Write(new MissionControlRequest
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = netId.Value,
            BaseVersion    = 0,
            Payload        = new MissionCommandUnion
            {
                _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = plan,
            },
        });
    }
};
```

This completely resolves the conflict. Shift+Right-clicks will now generate sequential multi-phase missions that the BTree executor will naturally advance through as each destination is reached.

Well - although adding task to mission is an alternative, i would rather do the following: if there is no behavior (brain death mode), this Shift+Right click was working like before, talking directly to the muscle (just for the sake of restoring the old functionality which was 'broken' by the introduction of the NacitationIntention split and the behaviros. It shouold be possible as under brain-death conditions there should be no coflict, correct?

You are absolutely correct in your reasoning.

Under "brain death" conditions (where the entity has no active behavior), there is no conflict between the brain and the muscle. This is because the **NavigationIntentBridgeSystem** **explicitly skips entities whose** **NavigationIntent.Mode** **is** **None**. If the brain is inactive, it stops projecting a `NavigationIntent`, which leaves the muscle layer's `NavState` completely free to be manipulated by direct legacy commands like your `_scenario.AddWaypoint()`.

You can easily restore the old functionality by adding a simple branching check in your `SimHostVisualization` right-click handler. If the entity has no active behavior, you bypass the mission system and talk directly to the muscle.

Here is how you can update the `_interactionTool.OnWorldClick` handler in `Hrot.SimHost/SimHostVisualization.cs` to achieve exactly this:

```
_interactionTool.OnWorldClick += (pos, btn, shift, ctrl, hitEntity) =>
{
    if (btn != MouseButton.Right) return;
    var entities = new List<Fdp.Kernel.Entity>(_selection.SelectedEntities);
    if (entities.Count == 0) return;

    foreach (var e in entities)
    {
        if (!repo.IsAlive(e)) continue;

        // 1. Check if the entity is in "brain death" mode
        bool isBrainDead = true;
        if (repo.HasComponent<BehaviorState>(e))
        {
            var behavior = repo.GetComponentRO<BehaviorState>(e);
            // BehaviorIds.None == 0
            isBrainDead = behavior.ActiveBehaviorHash == FDP.Toolkit.Behavior.BehaviorIds.None; 
        }

        // 2. Branch based on brain state
        if (isBrainDead)
        {
            // [MUSCLE PATH] Brain is dead. Talk directly to the kinematics layer.
            if (shift)
            {
                _scenario!.AddWaypoint(e, pos, _ui!.UIState.InterpolationMode);
            }
            else
            {
                _scenario!.SetDestination(e, pos, _ui!.UIState.InterpolationMode);
            }
        }
        else
        {
            // [BRAIN PATH] Brain is active. Command it via Mission plans.
            if (!repo.HasComponent<NetworkIdentity>(e)) continue;
            ref readonly var netId = ref repo.GetComponentRO<NetworkIdentity>(e);

            float speed = repo.HasComponent<VehicleParams>(e)
                ? repo.GetComponentRO<VehicleParams>(e).MaxSpeedFwd * 0.8f
                : 15f;

            var paramsJson = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{{\"X\":{0},\"Y\":{1},\"Speed\":{2},\"ArrivalRadius\":3.0}}",
                pos.X, pos.Y, speed);

            var taskId = Guid.NewGuid();
            var task = new MissionTask
            {
                TaskId          = taskId,
                ExecutingEngine = "CGFX",
                BehaviorId      = "MoveToLocation",
                BehaviorParams  = paramsJson,
                Triggers        = new List<Hrot.NED.Descriptors.MissionTrigger>(),
                State           = eTaskState.TASK_PLANNED,
            };

            var plan = new MissionPlan
            {
                ActiveTaskId = taskId,
                Tasks        = new List<MissionTask> { task },
            };

            _missionWriter!.Write(new MissionControlRequest
            {
                RequestId      = Guid.NewGuid(),
                TargetEntityId = netId.Value,
                BaseVersion    = 0,
                Payload        = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = plan,
                },
            });
        }
    }
};
```

**Why this is a solid solution:** By checking `behavior.ActiveBehaviorHash == BehaviorIds.None`, you cleanly support both paradigms. Your "Collision test" vehicles (which lack behaviors) and your aborted/brain-dead networked entities will immediately use the legacy spline-drawing `_scenario.AddWaypoint()` logic, while fully active AI units will continue to receive properly formatted mission updates.




---------------------
the clear-droctrine should be similar kindof command/event as the one for setting a behavior - i think there is some internal fdp event for assigning a behavior, so it should complemented with some cancel or clear one. it shoudl not be tied directly toa high level CMD\_ABORT\_ALL as this is different (higher) layer of control - the mission level.

You are absolutely right. Architecturally, the mission layer (`MissionDirectorSystem` and `MissionControlRequestSystem`) should not be directly micromanaging the `BehaviorState` components. That violates the separation of concerns between the Mission tier and the Cognitive/Behavior tier.

The internal FDP event you are thinking of is **AssignBehaviorEvent**, which is consumed by the **BehaviorIngressSystem**. To implement this cleanly, we should introduce a complementary **ClearBehaviorEvent** that the `BehaviorIngressSystem` can handle, allowing any higher-level system to request a "brain death" state through the proper channels.

Here is how to implement the clean, event-driven approach:

1\. Create the `ClearBehaviorEvent`

Create a new managed event class mirroring `AssignBehaviorEvent` in `FDP/Toolkits/FDP.Toolkit.Behavior/Events/ClearBehaviorEvent.cs`:

```
using Fdp.Kernel;

namespace FDP.Toolkit.Behavior.Events
{
    /// <summary>
    /// Managed event that requests the removal of any active behavior from an entity,
    /// placing it into a "brain dead" / idle state.
    /// Consumed synchronously by BehaviorIngressSystem.
    /// </summary>
    public sealed class ClearBehaviorEvent
    {
        public Entity Entity;
    }
}
```

2\. Update `BehaviorIngressSystem` to Handle the Clear Event

Open `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs` and update the `OnUpdate` method to consume and process the new clear events alongside the assign events:

```
protected override unsafe void OnUpdate()
{
    // ... existing AssignBehaviorEvent logic ...

    // ── Process ClearBehaviorEvents ──────────────────────────────────────
    var clearEvents = World.Bus.ConsumeManaged<ClearBehaviorEvent>();
    foreach (var evt in clearEvents)
    {
        if (evt == null) continue;
        if (!World.HasComponent<BehaviorState>(evt.Entity)) continue;

        ref var behavior = ref World.GetComponentRW<BehaviorState>(evt.Entity);
        
        // Wipe the active behavior
        behavior.ActiveBehaviorHash = BehaviorIds.None; // 0
        unchecked { behavior.InstanceId++; }            // Bump token to trigger channel arbitration preemption
        behavior.BrainTier = 0;                         // Reset tier

        // Reset BTree execution pointer if it exists
        if (World.HasComponent<BrainBTreeState>(evt.Entity))
        {
            ref var btState = ref World.GetComponentRW<BrainBTreeState>(evt.Entity);
            btState.State = default;
        }
    }
}
```

3\. Dispatch the Event from the Mission Layer

Now you can revert the explicit `BehaviorState` manipulation I suggested earlier, and replace it with publishing this new event.

**For** **MissionDirectorSystem** **(When a mission finishes naturally):** Open `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` and update the phase transition block:

    ```
    // ... inside phase transition ...
    if (queue.CurrentPhase < queue.PhaseCount)
    {
        unchecked { behavior.InstanceId++; }
        behavior.ActiveBehaviorHash = phases[queue.CurrentPhase].BehaviorId;
    }
    else
    {
        // Mission is completely finished. Publish event to clear the brain.
        World.Bus.PublishManaged(new FDP.Toolkit.Behavior.Events.ClearBehaviorEvent { Entity = entity });
    }
```

**For** **MissionControlRequestSystem** **(When an abort is explicitly commanded):** Open `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` and update the `CMD_ABORT_ALL` case:

```
case eMissionCommandType.CMD_ABORT_ALL:
{
    var abortQueue = new MissionPlanQueue
    {
        PhaseCount = 0,
        CurrentPhase = 0,
        PhaseElapsedSeconds = 0f
    };
    repo.SetComponent(entity, abortQueue);
    repo.RemoveComponent<Hrot.SimHost.Components.EntityMissionHolder>(entity);

    // Properly instruct the cognitive layer to clear the behavior
    repo.Bus.PublishManaged(new FDP.Toolkit.Behavior.Events.ClearBehaviorEvent { Entity = entity });
    
    _taskOrder[request.TargetEntityId] = new List<Guid>();
    
    currentVersion++;
    _missionVersions[request.TargetEntityId] = currentVersion;
    
    WriteAck(request.RequestId, SstErrorCode.Success, errorMessage: null, newVersion: currentVersion);
    return;
}
```

This properly adheres to the FDP architecture: the Mission systems maintain the `MissionPlanQueue`, and when they dictate a behavioral change (or cessation), they emit an event that the `BehaviorIngressSystem` translates into actual `BehaviorState` and `BrainBTreeState` resets. The `ChannelArbitrationSystem` will then cleanly shut down the locomotion muscle.

\[BUG\] DisType in badira sst entity master DDS topic struct is represented a plain long. It should be a @final structure with fields (kind, country etc, all 8 fields 1 byte each) to be easily readable in DDS monitoring tool. Inside the engine in the entity header the DIS type should be stores as FDP-specific fixed-layout memory struct (different from the DDS type as FDP should not be dependent on any project-specific DDS network) (for easy display during debug). But for quick filtering it might support fast comparison in the entity queries using the struct cast to long or something performance effective.

The engine side of your architecture actually already implements the exact fixed-layout optimization you are describing!

In **Fdp.Kernel**, the `DISEntityType` is defined using `[StructLayout(LayoutKind.Explicit, Size = 8)]`. This explicitly overlays the 8 individual DIS fields (Kind, Domain, Country, Category, Subcategory, Specific, Extra) directly on top of a single 64-bit `ulong Value` field at `[FieldOffset(0)]`. This memory layout allows the `EntityQuery` enumerator to perform lightning-fast filtering by applying a single CPU instruction: `(header.DisType.Value & _disFilterMask) != _disFilterValue`.

However, to fix the DDS side so that monitoring tools can easily read the struct, you need to update your **Hrot.NED** and the boundary translators to use a dedicated `@final` DDS struct instead of a plain `ulong`.

1\. Update the DDS Data Model

In `Hrot.NED/GenericDescriptors.cs`, define the new struct and update the `EntityMaster` topic. CycloneDDS will automatically treat simple structs as `@final` (non-extensible) if you don't add extensibility annotations.

```
using CycloneDDS.Schema;

namespace Hrot.NED.Descriptors
{
    // New @final struct for the DDS wire
    [DdsStruct]
    [DdsIdlFile("bdc-sst-generic-desc")]
    public partial struct DisTypeStruct
    {
        public byte Kind;
        public byte Domain;
        public ushort Country;
        public byte Category;
        public byte Subcategory;
        public byte Specific;
        public byte Extra;
    }

    [DdsTopic("EntityMaster")]
    [DdsIdlFile("bdc-sst-generic-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityMaster
    {
        [DdsKey]
        public int EntityId;
    
        public long TkbType;
    
        // FIX: Replaced plain ulong with the new struct
        public DisTypeStruct DisType; 
    
        public ulong Flags;
    }
}
```

2\. Update the Egress Translator

In `Hrot.Map.Common/Replication/Egress/EntityMasterEgressTranslator.cs`, map the FDP engine's `DISEntityType` to your new DDS struct before publishing.

```
// Read the full struct from the engine header instead of just the Value
DISEntityType dis = repo != null 
    ? repo.GetHeader(entity.Index).DisType 
    : default;

_writer.Write(new EntityMaster
{
    EntityId = (int)netId.Value,
    TkbType = tkb.TkbType,
    DisType = new DisTypeStruct 
    {
        Kind = dis.Kind,
        Domain = dis.Domain,
        Country = dis.Country,
        Category = dis.Category,
        Subcategory = dis.Subcategory,
        Specific = dis.Specific,
        Extra = dis.Extra
    },
    Flags = 0
});
```

3\. Update the Ingress Translators

When receiving the `EntityMaster` from DDS, map the 8 fields back into the FDP engine's fast overlay struct. You will need to update this wherever `EntityMaster` is decoded (such as `EntityMasterIngressTranslator.cs` or `DescriptorMapper.cs`).

```
// Pack the DDS struct fields back into the engine's memory-aligned type
var engineDisType = new DISEntityType
{
    Kind = master.DisType.Kind,
    Domain = master.DisType.Domain,
    Country = master.DisType.Country,
    Category = master.DisType.Category,
    Subcategory = master.DisType.Subcategory,
    Specific = master.DisType.Specific,
    Extra = master.DisType.Extra
};

// The underlying .Value property is automatically populated via the memory overlay
repo.SetDisType(entity, engineDisType); 
``` 

_(Note: You will also need to apply the exact same field mapping to_ _ModuleHost.Network.Cyclone.Topics.EntityMasterTopic_ _and its corresponding translators in the_ _FDP/ModuleHost_ _namespace to keep the core networking layer aligned with your_ _Hrot_ _app layer)_.

\[IMPROVEMENT\] Entity inspector component change indication If the component content has changed since previous frame, the component's imgui representation should indicate it somehow (maybe changing the text color of the component's expander line for that frame when change was detected) For unmanaged component simple caching and memcmp might be ok; for managed ones it might be easier to ignore the change detection whatsoever as as managed ones can be very large and on each change we would need to clone a copy.

To implement this improvement, we need to modify the **ComponentReflector** class located in `FDP/Toolkits/FDP.Toolkit.ImGui/Utils/ComponentReflector.cs`.

We will introduce a stateful cache that tracks the raw bytes of unmanaged components. For every frame, if the inspected entity hasn't changed, we marshal the unmanaged structs to memory, compare them byte-by-byte with the cache, and flag any differences. When a difference is detected, we push a highlight color (e.g., Yellow) to the ImGui text stack right before drawing the `CollapsingHeader`. Managed components (classes) are skipped entirely to avoid expensive cloning.

Here is the updated implementation for `ComponentReflector.cs`:

```
using System.Numerics;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.ImGui.Renderers;
using ImGuiNET;

using ImGuiApi = ImGuiNET.ImGui;

namespace FDP.Toolkit.ImGui.Utils;

/// <summary>
/// Helper that draws all ECS components attached to an entity as collapsible headers,
/// with optional custom per-component summary and details renderers (auto-discovered
/// via <see cref="ImGuiRendererRegistry"/>).
/// </summary>
internal class ComponentReflector
{
    /// <summary>Set to <c>true</c> this frame to force-expand all component headers.</summary>
    public bool ForceExpandAll   { get; set; }

    /// <summary>Set to <c>true</c> this frame to force-collapse all component headers.</summary>
    public bool ForceCollapseAll { get; set; }
    
    // ── State for change detection ────────────────────────────────────────────
    
    private Entity _lastInspectedEntity = Entity.Null;
    private readonly Dictionary<Type, byte[]> _unmanagedCache = new();
    
    /// <summary>
    /// Draws all components attached to <paramref name="e"/> as collapsible sections.
    /// Consumes <see cref="ForceExpandAll"/> / <see cref="ForceCollapseAll"/> after rendering.
    /// </summary>
    public void DrawComponents(IInspectableSession session, Entity e)
    {
        // If the operator selected a new entity, invalidate the change-detection cache
        if (e != _lastInspectedEntity)
        {
            _unmanagedCache.Clear();
            _lastInspectedEntity = e;
        }
    
        var allTypes = session.GetAllComponentTypes().OrderBy(t => t.Name).ToList();
    
        int componentIndex = 0;
        foreach (var type in allTypes)
        {
            if (!session.HasComponent(e, type)) continue;
    
            // Push a stable unique ID scope
            ImGuiApi.PushID(componentIndex++);
    
            // Apply bulk open/close request for this header
            if (ForceExpandAll)
                ImGuiApi.SetNextItemOpen(true,  ImGuiCond.Always);
            else if (ForceCollapseAll)
                ImGuiApi.SetNextItemOpen(false, ImGuiCond.Always);
    
            object? data  = session.GetComponent(e, type);
            string  label = BuildHeaderLabel(type, data);
    
            // ── Change Detection ──────────────────────────────────────────────
            bool changed = false;
            // Only perform byte-comparison for unmanaged structs (Tier 1). 
            // Managed classes are intentionally bypassed to avoid deep-clone overhead.
            if (data != null && type.IsValueType)
            {
                changed = CheckAndUpdateCache(type, data);
            }
    
            if (changed)
            {
                // Highlight the expander text in Yellow if data mutated since last frame
                ImGuiApi.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));
            }
    
            // Headers are collapsed by default (no DefaultOpen flag)
            bool open = ImGuiApi.CollapsingHeader(label);
    
            if (changed)
            {
                ImGuiApi.PopStyleColor();
            }
    
            if (open && data != null)
            {
                ImGuiApi.Indent();
    
                var renderer = ImGuiRendererRegistry.GetRenderer(type);
                bool handled = renderer != null && renderer.RenderValue(data);
    
                if (!handled)
                    ImGuiPropertyTree.Render(data, contextType: type);
    
                ImGuiApi.Unindent();
            }
    
            ImGuiApi.PopID();
        }
    
        ForceExpandAll   = false;
        ForceCollapseAll = false;
    }
    
    /// <summary>
    /// Marshals the component to a temporary unmanaged pointer, compares it against 
    /// the cached bytes, and updates the cache if differences are found.
    /// Allocates the managed byte array exactly once per component type per entity.
    /// </summary>
    private bool CheckAndUpdateCache(Type type, object data)
    {
        int size = System.Runtime.InteropServices.Marshal.SizeOf(type);
        IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
        
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(data, ptr, false);
    
            bool changed = false;
            if (!_unmanagedCache.TryGetValue(type, out var cachedBytes) || cachedBytes.Length != size)
            {
                // First time we are inspecting this component on this entity.
                // Store its baseline state but do not flag it as "changed" to prevent initial flashing.
                cachedBytes = new byte[size];
                _unmanagedCache[type] = cachedBytes;
                System.Runtime.InteropServices.Marshal.Copy(ptr, cachedBytes, 0, size);
                return false;
            }
    
            // Simple Memcmp equivalent
            for (int i = 0; i < size; i++)
            {
                if (cachedBytes[i] != System.Runtime.InteropServices.Marshal.ReadByte(ptr, i))
                {
                    changed = true;
                    break;
                }
            }
    
            // Update the cache so it stops flashing if it stays stable on the next frame
            if (changed)
            {
                System.Runtime.InteropServices.Marshal.Copy(ptr, cachedBytes, 0, size);
            }
    
            return changed;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
        }
    }
    
    private static string BuildHeaderLabel(Type type, object? data)
    {
        if (data == null) return type.Name;
    
        var renderer = ImGuiRendererRegistry.GetRenderer(type);
        string? summary = renderer?.GetSummary(data);
    
        if (!string.IsNullOrEmpty(summary))
            return $"{type.Name}  [{summary}]";
    
        string? auto = GetAutoSummary(data, type);
        return auto != null ? $"{type.Name}  {auto}" : type.Name;
    }
    
    private static string? GetAutoSummary(object data, Type type)
    {
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string))
            .Take(3)
            .ToArray();
    
        if (fields.Length == 0) return null;
    
        var parts = fields.Select(f =>
        {
            var v  = f.GetValue(data);
            string vs = v is float  fl ? fl.ToString("G4")
                      : v is double db ? db.ToString("G4")
                      : v?.ToString() ?? "null";
            return $"{f.Name}:{vs}";
        });
    
        return "(" + string.Join("  ", parts) + ")";
    }
}
```

How this works:

-   **Entity Tracking:** It keeps track of `_lastInspectedEntity` so that the cache is cleanly flushed when the operator selects a different entity on the map/list.-   **Type Enforcement:** It enforces `if (type.IsValueType)` which skips change-detection logic on Tier 2 managed component classes, eliminating expensive deep clones and reflection sweeps.-   **Memory Optimization:** Inside `CheckAndUpdateCache`, the `cachedBytes = new byte[size]` is allocated exactly _once_ per component type per session. On subsequent frames, it safely marshals the struct to a temporary unmanaged pointer (`AllocHGlobal`) and runs an inline byte-compare directly from that pointer, avoiding allocating temporary arrays on every frame's render path.

\[BUG\] loan object is created every tick on the hot path!

```
internal sealed class DdsCreateEntityRequestSource : ICreateEntityRequestSource
{
    private readonly DdsReader<CreateEntityRequest> _reader;

    public DdsCreateEntityRequestSource(DdsParticipant participant)
        => _reader = new DdsReader<CreateEntityRequest>(participant);
    
    public void ProcessRequests(Action<CreateEntityRequest> processor)
    {
        using var loan = _reader.Take(); // ALLOCATION?
        foreach (var sample in loan)
            if (sample.IsValid)
                processor(sample.Data);
    
        // AUTO-DISPOSE
    }
}
```

do we have many occurences in the codebase? How to convert it to zero-allocation?

Yes, this exact pattern is ubiquitous in your codebase. You will find `using var loan = _reader.Take();` (or `Take(maxSamples)`) in nearly every ingress translator and polling loop, including:

-   `AutoCycloneTranslator<T>.PollIngress`-   `EntityMasterIngressTranslator.PollIngress`-   `WorldPosIngressTranslator.PollIngress`-   `MissionControlRequestSystem.OnUpdate`-   `DdsIdAllocatorServer.ProcessRequests`

How to convert it to zero-allocation

The good news is that **the** **loan** **object itself is already zero-allocation.** You do not need to change or convert this pattern.

Here is exactly why `using var loan = _reader.Take();` does not allocate memory on the heap (the hot path):

-   **Stack-Allocated Structs:** Your project uses `FastCycloneDds` (an optimized wrapper). In this library, `_reader.Take()` returns a memory loan wrapper (a `struct` or `ref struct`), not a managed class. It lives entirely on the stack.-   **No Boxing on** **Dispose****:** Because the loan is a struct, the `using` keyword calls `Dispose()` directly at the end of the block. The C# compiler does not box the struct into an `IDisposable` interface, completely avoiding Garbage Collection (GC) pressure.-   **No Boxing on** **foreach****:** The struct returned by `Take()` implements a duck-typed `GetEnumerator()` that also returns a struct. The `foreach` loop iterates over the unmanaged pointers without allocating a standard enumerator on the heap.

The Catch: Payload Allocations

While the `loan` object and the loop are 100% allocation-free, the **payload data** (`sample.Data`) might allocate depending on the DDS topic schema.

-   **Zero-Allocation Topics:** For strictly unmanaged topics (like `FireInteractionEvent` or `WorldPos`), the entire process from the network wire into your ECS is genuinely zero bytes allocated. Your `CycloneNativeEventTranslator` explicitly highlights this fast, zero-alloc path.-   **Managed Topics:** In your specific snippet, the topic is `CreateEntityRequest`. If you look at its schema, it contains `[DdsManaged]` fields like `List<EntityDescriptorUnion>` and `string?`. When CycloneDDS deserialises this specific message, it _must_ allocate heap memory for the list and the string.

**Conclusion:** The allocation you might be seeing in a profiler is caused by CycloneDDS materialising the `List<>` and `string` fields inside `CreateEntityRequest`, not by the `loan` or the `Take()` method. The `using var loan = _reader.Take();` pattern is already perfectly optimized for the hot path and should be left exactly as it is.

\[BUG\] allocation on hot path

Hrot.SimHost\\Systems\\CreateEntityRequestSystem.cs creates lambda every tick

```
    public void Execute(ISimulationView view, float deltaTime)
    {
        // ── Phase 1: Drain all incoming requests this frame ───────────────
        // The callback fires synchronously for each valid DDS sample so no
        // List<CreateEntityRequest> is ever allocated on ingress (GC03).
        // Each valid request is validated, ID-allocated, ACK'd, and enqueued
        // on the same frame it arrives, giving the requester minimum latency (GC04).
        _requestSource.ProcessRequests(request => // <====== ALLOCATION here
        {
```

Such "hidden" allocations need to be eliminated on the hot path!

You are absolutely correct to spot this. While this lambda does not capture any local variables from the `Execute` method (which avoids allocating a hidden closure class), it **does capture instance fields** (`_tkbDb`, `_idAllocator`, `_ackSink`, `_pendingQueue`).

Because it captures `this`, the C# compiler cannot cache the delegate statically. Instead, it allocates a new `Action<CreateEntityRequest>` delegate on the heap every single frame, resulting in unnecessary Gen0 garbage collection pressure.

To completely eliminate this hot-path allocation, you should extract the lambda into a private method and manually cache the delegate in a `readonly` field during the system's construction.

Here is the zero-allocation fix for **Hrot.SimHost\\Systems\\CreateEntityRequestSystem.cs**:

1\. Add a cached delegate field

At the top of your class, add a `readonly` field to hold the delegate:

```
private readonly Action<CreateEntityRequest> _processRequestDelegate;
```

2\. Initialize the delegate in the constructor

Inside your constructor, assign the delegate to the new private method we will create:

```
public CreateEntityRequestSystem(
    ICreateEntityRequestSource requestSource,
    // ... other parameters ...
    BinaryInterpreter? binaryInterpreter = null)
{
    // ... existing assignments ...

    // Cache the delegate once at startup to avoid per-frame allocations
    _processRequestDelegate = ProcessIncomingRequest; 
}
```

3\. Update the `Execute` method

Replace the inline lambda with the cached delegate:

```
public void Execute(ISimulationView view, float deltaTime)
{
    // ── Phase 1: Drain all incoming requests this frame ───────────────
    // Now 100% allocation-free on the hot path.
    _requestSource.ProcessRequests(_processRequestDelegate);

    // ── Phase 2: Time-sliced spawn dispatch ────────────────────────────
    int toProcess = Math.Min(_pendingQueue.Count, MaxRequestsPerTick);
    for (int i = 0; i < toProcess; i++)
        ProcessPendingRequest(view, _pendingQueue.Dequeue());
}
```

4\. Extract the lambda into a private method

Move the original lambda body into a dedicated method:

```
private void ProcessIncomingRequest(CreateEntityRequest request)
{
    try
    {
        // Validate TkbType presence.
        long tkbType = DescriptorMapper.ExtractTkbType(
            request.InitialDescriptors, out ulong disType);
            
        if (tkbType == 0)
        {
            FdpLog<CreateEntityRequestSystem>.Warn(
                $"[SimHost] CreateEntity {request.RequestId}: No EntityMaster descriptor or TkbType=0. Rejecting.");
            SendErrorAck(request.RequestId, errorCode: 400);
            return;
        }
    
        // Validate TkbType exists in the database.
        if (!_tkbDb.TryGetByType(tkbType, out _))
        {
            FdpLog<CreateEntityRequestSystem>.Warn(
                $"[SimHost] CreateEntity {request.RequestId}: TkbType={tkbType} not found. Rejecting.");
            SendErrorAck(request.RequestId, errorCode: 404);
            return;
        }
    
        // Allocate a network ID and immediately ACK.
        long newNetworkId = _idAllocator.AllocateId();
        _ackSink.WriteAck(new CreateEntityAck
        {
            RequestId   = request.RequestId,
            NewEntityId = (int)newNetworkId,
            ErrorCode   = 0,
        });
    
        _pendingQueue.Enqueue(new PendingRequest
        {
            Request   = request,
            NetworkId = newNetworkId,
            TkbType   = tkbType,
            DisType   = disType,
        });
    }
    catch (Exception ex)
    {
        FdpLog<CreateEntityRequestSystem>.Error(
            $"[SimHost] CreateEntity ingress failed for request {request.RequestId}: {ex.Message}");
        SendErrorAck(request.RequestId, errorCode: 500);
    }
}
```

By caching the delegate at construction, you satisfy the FDP framework's strict zero-allocation requirement for hot-path ticks. The same `Action<CreateEntityRequest>` instance will be safely reused across all 60 frames per second.

