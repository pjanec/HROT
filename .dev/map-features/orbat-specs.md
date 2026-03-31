See [Changes to be integrated](#changes-to-be-integrated) at the end of the document.


# Design Document: BDC SST ORBAT & Mission Control System

**Version:** 1.0

**Status:** Draft

**Context:** Migration from GBB (Shared Memory) to BDC SST (DDS/ECS)

## 1. Executive Summary

This document defines the implementation of the **Order of Battle (ORBAT)** hierarchy and **Entity Mission Control** and mechanism of **Changing Entity State** within the BDC SST (Simulation State) architecture. The goal is to replace the direct GBB control with a DDS-based BDC SST approach.

The system allows distributed components (GUI, CGF) to interact asynchronously via DDS middleware, utilizing partial ownership of entities to manage logical command structures and tactical behavior definitions.


### MAIN PARTS
1.  ORBAT parent id stored in `EntityInfo` descriptor
2.  Missions stored in `EntityMission` descriptor, holding list of `MissionTasks` (each having triggers and a doctrine)
3.  Specific mission editing message `MissionControlRequest` allowing to add a task, reset to specific task etc.
4.  Generic entity state change request via `UpdateEntityDescriptorRequest`  for changing any entity descriptor (similar to SendDescriptorAsMessage as in GBB). Acknowledged by `UpdateEntityDescriptorAck`.
5.  Generic entity creation request via `CreateEntityRequest` carrying set of desriptors. Acknowledged by `CreateEntityAck` carrying the newly created entity id.

### MISSING PARTS (to be designed)
1. JSON schema for most commonly used task trigger parameters
2. JSON schema for most commonly used doctrine parameters

## 2. Architectural Concepts

### 2.1. The ECS Pattern in SST

In BDC SST, an "Entity" is not a monolithic object. It is a composition of loosely coupled Descriptors (DDS Topics) tied together by a common `EntityId`.

- **EntityMaster:** Controls the lifecycle. If this descriptor is disposed, the entity ceases to exist.
- **Partial Ownership:** A Simulation Node (e.g., CGF) may own the `EntityMission` descriptor, while a different node (e.g., GUI or Umpire) might request changes to `EntityInfo`.

See [BDC SST rules](/Products-and-components/Hrot.Infra/BDC/BDC-SST-Principles)
See [BDC SST Data Model Basics](/Products-and-components/Hrot.Infra/BDC/BDC-SST-Data-Model-Basics)

## 3. Data Model: ORBAT (Order of Battle)

The ORBAT represents the **logical command hierarchy**, which is distinct from physical attachments (turrets attached to hulls).

### 3.1. Hierarchy Definition

The hierarchy is defined implicitly via a "Parent Pointer" approach within the `EntityInfo` descriptor.

- **Taskforce (Root):** An entity with no parent. `OrbatParentId = 0`.
- **Unit/Individual:** An entity pointing to another entity. `OrbatParentId = [ParentEntityId]`.

### 3.2. Tree Reconstruction (GUI Logic)

Since DDS data arrives asynchronously and flatly, the GUI must reconstruct the tree dynamically:

1. **Ingest:** Subscribe to `EntityInfo`.
2. **Index:** Store entities in a map/hash table keyed by `EntityId`.
3. **Link:** For every entity, look up its `OrbatParentId`.
   - If `0`: Add to top-level view.
   - If `>0` and Parent exists: Add as a child node of the Parent.
   - If `>0` and Parent missing: Place in a temporary "Orphan" list until the Parent descriptor arrives.

## 4. Data Model: Mission System

A Mission is a linear sequence of Tasks. A Task defines a specific Doctrine (behavior) and conditions (Triggers).

### 4.1. Task Identification

- **Problem:** Using array indices (0, 1, 2) is unsafe in a distributed system where the mission might change while a command is in-flight.
- **Solution:** Every Task is assigned a **GUID (String)**. Commands reference this GUID (`TargetTaskId`).

### 4.2. Mission Descriptors

The system introduces a specialized descriptor: **`EntityMission`**.

- **Owner:** CGF (Computer Generated Forces).
- **Content:** Contains the `MissionPlan`—the list of tasks and the pointer to the currently active task.
- **Payloads:** Doctrines and Triggers use JSON strings for parameters. This allows flexibility without changing the IDL, but requires strict Schema validation at runtime.

## 5. Command Interface (GUI to CGF)

The GUI cannot write to `EntityMission` directly. It must publish a **`MissionControlRequest`**.

### 5.1. The "Jump to Task" Capability

The GUI can force an entity to restart execution from a specific task without re-uploading the whole mission.

1. GUI sends `CMD_JUMP_TO_TASK` with `TargetTaskId`.
2. CGF receives request.
3. CGF scans current mission for that ID.
4. CGF sets all preceding tasks to `TASK_SKIPPED` or `TASK_DONE`.
5. CGF sets target task to `TASK_ACTIVE` and initializes behavior.

### 5.2. The "Replace Mission" Capability

The GUI can upload a completely new mission plan.

1. GUI constructs a `MissionPlan` struct (Task List + Active Task ID).
2. GUI wraps it in `CMD_REPLACE_MISSION`.
3. CGF atomically replaces the internal plan with the new data.



## 6. IG model switches control

Principles:
1.  Each model (entity) exposes a set of **controllable** **properties**, for example:
    1.  Vehicle door opened/closed
    2.  Mast risen/retracted
    3.  Camouflage type
2.  Different models expose different properties depending on their capabilities.
3.  What properties are supported is published on the network.
4.  UI/CGF send **requests to change the value of a property**, like 'set door to open', 'set camo to desert' etc.
5.  UI/CGF can **ask about the value of concrete property** via query message, IG (SimHost) responds with a reply message.

Details about IG imnplementaion to be found in different document (available soon - Michal Toth)

## 7. Interface Definition Language (IDL)

The following IDL defines the data structures using **PascalCase** naming conventions and **inline types** for clarity.

``` c++
// ==============================================================================
// 1. ENUMS
// ==============================================================================

enum eTaskState {
    TASK_PLANNED,   // Waiting for triggers or sequence
    TASK_ACTIVE,    // Currently executing
    TASK_DONE,      // Completed successfully
    TASK_FAILED,    // Failed execution
    TASK_SKIPPED    // Skipped because a later task was forced active
};

enum eMissionCommandType {
    CMD_JUMP_TO_TASK,       // Switch active task to a specific ID
    CMD_APPEND_TASK,        // Add a single task to the end
    CMD_INSERT_TASK,        // Insert a task (specifics handled by logic/index)
    CMD_REPLACE_MISSION,    // Wipe everything and set a new full mission
    CMD_ABORT_ALL           // Stop everything
};

// ==============================================================================
// 2. STRUCTS (DATA BUILDING BLOCKS)
// ==============================================================================

// GUID
@final
struct CorrelationId {
   unsigned long long high;
   unsigned long long low;
};


struct MissionTrigger {
    string Type;          // e.g., "LineCrossed", "TimeElapsed"
    string Params;        // JSON string (Schema validated)
};

struct MissionTask {
    CorrelationId TaskId;        // Unique stringified GUID
    string ExecutingEngine;      // who is going to execute the behavior "CGFX" etc.
    string BehaviorId;           // e.g., "MoveToLocation", could be also bkbId od the doctrine (for CGFX)
    string BehaviorParams;       // JSON string (Schema validated) for the doctrine
    
    sequence<MissionTrigger> Triggers; 
    
    eTaskState State;     // Current status of this specific task
};

// Reusable structure for the "Content" of a mission.
// Used in both the EntityMission state and the REPLACE_MISSION command.
struct MissionPlan {
    // ID of the task currently running. 
    // Must match one of the TaskIds in the Tasks sequence.
    CorrelationId ActiveTaskId; 
    
    // Ordered list of all tasks
    sequence<MissionTask> Tasks;
};

// ==============================================================================
// 3. TOPIC: ENTITY INFO (ORBAT HIERARCHY)
// ==============================================================================

struct EntityInfo {
    long long EntityId; //@key
    
    string Name;

    eForceIdentifier ForceIdentifier;
    
    // ORBAT PARENT
    // 0 = This entity is a Root/Taskforce (No parent).
    // >0 = EntityId of the parent unit.
    long long CommanderId; 
};

#pragma topic EntityInfo
#pragma keylist EntityInfo EntityId

// ==============================================================================
// 4. TOPIC: ENTITY MISSION (STATE)
// ==============================================================================

struct EntityMission {
    long long EntityId; //@key
    
    // The current state of the mission
    MissionPlan Plan;
};

#pragma topic EntityMission
#pragma keylist EntityMission EntityId

// ==============================================================================
// 5. COMMAND INTERFACE (GUI -> CGF)
// ==============================================================================

union MissionCommandPayload switch (eMissionCommandType) {
    
    // CASE: Switch execution to a specific existing task
    case CMD_JUMP_TO_TASK:
        CorrelationId TargetTaskId;
        
    // CASE: Add new single tasks
    case CMD_APPEND_TASK:
    case CMD_INSERT_TASK:
        MissionTask NewTaskData;
        
    // CASE: Full Mission Upload
    // Reuses the MissionPlan struct to set list + active index atomically
    case CMD_REPLACE_MISSION:
        MissionPlan FullMissionData;

    // CASE: Commands with no parameters
    case CMD_ABORT_ALL:
        boolean UnusedPlaceholder; 
};

struct MissionControlRequest {
    // Unique ID for this specific request
    CorrelationId RequestId; //@key
    
    // The entity to control
    long long TargetEntityId;
    
    // The polymorphic payload
    MissionCommandPayload Payload;
};

#pragma topic MissionControlRequest
#pragma keylist MissionControlRequest RequestId

```

## 7. Implementation Guidelines

### 7.1. JSON Safety

- **Validation:** The CGF MUST NOT simply `JSON.parse()` blind data. It must validate `BehaviorParams` against a known schema for that `BehaviorId` inside a try-catch block.
- **Failure:** If parsing fails, the task state should move to `TASK_FAILED`, and an error log should be published. The simulation must not crash.

### 7.2. TKB Integration

- When a Unit is created based on a TKB (Tactical Knowledge Base) template, the Creator (CGF) is responsible for:
  1. Publishing the Unit's `EntityMaster` and `EntityInfo`.
  2. Looking up the TKB template to see what subordinates it has.
  3. Iteratively publishing `EntityMaster`/`EntityInfo` for all subordinates, setting their `OrbatParentId` to the Unit's ID.

### 7.3. Concurrency Edges

- **Orphans:** The GUI must robustly handle the case where a Child Entity descriptor arrives 500ms before the Parent Entity descriptor. Do not discard the child; render it in a temporary "Unassigned" folder.
- **Stale Commands:** If the GUI sends `CMD_JUMP_TO_TASK` with an ID that no longer exists (because the mission changed in the background), the CGF must ignore the request and log a warning.


**Document End**


# Changes to be integrated



### Relation to current CGFX
In current CGFX the command-enforced granular change `SetPosition` message somehow differs in its effect from plain seting a new position to the entity. Some agents are responsing to this message differently than to a plain change in the entity position.

This seems to be
 - Partly a design flaw (changing entity attribute in whatever way should result in the same outcome),
 - Partly a pragmatic choice (no need to check for entity attribute changes, responding to the concrete mesage only)

The pragmatism does not need to be broken - CGFX could repsond to the `SetEntityAttributes` by issuing its internal `SetPosition` message as now, accepting the 'flaw' of ignoring other types of changes (like whole descriptor changes).


## Unit creation

IOS needs to task the system to create a whole unit containing subordinates.

Current CGFX way as a  bit clumsy, involving file names and import/export.

BDC should approach this in a simple way:
 - A unit definiton is just another TKB record, specifying
   - what sub-units to create
 - The unit creation is as simple as single-entity creation, using same entity creation mechanism.
 - Single ACK (entity created) should be enough as such operation should be atomic (either whole unit created or nothing).




# Presentations
![image.png](/.attachments/image-5abe6408-c780-494d-874b-2aa8ecfaafd1.png)
![image.png](/.attachments/image-36104406-8e82-4b47-a055-1ac6fe8dad59.png)
![image.png](/.attachments/image-853652b3-491b-45a6-a807-502699a35fc8.png)
![image.png](/.attachments/image-5c7d44e8-0e3e-4202-b0a9-12dce108912a.png)
![image.png](/.attachments/image-e8b7f345-87c1-4ba0-a344-ec3044ac7e30.png)
![image.png](/.attachments/image-944b39c5-3af7-4214-b3e0-94a4ef33691e.png)
![image.png](/.attachments/image-ce13d262-6a39-44c0-bd90-ecdba2895898.png)

switches
![image.png](/.attachments/image-04576ef0-f449-4ce9-8cb4-f16634c1a51b.png)

granular updates
![image.png](/.attachments/image-5d84be69-1ad1-458b-8ead-b1973d5f427c.png)

Units
![image.png](/.attachments/image-1f3ce1f4-e28c-4231-b2f4-66c098a5af24.png)
![image.png](/.attachments/image-8204663e-0697-40bc-87ee-84746cf7f636.png)

