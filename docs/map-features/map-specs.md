# 1. Current state of legacy system - features of current IOS map based on maplink

### **1. Navigation & View Control**

*   **Panning:** Left-click and drag on empty space (cursor changes to a hand).
    
*   **Zooming:** Mouse wheel (implied) or specific tools.
    
*   **Zoom Tool (Magnifying Glass):** Allows drawing a rectangle to zoom immediately into that specific area.
    
*   **Map Detail Levels:** Toggle between "Detailed" and "Undetailed" map backgrounds (e.g., for large maps like Europe).
    
*   **Center on IG:** Centers the 2D map on the current 3D visual (Image Generator) view.
    
*   **Search:** Incremental search bar (contains search) to find and center on entities, areas, or routes.

### **2. Creation Tools (Right-Click on Empty Space)**

All creation actions trigger a workflow involving the IOS (Instructor Operator Station).
*   **Create Area:** Draws a polygon. Workflow: Draw $\rightarrow$ Finish $\rightarrow$ Save (in IOS).
    
*   **Create Route:** Creates a path. Workflow: Click waypoints $\rightarrow$ Save.
    
*   **Create Interest Point (IP):** Opens a form to create a specific point.
    
*   **Create CGF (Entity):** Opens the entity creation table to spawn units/vehicles.
    
*   **Create Unit:** Allows creating hierarchical units (e.g., Squads).
    

### **3. Entity Management (Right-Click on Entity)**

Right-clicking an existing entity offers three main sub-menus:
**A. Command (Properties)**
*   Opens the property window for that entity in the IOS.
    

**B. Display (View Options)**
*   **Center on 3D / Map:** Centers the respective view on the entity.
    
*   **Follow in 3D / Map:** Locks the camera to follow the moving entity.
    
*   **Show on Timeline:** Displays the entity's doctrine/status on the IOS timeline.
    
*   **Show Subordinates:** Toggles visibility of member units within a squad/aggregate.
    
*   **History Trail:** Displays the path the entity has traveled (specifically mentioned for UAVs/Ground units).
    

**C. Manage (State & Action)**
*   **Delete:** Removes entity (requires confirmation).
    
*   **Duplicate:** Copies the entity; requires clicking a new location on the map to spawn the clone.
    
*   **Reset Attrition:** Repairs the entity (resets damage/health).
    
*   **Set Position:** Teleports the entity to a new clicked location.
    
*   **Lights:** Toggles entity lights on/off (visible in 3D only).
    
*   **Status Tags (with Visual Indicators):**
    *   _Set as Fireman:_ Adds a **red circle** indicator.
        
    *   _Set as Operator:_ Adds a **blue triangle** indicator.
        
    *   _Set as No Strike:_ No visual map indicator mentioned.
        
    *   _Set as Source Target:_ Sets entity as a target.
        

### **4. Editing & Modification**

*   **Route/Area Editing:**
    *   **Edit Mode:** Changes the line color (e.g., to orange/yellow) and shows vertex dots.
        
    *   **Vertex Manipulation:** Drag and drop dots to move them.
        
    *   **Context Menu on Line:** Add point before/after, Remove point.
        
    *   **Save/Cancel:** Must be finalized in the IOS interface.
        

### **5. Selection Tools**

*   **Single Select:** Left-click one entity.
    
*   **Multi-Select (Control + Click):** Select multiple specific entities.
    
*   **Multi-Select (Arrow Tool):** Drag a box around an area to select all entities within it.
    
*   **Reset Tool:** Standard cursor icon to exit Zoom/Select modes.


These tools use CorrelationIds. If the user switches tools before finishing, the IOS ignores the subsequent ClickEvent from the IG.
    

### **6. Analysis Tools**

*   **Measure:** Linear (ruler) and Circular (radius/area) measurement tools.
    
*   **Line of Sight (LOS):** Checks visibility between two points (Green = Visible, Red = Obstructed).
    
*   **Radio Coverage:** Similar visualization to LOS but for radio signal reach.

These tools use CorrelationIds. If the user switches tools before finishing, the IOS ignores the subsequent ClickEvent from the IG.
    

### **7. Visualization, Layers & Aggregation**

These settings are often controlled via the IOS "Display" tab but affect the map render.
*   **Grid Lines:** Toggle visibility of map coordinates grid.
    
*   **Map Hiding:** Can hide the map background entirely.
    
*   **Entity Decluttering (Detail Level):**
    *   _None:_ Shows only the tactical symbol.
        
    *   _Detailed:_ Shows Symbol + Name + Attrition + Speed + Doctrine.
        
    *   _Zoom Logic:_ As you zoom out, details automatically disappear to reduce clutter.
    
*   **Aggregation:**
    *   When zooming out, subordinate units disappear, and only the **Unit Symbol** (e.g., Squad leader) remains.
        
    *   Commands given to the Unit Symbol propagate to all subordinates.
    
*   **Graphical Overlays:** A separate layer for tactical drawings (arrows, symbols) created in the "Arena" tool. These do not scale/resize when zooming the map.
    
*   **Nav Plan Visualization:** Shows the _planned_ route (straight lines) vs. the _calculated_ CGF path (obstacle avoidance path).

### **8. Technical/System Notes**

*   **Performance:** The map rendering is described as "really slow."
    
*   **Symbol Source:** Entity icons are loaded from TKB config files; they cannot be customized (size/color) by the user in the app.
    
*   **Bugs:** A specific bug was noted where the coordinate display (for cursor and entity) is missing from the bottom right corner.

* * *

## 2. System Overview


### 2.1 System Context

::: mermaid
graph TD
    subgraph Control Plane
      IOS
    end

    subgraph Data Backbone
      Topic_Shared
      Topic_Local
    end
    
    subgraph Execution Plane
      IG
      SimHost
    end

  











    IOS -->|Commands| IG
    
    IG -->|Request Create| SimHost
    
    SimHost -->|Publishes| Topic_Shared
    
    IG -->|Publishes| Topic_Local
    
    Topic_Shared -.->|Subscribes| IG
    
    Topic_Local -.->|Subscribes| IG
    
    Topic_Local -.->|Subscribes| IOS
    
    Topic_Shared -.->|Subscribes| IOS
:::

### 2.2 Component Responsibilities

#### IOS \(Instructor Operating Station)

- **Role:** Session Logic & Command Source / Orchestrator.
- **Actions:**

    - Issues `MapCommandRequest(CMD_PLACE_ENTITY)` or `MapCommandRequest(CMD_DRAW_TACTICAL_GRAPHIC)` with optional JSON overrides; the IG handles descriptor construction and SimHost dispatch autonomously.
    - Consumes `MapCommandAck` to learn created entity IDs and tool completion status.
    - Subscribes to `EntityMaster` to show entity lists.
    - **Optionally** subscribes to `MapVisualOverlay` \(Geometry) if a specific UI feature \(e.g., Radar View) requires it.
    - **Never owns/publishes geometry; never needs to know IDL descriptor structures for standard creation workflows.**

#### IG \(Image Generator)

- **Role:** Interaction Handler, Session Data Owner, and Autonomous Entity-Creation Agent.
- **Actions:**

    - **Scenario Entities:** Captures input, resolves TKB defaults + IOS JSON overrides, dispatches `CreateEntityRequest` to SimHost, bridges `CreateEntityAck` back to IOS as `MapCommandAck`.
    - **Session Entities:** Captures input, **Directly Publishes/Owns** entities on Backbone.
    - Renders all backbone entities \(subscribes to both Shared and Local topics).

#### SimHost \(Simulation Host)

- **Role:** Scenario Authority.
- **Actions:**

    - Owns "Tactical/Shared" entities \(Units, Routes, Borders).
    - Validates creation requests from IG.


#### **2.4 Identity Concepts: Instance vs. Role**

The system strictly separates **Logical Roles** from **Concrete Instances**:

- **`MapGroupId` (Role/View):** Identifies a logical group of displays that share business logic, configuration, and styling (e.g., "Blue Force", "Instructor Station", "VIP View").

    - *Usage:* Configuration, Styling (`MapEntitySymbol`), Context Menus.
- **`MapId` (Instance/Hardware):** Identifies a specific physical application instance or window.

    - *Usage:* Input events (Clicks, Drags), Hardware status, Imperative commands (`CMD_PAN_TO`).

## 2. Key Design Decisions

1. **IOS as Orchestrator, IG as Autonomous Creation Agent:** The IOS activates tools via `MapCommandRequest` and injects JSON overrides, but **never needs to know IDL descriptor structures** for standard entity placement or tactical drawing. The IG handles descriptor resolution and dispatches `CreateEntityRequest` to SimHost autonomously. The IOS learns creation outcomes via `MapCommandAck`.
2. **Dual Ownership Model:**

    - **Scenario Entities \(Shared):** Owned by **SimHost/CGF**. Persistent.
    - **Session Entities \(Local):** Owned by **IG Map**. Temporary.
3. **IG-Centric Authoring:** The IG handles user interaction and publishes the resulting entities to the Backbone.
4. **Open Visibility, Selective Processing:** All geometry is published to the Backbone. The IOS *can* see it \(e.g., for a Mini-Map), but for standard operations, it operates on lightweight Entity IDs and metadata, ignoring the heavy geometry payloads.




* * *

## 3. Architecture Principles

### 3.1 Ownership & Lifecycle Rules

| **Feature**          | **Scenario Entities \(Shared)**                     | **Session Entities \(Local/Map-Local)** |
| -------------------- | --------------------------------------------------- | --------------------------------------- |
| **Primary Use Case** | Areas, FEBA Lines, Routes                           | Scribbles, Annotations                  |
| **DDS Owner**        | **SimHost/CGF**                                     | **IG**                                  |
| **Creation Flow**    | IG Request → SimHost Publish                        | IG Direct Publish                       |
| **Persistence**      | Scenario/Exercise Recording Database \(SimHost/CGF) | Session \(IG) - in memory               |
| **Visibility**       | Public \(Backbone)                                  | Public \(Backbone)                      |


### 3.2 Geometry Abstraction via Selective Subscription

While all map geometry \(vertices, polygons) is published to the Backbone, the IOS typically does not need to consume it to perform its duties.

- **Principle:** The IOS subscribes primarily to
    - `EntityMaster` \(Identity/Metadata)
    - `EntityInfo` (entity name, affiliation, orbat hierarchy...)
    - `MapEntitySymbol` (Type/Style).
- **Efficiency:** The IOS ignores the heavy `MapVisualOverlay` \(Geometry) topic unless it has a specific need \(e.g., verifying a coordinate, rendering a secondary map view).
- **Consistency:** The IOS manipulates entities via **Reference** \(Entity ID), trusting the IG \(or SimHost/CGF) to maintain the geometric truth.

## 4. Interaction Workflows

### 4.1 Creating a SCENARIO \(Shared) Drawing

**Goal:** User draws a "Company Boundary" scenario-persistent drawing. SimHost saves it.

1. **IOS:** Sends `MapCommandRequest(CMD_DRAW_TACTICAL_GRAPHIC, RequestId=UUID-X, CommandArgsJson={"persistence":"SCENARIO", ...optionalOverrides})`.  
   The IOS can inject optional property overrides (e.g., affiliation, type) so the IG does not guess everything from TKB defaults alone.
2. **IG:** Switches to tool. User draws.
3. **IG \(Finish):** Sends `UpdateEntityDescriptorRequest` to **SimHost**.
4. **SimHost:** Publishes new Entity \(Owner=SimHost).
5. **IG:** Receives SimHost Ack.
6. **IG:** Sends `MapCommandAck(RequestId=UUID-X, StatusCode=0, DataJson={"entityId":5001})` to IOS.
7. **IOS:** Sees Entity `5001` appear on Backbone \(via `EntityMaster` subscription).


::: mermaid
sequenceDiagram
    autonumber
    participant IOS as IOS (Orchestrator)
    participant IG as IG (Interaction/Creation)
    participant SimHost as SimHost (Scenario Owner)

    Note over IOS, SimHost: 6.1 Creating SCENARIO (Shared) Drawing
    
    IOS->>IG: MapCommandRequest<br/>(CMD_DRAW_TACTICAL_GRAPHIC, RequestId=UUID-X, overrides={...})
    
    Note over IG: IG switches tool.<br/>User draws "Company Boundary".
    
    IG->>SimHost: UpdateEntityDescriptorRequest<br/>(Create Entity, Payload=Geometry)
    
    Note right of IG: IG requests SimHost to own it
    
    SimHost->>SimHost: Validate & Allocate ID (e.g., 5001)
    SimHost-->>SimHost: Publish EntityMaster + Overlay<br/>(Owner = SimHost)
    
    par Backbone Update (DDS)
        SimHost->>IG: Entity Data Received
        SimHost->>IOS: Entity Data Received
    end
    
    SimHost->>IG: UpdateEntityDescriptorAck (Success)
    IG->>IOS: MapCommandAck (RequestId=UUID-X, StatusCode=0, DataJson={entityId:5001})
:::



### 4.2 Creating a SESSION \(Local) Drawing

**Goal:** User uses "Create Scribble" from IOS dialog. IG remebers the drawing for the duration of the session.

1. **IOS:** Sends `MapCommandRequest(CMD_DRAW_TACTICAL_GRAPHIC, RequestId=UUID-Y, CommandArgsJson={"persistence":"SESSION", ...optionalOverrides})`.
2. **IG:** Switches to tool. User draws.
3. **IG (Finish):**
    - Allocates ID `9001` (from IG pool).
    - **Directly Publishes** `EntityMaster` and `MapVisualOverlay` to Backbone (Owner=IG).
4. **IG:** Sends `MapCommandAck(RequestId=UUID-Y, StatusCode=0, DataJson={"entityId":9001})` to IOS.
5. **IOS:** Sees Entity `9001` appear on Backbone.

    - *Note:* IOS *could* inspect the geometry of `9001` via DDS if needed, but typically just lists "Scribble/Annotation 1" in the UI.

::: mermaid
sequenceDiagram

    autonumber
    participant IOS as IOS (Orchestrator)
    participant IG as IG (Session Owner)
    participant DDS as DDS Backbone
    
    Note over IOS, DDS: 6.2 Creating SESSION (Local) Drawing


    IOS->>IG: MapCommandRequest<br/>(CMD_DRAW_TACTICAL_GRAPHIC, RequestId=UUID-Y, overrides={...})
    
    Note over IG: IG switches tool.<br/>User draws "Scribble/Annotation".
    
    IG->>IG: Allocate ID from Local Pool (e.g., 9001)
    
    Note right of IG: IG takes ownership directly
    
    IG->>DDS: Publish EntityMaster (Owner=IG)
    
    IG->>DDS: Publish MapVisualOverlay (Geometry)
    
    par Backbone Update (DDS)
        DDS->>IG: Data Available (Loopback)
        DDS->>IOS: Data Available
    end
    
    IG->>IOS: MapCommandAck (RequestId=UUID-Y, StatusCode=0, DataJson={entityId:9001})
    Note left of IOS: IOS lists entity 9001 in UI.<br/>Optionally inspects geometry via DDS.
:::




# Design Intro

## 1. What Is This All About? (Big Picture)


This document explains **how the Instructor Operating Station (IOS)** and the **Image Generator (IG)** cooperate to provide an **interactive 2D tactical map** in Bagira systems.
In simple terms:
*   **IG** draws the map, symbols, and graphics, and captures mouse/keyboard input.
    
*   **IOS** decides _what those interactions mean_ in training or simulation terms.
    
*   Both communicate over **DDS**, using **BDC SST (Shared Simulation State)** principles.


The goal is to:
*   Replace legacy map solutions (e.g. MapLink)
    
*   Keep rendering fast and flexible
    
*   Stay consistent with the existing BDC entity model
    
*   Support both simple single-user use and future multi-user cooperation
    

Think of it as:

> **IOS = decision-maker / orchestrator**  
> **IG = eyes, hands, and domain expert**

For standard entity placement and tactical drawing, the IG operates autonomously — resolving
entity descriptors from TKB defaults plus IOS-injected JSON overrides, dispatching creation
requests directly to SimHost, and reporting outcomes back to the IOS via `MapCommandAck`.
The IOS retains strategic control by parametrising tool activations and consuming
acknowledgements; it is not required to understand IDL descriptor structures.


## 2. Why Is It Designed This Way?

Several constraints shape the design:
1.  **Real-time interaction**  
    Clicking, dragging, and drawing must feel instant.
    
2.  **Data-centric architecture (SST)**  
    Simulation objects already exist as _entities with descriptors_. The map must reuse that model, not invent a parallel one.
    
3.  **Flexibility without recompiling**  
    Styling, layers, tools, and menus change often. JSON is used where flexibility matters; IDL where performance matters.
    
4.  **Clear responsibility split**
    *   IG handles interaction mechanics and entity-creation dispatch, parametrised by IOS intent; it does not make scenario-level policy decisions (e.g., which units are valid, whether doctrine allows placement)
        
    *   IOS never renders pixels; it never needs to understand IDL descriptor structures for standard creation workflows
        


## 3. Core Mental Model

### 3.1 Everything on the Map Is an Entity

Whether it is:
*   a tank
    
*   a route
    
*   a fire line
    
*   a temporary measurement
    

…it is represented as a **BDC entity** with a unique `EntityId`.
An entity is not a single object, but a **set of descriptors**:
*   `EntityMaster_MapObject` — identity (what it is, who owns it)
    
*   `MapEntitySymbol` — override of how units look (optional, usually driven by global map setting)
    
*   `MapVisualOverlay` — lines, areas, effects
    
*   `MapRoute` — waypoints
    
*   `MapEntityLock` — who is editing (optional)
    

> **Rule of thumb:**  
> _If `EntityMaster_MapObject` exists and is ALIVE, the entity exists._


### 3.2 SST Rules Still Apply

This map system does **not** invent new lifecycle rules.
*   Creating an entity = **publish descriptors**
    
*   Deleting an entity = **DDS dispose of `EntityMaster_MapObject`**
    
*   Updating your own entity = **write directly**
    
*   Updating someone else’s entity = **send update request**
    

No REST-style "create/delete" commands exist here.

## 4. High-Level Architecture


### Responsibilities

**IOS**
*   Activates tools via `MapCommandRequest`, injecting optional JSON overrides
    
*   Consumes `MapCommandAck` for entity IDs and tool-completion status
    
*   Interprets user intent at the strategic level
    
*   Asks SimHost or IG to create and modify entities when needed directly (volatile graphics, direct backbone writes)
    
*   Defines context menus
    

**IG**
*   Renders entities
    
*   Applies styles
    
*   Handles mouse/keyboard input
    
*   Emits interaction events (selection, drag, generic clicks when enabled)
    
*   For `CMD_PLACE_ENTITY` / `CMD_DRAW_TACTICAL_GRAPHIC`: autonomously resolves descriptors (TKB defaults + IOS JSON overrides) and dispatches `CreateEntityRequest` to SimHost, then bridges the ack back as `MapCommandAck`
    

**BDC SST Backbone**
*   Stores shared simulation state (network)
    
*   Enforces ownership rules
    

## 5. Configuration vs Interaction (Very Important Distinction)


### 5.1 Configuration = “How the map behaves”

Examples:
*   active tool
    
*   visible layers
    
*   camera mode
    
*   symbol sizes
    

Configuration:
*   Is **low frequency**
    
*   Is expressed as **JSON**
    
*   Uses **JSON Merge Patch (RFC 7396)**
    

IOS sends _only what changed_. IG merges it into current state and reports back the full applied configuration.
This gives:
*   small messages
    
*   late-joiner safety
    
*   UI always in sync
    


### 5.2 Interaction = “What the user just did”

Examples:
*   mouse click
    
*   drag start / drag end
    
*   selection change
    

Interaction:
*   Is **high frequency**
    
*   Uses **IDL (binary DDS)**
    
*   Carries precise coordinates
    

Every `MapCommandAck` carries the `RequestId` of the originating `MapCommandRequest`, so IOS
knows which tool activation produced which result. Generic `MapClickEvent`s (fallback mode)
still carry an interaction context ID for the same reason.


## 6. Context IDs: How We Avoid Confusion

A **Context ID** (`RequestId`) is a UUID that links a `MapCommandRequest` to its `MapCommandAck` responses.

Example — IG-autonomous entity placement:

1.  IOS sends `MapCommandRequest(CMD_PLACE_ENTITY, RequestId=A, args={...overrides})`
    
2.  User clicks on map
    
3.  IG dispatches `CreateEntityRequest` to SimHost, receives `CreateEntityAck`
    
4.  IG sends `MapCommandAck(RequestId=A, StatusCode=0, DataJson={entityId:9001})`
    
5.  IOS checks if RequestId `A` is still registered → correlates result to "Add Tank" operation

Example — pure coordinate selection fallback:

1.  IOS sends `MapCommandRequest(CMD_PICK_LOCATION, RequestId=B)`
    
2.  User clicks on map
    
3.  IG sends `MapCommandAck(RequestId=B, StatusCode=0, DataJson={position:{...}})`
    
4.  IOS checks if RequestId `B` is still registered → uses the coordinate

If the user cancels or the IOS unregisters a `RequestId`, any subsequent `MapCommandAck` or
`MapClickEvent` carrying that ID is safely ignored without blocking the UI.



## 7. Styling and the TKB (3 Layers)

Visual appearance is resolved in **three layers**:
1.  **JSON override** (highest priority)
    
2.  **Style preset name** (named variant)
    
3.  **TKB default** (lowest priority)

This allows:
*   consistent defaults
    
*   easy project-wide changes
    
*   fine-grained overrides when needed
    

Example:
*   TKB says fire line = orange dashed
    
*   Preset says “Friendly” = blue
    
*   JSON override says `lineWidth = 3`
    

Result: **blue dashed line, width 3**

## 8. Persistent vs Volatile Objects

Not everything on the map should be saved.

### Persistent (Scenario)

*   Units
    
*   Routes
    
*   Shared overlays
    

### Volatile (Map-local or RAM-only)

*   Fire effects
    
*   Hit markers
    
*   Measurement tools
    

Volatile objects:
*   Can auto-delete after N seconds
    
*   Do not survive restarts
    
    
## 9. Context Menus: Why They Are “Pushed”


Traditional right-click menus feel slow over a network.

This design uses **pre-fetching**:

1.  User selects an entity
    
2.  IOS immediately calculates allowed actions
    
3.  IOS pushes menu definition to IG
    
4.  User right-clicks → menu opens instantly
    

Fallback exists for edge cases, but the common path is zero-latency.


## 10. Cooperative Editing (Optional, Advanced, Future)

Single-user mode needs **no locking**.

Multi-user mode introduces:

*   optimistic locking (optional)
    

## 11. Typical End-to-End Example


**Placing a Tank (IG-Autonomous Workflow)**:
1.  IOS sends `MapCommandRequest(CMD_PLACE_ENTITY, RequestId=UUID-12345)` with
    optional JSON overrides (e.g., `{"affiliation":"FRIENDLY","objectClass":"T72_Tank"}`).
    IG does not need to guess everything from TKB defaults alone.
    
2.  User clicks map
    
3.  IG resolves TKB defaults + IOS-injected JSON overrides, constructs a full
    `CreateEntityRequest` and dispatches it directly to SimHost
    
4.  SimHost allocates EntityId (e.g., 9001), publishes descriptors, returns `CreateEntityAck`
    
5.  IG bridges the acknowledgement back to IOS as `MapCommandAck`
    (`RequestId=UUID-12345, StatusCode=0, DataJson={"entityId":9001}`)
    
6.  IG renders the tank

No IOS-side ID allocation or descriptor publishing required.
Context IDs keep the async workflow safe against cancellations and tool switches.


## 12. Key Takeaways


1.  **Map objects are BDC SST entities**
    
2.  **IG renders, handles input, and autonomously dispatches entity creation; IOS provides intent via parameterised tool activation and consumes `MapCommandAck` results**
    
3.  **Configuration = JSON, Interaction = IDL**
    
4.  **Create/delete via**
    -  DDS publish/dispose (temporary map objects)
    -  CreateEntity/UpdateEntity requests (persistent map objects)
    
5.  **`RequestId` in `MapCommandAck` keeps async creation workflows safe — IOS correlates results or ignores stale acks if the tool was cancelled**
    
6.  **TKB provides defaults; JSON fine-tunes**
    

Once these click, the rest of the document becomes much easier to follow.


---
Note: most of the data communication is based on [BCD SST principles](/Products-and-components/Bagira.Infra/BDC/BDC-SST-Principles). READ IT FIRST!

Status: **Rougly designed.**
 - Needs a review to check if fulfills the needs for map usage
 - Needs adapation to the current state of the BDC
    1. Using EntityMaster descriptor (not a special MapObjectMatser)
    2. Resolve the OwnerId with the current way of identifying owners in BDC (see BDC SST principles link above)
    3. Find out where to save the contextId needed for "cooperative editing" - maybe some new map-related descriptor for entities.

Messages and descriptors overview: see chapter 5.10 Messages and descriptors overview


# IOS ↔ IG 2D Map Network Interface Design

**Version:** 1.7  
**Date:** March 11, 2026  
**Status:** Updated — IG-Autonomous Creation Architecture (see design talk integration notes)

---

## Table of Contents

1. [Executive Summary](#1.-executive-summary)
2. [System Overview](#2-system-overview)
3. [Architecture Principles](#3-architecture-principles)
4. [Data Model Foundation](#4-data-model-foundation)
5. [DDS Topic Design](#5-dds-topic-design)
6. [Interaction Workflows](#6-interaction-workflows)
7. [Implementation Considerations](#7-implementation-considerations)
8. [Quality Attributes](#8-quality-attributes)
9. [Open Issues and Future Work](#9-open-issues-and-future-work)

---

## 1. Executive Summary

### 1.1 Purpose

This document defines the DDS-based communication protocol between the Instructor Operating Station (IOS) and the Image Generator (IG) 2D Map component. The system replaces the legacy MapLink representation with an IG-rendered 2D map that provides NATO and custom entity symbols, overlay authoring, and interactive map operations.

### 1.2 Key Design Decisions

1. **Relaxed Controller-View Separation**: IOS acts as the high-level orchestrator; IG is the renderer, input capture engine, and autonomous entity-creation agent. For standard unit placement and tactical drawing, the IG resolves TKB defaults plus IOS-injected JSON overrides, dispatches `CreateEntityRequest` directly to SimHost, and reports outcomes via `MapCommandAck`. The strict “IOS decides domain meaning” model is preserved as an opt-in fallback (`CMD_PICK_LOCATION` for pure coordinate selection; generic `MapClickEvent` when enabled by the active tool).
2. **Hybrid Data Model**: JSON for flexible configuration; strict IDL for high-frequency interaction
3. **Entity-Component System**: Unified entity model aligned with the existing BDC SST (Shared Simulation State) backbone
4. **Context-Based Interaction**: Correlation IDs ensure safe disambiguation in asynchronous workflows
5. **JSON Merge Patch Configuration**: Partial updates via RFC 7396 with state feedback loop for efficiency
6. **TKB-Based Styling**: 3-layer style resolution (JSON override → Preset name → TKB defaults)
7. **Volatile Overlays**: Support for temporary graphics (fire lines, hit indicators) with automatic real-time cleanup
8. **Cooperative Editing**: Optional multi-user support via logical locking and map contexts

### 1.3 Scope

**In Scope:**
- Entity rendering with configurable symbol styles (NATO, Russian, custom)
- Overlay authoring (routes, areas, tactical graphics)
- Volatile overlays with auto-timeout (fire lines, hit indicators, effects)
- TKB-based default properties and style overrides
- Interactive operations (pan, zoom, selection, drag, context menus)
- Layer and filter management
- Coordinate system switching
- Capabilities discovery (layers, modes, symbol sets, TKB manifest)
- Optional cooperative editing with locking support

**Out of Scope:**
- 3D terrain visualization
- Real-time physics simulation
- Direct entity control (handled by backbone)
- Network latency compensation

---

## 2. System Overview

### 2.1 System Context

```
┌─────────────┐         Map control         ┌─────────────┐
│             │◄───────────────────────────►│             │
│     IOS     │   Configuration/Commands    │   IG 2D     │
│ (Controller)│   Events/Feedback           │     Map     │
│             │   Context Menus             │  (Renderer) │
└─────────────┘                             └─────────────┘
       │                                            │
       │                                            │
       └────────────► Data Backbone ◄───────────────┘
                    (Entity State - SST)
```

### 2.2 Component Responsibilities

#### IOS (Instructor Operating Station)
- Selects map modes and activates tools via `MapCommandRequest` (imperative) or presets
- Injects optional JSON overrides into `MapCommandRequest.CommandArgsJson` so the IG can parametrise entity creation without guessing from TKB defaults alone
- Consumes `MapCommandAck` to learn which entities were created, and to track multi-step tool progress
- Receives user interaction events (selection changes, drag events, generic clicks)
- Performs direct domain actions via backbone when needed (entity modification, volatile graphic requests)
- Provides dynamic context menu definitions

#### IG 2D Map
- Renders simulation state from data backbone
- Resolves symbol appearance using TKB (Technical Knowledge Base)
- Implements interaction primitives (pan/zoom, selection, drag, drawing)
- **Autonomously dispatches entity creation**: for `CMD_PLACE_ENTITY` and `CMD_DRAW_TACTICAL_GRAPHIC`, resolves TKB defaults + IOS-injected JSON overrides, constructs `CreateEntityRequest`, and sends it directly to SimHost
- Bridges SimHost `CreateEntityAck` back to IOS as `MapCommandAck` (tagged with original `RequestId`)
- Publishes interaction events to IOS (selection changes, drag events, generic clicks when enabled)
- Applies configuration received from IOS
- Manages map-local objects (non-persisted overlays)

#### Data Backbone (SST)
- Source of truth for simulation entities
- Persistent storage for backbone-backed overlays
- Entity-Component System over DDS

### 2.3 Control Topology

**Primary Mode (Single-User)**:
- One IOS instance controls one IG map instance
- Simpler architecture, no locking overhead
- Recommended for most use cases

**Standalone Mode**:
- IG can operate without any IOS using predefined presets
- Useful for debugging, demonstrations, or read-only observers


---

## 3. Architecture Principles

### 3.1 Core Principles

1. **IG is authoritative for interaction mechanics and entity-creation dispatch; IOS is authoritative for strategy and parametrisation**
   - IG determines how clicks/drags behave based on the active tool
   - For `CMD_PLACE_ENTITY` and `CMD_DRAW_TACTICAL_GRAPHIC`, the IG resolves entity descriptors autonomously (TKB defaults + IOS JSON overrides in `CommandArgsJson`) and dispatches `CreateEntityRequest` directly to SimHost — identically to how volatile graphics are already handled
   - The IG bridges SimHost `CreateEntityAck` back to IOS as `MapCommandAck` (with original `RequestId`), carrying the newly allocated entity ID in `DataJson`
   - IOS retains full control by injecting JSON overrides (affiliation, type, etc.) into `MapCommandRequest.CommandArgsJson`, removing the need for the IOS to know IDL descriptor structures
   - The pure “click-to-IOS” model is retained as an opt-in fallback: `CMD_PICK_LOCATION` returns a coordinate via `MapCommandAck`; generic `MapClickEvent` is emitted when enabled by the active tool or map state

2. **Prefer presets for broad behavior, overrides for incremental changes**
   - Named presets provide coherent, tested configurations
   - JSON-based overrides allow runtime customization

3. **Asynchronous by default; avoid blocking UX**
   - Context menus fall back to defaults if IOS doesn't respond
   - Commands use correlation IDs for non-blocking request/response

4. **Everything observable is also controllable**
   - Selection, tools, filters can be both queried and set
   - Bidirectional state synchronization

5. **Tool internal decoupling**
   - IG tools know natively how to construct entity creation requests and draw shapes
   - However, tools are decoupled from the specific network protocol used to activate them or report results
   - A tool receives well-defined input parameters (from `MapCommandRequest.CommandArgsJson` or local UI) and communicates intermediate achievements through an internal delegate/callback mechanism
   - The upper layer (which handles `MapCommandRequest` ingestion and `MapCommandAck` publishing) is the only layer that knows about the DDS wire protocol
   - This ensures tools are reusable and testable independently of the network layer, and can be activated from local IG UI as well as from IOS commands

6. **Capability discovery prevents tight coupling**
   - IG announces available features and tool capabilities via `IGCapabilitiesAnnounce`
   - IOS adapts to IG capabilities dynamically



### 3.2 Design Patterns

#### JSON Merge Patch Pattern (Configuration - RFC 7396)
- **MapInteractionConfig** supports partial updates (deltas)
- **MapConfigStatus** provides current state feedback loop
- IOS learns defaults from JSON Schema and current state from status topic
- Merge semantics: received JSON patches into current state
- `null` values reset fields to schema defaults
- Late-joiner safety: IG publishes complete state on MapConfigStatus (Transient Local)

#### Cascading Configuration (Styling)
- **Level 1**: Global defaults in MapInteractionConfig (JSON)
- **Level 2**: Entity-specific overrides in descriptors (JSON)
- Benefit: Change global settings with one message; individual overrides still possible

#### TKB-Based 3-Layer Style Resolution
Overlay visual properties are resolved using this priority order (highest to lowest):
1. **JSON Override** (Highest): Instance-specific JSON in `styleOverrideJson`
2. **Preset Name**: Named style variant from `stylePresetName` (e.g., "Hostile_Dashed")
3. **TKB Default** (Lowest): Default properties from Technical Knowledge Base based on `tkbTypeId`

**Example**: If TKB ID 8801 = "Fire Line" defaults to orange/dashed, but IOS sends `stylePresetName="Friendly"`, the line renders blue. Adding `styleOverrideJson={"lineWidth": 3.5}` overrides width while keeping preset color.

#### Volatile Overlay Lifecycle
- **Persistence Modes**: `MODE_VOLATILE` (RAM-only) vs. `MODE_PERSISTENT` (saved to database)
- **Auto-Timeout**: volatile overlays can auto-delete after N seconds (real-time, not sim-time)
- **ID Reuse**: IOS maintains circular buffer of IDs (e.g., 10000-11000) for volatile FX
- **Use Cases**: Fire lines, hit indicators, detonation markers, temporary measurements

#### Context Round-Trip (Interaction Safety)
- Commands carry correlation IDs
- Events echo the correlation ID
- IOS can safely ignore stale events after timeout/cancel

---

## 4. Data Model Foundation

### 4.1 Entity Identity Model

**Principle**: Every map object is an entity with a unique global `EntityId`, regardless of kind or storage mode.

```
Entity ID Space: 64-bit unsigned integer
  ├─ Backbone-Persisted Entities (e.g., simulation units)
  └─ Map-Local Entities (e.g., temporary drawing overlays like annotations and scribbles)
```

**Allocation**: Centralized DDS-based allocator (BDC SST pattern) ensures global uniqueness

### 4.2 Descriptor-Per-Topic Model

Following SST conventions:
- Each descriptor/component has its own DDS topic
- First field in every descriptor is `EntityId` (instance key)
- Multi-instance descriptors use second key field `InstanceId`
- QoS: KeepLast(1) for state-style components

### 4.3 Lifecycle Consistency

**Backbone-Persisted Entities**:
- Follow SST lifecycle semantics
- Entity exists iff `EntityMaster` instance exists and is ALIVE
- Deletion driven by disposing `EntityMaster`

**Map-Local Entities**:
- Lifecycle is local to IG window
- Not published to backbone topics
- Scoped per IG instance

### 4.4 Ownership and Write Authority

**SST Ownership Rules Apply**:
- Only current owner (most recent writer) should publish updates to descriptors
- Non-owner writes are undefined behavior

**Descriptor Change Request Pattern**:
- Non-owners send `UpdateEntityDescriptorRequest` to current owner
- Owner validates and publishes update to descriptor topic
- Prevents ownership conflicts

### 4.5 Persistence Modes

Every map object has a `StorageMode`:

| Mode         | Description                                               | Use Case                                   |
| ------------ | --------------------------------------------------------- | ------------------------------------------ |
| **Backbone** | Published to SST topics, shared and persistent            | Simulation entities, shared overlays       |
| **Local**    | Scoped to single IG window (or a group of same map views) | Temporary drawings, instructor annotations |

Switching modes supported: "Commit local overlay to backbone" operation

---

## 5. DDS Topic Design

```c++

// ===================================================================================
// MAP / SCENARIO DESCRIPTORS
// ===================================================================================
// These descriptors are specific to the 2D/3D map functionality. 
// They allow the IOS to draw tactical graphics, override visuals, and define routes.
// ===================================================================================

    // Defines a visual override for a specific entity, targeted at a specific
    // group of displays (MapGroup).
    // Purpose: Allows "False Flag" operations or Instructor-only highlights.
    // Principle:
    // - If MapGroupId == 0, it applies to EVERYONE (Global override).
    // - If MapGroupId > 0, it applies ONLY to IGs configured with that GroupId.
    // - IG Logic: Look for specific override; if none, look for global; if none, use TKB default.
    struct MapEntitySymbol 
    {
        // Primary Key: Which entity is being modified?
        @key
        long EntityId; 
        
        // Target Group (Role).
        // 0 = Global Override (Applies to everyone).
        // >0 = Scoped Override (Applies ONLY to IGs with this MapGroupId).
        // Resolution Priority: Scoped > Global > TKB Default.
        @key
        long MapGroupId;

        // Named style set to apply (e.g., "False_Flag_Blue").
        // If empty, uses the entity's standard style.
        string StyleSetId;

        // Fine-grained visual overrides in JSON format.
        // e.g., { "colorOverride": "#0000FF", "forceLabel": "DECOY", "halo": true }
        string StyleParamsJson;
    };
    
    // Visual overlay descriptor (fire lines, tactical graphics, effects)
    // Supports both persistent and volatile (auto-deleting) instances
    enum PersistenceMode {
        MODE_VOLATILE,    // RAM-only, auto-delete timeout supported
        MODE_PERSISTENT   // Saved to database, survives restarts
    };

    struct MapVisualOverlay {
        @key long EntityId;
        
        // Persistence and lifecycle
        PersistenceMode PersistenceMode;
        
        // Birth Timestamp
        // The absolute time (csharp UTC datetime now ticks) when this graphic was created.
        // Requires for MODE_VOLATILE to calculate remaining life correctly.
        long long BirthTimestamp;
        
        // Auto-delete timeout (real-time, not sim-time)
        // 0.0 = manual delete only, > 0.0 = auto-delete after N seconds
        // Only valid for MODE_VOLATILE
        float AutoDeleteTimeoutSeconds;
        
        // TKB-based styling (3-layer resolution)
        // 1. styleOverrideJson (highest priority - instance-specific)
        // 2. stylePresetName (named variant, e.g., "Hostile_Dashed")
        // 3. TKB default based on tkbTypeId in Master (lowest priority)
        string StylePresetName;       // Empty = use TKB default
        string StyleOverrideJson;     // Empty = no overrides
        
        // Geometry - interpretation depends on TKB type
        // For icon/bitmap: points[0] is anchor position
        // For line/area: points define vertices
        sequence<GeoPoint> Points;
        
        // Performance optimization for large shapes during editing
        boolean IsPartialUpdate;
        sequence<long> ChangedIndices;  // Which points changed
        
        // Interaction flags
        boolean IsEditable;   // Can user drag/reshape?
        boolean IsClickable;  // Can user select/click for details?
    };


    // Defines a single point in a navigation path.
    struct Waypoint 
    {
        // 3D Position.
        GeoPosition Position;

        // Optional label (e.g., "Checkpoint Alpha").
        string Name; 

        // Desired speed when traveling to this point (m/s).
        double SpeedMetersPerSec;

        // JSON payload for mission-specific logic (e.g., "Hold for 5 mins", "Deploy sensors").
        string ExtensionJson;
    };

    // Defines a navigation route composed of multiple waypoints.
    struct MapRoute 
    {
        // The Entity ID representing this route.
        long EntityId;

        // Ordered list of waypoints.
        sequence<Waypoint> Points;

        // If true, the route connects the last point back to the first.
        boolean IsLoop;

        // Global mission data for the whole route (JSON).
        string ExtensionJson; 
    };


    // ===================================================================================
    // MAP CONFIGURATION & STATUS
    // ===================================================================================
    // These messages manage the setup of the map display (Layers, Tools) and the
    // reporting of the IG's capabilities.
    // Principle:
    // - IOS sends "Configuration" to a MapGroup (Role).
    // - IG sends "Status" for its specific MapId (Instance).
    // ===================================================================================

    // Configuration command sent from IOS to a group of IGs.
    // Uses JSON Merge Patch to support partial updates (e.g., just toggle one layer).
    // Scope: Group-based (Role). All IGs in "Blue Force" group receive this.
    struct MapInteractionConfig 
    {
        // The Target Group ID.
        long MapGroupId;

        // The Correlation ID of the currently active tool on the IOS side.
        // Example: IOS activates "Place Tank" tool -> Generates GUID "A".
        // IG receives "A" -> Stores it.
        // When user clicks map -> IG sends ClickEvent with ContextId "A".
        // IOS validates "A" matches current tool -> Executes logic.
        CorrelationId ActiveContextId;

        // Version number for the JSON schema, ensuring compatibility.
        long JsonSchemaVersion;

        // The configuration payload (JSON Merge Patch - RFC 7396).
        // Keys include: "view" (layers), "tools" (active cursor), "styles".
        // Null values in JSON indicate "Reset to Default".
        string ConfigurationJson;
    };




    // Feedback from a concrete IG instance reporting its current state.
    // Used by the IOS to synchronize its UI (e.g., checkboxes) with the reality.
    // Scope: Instance-based.
    struct MapConfigStatus
    {
        // The specific IG Instance reporting status.
        long MapId;

        // Name of the preset currently loaded (e.g., "Tactical_Default").
        string PresetName;
        
        // The FULL current configuration state (JSON).
        // Unlike 'MapInteractionConfig' which can be partial, this is always the complete Truth.
        string CurrentSettingsJson;
    };


    // Announcement message sent by an IG instance when it starts up.
    // Enables the IOS to dynamically build its UI based on what the IG supports.
    // IG -> IOS
    struct IGCapabilitiesAnnounce
    {
        // The specific IG Instance.
        long MapId;

        // Defines the layer structure (folders/items) for the IOS "Layers" panel.
        // JSON Tree format.
        string LayerTreeJson;

        // JSON Schemas defining valid configuration options (e.g., "What tools are available?").
        string ConfigurationSchemasJson;
        
        // JSON Schema validating the 'styleOverrideJson' field in overlays.
        string OverlayStyleSchemaJson;

        // JSON Manifest of TKB types that this IG specifically supports with special visuals.
        // Used to populate "Add Entity" menus with only valid options.
        string TkbManifestJson;
    };
    #pragma topic IGCapabilitiesAnnounce
    #pragma keylist IGCapabilitiesAnnounce MapGroupId
    #pragma topic reliability reliable
    #pragma topic durability transient_local



    // ===================================================================================
    // MAP INTERACTION: USER INPUT EVENTS
    // ===================================================================================
    // Events generated by the IG when the user interacts with the map hardware (Mouse/Touch).
    // All events are scoped to a specific MapId (Instance).
    // ===================================================================================

    enum EEntitySymbolPart
    {
        ESP_BODY,
        ESP_ICON,
        ESP_LABEL
    };


    // Describes an object under the mouse cursor.
    struct ObjectRef
    {
        // The ID of the entity.
        long EntityId;

        // The type of the entity (helper to avoid looking up Master).
        long TkbType; 

        // Legacy DIS type info.
        DisEntityType DisType;

        // Which part was clicked? (Icon, Label, or the 3D Body).
        EEntitySymbolPart VisualPart;
    };

    // Event sent when the user clicks on the map.
    // Includes a "Hit Stack" of all objects under the cursor (for disambiguation).
    struct MapClickEvent 
    {
        // The specific IG Instance where the click happened.
        long MapId;

        // World coordinates of the click.
        GeoPoint Position;

        // List of objects under the cursor, ordered Top-to-Bottom (Z-order).
        sequence<ObjectRef> HitStack; 
        
        // The Context ID active at the time of the click.
        // Allows IOS to route this click to the correct Tool logic.
        CorrelationId InteractionContextId;
    };


    enum DragState {
        DRAG_START,
        DRAG_UPDATE,
        DRAG_END,
        DRAG_CANCEL
    };

    // Event sent during a drag-and-drop operation.
    // Used for "Local Prediction": IG moves the object visually, sends updates to IOS.
    struct DragEvent 
    {
        // The specific IG Instance.
        long MapId;

        // Current phase: START (Picked up), UPDATE (Moving), END (Dropped), CANCEL (Esc).
        DragState State;

        // The Entity being dragged.
        long EntityId;

        // Current world coordinates of the cursor/object.
        GeoPoint CurrentPosition;

        // The Context ID associated with this drag operation.
        CorrelationId InteractionContextId;
    };


/*
**Drag Modes** (configured in tool settings):

| Mode                    | Behavior                                         | Use Case                        |
| ----------------------- | ------------------------------------------------ | ------------------------------- |
| **Local Preview**       | Only local visualization moves; commit on drop   | Default safe mode               |
| **Backbone on Drop**    | IG emits DragEnd; IOS commits to backbone        | Standard entity move            |
| **Continuous Backbone** | Intermediate updates published (explicit opt-in) | Real-time collaborative editing |
*/

    // Event sent when the user modifies the selection set (e.g., Box Select, Ctrl+Click).
    // The IG is authoritative for the selection state.
    struct SelectionChangedEvent 
    {
        // The specific IG Instance.
        long MapId;

        // The complete list of currently selected Entity IDs.
        // Replaces any previous selection state.
        sequence<long> SelectedEntityIds;
    };


// 4. Commands (IOS -> IG)
    enum CommandType {
        CMD_SET_VIEW,                   // Pan/zoom/center the camera to a geographic position
        CMD_SET_SELECTION,              // Programmatically change the entity selection set
        CMD_START_EDITING,              // Enter vertex-edit mode on an existing entity

        // IG-autonomous placement: IG resolves TKB defaults + IOS JSON overrides,
        // dispatches CreateEntityRequest to SimHost, and reports back via MapCommandAck.
        // One left-click = one entity; right-click/ESC finishes the tool.
        // Multiple placements in one session each yield an intermediate MapCommandAck
        // (StatusCode=1); the final right-click/ESC yields a closing MapCommandAck (StatusCode=0).
        CMD_PLACE_ENTITY,

        // IG-autonomous tactical-graphic drawing (areas, routes, phase lines, etc.).
        // Analogous to CMD_PLACE_ENTITY but for multi-point geometry.
        // Supports persistence="SCENARIO" (SimHost-owned) or persistence="SESSION" (IG-owned).
        CMD_DRAW_TACTICAL_GRAPHIC,

        // Pure coordinate-capture: IG acts as a dumb crosshair and returns the selected
        // GeoPoint in a MapCommandAck. No entity is created. Cancelled by ESC
        // (MapCommandAck with StatusCode != 0 and DataJson={"cancelled":true}).
        CMD_PICK_LOCATION
    };

    // Imperative commands sent from IOS to a specific IG Instance.
    // Used to force a specific behavior (e.g., "Pan Camera Here", "Enter Edit Mode").
    struct MapCommandRequest 
    {
        // Unique ID for this request.
        // Echoed in MapCommandAck.RequestId for correlation.
        CorrelationId RequestId;

        // Target IG Instance.
        long MapId;

        // Type of command (View, Selection, Editing).
        CommandType Type;

        // Arguments for the command in JSON format.
        // e.g., for CMD_SET_VIEW:  { "lat": 45.0, "lon": 12.0, "zoom": 1000 }
        // e.g., for CMD_PLACE_ENTITY / CMD_DRAW_TACTICAL_GRAPHIC:
        //   {
        //     "objectClass":  "T72_Tank",        // optional: overrides TKB default class
        //     "affiliation":  "FRIENDLY",         // optional: overrides TKB default affiliation
        //     "persistence":  "SCENARIO",         // optional: SCENARIO | SESSION (default: SCENARIO)
        //     "stylePresetName": "CommandUnit",   // optional: named style preset
        //     "styleOverrideJson": { ... },        // optional: fine-grained visual overrides
        //     "multiPlace": true                  // optional: keep tool active for repeated clicks
        //   }
        // All fields are optional. Unspecified fields fall back to the 3-layer resolution
        // pipeline: JSON Override -> Preset Name -> TKB Default.
        // e.g., for CMD_PICK_LOCATION:
        //   { "cursorLabel": "Select Artillery Target", "cursorIcon": "crosshair_red" }
        string CommandArgsJson; 
    };
    #pragma topic MapCommandRequest
    #pragma keylist MapCommandRequest RequestId


    // Response to a MapCommandRequest.
    // One request can generate multiple responses. For example,
    //   a CMD_PLACE_ENTITY tool configured with multiPlace=true creates one entity
    //   per left-click and finishes on right-click or ESC.
    // Each intermediate click yields StatusCode=1; the final close yields StatusCode=0.
    // Direction: IG -> IOS
    struct MapCommandAck
    {
        // Correlation ID matching the originating MapCommandRequest.RequestId.
        DDS::DM::Guid RequestId;

        // 0 = request finished (tool closed, no more acks for this RequestId)
        // 1 = intermediate result (tool still active, more acks to come)
        // other values reserved for error codes (e.g., SimHost rejected the CreateEntityRequest)
        long StatusCode;

        // Request-type-specific payload (JSON).
        // CMD_PLACE_ENTITY / CMD_DRAW_TACTICAL_GRAPHIC success:
        //   { "entityId": 9001 }                          // single entity created
        //   { "entityId": 9001, "toolFinished": true }    // last entity + tool closed (StatusCode=0)
        // CMD_PICK_LOCATION success:
        //   { "position": { "lat": 45.0, "lon": 15.0, "alt": 0.0 } }
        // CMD_PICK_LOCATION / any tool cancelled:
        //   { "cancelled": true }
        // Error cases:
        //   { "error": "SimHost rejected CreateEntityRequest", "code": 42 }
        string DataJson;
    };
    #pragma topic MapCommandAck reliable volatile keep_all


    // ===================================================================================
    // MAP INTERACTION: CONTEXT MENUS (PUSH MODEL)
    // ===================================================================================
    // Principle: Zero-Latency UI.
    // 1. User Selects Entity -> IOS calculates menu -> IOS Pushes 'ContextActionsUpdate'.
    // 2. IG caches update.
    // 3. User Right-Clicks -> IG opens menu from cache instantly.
    // 4. Fallback: If cache miss, IG sends 'ContextMenuRequest'.
    // 5. Execution: User clicks item -> IG sends 'ContextActionInvoked'.
    // ===================================================================================


    // Event sent by the IG when the user clicks a specific item in the context menu.
    // Direction: IG -> IOS
    struct ContextActionInvoked 
    {
        // Source IG Instance.
        long MapId;

        // The ID of the action chosen (defined in the JSON menu).
        long ActionId;

        // The ID of the specific entity on which the menu was opened.
        // Necessary for resolving ambiguity in multi-selections.
        long ContextEntityId;

        // The Context ID active when the menu was opened.
        CorrelationId ContextId;
    };
    #pragma topic ContextActionInvoked
    #pragma keylist ContextActionInvoked MapGroupId


    // Fallback request sent by the IG when the user right-clicks an entity
    // that does NOT have a cached menu definition available.
    // Direction: IG -> IOS
    struct ContextMenuRequest 
    {
        // Unique ID for this request. Must be echoed in the response.
        CorrelationId RequestId;

        // Source IG Instance (where the user clicked).
        long MapId;

        // The list of entities currently selected/clicked.
        // IOS uses this to generate the appropriate menu options.
        sequence<long> ForSelection;
    };
    #pragma topic ContextMenuRequest
    #pragma keylist ContextMenuRequest RequestId




    // Proactive update from IOS containing the context menu definition for a specific selection.
    // Direction: IOS -> IG
    struct ContextActionsUpdate 
    {
        // Target Map Group (Role).
        // Menus are business logic, so they apply to the Role (e.g. "Instructor"), 
        // not just one screen.
        long MapGroupId;

        // Validation list: Which selected entities does this menu apply to?
        // The IG checks this against its current selection. If the user's selection 
        // has changed since this message was generated, the IG discards this update.
        sequence<long> ForSelection;
        
        // Menu structure as JSON.
        // Supports nested menus, icons, shortcuts, and enable/disable states.
            // Menu structure as JSON (flexible, nestable)
            // See "Menu JSON Schema" below
            /*
                [
                {
                    "id": 100,
                    "label": "Move Here",
                    "icon": "move_cursor",
                    "shortcut": "M",
                    "enabled": true
                },
                { "type": "separator" },
                {
                    "label": "Logistics",
                    "icon": "gear",
                    "children": [
                    {
                        "id": 201,
                        "label": "Resupply",
                        "enabled": false,
                        "tooltip": "Cannot resupply: Unit is moving"
                    },
                    { "id": 202, "label": "Repair" }
                    ]
                }
                ]
            */
            string MenuDefinitionJson;
    };
```

**Layer Tree JSON Schema**:
```json
[
  {
    "id": "layer_sat_group",
    "name": "Satellite Imagery",
    "type": "folder",
    "children": [
      {
        "id": "layer_sat_highres",
        "name": "High Res (2024)",
        "type": "layer",
        "defaultVisible": true
      }
    ]
  }
]
```

**Configuration Schemas JSON**:
```json
{
  "modes": {
    "tactical_view": {
      "$schema": "http://json-schema.org/draft-07/schema#",
      "title": "Tactical Settings",
      "properties": {
        "showGrid": { "type": "boolean" },
        "gridOpacity": { "type": "number", "minimum": 0, "maximum": 1 }
      }
    }
  },
  "symbol_sets": {
    "mil-std-2525b": {
      "title": "2525B Options",
      "properties": {
        "iconSize": { "type": "integer", "minimum": 16, "maximum": 64 }
      }
    }
  }
}
```

**Configuration JSON Structure (Full State Example)**:

> **Note:** The `activeTool` field reflects the *display state* of the IG cursor for
> standalone/debug mode. In production, tool activations for entity creation use
> `MapCommandRequest(CMD_PLACE_ENTITY / CMD_DRAW_TACTICAL_GRAPHIC)` instead.
> The `MapInteractionConfig` alone should not trigger entity creation.

```json
{
  "interaction": {
    "activeTool": "CURSOR_PLACE_ENTITY",
    "toolSettings": {
      "objectClass": "T72_Tank",
      "persistenceScope": "BACKBONE",
      "snapping": true
    },
    "selection": {
      "color": "#00FF00",
      "haloSize": 1.5,
      "multiSelect": true
    }
  },
  "view": {
    "cameraMode": "TACTICAL_2D",
    "minZoom": 100,
    "maxZoom": 50000
  },
  "layers": {
    "visible": ["Map_Background", "Military_Overlay"],
    "hidden": ["Debug_Grid"],
    "filters": [
      { "field": "affiliation", "operator": "!=", "value": "NEUTRAL" }
    ]
  },
  "styles": {
    "globalStandard": "NATO_APP6",
    "defaults": {
      "iconSize": 32,
      "labelVisible": true,
      "font": "Arial"
    }
  }
}
```

**Partial Update Example (IOS → IG)**:
```json
{
  "view": {
    "cameraMode": "FREE_CAM"
  }
}
```

**Reset to Default Example**:
```json
{
  "styles": {
    "defaults": {
      "iconSize": null  // Resets to schema default
    }
  }
}
```

```

**Example Command Arguments**:

**CMD_SET_VIEW**:
```json
{
  "lat": 45.0,
  "lon": 14.0,
  "zoom": 1000,
  "heading": 0,
  "pitch": -90
}
```

**CMD_START_EDITING**:
```json
{
  "targetEntityId": 505,
  "enableSnapping": true
}
```

**CMD_PLACE_ENTITY** (all fields optional — unspecified fields fall back to TKB defaults):
```json
{
  "objectClass":      "T72_Tank",
  "affiliation":      "FRIENDLY",
  "persistence":      "SCENARIO",
  "multiPlace":       false,
  "stylePresetName":  "CommandUnit",
  "styleOverrideJson": { "labelVisible": true }
}
```

**CMD_DRAW_TACTICAL_GRAPHIC** (all fields optional):
```json
{
  "tkbTypeId":        8801,
  "persistence":      "SCENARIO",
  "stylePresetName":  "Hostile_Dashed",
  "styleOverrideJson": { "lineWidth": 3 }
}
```

**CMD_PICK_LOCATION**:
```json
{
  "cursorLabel": "Select Artillery Target",
  "cursorIcon": "crosshair_red"
}
```



### 5.6 Context Menu Topics


Traditional context menus use a request/response pattern: user right-clicks → network delay → menu appears. This creates a frustrating ~500ms lag.

The **push model** eliminates this by leveraging "think time": when the user selects an entity (left-click), the IOS immediately calculates and pushes valid actions. By the time the user right-clicks (typically 500-2000ms later), the menu is already cached and opens instantly.

**Architecture**:
1. **Push (Common Case)**: IOS sends menu on selection change → IG caches → Right-click opens instantly
2. **Graceful Degradation (Edge Case)**: Right-click on unselected item → Show TKB defaults + "Loading..." → Dynamic items arrive

**Why JSON Instead of Recursive IDL**:
- **Flexibility**: Support icons, tooltips, keyboard shortcuts without recompiling
- **Simplicity**: Native nesting without DDS recursion complexity
- **Future-Proof**: Add new UI features (checked states, badges) without IDL changes

---

**Menu JSON Schema**:
```json
[
  {
    "id": 100,
    "label": "Move Here",
    "icon": "move_cursor",
    "shortcut": "M",
    "enabled": true
  },
  { "type": "separator" },
  {
    "label": "Logistics",
    "icon": "gear",
    "children": [
      {
        "id": 201,
        "label": "Resupply",
        "enabled": false,
        "tooltip": "Cannot resupply: Unit is moving"
      },
      { "id": 202, "label": "Repair" }
    ]
  }
]
```


---

**Workflow: Zero-Latency Menu (Common Case)**:

```
Step 1: User Left-Clicks Unit
    IG: Highlights unit, publishes SelectionChangedEvent

Step 2: Think Time (500-2000ms - user moves mouse, considers action)
    IOS: Receives SelectionChanged
         Calculates logic: "Is unit damaged?" → Enable "Repair"
                          "Is unit moving?" → Disable "Resupply"
         Publishes ContextActionsUpdate (JSON)
    
    IG: Receives update, caches JSON against selection IDs

Step 3: User Right-Clicks
    IG: Checks cache → HIT
        Merges: TKB Defaults + Cached JSON
        Opens menu **instantly (0ms latency)**
```

**Workflow: Cache Miss (Edge Case)**:

```
User Right-Clicks Unselected Entity:
    IG: Check cache → MISS
        Immediately opens menu with:
            - TKB defaults (from manifest)
            - "Loading dynamic actions..." spinner
        Publishes ContextMenuRequest
    
    IOS: Receives request
         Calculates and sends ContextActionsUpdate
    
    IG: Receives update
        Removes spinner
        Inserts dynamic items into **already-open** menu
        
    Result: ~500ms delay (same as old model), but TKB defaults show immediately
```

### 5.7 Data Backbone Topics
see [BDC SST Data Model Basics](/Products-and-components/Bagira.Infra/BDC/BDC-SST-Data-Model-Basics)


## 6. Interaction Workflows

### 6.1 Initial Setup Workflow

```
IOS Startup
    │
    ├─► Subscribe to IGCapabilitiesAnnounce
    │
    ├─► Subscribe to MapConfigStatus
    │
    ├─► Receive capabilities (layers, schemas with defaults)
    │
    ├─► Receive current config status (if IG already running)
    │
    ├─► Generate UI (use schema defaults + current status)
    │
    ├─► Publish MapInteractionConfig (selected preset or partial updates)
    │
    └─► IG applies configuration and renders

IG Startup
    │
    ├─► Load factory defaults from schemas
    │
    ├─► Publish IGCapabilitiesAnnounce (latching)
    │
    ├─► Publish MapConfigStatus (current state = defaults)
    │
    ├─► Subscribe to MapInteractionConfig
    │
    └─► Apply defaults (standalone mode) until IOS connects
```

### 6.1a Configuration Update Workflow (Partial Update)

This demonstrates the JSON Merge Patch pattern in action.

```
Initial State (IG):
    { "view": { "cameraMode": "TACTICAL_2D", "gridOpacity": 0.5 },
      "styles": { "iconSize": 32 } }

User Action: Change grid opacity to 0.8
    │
IOS:
    ├─► Read current state from local cache (last MapConfigStatus)
    │   └─► Current opacity = 0.5
    │
    ├─► Construct partial JSON (only the change)
    │   └─► { "view": { "gridOpacity": 0.8 } }
    │
    └─► Publish MapInteractionConfig
        └─► configurationJson = '{"view":{"gridOpacity":0.8}}'

IG:
    ├─► Receive MapInteractionConfig
    │
    ├─► Apply JSON Merge Patch (RFC 7396):
    │   ├─► Current: { "view": { "cameraMode": "TACTICAL_2D", "gridOpacity": 0.5 }, ... }
    │   ├─► Patch:   { "view": { "gridOpacity": 0.8 } }
    │   └─► Result:  { "view": { "cameraMode": "TACTICAL_2D", "gridOpacity": 0.8 }, ... }
    │                 (Note: cameraMode unchanged)
    │
    ├─► Validate merged result against schema
    │
    ├─► Apply to renderer
    │
    └─► Publish MapConfigStatus
        └─► currentSettingsJson = '{"view":{"cameraMode":"TACTICAL_2D","gridOpacity":0.8}, ...}'

IOS:
    ├─► Receive MapConfigStatus
    │
    ├─► Verify gridOpacity now 0.8 ✓
    │
    └─► Update local cache (for future partial updates)
```

**Reset to Default Example**:

```
User Action: Reset icon size to default
    │
IOS:
    ├─► Lookup default from schema: iconSize.default = 32
    │
    ├─► Construct reset JSON (null value)
    │   └─► { "styles": { "iconSize": null } }
    │
    └─► Publish MapInteractionConfig

IG:
    ├─► Receive patch with null value
    │
    ├─► Detect null → lookup schema default (32)
    │
    ├─► Set iconSize = 32
    │
    └─► Publish MapConfigStatus (updated state)
```

**Benefits of this Pattern**:
- **Bandwidth**: Only send changed fields (e.g., 20 bytes vs 2KB full config)
- **No Synchronization Logic**: IOS doesn't need to track "what changed"
- **Defaults Known**: Schema provides factory defaults; status provides current
- **Late Joiner Safe**: MapConfigStatus is Transient Local (new IOS gets state)
- **Standard Protocol**: RFC 7396 has library implementations

### 6.2 Place Entity Workflow

The primary workflow delegates entity creation entirely to the IG. The IOS parametrises the
tool via `MapCommandRequest` and consumes `MapCommandAck` results; it never needs to allocate
IDs or understand IDL descriptor structures.

#### 6.2a Primary: IG-Autonomous Single-Placement

```
User Action: "Add Tank"
    │
IOS:
    ├─► Publish MapCommandRequest
    │   └─► requestId = UUID-12345
    │   └─► type = CMD_PLACE_ENTITY
    │   └─► commandArgsJson = {
    │           "objectClass":   "T72_Tank",   // optional - overrides TKB default
    │           "affiliation":   "FRIENDLY",   // optional - overrides TKB default
    │           "persistence":   "SCENARIO",   // optional - default: SCENARIO
    │           "multiPlace":    false         // single click then tool closes
    │         }
    │
IG:
    ├─► Receive MapCommandRequest
    │
    ├─► Merge: TKB defaults for T72_Tank  +  IOS JSON overrides
    │
    ├─► Change cursor to crosshair + ghost tank
    │
    └─► Wait for user click

User clicks map (Lat 45.0, Lon 15.0)
    │
IG:
    ├─► Construct CreateEntityRequest
    │   └─► Complete set of descriptors (Master + Symbol + Pose …)
    │   └─► Using merged TKB defaults + IOS overrides
    │
    ├─► Publish CreateEntityRequest → SimHost
    │
SimHost:
    ├─► Validate & Allocate EntityId = 9001
    ├─► Publish EntityMaster / EntityInfo / Pose on Backbone
    └─► Publish CreateEntityAck (Success, entityId=9001) → IG

IG:
    ├─► Receive CreateEntityAck
    │
    ├─► Publish MapCommandAck → IOS
    │   └─► requestId    = UUID-12345
    │   └─► statusCode   = 0   (tool finished / closed)
    │   └─► dataJson     = { "entityId": 9001 }
    │
    └─► Renders tank (entity now visible via Backbone subscription)

IOS:
    ├─► Receive MapCommandAck
    │
    ├─► Maps requestId UUID-12345 → "Add Tank" operation
    │
    ├─► Reads entityId 9001 from dataJson
    │
    └─► Optionally sends CMD_SET_SELECTION to highlight the new entity
```

::: mermaid
sequenceDiagram
    autonumber
    participant IOS as IOS (Orchestrator)
    participant IG as IG (Autonomous Agent)
    participant SimHost as SimHost (Scenario Owner)

    Note over IOS, SimHost: 6.2a Place Entity — IG-Autonomous Workflow

    IOS->>IG: MapCommandRequest<br/>(CMD_PLACE_ENTITY, RequestId=UUID-12345,<br/>args={objectClass:T72_Tank, affiliation:FRIENDLY})

    Note over IG: Merges TKB defaults + IOS overrides.<br/>Shows ghost tank cursor.

    Note over IG: User clicks map at (45.0, 15.0)

    IG->>SimHost: CreateEntityRequest<br/>(Full descriptor set — Master, Symbol, Pose…)

    SimHost->>SimHost: Validate & Allocate EntityId=9001
    SimHost-->>SimHost: Publish Backbone descriptors

    par Backbone Update
        SimHost->>IG: Entity 9001 data
        SimHost->>IOS: Entity 9001 data
    end

    SimHost->>IG: CreateEntityAck (Success, entityId=9001)

    IG->>IOS: MapCommandAck<br/>(RequestId=UUID-12345, StatusCode=0,<br/>DataJson={entityId:9001})

    Note left of IOS: IOS correlates via RequestId.<br/>Optionally selects entity 9001.
:::


#### 6.2b Extended: Multi-Placement Tool

When `multiPlace: true` is set in `CommandArgsJson`, the tool stays active after each click. Each
entity creation yields a `StatusCode=1` (intermediate) ack; right-click or ESC closes the tool
and yields the final `StatusCode=0` ack.

```
IOS:
    └─► MapCommandRequest (CMD_PLACE_ENTITY, requestId=UUID-X, args={..., "multiPlace": true})

User clicks → entity A created:
    IG: MapCommandAck (requestId=UUID-X, StatusCode=1, DataJson={"entityId": 9001})
    // Tool is still active

User clicks → entity B created:
    IG: MapCommandAck (requestId=UUID-X, StatusCode=1, DataJson={"entityId": 9002})

User right-clicks → tool closes:
    IG: MapCommandAck (requestId=UUID-X, StatusCode=0, DataJson={"toolFinished": true})
    // IOS knows request UUID-X is now closed
```


#### 6.2c Fallback: Pure Click Forwarding (CMD_PICK_LOCATION)

For workflows where the IOS strictly requires a geographic coordinate rather than an entity
instantiation (e.g., "Where should artillery fire?"), use `CMD_PICK_LOCATION`. The IG acts
as a dumb crosshair and returns the coordinate without creating anything.

```
IOS:
    └─► MapCommandRequest (CMD_PICK_LOCATION, requestId=UUID-77,
                           commandArgsJson={"cursorLabel": "Select Target", "cursorIcon": "crosshair_red"})

User clicks:
    IG: MapCommandAck (requestId=UUID-77, StatusCode=0,
                       dataJson={"position": {"lat": 45.1, "lon": 15.3, "alt": 0.0}})

User presses ESC (cancelled):
    IG: MapCommandAck (requestId=UUID-77, StatusCode=0, dataJson={"cancelled": true})
```


#### 6.2d Legacy / Optional: Generic Click Pass-through

The IG may still emit `MapClickEvent` if the currently active tool or map state explicitly
enables generic click pass-through. This retains the original IOS-centric flow for advanced
customisation scenarios but is **not the default creation path**.

```
[only when generic clicks are enabled by active tool/map state]

User clicks map
    │
IG:
    └─► Publish MapClickEvent
        └─► position = (Lat 45.0, Lon 15.0)
        └─► interactionContextId = <active context id>

IOS:
    ├─► Verify context id is still valid
    └─► Interprets click as appropriate (e.g., custom logic, not standard entity creation)
```

### 6.3 Drag and Drop Workflows

Drag and drop operations use a **"Local Prediction, Global Commit"** model to ensure responsiveness regardless of network latency. The workflow differs based on who owns the entity.

**Scenario A: Dragging a SESSION Entity (IG Owned)**
*Example: Moving a local ruler or annotation.*
Since the IG owns the entity, it can write updates directly to the backbone. To ensure smooth movement for other observers, the IG throttles updates.

1. **Mouse Down:** User clicks entity.
2. **Drag Loop:** * IG updates local rendering instantly (60fps).

    - IG publishes `MapVisualOverlay` updates to DDS throttled at ~10Hz.
3. **Mouse Up:** IG publishes final `MapVisualOverlay` position.

**Scenario B: Dragging a SCENARIO Entity (SimHost Owned)**
*Example: Moving a Tank or tactical boundary.*
The IG cannot write directly. It uses a "Ghost" for feedback and sends a single commit request at the end.

1. **Mouse Down:** IG creates a semi-transparent "Ghost" of the entity.
2. **Drag Loop:** * User moves mouse; IG updates Ghost position locally.

    - Real entity remains stationary on the map.
3. **Mouse Up (Commit):**

    - IG sends `UpdateEntityDescriptorRequest` (or `UpdateEntityAttributeRequest`) with the new coordinates.
    - SimHost validates and updates the entity state.
    - DDS updates the entity position; IG snaps real entity to new spot and removes Ghost.

**Optimization: Partial Updates**
For complex geometry (e.g., a polygon with 50 points), the IG does not resend the entire shape.

- **Payload:** `isPartialUpdate = true`, `changedIndices = [3]`, `points = [New_Pos_Vertex_3]`.


```
IG configured: dragMode = "Local-Preview"

User drags entity 9001 (Shared/Scenario Entity)
   │
IG:
   ├─► Publish DragEvent (For UI/Logging only)
   │   └─► state = DRAG_START
   │   └─► entityId = 9001
   │
   ├─► Create visual "Ghost" (Local only)
   │
   └─► Update Ghost position as mouse moves (Terrain snapping applied)

User releases mouse (Commit)
   │
IG:
   ├─► Publish UpdateEntityDescriptorRequest (Direct to Owner)
   │   └─► entityId = 9001
   │   └─► currentOwnerId = <owner from metadata>
   │   └─► descriptorType = POSE
   │   └─► payload.pose = final position
   │
   └─► Publish DragEvent (For UI/Logging only)
       └─► state = DRAG_END

Owner (SimHost):
   ├─► Receive Request
   │
   ├─► Validate logic (Terrain check, collision check)
   │
   └─► Publish EntityState (New Position) on Backbone

IG & IOS:
   └─► Receive EntityState update via DDS
       ├─► IG removes Ghost, snaps real entity to new position
       └─► IOS updates coordinate readout
```
::: mermaid
sequenceDiagram
    autonumber
    actor User
    participant IG
    participant Owner as SimHost (Owner)
    participant IOS as IOS (Observer)

    Note over User, IOS: Corrected 6.3 Workflow (Direct Request)

    User->>IG: Mouse Down (on Tank 9001)
    IG->>IG: Create "Ghost" (Local Preview)
    
    IG->>IOS: Publish DragEvent (DRAG_START) 
    Note right of IG: IOS receives this for UI/Logging only

    loop Dragging
        User->>IG: Mouse Move
        IG->>IG: Update Ghost Position (Snapping to Terrain)
    end

    User->>IG: Mouse Up (Release)

    Note right of IG: Critical Change: IG sends request directly
    IG->>Owner: UpdateEntityDescriptorRequest<br/>(id=9001, payload=FinalPosition)

    Owner->>Owner: Validate & Update
    Owner->>IG: Ack (Success)
    
    par Update Visibility
        Owner->>IG: Publish Entity Pose (New)
        Owner->>IOS: Publish Entity Pose (New)
    end

    IG->>IG: Remove Ghost, Snap Entity
    IG->>IOS: Publish DragEvent (DRAG_END)
:::

### 6.3-alternative using ownership transfer

This model is useful if you need high-frequency updates visible to everyone during the drag (e.g., collaborative real-time editing) but requires handling the ownership handover latency.

In this approach, the IG explicitly takes ownership of the entity's position descriptor for the duration of the interaction. This allows the IG to publish high-frequency updates directly to the backbone, giving all other observers real-time feedback of the movement.

**Core Concept:**

1. **Acquire:** On mouse down, IG requests ownership.
2. **Drive:** During drag, IG writes directly to the topic (no requests/acks).
3. **Release:** On mouse up, IG returns ownership to the Simulation Host.

#### ASCII Interaction Flow
```
User initiates drag on entity 9001 (Shared/Scenario Entity)
   │
IG:
   ├─► Publish OwnershipUpdate (Target=Self/IG)
   │   └─► EntityId = 9001
   │   └─► DescriptorType = POSE
   │   └─► NewOwner = <IG_Node_Id>
   │
   └─► Wait for SimHost to acknowledge/cease writing (implicitly via DDS)

SimHost (Current Owner):
   ├─► Receive OwnershipUpdate
   │
   └─► Stop publishing updates for Entity 9001
       └─► (Internal state updated to "Remote Controlled")

User drags mouse (Real-time Phase)
   │
IG (Now the Owner):
   ├─► Calculate new position (Terrain snapping)
   │
   └─► Publish EntityState (Direct Write) @ ~30Hz
       └─► All other clients see entity moving smoothly in real-time

User releases mouse (Commit)
   │
IG:
   ├─► Publish OwnershipUpdate (Target=SimHost)
   │   └─► EntityId = 9001
   │   └─► NewOwner = <SimHost_Node_Id>
   │
   └─► Stop publishing updates

SimHost:
   ├─► Receive OwnershipUpdate
   │
   ├─► Read final position from Backbone (last value published by IG)
   │
   └─► Resume ownership (Internal state "Locally Controlled")
       └─► Validate final position (clamp to valid area if needed)
       └─► Publish EntityState (Authoritative correction if needed)
```
#### Mermaid Sequence Diagram
::: mermaid
sequenceDiagram
    autonumber
    actor User
    participant IG
    participant SimHost as SimHost (Default Owner)
    participant DDS as DDS Backbone

    Note over User, DDS: Alternative 6.3: Temporary Ownership Transfer

    User->>IG: Mouse Down (on Tank 9001)

    Note right of IG: Phase 1: Acquire Control
    IG->>DDS: OwnershipUpdate(Entity=9001, NewOwner=IG)
    
    SimHost->>DDS: Receive OwnershipUpdate
    SimHost->>SimHost: Disable internal Writer (Yield)

    Note right of IG: Phase 2: Real-time Driving
    loop Dragging
        User->>IG: Mouse Move
        IG->>DDS: Publish EntityState (Direct Write)
        DDS->>SimHost: Update (SimHost sees movement)
        DDS->>IOS: Update (IOS sees movement)
    end

    User->>IG: Mouse Up (Release)

    Note right of IG: Phase 3: Return Control
    IG->>DDS: OwnershipUpdate(Entity=9001, NewOwner=SimHost)
    IG->>IG: Disable internal Writer
    
    SimHost->>DDS: Receive OwnershipUpdate
    SimHost->>SimHost: Resume internal Writer
    SimHost->>SimHost: Validate Final Position
    SimHost->>DDS: Publish EntityState (Confirmed)
:::





### 6.4 Context Menu Workflow

To eliminate the ~500ms network lag often associated with requesting menus over a network, the system uses a **Proactive Push** model, acompanied with a fallback to "Pull" model where push is not possible.

**Phase 1: Proactive Calculation (The "Think Time")**

1. **User Selects Entity:** IG sends `SelectionChangedEvent`.
2. **IOS Calculation:** IOS receives selection, determines valid actions (e.g., "Unit is Damaged" -> enable "Repair"), and constructs the menu JSON.
3. **Push:** IOS publishes `ContextActionsUpdate` targeting the `MapGroupId`.
4. **Cache:** IG receives the update and caches it against the Entity ID.

**Phase 2: Instant Open**

1. **User Right-Clicks:** * IG checks cache.

    - **Hit:** IG merges IOS actions with local IG actions (e.g., "Center Map") and opens menu instantly (0ms latency).
    - **Miss:** (Edge case, fast click) IG opens menu with local actions and a "Loading..." spinner, then sends `ContextMenuRequest`.

**Phase 3: Execution**

- **IG Action:** If JSON has `"actionName": "IG_Camera_Lock"`, IG executes locally.
- **IOS Action:** If `"actionName"` is null, IG sends `ContextActionInvoked` to IOS.


```
User Left-Clicks Entity 9001 (Select)
   │
IG:
   ├─► Publish SelectionChangedEvent
   │   └─► selectedIds = [9001]
   │
   └─► Render Selection Halo

IOS (Proactive Calculation):
   ├─► Receive SelectionChangedEvent
   │
   ├─► Logic: Entity 9001 is Damaged? -> Enable "Repair"
   │
   └─► Publish ContextActionsUpdate (Push)
       └─► targetGroupId = <InstructorGroup>
       └─► forSelection = [9001]
       └─► menuJson = [{ label: "Repair", actionId: 102 }]

IG (Cache Update):
   └─► Receive ContextActionsUpdate
       └─► Store JSON in cache key [9001]

User Right-Clicks Entity 9001
   │
IG:
   ├─► Check Cache for [9001] -> HIT
   │
   ├─► Merge: IOS Items ("Repair") + IG Defaults ("Center Map")
   │
   └─► Display Menu Instantly (0ms latency)

User selects "Repair"
   │
IG:
   └─► Publish ContextActionInvoked
       └─► actionId = 102
       └─► contextEntityId = 9001
```

::: mermaid
sequenceDiagram
    autonumber
    actor User
    participant IG
    participant IOS

    Note over User, IOS: Scenario A: Proactive Push (Zero Latency)
    
    User->>IG: Left Click (Select 9001)
    IG->>IOS: SelectionChangedEvent ([9001])
    
    Note right of IOS: IOS thinks...
    IOS->>IG: ContextActionsUpdate<br/>(for=[9001], items=["Repair"])
    
    IG->>IG: Cache Menu Definition
    
    User->>IG: Right Click (on 9001)
    
    IG->>IG: Check Cache (Hit)
    IG-->>User: Show Menu Instantly
:::


### 6.4b Context Menu Workflow (Scenario B: Fallback / Pull)

*Triggered when the user right-clicks an entity that is NOT currently selected (Cache Miss).*

```
User Right-Clicks Entity 9001 (Unselected)
   │
IG:
   ├─► Check Cache for [9001] -> MISS
   │
   ├─► Display Menu Immediately containing:
   │   ├─► Local Defaults ("Center Map", "Deselect")
   │   └─► [Spinner] "Loading options..."
   │
   └─► Publish ContextMenuRequest (Pull)
       └─► requestId = UUID-55
       └─► targetEntityId = 9001

IOS:
   ├─► Receive ContextMenuRequest
   │
   ├─► Logic: Entity 9001 is Hostile? -> Enable "Attack"
   │
   └─► Publish ContextActionsUpdate (Push)
       └─► requestId = UUID-55  <-- Correlates to request
       └─► menuJson = [{ label: "Attack", actionId: 300 }]

IG:
   ├─► Receive ContextActionsUpdate
   │
   └─► Update Open Menu (Live)
       ├─► Remove "Loading..." spinner
       └─► Insert "Attack" item
```

::: mermaid
sequenceDiagram
    autonumber
    actor User
    participant IG
    participant IOS

    Note over User, IOS: Scenario B: Right-Click on Unselected (Cache Miss)

    User->>IG: Right Click (on 9001)
    
    IG->>IG: Cache Miss
    IG-->>User: Show Menu (Defaults + Spinner)
    
    IG->>IOS: ContextMenuRequest (target=9001)
    
    Note right of IOS: IOS calculates logic...
    IOS->>IG: ContextActionsUpdate (items=["Attack"])
    
    IG-->>User: Update Menu UI<br/>(Replace Spinner with "Attack")
:::



### 6.5 Edit Overlay Workflow

```
User: "Edit fire support line 505"

IOS:
    └─► Publish MapCommandRequest
        └─► requestId = UUID-77
        └─► type = CMD_START_EDITING
        └─► commandArgsJson = { "targetEntityId": 505, "enableSnapping": true }

IG:
    ├─► Receive command
    │
    ├─► Load geometry from backbone (entity 505)
    │
    ├─► Display vertex handles
    │
    └─► Enter edit mode

User drags vertex 3

IG:
    ├─► Publish DragEvent
    │   └─► entityId = 505
    │   └─► state = DRAG_UPDATE
    │   └─► interactionContextId = UUID-77
    │
    └─► Update local preview

User releases (commits)

IG:
    └─► Publish DragEvent
        └─► state = DRAG_END

IOS:
    ├─► Receive DragEvent (DRAG_END)
    │
    └─► Publish UpdateEntityDescriptorRequest
        └─► entityId = 505
        └─► descriptorType = GEOMETRY
        └─► payload.geometry.isPartialUpdate = true
        └─► payload.geometry.changedIndices = [3]
        └─► payload.geometry.points = [<new point 3>]

Backbone:
    └─► Owner updates geometry descriptor

IG:
    └─► Receives updated geometry, renders
```

**Performance Optimization**: Partial updates reduce bandwidth for large geometries.

### 6.6 Create Volatile Graphic (Fire Line with Auto-Timeout)


In this scenario, the IOS defines the **content** of the visual effect (using standard descriptors) but requests the IG to **create and own** the entity locally. This allows the IOS to utilize the standard entity definition mechanism without needing to manage the high-frequency lifecycle of temporary objects.

**Architectural Approach:**
1.  **IOS defines the "What":** The IOS constructs a `CreateEntityRequest` containing the full definition (Descriptors) of the fire line.
    
2.  **IG handles the "How":** The IG receives the request, allocates a local ID, publishes the descriptors as the owner, and manages the auto-deletion timer.
    
Scenario: Show tracer fire from Tank (Point A) to Target (Point B) for 2 seconds.
    
    1. IOS:
       └─► Decides a visual effect is needed.
       └─► Constructs `CreateEntityRequest`:
           ├─► RequestId = UUID-200
           ├─► Owner = <Target IG NodeId>  (Asking IG to take ownership)
           ├─► Flags = VOLATILE_LIFECYCLE  (Hint to use local ID pool)
           └─► InitialDescriptors (Sequence):
               ├─► [0] EntityMaster:
               │    ├─► TkbType = 8801 ("Tracer Fire Line")
               │    └─► DisType = ...
               │
               └─► [1] MapVisualOverlay:
                    ├─► PersistenceMode = MODE_VOLATILE
                    ├─► AutoDeleteTimeoutSeconds = 2.0  (Real-time)
                    ├─► Points = [ {LatA, LonA}, {LatB, LonB} ]
                    └─► StylePresetName = "High_Contrast"
    
       └─► Publishes `CreateEntityRequest` to DDS.
    
    2. IG:
       ├─► Receives `CreateEntityRequest` addressed to it.
       │
       ├─► Allocates ID = 90001 
       │   (Ignores any ID placeholder sent in the descriptors).
       │
       ├─► Overwrites `EntityId` in all descriptors to 90001.
       │
       ├─► Publishes descriptors to DDS (as Owner):
       │   ├─► EntityMaster (ID=90001)
       │   └─► MapVisualOverlay (ID=90001)
       │
       ├─► Sends `CreateEntityAck` (Success, NewEntityId=90001).
       │
       └─► Reads `AutoDeleteTimeoutSeconds` (2.0) and starts internal timer.
    
    3. Backbone (DDS):
       └─► Distributes the new entity. Other IGs (if subscribed) see it.
    
    4. IG (after 2.0 seconds):
       ├─► Timer expires.
       ├─► Disposes `EntityMaster` (Instance 90001) and other descriptors for this entity.
       └─► ID 90001 is returned to the reuse pool (or not, depends on the id alloc policy).

::: mermaid
sequenceDiagram
autonumber
participant IOS as IOS (Controller)
participant IG as IG (Owner/Renderer)
participant DDS as DDS Backbone
Note over IOS, DDS: 6.6 Volatile Graphic via Standard Request
    
    Note right of IOS: IOS defines the entity<br/>using standard descriptors
    
    IOS->>DDS: CreateEntityRequest<br/>(Owner=IG, Payload=[Master, Overlay])
    
    DDS->>IG: CreateEntityRequest
    
    Note over IG: IG allocates Local ID (e.g., 90001)
    
    par Publication & Ack
        IG->>DDS: Publish EntityMaster (ID=90001)
        IG->>DDS: Publish MapVisualOverlay (ID=90001)
        IG->>IOS: CreateEntityAck (Success, NewID=90001)
    end
    
    Note over IG: IG Renders Tracer Line & Starts Timer
    
    Note over IG: ... 2.0 Seconds Pass ...
    
    IG->>DDS: Dispose EntityMaster (90001)
    
    Note right of IG: Entity removed automatically
:::
**Benefits of this approach:**
*   **Standardization:** Uses the same data structures (`EntityMaster`, `MapVisualOverlay`) for both persistent units and temporary effects. No need for custom JSON command parsers.
    
*   **Decoupling:** The IOS knows _what_ a fire line is (TKB Type 8801). The IG just knows how to host and render entities.
    
*   **Flexibility:** If the IOS wants to change the color or thickness, it just changes the fields in the `MapVisualOverlay` descriptor within the request, without needing to update a specific "Draw Fire Line" command on the IG side.



### 6.7 Race Condition: Stale Context

For `CMD_PICK_LOCATION`, the IG sends a `MapCommandAck` with `{"cancelled": true}` when the
tool is cancelled (ESC or IOS explicit cancellation). This is the clean path.

However, if the IOS simply stops caring about a request (e.g., the user switches views), a
`MapClickEvent`-based stale event may still arrive. The IOS resolves this via `RequestId` lookup: 

```
IOS:
    ├─► Publish MapCommandRequest (CMD_PICK_LOCATION, requestId=UUID-50)
    │
    └─► Register callback keyed on UUID-50

IG:
    └─► Show "Select Target" cursor, active requestId = UUID-50

[1 second later]

IOS:
    └─► User cancels → Unregister callback for UUID-50

[500ms later: user clicks map before IG receives any cancellation]

IG:
    └─► Publishes MapCommandAck (requestId=UUID-50, statusCode=0,
                                  dataJson={"position":{"lat":45.0,"lon":15.0}})

IOS:
    ├─► Receive MapCommandAck
    │
    ├─► Check callbacks for UUID-50 → NOT FOUND
    │
    └─► **Safely ignore** (no action taken)
```

**IG Cancellation**: The IOS can cancel a pending tool by sending a `MapCommandRequest` with the same `RequestId` and `CommandType = CMD_SET_VIEW` (or a dedicated cancel flag in `CommandArgsJson`). The IG sends `MapCommandAck(StatusCode=0, DataJson={"cancelled":true})` and resets the cursor.

**IG Timeout Logic**: After 10 seconds of no `MapCommandRequest` activity, the IG discards the active request context and emits a `MapCommandAck(StatusCode=0, DataJson={"cancelled":true, "reason":"timeout"})`.

---

## 7. Implementation Considerations

### 7.1 JSON Design Guidelines

#### When to Use JSON vs. IDL

| Data Type         | Recommendation   | Rationale                                 |
| ----------------- | ---------------- | ----------------------------------------- |
| Entity Positions  | **IDL (Binary)** | High frequency (60+ Hz), strict typing    |
| Configuration     | **JSON**         | Infrequent, complex nesting, flexibility  |
| Style Overrides   | **JSON**         | Deep nesting, project-specific extensions |
| Commands/Events   | **IDL**          | Type safety, DDS content filtering        |
| Schemas/Discovery | **JSON**         | Standard libraries, UI generation         |

#### JSON Safety Measures

**1. Schema Versioning**:
```json
{
  "$version": 1,
  "interaction": { ... }
}
```

**2. Validation Requirements**:
- IG MUST validate JSON against expected schema
- IG MUST publish `MapCommandAck` with an error `StatusCode` and `DataJson` error description if parsing fails
- IOS MUST display validation errors to user

**3. Error Handling**:
```json
{
  "errorType": "JSON_PARSE_ERROR",
  "field": "interaction.selection.color",
  "message": "Invalid hex color format",
  "receivedValue": "FFG00"
}
```

#### JSON Merge Patch Implementation (RFC 7396)

**Standard**: Use [RFC 7396](https://tools.ietf.org/html/rfc7396) for configuration merge semantics.

**Library Recommendations**:

| Language   | Library           | Notes                                               |
| ---------- | ----------------- | --------------------------------------------------- |
| C++        | `nlohmann/json`   | Built-in `merge_patch()` method                     |
| C#         | `Newtonsoft.Json` | Use `JsonMerge` with `MergeNullValueHandling.Merge` |
| Python     | `jsonmerge`       | Standard implementation                             |
| JavaScript | Native            | `Object.assign()` + null handling                   |

**Merge Semantics**:

```javascript
// Current state (IG)
{
  "view": { "cameraMode": "TACTICAL", "zoom": 1000 },
  "styles": { "iconSize": 32 }
}

// Patch received (IOS)
{
  "view": { "zoom": 2000 },
  "styles": { "iconSize": null }
}

// Result (after merge)
{
  "view": { "cameraMode": "TACTICAL", "zoom": 2000 },  // zoom updated, cameraMode preserved
  "styles": { "iconSize": 32 }  // null → reset to schema default (32)
}
```

**Implementation Requirements**:

1. **IG Side (Receiver)**:
   ```cpp
   void onConfigReceived(MapInteractionConfig msg) {
       json patch = json::parse(msg.configurationJson);
       currentConfig = currentConfig.merge_patch(patch);
       
       // Handle null values: reset to schema defaults
       applySchemaDefaults(currentConfig, patch);
       
       // Validate merged result against schema
       if (!validateAgainstSchema(currentConfig, schema)) {
           publishError("Schema validation failed");
           return;
       }
       
       // Apply to renderer
       applyConfiguration(currentConfig);
       
       // Publish feedback
       publishConfigStatus(currentConfig);
   }
   ```

2. **IOS Side (Sender)**:
   ```cpp
   void onUserChangedOpacity(double newValue) {
       json patch = {
           {"view", {{"gridOpacity", newValue}}}
       };
       
       MapInteractionConfig msg;
       msg.configurationJson = patch.dump();
       publish(msg);
       
       // Optimistic local update (confirmed by MapConfigStatus)
       localCache["view"]["gridOpacity"] = newValue;
   }
   ```

3. **Null Handling for Defaults**:
   ```cpp
   void applySchemaDefaults(json& config, const json& patch) {
       for (auto& [key, value] : patch.items()) {
           if (value.is_null()) {
               // Reset to schema default
               config[key] = schema[key]["default"];
           } else if (value.is_object()) {
               applySchemaDefaults(config[key], value);
           }
       }
   }
   ```

**Key Behaviors**:
- **Additive**: New fields added to current state
- **Overwrite**: Existing fields overwritten by patch
- **Null = Reset**: `null` values trigger reset to schema default
- **Deep Merge**: Nested objects merged recursively
- **Array Replace**: Arrays are replaced, not merged (RFC 7396 behavior)

### 7.2 Performance Optimization

#### Throttling Rules

| Event Type                | Frequency  | QoS         |
| ------------------------- | ---------- | ----------- |
| MapClickEvent             | Per-click  | Reliable    |
| DragEvent (START/END)     | Per-drag   | Reliable    |
| DragEvent (UPDATE)        | Max 10 Hz  | Best Effort |
| SelectionChanged          | Per-change | Reliable    |
| Hover Events (if enabled) | Max 5 Hz   | Best Effort |

#### Large Geometry Handling

**Problem**: Dragging 5,000-vertex polygon generates massive payloads.

**Solutions**:
1. **LOD During Drag**: Send simplified geometry (decimated to 100 points) during UPDATE
2. **Full Resolution on End**: Send complete geometry only on DRAG_END
3. **Partial Updates**: Use `isPartialUpdate` + `changedIndices` for vertex editing

**Example**:
```idl
// During vertex edit: only send changed vertex
payload.geometry.isPartialUpdate = true;
payload.geometry.changedIndices = [42];
payload.geometry.points = [<vertex 42 new position>];
```

#### DDS Content Filtering

Use `currentOwnerId` field for CPU optimization:
```sql
-- Only process requests addressed to this node
SELECT * FROM UpdateEntityDescriptorRequest 
WHERE currentOwnerId = 0 OR currentOwnerId = @MyNodeId
```

### 7.3 Robustness Patterns

#### Late Joiner Synchronization

**Problem**: New IOS connects or reconnects - how does it learn current state?

**Solution**: MapConfigStatus (Transient Local) provides complete state snapshot.

```
Configuration State Management:
  ├─► IOS → IG: MapInteractionConfig (Volatile, partial updates)
  └─► IG → IOS: MapConfigStatus (Transient Local, full state)

Scenario: IOS Reconnects
    │
New IOS Starts:
    ├─► Subscribe to MapConfigStatus (Transient Local)
    │
    ├─► Receive last MapConfigStatus (full current state) ✓
    │
    ├─► Initialize UI with current values
    │
    └─► User makes change → Send partial patch

Scenario: IG Restarts
    │
IG Restarts:
    ├─► Load factory defaults from schemas
    │
    ├─► Publish MapConfigStatus (defaults)
    │
IOS (still running):
    ├─► Receive MapConfigStatus (detects IG restarted - state is defaults)
    │
    ├─► Re-send full configuration as single patch
    │   └─► configurationJson = complete desired state
    │
    └─► IG merges, applies, publishes updated MapConfigStatus
```

**Key Points**:
- **MapConfigStatus is Transient Local**: Late joiners always get current state
- **MapInteractionConfig is Volatile**: Lightweight, supports high-frequency partial updates
- **State of Truth**: MapConfigStatus, not MapInteractionConfig
- **IOS Recovery**: On detecting IG restart, IOS can re-send complete config
- **No History Accumulation**: Don't need to replay all partial updates

#### Zombie Context Cleanup

**Problem**: IOS crashes, leaving IG in stale tool mode.

**Solution**:
1. **IG Heartbeat Monitor**: Reset `activeContextId = 0` if no MapInteractionConfig received in 10 seconds
2. **IOS Reconnect**: Publish MapInteractionConfig (snapshot) on startup
3. **User Override**: Allow manual "Reset to Default" in IG UI

#### Optimistic Locking

**Problem**: Two nodes try to update same entity simultaneously.

**Solution**: `currentVersion` field in `UpdateEntityDescriptorRequest`:

```
IOS reads entity 9001:
  currentVersion = 42

IOS sends update:
  UpdateEntityDescriptorRequest(entityId=9001, currentVersion=42, ...)

Owner validates:
  if (stored_version != 42) {
    Ack(success=false, errorMsg="Version conflict: entity modified")
  }
```


see BDC SST  Data Model Basics page for the `DescriptorOptimisticLock` descriptor implementation.

## 8. Quality Attributes

### 8.1 Reliability

**Automatic Recovery**:
- IG operates standalone without IOS (preset defaults)
- IOS reconnection triggers full state sync (snapshot)
- Transient Local topics automatically deliver last state to new subscribers

**Error Handling**:
- All `MapCommandRequest` messages have a corresponding `MapCommandAck` response (one or many)
- `MapCommandAck.StatusCode` provides machine-readable status; `DataJson` carries error details
- Validation errors prevent silent failures

### 8.2 Performance

**Targets**:
- Map interaction latency: < 50ms (click to event)
- Configuration apply latency: < 100ms
- Support 10,000 entities rendering at 30 FPS
- DDS bandwidth: < 10 MB/s for typical scenario (100 entities, 1 user)

**Optimizations**:
- Binary IDL for high-frequency data
- DDS content filtering (ownership routing)
- Throttled drag updates
- Partial geometry updates

### 8.3 Usability

**Developer Experience**:
- JSON Schema enables standard tooling
- Clear separation of concerns (Controller/View)
- Capability discovery prevents hardcoded assumptions

**Operator Experience**:
- Configurable timeouts (context menu)
- Fallback to defaults (robust UX)
- User-friendly error messages (validation)

### 8.4 Extensibility

**Plugin Architecture**:
- Project-specific plugins can add tools/cursors
- IG announces new capabilities via IGCapabilitiesAnnounce
- IOS adapts dynamically (no recompilation)

**Versioning**:
- `jsonSchemaVersion` in configuration
- Forward compatibility: unknown JSON fields ignored
- Backward compatibility: old schemas supported with warnings

---

## 9. Open Issues and Future Work

### 9.1 Resolved Design Decisions

| Decision                       | Choice                                               | Rationale                                                                                                        |
| ------------------------------ | ---------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| Entity creation authority      | IG-autonomous (`CMD_PLACE_ENTITY` / `CMD_DRAW_TACTICAL_GRAPHIC`) | IG already knows TKB descriptors; eliminates redundant IOS coupling; IOS parametrises via JSON overrides |
| Tool activation result channel | `MapCommandAck` (not generic `MapClickEvent` → IOS logic) | Unified ack/nak pattern; supports multi-step tools; typed correlations via `RequestId` |
| Click-based fallback           | Retained as opt-in (`CMD_PICK_LOCATION` + generic clicks) | Future extensibility; IOS-centric control preserved for advanced custom scenarios                         |
| Tool decoupling                | Tools use internal delegates; DDS protocol is an adapter layer | Tools reusable from local UI; independent testability                                                   |
| Overlay persistence            | Hybrid (per-instance)                                | Flexibility for local drawings + shared objects                                                                  |
| Drag commit model              | Hybrid (configurable)                                | Safety (local preview) + real-time collaboration option                                                          |
| Context menu timing            | Wait with timeout (500ms)                            | Balance customization vs. responsiveness                                                                         |
| Filtering implementation       | IG-side, IOS-defined                                 | CPU efficiency + centralized policy                                                                              |
| Multiple controllers           | Single controller                                    | Simplifies conflict resolution                                                                                   |
| Pick semantics                 | Hit stack                                            | Flexibility for disambiguation                                                                                   |
| Symbol parameters              | JSON                                                 | Accommodate project-specific styles                                                                              |

### 9.2 Known Limitations

**Coordinate Precision**:
- 64-bit float sufficient for tactical maps (cm precision)
- Sub-millimeter engineering CAD not supported

**Geometry Complexity**:
- Partial updates optimize < 10,000 vertices
- Very large terrains (100K+ vertices) require LOD strategy

**Network Latency**:
- No latency compensation for real-time collaboration
- Assumes LAN deployment (< 10ms latency)

### 9.3 Future Enhancements

**Planned**:
- Multi-user collaboration (separate design required)
- Undo/redo protocol for authoring operations
- Replay/recording of interaction sessions
- Offline mode with delta synchronization

**Under Consideration**:
- WebRTC streaming for remote access
- Progressive geometry loading (tiled maps)
- Predictive drag for high-latency networks
- AR/VR integration (coordinate frame extensions)

---

## Appendix B: QoS Recommendations

| Topic                         | Reliability | Durability      | History      | Liveliness |
| ----------------------------- | ----------- | --------------- | ------------ | ---------- |
| IGCapabilitiesAnnounce        | Reliable    | Transient Local | KeepLast(1)  | Automatic  |
| MapInteractionConfig          | Reliable    | Volatile        | KeepLast(5)  | Automatic  |
| MapConfigStatus               | Reliable    | Transient Local | KeepLast(1)  | Automatic  |
| MapCommandRequest             | Reliable    | Volatile        | KeepLast(10) | Automatic  |
| MapCommandAck                 | Reliable    | Volatile        | KeepLast(10) | Automatic  |
| MapClickEvent                 | Reliable    | Volatile        | KeepLast(5)  | Automatic  |
| DragEvent (START/END)         | Reliable    | Volatile        | KeepLast(5)  | Automatic  |
| DragEvent (UPDATE)            | Best Effort | Volatile        | KeepLast(1)  | Automatic  |
| SelectionChangedEvent         | Reliable    | Volatile        | KeepLast(5)  | Automatic  |
| ContextMenuRequest            | Reliable    | Volatile        | KeepLast(5)  | Automatic  |
| ContextMenuDefinition         | Reliable    | Volatile        | KeepLast(5)  | Automatic  |
| UpdateEntityDescriptorRequest | Reliable    | Volatile        | KeepLast(10) | Automatic  |
| UpdateEntityDescriptorAck     | Reliable    | Volatile        | KeepLast(10) | Automatic  |
| CommandStatus                 | Reliable    | Volatile        | KeepLast(10) | Automatic  |

---

## Appendix C: Glossary

**Backbone**: The SST-based entity-component system providing simulation state over DDS

**Context ID / RequestId**: 128-bit UUID linking a `MapCommandRequest` to its `MapCommandAck` responses; allows the IOS to safely correlate asynchronous creation results or discard stale events if a tool was cancelled

**Descriptor**: An entity component (SST terminology); e.g., Pose, Symbol, Geometry

**IG**: Image Generator - the rendering, interaction engine, and autonomous entity-creation agent

**IG-Autonomous Creation**: Workflow where the IG resolves TKB defaults + IOS JSON overrides, constructs a `CreateEntityRequest`, dispatches it to SimHost, and bridges the `CreateEntityAck` back to IOS as a `MapCommandAck` — without IOS knowledge of IDL descriptor structures

**IOS**: Instructor Operating Station - the high-level orchestrator; parametrises IG tool activations and consumes creation acknowledgements

**Map-Local**: Entities scoped to single IG instance (not on backbone)

**MapCommandAck**: DDS response message from IG to IOS correlating with a `MapCommandRequest` via `RequestId`; `StatusCode=0` means finished, `StatusCode=1` means intermediate (more acks to come), other values indicate errors; `DataJson` carries result payload (entity IDs, coordinates, cancellation flags)

**Preset**: Named configuration bundle (layers, tools, styles)

**Snapshot Pattern**: Sending full state instead of deltas

**SST**: Shared Simulation State - the existing ECS backbone architecture, part of BDC (Bagira Data Cloud) family

**TKB**: Technical Knowledge Base - static entity type definitions

**TKB 3-Layer Resolution (Entity Creation)**: When IG creates an entity, properties are resolved in priority order: (1) IOS JSON overrides from `MapCommandRequest.CommandArgsJson` → (2) Named preset → (3) TKB defaults for the entity type

**Tool Decoupling**: IG tools are implemented independently of the DDS wire protocol; they receive well-defined input parameters and communicate results via internal delegates; the IOS command/ack layer is a thin adapter on top

**Volatile Overlay**: Temporary graphics (e.g., fire lines, hit indicators) stored in RAM only, with optional real-time auto-timeout

**Persistence Mode**: Storage behavior - MODE_VOLATILE (RAM-only) vs. MODE_PERSISTENT (database-saved)

**DDS Dispose**: SST-compliant entity deletion mechanism - sets instance state to NOT_ALIVE_DISPOSED instead of using delete requests

**Content Filtered Topic**: DDS feature for selective subscription (e.g., viewport filtering, on-demand geometry loading)

**3-Layer Style Resolution**: Priority order for visual properties: JSON override → Preset name → TKB default

**Map Context**: Partition ID separating shared objects ("Mission_Layer") from private objects ("User_3_Private")

**Push/Pre-Fetch Pattern**: Proactive data delivery before user requests it, enabling zero-latency UX by leveraging "think time"

**Think Time**: Natural delay (500-2000ms) between user actions (e.g., select → consider → right-click), used to hide network latency

---

**Document End**


# Addendums

## Scoped Visual Overrides

We leverage standard DDS/SST capabilities \(Multi-Instance Topics) to solve the "Asymmetric View" problem—where different participants need to see different truths or highlights for the exact same entity.

### 1. The Concept

Instead of one global "Symbol Definition" for an entity, we have multiple "Visual Profiles" for that entity, each targeted at a specific group of maps.

- **Global Truth \(Backbone):** Entity 505 is a T-72, Hostile.
- **Map Group 1 \(Blue Force):** See Entity 505 as "Hostile Tank" \(Standard).
- **Map Group 2 \(Red Force):** See Entity 505 as "Friendly Tank" \(False Flag scenario).
- **Map Group 3 \(Instructor):** See Entity 505 with a "Highlight" halo \(to track it easily).

### 

### 3. Implementation Workflow

#### Step 1: Configuring the IG

When the IG starts up \(or via `MapInteractionConfig`), it is assigned a `mapGroupId`.

- **Blue IG:** `mapGroupId = 10`
- **Red IG:** `mapGroupId = 20`
- **Instructor IG:** `mapGroupId = 99`

#### Step 2: Publishing Overrides \(IOS Logic)

The IOS logic decides who needs to see the override.

- **Scenario:** We want to highlight Tank 505 only for the Instructor.
- **Action:** IOS publishes `MapEntitySymbol`:

    - `entityId`: 505
    - `mapGroupId`: 99 \(Instructor)
    - `styleParamsJson`: `{ "renderHalo": true, "haloColor": "Yellow" }`
- **Scenario:** A "False Flag" operation. Tank 505 is Red, but should look Blue to the Blue Team.
- **Action:** IOS publishes `MapEntitySymbol`:

    - `entityId`: 505
    - `mapGroupId`: 10 \(Blue Team)
    - `styleParamsJson`: `{ "forceAffiliation": "FRIENDLY" }`
    - *\(Note: Red Team IG \(Group 20) finds no descriptor, so it uses the TKB default → Renders as Red/Hostile).*

#### Step 3: IG Subscription \(Content Filtering)

To avoid processing irrelevant descriptors, the IG filter out irrelevant descriptors.

### 4. Resolution Logic \(The "Layered Cake")

The IG resolution engine adds one more layer to the stack:

| **Priority**  | **Layer**            | **Source**                                      | **Logic**                            |
| ------------- | -------------------- | ----------------------------------------------- | ------------------------------------ |
| **1 \(High)** | **Scoped Override**  | `MapEntitySymbol` \(where `mapGroupId` == MyID) | Specific tweak for *this* map group. |
| **2**         | **Global Override**  | `MapEntitySymbol` \(where `mapGroupId` == 0)    | Tweak intended for *everyone*.       |
| **3**         | **Map Policy**       | `MapInteractionConfig` \(Local)                 | "We use NATO symbols here."          |
| **4 \(Low)**  | **Simulation Truth** | `EntityMaster` + `EntityInfo`                   | "I am a T-72, Hostile."              |

### 5. Why this is Robust

1. **SST Native:** It strictly adheres to the BDC SST pattern of `EntityId` + `PartId` keying.
2. **Bandwidth Efficient:** Using CFTs means the Blue IG never receives the data meant for the Red IG.
3. **Decoupled:** The "False Flag" logic is data on the bus. If a new Map joins the "Blue Group", it immediately sees the decoy correctly without the IOS needing to re-send commands.
4. **Flexible Grouping:** `mapGroupId` can represent a specific role \("Fire Support Terminals"), a specific room \("Main Hall"), or a single node \("Commander's Screen"), depending on how you assign the IDs.

### 6. Summary Diagram

::: mermaid
graph TD
    subgraph Backbone
    D1[Descriptor: Entity 505<br/>GroupId: 10 Blue Style: 'Looks Friendly']
    D2[Descriptor: Entity 505<br/>GroupId: 99 Admin<br/>Style: 'Highlight Yellow']
    end

    subgraph IG_Blue [IG: Blue Force ID: 10]
    Filter1[DDS Filter:<br/>GroupId=10 OR 0]
    R1[Render Result:<br/>Friendly Icon]
    end
    
    subgraph IG_Red [IG: Red Force ID: 20]
    Filter2[DDS Filter:<br/>GroupId=20 OR 0]
    R2[Render Result:<br/>Hostile Icon Default]
    end
    
    subgraph IG_Admin [IG: Instructor ID: 99]
    Filter3[DDS Filter:<br/>GroupId=99 OR 0]
    R3[Render Result:<br/>Hostile Icon + Halo]
    end
    
    D1 --> Filter1
    D1 -.-> Filter2
    D1 -.-> Filter3
    
    D2 -.-> Filter1
    D2 -.-> Filter2
    D2 --> Filter3
    
    Filter1 --> R1
    Filter2 --> R2
    Filter3 --> R3
    
    linkStyle 3,4,5,6 stroke-width:1px,fill:none,stroke:lightgrey,dasharray: 5 5;
:::


## Drag and Drop explained

### **1. The Core Philosophy: "Local Prediction, Global Commit"**

Drag and drop in a distributed system is tricky because of network latency. If we waited for the backbone to update the position for every pixel the mouse moves, the drag would feel laggy (jittery).

Therefore, we use a **Local Prediction** model:

1. **Local Visual:** The IG moves the entity (or a "ghost" of it) locally on the screen instantly, following the mouse.  
2. **Backbone Commit:** The IG only writes the new position to the Backbone when the user *releases* the mouse (or throttled during drag).

### **2. Interaction Workflow**

There are two main modes of dragging, depending on who owns the entity.

#### **Scenario A: Dragging a SESSION Entity (Owned by IG)**

*Example: Moving a Ruler or a Map Annotation.*

Since the IG is the owner, it can write to the backbone as often as it likes.

1. **Mouse Down:** User clicks the entity.  
2. **Drag:** IG updates the local render instantly.  
3. **Throttled Update:** Every \~100ms (10Hz), the IG publishes the new MapVisualOverlay geometry to the Backbone.  
   * *Benefit:* Other IOS stations see the movement in near real-time.  
4. **Mouse Up (Commit):** IG publishes the final position to the Backbone.

#### **Scenario B: Dragging a SCENARIO Entity (Owned by SimHost)**

*Example: Moving a Tank or a Tactical Boundary.*

The IG cannot write directly. It must request changes. We don't want to spam the SimHost with 60 requests per second.

2. **Local Drag:**  
   * The actual entity stays put (on the backbone).  
   * The IG renders a **Ghost** (semi-transparent copy) that follows the mouse.  
3. **Mouse Up (Commit):**  
   * IG sends **one** UpdateEntityDescriptorRequest to SimHost with the final coordinates.  
   * SimHost updates the entity.  
   * IG sees the backbone update and snaps the real entity to the new location (removing the ghost).

### **3. Dragging Complex Geometry (Partial Updates)**

Dragging a single vertex of a complex polygon (e.g., a "No Fire Zone" with 50 points) should not require resending the entire shape.

We use the **isPartialUpdate** feature in the descriptor.

**Descriptor Optimization:**


    struct MapVisualOverlay {  
        // ...  
        boolean isPartialUpdate;       // TRUE  
        sequence<long> changedIndices; // [3] (Only vertex #3 moved)  
        sequence<GeoPosition> points; // [New_Pos_Of_Vertex_3]  
    };



**Workflow:**

1. User drags Vertex \#3.  
2. IG sends request to SimHost: "Update Entity 505, but ONLY Index 3 is changing to (Lat, Lon)."  
3. SimHost patches its internal model and publishes the result.

### **4. Drag & Drop "Into" Containers (Aggregation)**

*Example: Dragging a Soldier into a Truck (to load them).*

This is not just a position change; it's a hierarchy change.

1. **Drag:** User drags Soldier icon over Truck icon.  
2. **Feedback:** IG detects hover. Highlights Truck (visual cue: "Load?").  
3. **Drop:**  
   * IG detects the drop target is another entity (Truck ID: 200).  
   * IG sends UpdateEntityDescriptorRequest for the **Soldier**:  
     * Update EntityInfo → ParentId \= 200 (Truck).  
     * Update EntityState → Visibility \= Hidden (Loaded inside).

### **5. Summary Diagram**

::: mermaid

sequenceDiagram  
    autonumber  
    actor User  
    participant IG  
    participant SimHost

    Note over User, SimHost: Scenario: Dragging a Shared Tank (SimHost Owned)
    
    User->>IG: Mouse Down (on Tank 505)  
    IG->>IG: Create "Ghost" Tank  
      
    loop Dragging  
        User->>IG: Mouse Move  
        IG->>IG: Update Ghost Position (Local)  
    end  
      
    User->>IG: Mouse Up (Release)  
    IG->>SimHost: UpdateEntityDescriptorRequest<br/>(Entity 505, New Position)  
      
    SimHost->>SimHost: Validate & Update State  
    SimHost-->>IG: Publish EntityState (New Pos)  
      
    IG->>IG: Remove Ghost, Render Real Tank at New Pos

:::

### **6. Event Topics (Feedback to IOS)**

Even though IG handles the physics, the IOS logic might want to know *that* a drag happened (e.g., to log it).

**Topic:** DragEvent (Sent by IG)

* **Status:** DRAG_START, DRAG_END, DRAG_CANCEL  
* **EntityId:** 505  
* **Context:** "User dragged Tank 505 to (Lat, Lon)"

This allows the IOS to enforce business logic *after* the fact (e.g., "You moved a tank into a minefield\! Warning\!").

Here are the separate Mermaid diagrams for the two drag-and-drop scenarios.

### **Scenario A: Dragging a SESSION Entity (IG Owned)**

In this scenario, the IG owns the entity (e.g., a local ruler or annotation). It can update the backbone immediately and frequently because it is the authoritative writer.

::: mermaid

sequenceDiagram  
    autonumber  
    actor User  
    participant IG  
    participant DDS as DDS Backbone  
    participant IOS as IOS (Observer)

    Note over User, IOS: Scenario A: Dragging a SESSION Entity (IG Owned)
    
    User->>IG: Mouse Down (on Ruler 9001)  
      
    loop Dragging (High Frequency)  
        User->>IG: Mouse Move  
        IG->>IG: Update Local Render (Instant)  
          
        Note right of IG: Throttled Update (\~10Hz)  
        IG->>DDS: Publish MapVisualOverlay (New Position)  
          
        par Visibility  
            DDS->>IOS: Update (IOS sees movement)  
            DDS->>IG: Update (Loopback)  
        end  
    end  
      
    User->>IG: Mouse Up (Commit)  
    IG->>DDS: Publish MapVisualOverlay (Final Position)  
      
    IG->>IOS: DragEvent (DRAG_END, Final Pos)

:::


### **Scenario B: Dragging a SCENARIO Entity (SimHost Owned)**

In this scenario, the entity is shared (e.g., a Tank or Phase Line). The IG cannot write directly. It uses a "Ghost" for local feedback and sends a single commit request at the end to avoid flooding the SimHost.

::: mermaid

sequenceDiagram  
    autonumber  
    actor User  
    participant IG  
    participant SimHost as SimHost (Owner)  
    participant DDS as DDS Backbone

    Note over User, DDS: Scenario B: Dragging a SCENARIO Entity (SimHost Owned)
    
    User->>IG: Mouse Down (on Tank 505)  
      
    
    IG->>IG: Create Visual "Ghost" of Tank 505  
      
    loop Dragging  
        User->>IG: Mouse Move  
        IG->>IG: Update "Ghost" Position (Local Only)  
        Note right of IG: Real entity 505 stays put on map  
    end  
      
    User->>IG: Mouse Up (Commit)  
      
    IG->>SimHost: UpdateEntityDescriptorRequest<br/>(Entity 505, New Position)  
      
    SimHost->>SimHost: Validate Logic (e.g. Terrain Check)  
    SimHost->>DDS: Publish EntityState (New Position)  
      
    DDS->>IG: Update Received (Entity 505 moved)  
    IG->>IG: Remove "Ghost", Snap Real Tank to new pos
:::

## Entity Selection explained

This is the design for handling Entity Selection, adhering to the principle that the **IG owns the interaction**, while the **IOS acts as a remote controller/observer**.

### **1. Core Principles**

1. **IG is Authoritative for Interaction:** The IG calculates hit-testing (mouse vs. geometry) and maintains the ephemeral "Current Selection" set.  
2. **Bidirectional Synchronization:**  
   * **User selects on Map:** IG notifies IOS (SelectionChangedEvent).  
   * **User selects in IOS List:** IOS commands IG (CMD_SET_SELECTION).  
3. **Visual Feedback:** The IG is responsible for rendering selection indicators (halos, brackets, glows) based on its local configuration.

### **2. Interaction Logic (IG Side)**

The IG implements standard OS-style selection logic natively:

* **Click:** Clears previous, selects new target (Hit Test Top).  
* **Ctrl+Click:** Toggles target in the current set.  
* **Shift+Click:** Range selection (if applicable) or Add to set.  
* **Click Empty Space:** Deselects all.  
* **Box Select:** Adds all entities within the screen-space rectangle to the set.

### **3. DDS Topics**

#### **A. Notification (IG → IOS)**

**Topic:** SelectionChangedEvent

**Trigger:** Fires whenever the selection set changes (mouse click, box drag, etc.).

```
module Interaction {  
    struct SelectionChangedEvent {  
        // The ID of the map/IG instance (if multiple maps exist)  
        Common::EntityId mapId;   
          
        // Context: Did this happen during a specific tool workflow?  
        Common::CorrelationId contextId;
    
        // The FULL list of currently selected IDs (Snapshot)  
        // We send the full list to ensure perfect sync (stateless).  
        sequenceCommon::EntityId> selectedEntityIds;  
    };  
};
``` c++

#### **B. Remote Control (IOS → IG)**

**Topic:** MapCommandRequest

**Command:** CMD_SET_SELECTION

**Usage:** When user clicks an entity in the IOS "Order of Battle" tree view, the map should highlight it.

```
struct MapCommandRequest {  
    // ... header ...  
    CommandType type; // CMD_SET_SELECTION  
      
    // JSON Payload  
    // {   
    //   "entityIds": [505, 506],   
    //   "mode": "REPLACE" // or "ADD", "REMOVE"  
    // }  
    string commandArgsJson;  
};
``` c++

### **4. Workflows**

#### **Scenario A: User Selects on Map (Click or Box)**

1. **User Action:** User drags a selection box around a Tank Platoon.  
2. **IG Logic:**  
   * Calculates screen-space intersection.  
   * Identifies Entities [101, 102, 103, 104].  
   * Updates local rendering (draws Green Halos).  
3. **IG Publish:** Sends SelectionChangedEvent with IDs [101, 102, 103, 104].  
4. **IOS Reaction:**  
   * Receives Event.  
   * Highlights the corresponding rows in the Entity List panel.  
   * Updates the "Properties" panel to show common properties of the selection.

#### **Scenario B: User Selects in IOS (Remote Sync)**

1. **User Action:** User clicks "Tank 505" in the IOS list.  
2. **IOS Publish:** Sends MapCommandRequest(CMD_SET_SELECTION).  
   * args: { "entityIds": [505], "mode": "REPLACE" }  
3. **IG Reaction:**  
   * Updates local selection set.  
   * Renders Halo around Entity 505.  
   * (Optionally) Pans camera to bring 505 into view (if configured to do so).

### **5. Visual Styling (Configuration)**

The IOS can configure *how* the selection looks via the MapInteractionConfig topic (sent at startup or runtime).


```
{  
  "interaction": {  
    "selectionStyle": {  
      "color": "\#00FF00",      // Green  
      "method": "HALO",        // vs "BOX" or "TINT"  
      "width": 2.0,  
      "pulsate": false  
    }  
  }  
}

``` json

### **6. Edge Case: Disambiguation**

**Problem:** User clicks a pile of 5 overlapping icons.

**IG Logic:**

1. IG detects multiple hits.  
2. IG does **not** auto-select.  
3. IG opens a small, transient "Disambiguation Menu" (List of names).  
4. User clicks specific item in menu.  
5. IG processes as a standard Single Click (Selects item, sends SelectionChangedEvent).

Fragment kódu

sequenceDiagram  
    autonumber  
    actor User  
    participant IG  
    participant IOS

    Note over User, IOS: Scenario: Box Selection on Map
    
    User->>IG: Drag Selection Box  
    IG->>IG: Calculate Hits (Geom Test)  
    IG->>IG: Update Render (Show Halos)  
      
    IG->>IOS: SelectionChangedEvent<br/>(ids=[101, 102, 103])  
      
    IOS->>IOS: Update Property Panel<br/>(Show shared props for 3 items)

## Context menu handling explained

This design addresses the context menu workflow, focusing on **responsiveness** (zero latency) and **flexibility** (IOS business logic vs. IG local actions).

We use a **Proactive Push Model**: The IOS calculates and "pushes" the menu options to the IG *as soon as the user selects an entity*. By the time the user actually right-clicks (typically 500ms–2s later), the menu is already cached on the IG and opens instantly.

### **1. The Workflow Strategies**

#### **Scenario A: The "Happy Path" (Select → Think → Right-Click)**

*This covers 90% of use cases. It ensures zero network lag when opening the menu.*

1. **User Left-Clicks Entity:** IG sends SelectionChangedEvent.  
2. **IOS Reaction:**  
   * IOS receives selection.  
   * IOS logic runs: "Ah, a Tank. It has fuel. It is damaged."  
   * IOS generates a Menu Definition: ["Repair", "Refuel"].  
   * IOS **Pushes** ContextActionsUpdate to IG.  
3. **IG Cache:** IG stores this menu JSON against the Selection ID.  
4. **User Right-Clicks:**  
   * IG checks cache → **Hit\!**  
   * IG merges cached IOS items with its own local items (e.g., "Center Map").  
   * **Menu opens instantly.**

#### **Scenario B: The "Fast Path" (Right-Click on Unselected)**

*This happens if the user right-clicks an entity they haven't selected yet.*

1. **User Right-Clicks Entity:**  
   * IG checks cache → **Miss.**  
   * IG opens menu **immediately** showing only:  
     * Local Actions ("Center View", "Deselect").  
     * A "Loading..." spinner.  
   * IG sends ContextMenuRequest to IOS.  
2. **IOS Reaction:**  
   * Receives request.  
   * Calculates logic.  
   * Sends ContextActionsUpdate.  
3. **IG Update:**  
   * Receives update.  
   * Removes spinner.  
   * Inserts new items into the **already open** menu.

### **2. The Data Structure (Mixed Capabilities)**

The menu definition is a JSON array that allows mixing **IG-Native Actions** (handled locally) and **IOS-Logic Actions** (handled by message).

JSON

[  
  {  
    "label": "Movement",  
    "children": [  
      {  
        "id": 101,  
        "label": "Move Fast",   
        "icon": "arrow_fast"   
        // No "actionName" \-> This is an IOS Action  
      },  
      {  
        "label": "Lock Camera Here",  
        "actionName": "IG_Lock_Camera"   
        // Has "actionName" \-> IG executes this internally (no network traffic)  
      }  
    ]  
  },  
  { "type": "separator" },  
  {  
    "id": 102,  
    "label": "Repair",  
    "enabled": false,  
    "tooltip": "Cannot repair: Unit is under fire"  
  }  
]

### **3. Execution (What happens when clicked?)**

* **If actionName exists (IG Action):** The IG executes the logic immediately (e.g., locks camera). No message is sent to IOS.  
* **If actionName is null (IOS Action):** The IG sends a ContextActionInvoked event.

**Topic:** ContextActionInvoked

Fragment kódu

struct ContextActionInvoked {  
    Common::EntityId mapId;  
    long actionId;           // Matches the "id" in the JSON (e.g., 101)  
    Common::EntityId contextEntityId; // Which entity was the context?  
};

### **4. Sequence Diagrams**

#### **Scenario A: Proactive Push (Zero Latency)**

Fragment kódu

sequenceDiagram  
    autonumber  
    actor User  
    participant IG  
    participant IOS

    Note over User, IOS: Scenario A: Select \-> Think \-> Context Menu
    
    User->>IG: Left Click (Select Tank 505)  
    IG->>IOS: SelectionChangedEvent ([505])  
      
    Note right of IOS: IOS calculates available<br/>commands for Tank 505  
    IOS->>IG: ContextActionsUpdate<br/>(for=[505], items=["Repair", "Move"])  
      
    IG->>IG: Cache Menu Definition  
      
    Note over User: (User thinks for 1 second)  
    User->>IG: Right Click (on Tank 505)  
      
    IG->>IG: Merge (Cache \+ Defaults)  
    IG-->>User: Show Menu (Instantly)

#### **Scenario B: Immediate Right-Click (Lazy Load)**

::: mermaid

sequenceDiagram  
    autonumber  
    actor User  
    participant IG  
    participant IOS

    Note over User, IOS: Scenario B: Right Click on Unselected Object
    
    User->>IG: Right Click (on Tank 505)  
      
    IG->>IG: Cache Miss  
    IG-->>User: Open Menu (Defaults \+ Spinner)  
    IG->>IOS: ContextMenuRequest (target=505)  
      
    IOS->>IOS: Calculate Logic  
    IOS->>IG: ContextActionsUpdate (items=["Repair"])  
      
    IG-->>User: Update Open Menu<br/>(Replace Spinner with "Repair")

:::


## Chapter X: Concurrency Control (Optimistic Locking)

This chapter details the mechanism for managing concurrent edits to shared map entities. To avoid heavy centralized locking (mutexes) while preventing data overwrites (the "Last Writer Wins" problem), the system employs **Optimistic Locking** at the descriptor level.

This mechanism is **generic** and can be applied to any SST descriptor, not just map objects.


### **X.1 Core Concept**

Optimistic Locking assumes that conflicts are rare. Instead of locking an object before editing (which requires complex session management), the system checks for conflicts only at the moment of **commit**.

The "Truth" of the current version is stored in a dedicated sidecar descriptor: **`DescriptorOptimisticLock`**.

**Architecture Principles:**

1. **Granularity:** Versioning is tracked *per descriptor*, not per entity. This allows Node A to update the *Symbol* while Node B updates the *Position* without conflict.
2. **Sidecar Pattern:** The version number is stored in a separate, small descriptor. This avoids modifying the standard BDC/SST descriptors (`EntityMaster`, `MapVisualOverlay`, etc.).
3. **Check-then-Act:** Every `UpdateEntityDescriptorRequest` includes the version the requester *believes* is current. The owner validates this against the `DescriptorOptimisticLock`.



### **X.2 Data Model**

**Topic:** `DescriptorOptimisticLock`
**QoS:** Reliable, Transient Local, History(1)
**Ownership:** Owned by the same participant that owns the corresponding data descriptor.

``` c++
module DataBackbone {
    import Common;

    // A generic multi-part (dual key) sidecar descriptor to enforce optimistic concurrency control
    // on a per-descriptor basis.
    struct DescriptorOptimisticLock {
        
        // Key 1: The Entity this version belongs to
        @key long entityId;

        // Key 2: The specific Descriptor/Component type being protected
        // (e.g., 0=Master, 1=Symbol, 2=Overlay, 10=Position)
        @key long DescriptorType; 

        // The Version/Sequence Number
        // Checked against 'UpdateEntityDescriptorRequest.currentVersion'
        // Incremented strictly monotonically on every successful write.
        long long currentVersion;
        
        // (Optional) Diagnostic info: Who made the last change?
        string<32> lastWriterSessionId;
    };
};

```

**Integration with Update Request:**

The existing request structure already supports this pattern via the `currentVersion` field.

``` c++
struct UpdateEntityDescriptorRequest {
    // ... existing header fields ...
    
    // The Descriptor we want to update
    Common::EntityId entityId;
    long descriptorType;
    
    // The Optimistic Lock Expectation
    // "I am basing this update on Version X. If the real version is > X, reject me."
    long long currentVersion; 
    
    // The Data
    DescriptorPayload payload;
};

```


### **X.3 Interaction Workflow**

The workflow consists of three phases: **Read**, **Edit**, and **Commit**.

**Phase 1: Read (Implicit)**
The IG is always subscribed to `DescriptorOptimisticLock`. When it renders Entity 505, it knows that the Geometry (Type 2) is at Version `42`.

**Phase 2: Edit (Local)**
The user drags the entity. This happens locally on the IG. The IG remembers: *"I started dragging when Geometry was Version 42."*

**Phase 3: Commit (Request)**
The IG sends the update request, explicitly stating `currentVersion = 42`.

**Phase 4: Validation (Owner Side)**
The Owner (e.g., SimHost) receives the request:

1. Look up `DescriptorOptimisticLock` for `{Entity: 505, Type: 2}`.
2. Compare stored version vs. request version.
3. **Match:** Apply update, increment stored version to `43`, publish both.
4. **Mismatch:** Reject request, send `CommandStatus(FAILURE, "Concurrency Conflict")`.


### **X.4 Use Case: Successful Update (No Conflict)**

**Scenario:** User A moves a "Fire Support Line" (Entity 505). No one else is editing it.

::: mermaid
sequenceDiagram
autonumber
actor User
participant IG_A as IG (User A)
participant Backbone as Data Backbone
participant Owner as SimHost (Owner)


Note over IG_A, Owner: Precondition: Entity 505, Overlay(Type 2) is Version 10

Backbone->>IG_A: DescriptorOptimisticLock<br/>(id=505, type=2, version=10)

User->>IG_A: Drag Entity 505 to new pos

IG_A->>Owner: UpdateEntityDescriptorRequest<br/>(id=505, type=2, currentVersion=10)

Note right of Owner: Validation: Stored(10) == Request(10) -> OK

Owner->>Backbone: Publish MapVisualOverlay (New Data)
Owner->>Backbone: Publish DescriptorOptimisticLock (Version=11)

Owner->>IG_A: UpdateEntityDescriptorAck (Success)

par Update
    Backbone->>IG_A: MapVisualOverlay (New Pos)
    Backbone->>IG_A: DescriptorOptimisticLock (Version 11)
end

:::

---

#### **X.5 Use Case: Conflict Resolution (The Race Condition)**

**Scenario:** Users A and B try to move the same "Fire Support Line" at the exact same time.

1. **Start:** Both IGs see Version `10`.
2. **Action:** Both users drag and release.
3. **Race:** IG A's request arrives at the Owner 5ms before IG B's request.

::: mermaid
sequenceDiagram
autonumber
participant IG_A
participant IG_B
participant Owner as SimHost (Owner)

```
Note over IG_A, Owner: Both start with Version 10

par Race Condition
    IG_A->>Owner: Request(id=505, ver=10)
    IG_B->>Owner: Request(id=505, ver=10)
end

Note right of Owner: Process A: 10 == 10 (OK)<br/>Update Version to 11

Owner->>IG_A: Ack(Success)

Note right of Owner: Process B: 11 != 10 (FAIL)<br/>Reject Update

Owner->>IG_B: Ack(Failure, "Optimistic Lock Failed")

Owner->>IG_B: Publish New Data (Result of A)

Note left of IG_B: IG B receives new position from A.<br/>Snaps entity to A's position.

```

:::

---

#### **X.6 Implementation Notes**

**1. Handling "Force" Updates:**
Sometimes an automated process (or an Admin/Instructor) needs to overwrite data regardless of version.

* **Convention:** If `UpdateEntityDescriptorRequest.currentVersion == 0` (or `-1`, depending on implementation preference), the Owner **skips** the check and forces the update.

**2. Handling Creation (Version 0):**
When creating a new descriptor that doesn't exist yet:

* The request should send `currentVersion = 0`.
* The Owner validates that the descriptor *does not exist* (or version is 0).
* On success, it creates the descriptor and publishes `DescriptorOptimisticLock(version=1)`.

**3. Visual Feedback for Lock Failure:**
When an IG receives a `Failure` Ack due to optimistic locking:

1. **Do NOT rollback manually.** Wait for the DDS update.
2. The Owner (who processed the winner's request) will publish the *new* valid state (Version 11).
3. The IG will receive this update naturally via DDS.
4. The IG simply renders the new state. To the user (Loser B), it looks like their drag was "snapped back" or "teleported" to User A's location.

**4. Generic Applicability:**
This `DescriptorOptimisticLock` should be defined in the `Common` or `Backbone` module, not `Map`. It can be used for:

* **Radio:** Two users tuning the same radio frequency.
* **Logistics:** Two users trying to consume the same ammo pallet.
* **Weather:** Two instructors changing the cloud ceiling.

---

### **X.7 Summary of Benefits**

| Feature | Benefit |
| --- | --- |
| **No "Hard" Locks** | No need for `Acquire/Release` messages. No "zombie locks" if an IOS crashes. |
| **Granular** | Updating the *Symbol* (Type 1) never blocks updating the *Geometry* (Type 2). |
| **Stateless Client** | The IG doesn't need to maintain complex lock states; it just remembers "I saw Version X". |
| **SST Compliant** | Uses standard DDS topics and data-centric patterns without breaking existing schemas. |




## Layers


### 1. The Data Contract (JSON)

In the `MapInteractionConfig` topic, the IOS sends a dictionary of layer states.

``` json
    {
      "view": {
        "layers": {
          "map_background": true,
          "units_ground": true,
          "units_air": false,
          "tactical_graphics": true,
          "measurements": false,
          "weather_clouds": true
        }
      }
    }
```

**Behavior:**
*   **Explicit Control:** IOS explicitly sets visibility for known keys.
    
*   **Missing Keys:** If a key is missing from the JSON update (e.g., in a partial Merge Patch), its state remains unchanged (or defaults to `true` on startup).
    
*   **Unknown Keys:** If IOS sends `"future_layer": true` but the IG is old, the IG simply ignores it (Forward Compatibility).
    

### 2. The Internal Logic (The "Bridge")

Since the Entity Type is hardcoded/known, the IG needs a mapping system to translate **"T-72 Tank"** (Type ID) into **"units_ground"** (Layer Name).

**The Convention (Shared Header/Doc):**

IOS and IG teams agree on a static mapping of Strings to logical categories.
| **JSON Key Name** | **Meaning** | **Affected Entity Types (Examples)** |
| --- | --- | --- |
| `"units_ground"` | Land Platforms | Tanks, Trucks, Infantry, APCs |
| `"units_air"` | Air Platforms | Fixed Wing, Rotary Wing, UAVs |
| `"tactical_graphics"` | Control Measures | Phase Lines, FLOT, Areas, Minefields |
| `"measurements"` | Tools | Ruler lines, LOS fans (Map-local objects) |
| `"sat_imagery"` | Background | Raster map tiles (WMS) |

### 3. Handling "One Entity, Many Layers"

An entity can belong to **multiple layers**.
*   _Example:_ A "Fire Support Line" might belong to both `"tactical_graphics"` AND `"fire_support"`.
    
*   _Example:_ An "Amphibious Tank" might technically belong to `"units_ground"` AND `"units_naval"` (depending on doctrine).
    
If an entity maps to multiple layer names, it should be visible if **at least one** of those layers is set to `true` in the config.


**Render Logic (Union/OR):**
Standard map behavior is usually **Additive (OR)**. If an entity belongs to _any_ currently visible layer, it is shown.

**Implementation (Optimization):**
To keep rendering fast (avoiding string lookups 60 times a second per entity), the IG should map these strings to an internal bitmask on config receipt.

**Sequence:**
1.  **Receive Config:** IG receives JSON `{"units_ground": true, "tactical_graphics": false}`.
    
2.  **Update Mask:** IG updates its internal `activeViewMask`.
    *   `units_ground` $\rightarrow$ Bit 1 (Set to 1)
        
    *   `tactical_graphics` $\rightarrow$ Bit 2 (Set to 0)
        
3.  **Render Loop:**
    *   Entity `T72` has hardcoded mask `0b0010` (Bit 1).
        
    *   Check: `(0b0010 & activeViewMask) != 0` $\rightarrow$ **Render**.
        
    *   Entity `PhaseLine` has hardcoded mask `0b0100` (Bit 2).
        
    *   Check: `(0b0100 & activeViewMask) == 0` $\rightarrow$ **Hide**.
        

### 4. Summary for the Design Doc

2.  **Convention:** Define the list of "Standard Layer Names" in the project documentation (IG sends full ist as part of of the "current settings" of the map).
    
3.  **Mapping:** State that the IG is responsible for mapping internal `tkbTypeId`s (or DIS Type Ids) to these Layer Names based on the agreed convention.
    





## MapId vs MapGroupId

We need to strictly separate **Logical Roles** (`MapGroupId`) from **Concrete Instances** (`MapId`).
*   **`MapGroupId` (Role/View):** Used for configuration, styling, and business logic rules (e.g., "Blue Force" view, "Instructor" view). Multiple physical IGs can share one Group ID to stay synchronized visually.
    
*   **`MapId` (Instance/Hardware):** Used for input events, camera control, and status reporting from a specific window or screen.
    
Here is the correct assignment for each struct.

### 1. Configuration & Styling (Shared Visuals -> `MapGroupId`)

These structs define "how the map looks" or "what the user can do" based on their role. They target the Group so that all displays belonging to that role (e.g., a video wall of 3 monitors showing "Blue Force") update together.
| **Struct** | **ID Used** | **Rationale** |
| --- | --- | --- |
| **`MapEntitySymbol`** | `MapGroupId` | Defines visual overrides (e.g., "Show as Hostile") for a specific team/role. |
| **`MapInteractionConfig`** | `MapGroupId` | Sets layers, visibility, and active tools. As per your definition, entities sharing a Group ID share these settings. |
| **`ContextActionsUpdate`** | `MapGroupId` | The _logic_ for what a user can do (e.g., "Can I delete this tank?") depends on their Role/Group, not which monitor they are using. |

### 2. Input & Status (Concrete Instance -> `MapId`)

These structs relate to a physical interaction or a specific application instance.
| **Struct** | **ID Used** | **Rationale** |
| --- | --- | --- |
| **`IGCapabilitiesAnnounce`** | `MapId` | A specific executable instance announcing _its_ capabilities and version. |
| **`MapConfigStatus`** | `MapId` | The feedback from a concrete IG instance reporting its current state (truth). |
| **`MapClickEvent`** | `MapId` | A physical mouse click happens on a specific window. |
| **`DragEvent`** | `MapId` | Dragging is a screen-space operation on a specific window. |
| **`SelectionChangedEvent`** | `MapId` | Selection happens on a specific client. (Even if synced later, the event originates from one ID). |
| **`MapCommandRequest`** | `MapId` | Imperative commands like `CMD_SET_VIEW` (Camera) or `CMD_PICK_LOCATION` target a specific window/screen. |
| **`ContextMenuRequest`** | `MapId` | The fallback request ("I right-clicked") comes from a specific user interaction. |
| **`ContextActionInvoked`** | `MapId` | The user clicked a menu item on a specific screen. |



# Presentation
![image.png](/.attachments/image-c4dce43f-c54a-4e93-88a8-054678e4155a.png)


![image.png](/.attachments/image-3520a233-9901-4491-9270-3f98892593a3.png)

![image.png](/.attachments/image-04df523d-552a-47ac-b4aa-209da66cbaf2.png)
    
![image.png](/.attachments/image-4fd72986-be14-4cf9-9eef-8fa40836b875.png)

![image.png](/.attachments/image-4b6e2944-539a-4673-8cd4-30e1e256265c.png)


![image.png](/.attachments/image-b5206598-c454-42d4-b60a-50ebcd6f2bce.png)

![image.png](/.attachments/image-f64bd7c1-4e61-4f3b-be61-dded9a37de99.png)

![image.png](/.attachments/image-c8848b49-79d5-462a-a049-ea2d4d46223c.png)

![image.png](/.attachments/image-85dc716b-b687-446f-bfaf-5ef8d8810bf3.png)


![image.png](/.attachments/image-ade40e66-622a-4190-900c-2843aa9a53de.png)

![image.png](/.attachments/image-005005d1-ef73-4b4e-a916-af70d62af625.png)


![image.png](/.attachments/image-ada4a06b-402e-4249-b44a-63adb639fa97.png)

![image.png](/.attachments/image-9ea6cacb-836c-4120-aacf-87279273849d.png)

![image.png](/.attachments/image-a89b31eb-2b17-4173-9bcb-29e72a976f94.png)



![image.png](/.attachments/image-ab73a405-a54c-471a-a789-765539c7e145.png)

![image.png](/.attachments/image-9d3c7b11-dffd-4ae9-b11e-11b5f5dcfaf6.png)

![image.png](/.attachments/image-2d5cd99d-bece-46be-ac31-fb911faea12d.png)

![image.png](/.attachments/image-3ce10269-36d3-4b68-8928-fba277438947.png)

![image.png](/.attachments/image-4342676b-8996-4d3f-b944-1935a3345d82.png)


![image.png](/.attachments/image-2d2bff3f-6189-4fa5-835f-9bf0cc7d7198.png)

![image.png](/.attachments/image-00efafec-55d8-4ada-87f8-f77a217b05e6.png)
