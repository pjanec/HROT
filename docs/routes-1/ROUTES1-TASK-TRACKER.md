# Routes-1 Task Tracker

**Workstream:** ROUTES1  
**Reference:** See [ROUTES1-TASK-DETAIL.md](./ROUTES1-TASK-DETAIL.md) for detailed task descriptions  
**Design:** See [ROUTES1-DESIGN.md](./ROUTES1-DESIGN.md) for architecture and phase descriptions

---

## Phase 1 — Core ECS Data Layer

**Goal:** Introduce the new ECS components and events so all downstream phases have a stable foundation.

- [x] **ROUTES1-T001** RoutePlan Managed Component [details](./ROUTES1-TASK-DETAIL.md#routes1-t001--routeplan-managed-component)
- [x] **ROUTES1-T002** Supporting Components and Events [details](./ROUTES1-TASK-DETAIL.md#routes1-t002--supporting-components-and-events)
- [x] **ROUTES1-T003** TKB Blueprint for TacGraphic_Route [details](./ROUTES1-TASK-DETAIL.md#routes1-t003--tkb-blueprint-for-tacgraphic_route)

---

## Phase 2 — DDS Replication

**Goal:** Route entities replicate seamlessly over DDS using the pre-existing `MapRoute` descriptor.

- [x] **ROUTES1-T004** MapRouteEgressTranslator [details](./ROUTES1-TASK-DETAIL.md#routes1-t004--maprouteegresstranslator)
- [x] **ROUTES1-T005** MapRouteIngressTranslator [details](./ROUTES1-TASK-DETAIL.md#routes1-t005--maprouteingresstranslator)

---

## Phase 3 — Trajectory Pool Integration

**Goal:** Route entity mutations automatically propagate to the `TrajectoryPoolManager`.

- [x] **ROUTES1-T006** RouteTrajectorySyncSystem [details](./ROUTES1-TASK-DETAIL.md#routes1-t006--routetrajectorysyncystem)

---

## Phase 4 — Shared Route Authoring

**Goal:** Operators can author new shared routes from the IOS/IG map canvas.

- [x] **ROUTES1-T007** Shared Route Authoring via CMD_START_AUTHORING [details](./ROUTES1-TASK-DETAIL.md#routes1-t007--shared-route-authoring-via-cmd_start_authoring)

---

## Phase 5 — Personal Route Authoring

**Goal:** Shift+Right-Click authors a vehicle-specific child route entity.

- [x] **ROUTES1-T008** PersonalRouteAuthoringSystem [details](./ROUTES1-TASK-DETAIL.md#routes1-t008--personalrouteauthoringsystem)
- [x] **ROUTES1-T009** IG Input Wiring for Shift+Right-Click [details](./ROUTES1-TASK-DETAIL.md#routes1-t009--ig-input-wiring-for-shiftright-click)

---

## Phase 6 — Rendering

**Goal:** Routes are visible on the IG 2D map.

- [x] **ROUTES1-T010** RouteRenderLayer [details](./ROUTES1-TASK-DETAIL.md#routes1-t010--routerenderlayer)
- [x] **ROUTES1-T011** SimHostTrajectoryLayer Extension [details](./ROUTES1-TASK-DETAIL.md#routes1-t011--simhosttrajectorylay-extension)

---

## Phase 7 — Editing

**Goal:** Operators can modify routes — move, insert, delete waypoints and set per-waypoint metadata.

- [x] **ROUTES1-T012** RouteEditTool [details](./ROUTES1-TASK-DETAIL.md#routes1-t012--routeediittool)
- [x] **ROUTES1-T013** WaypointEditorPanel [details](./ROUTES1-TASK-DETAIL.md#routes1-t013--waypointeditorpanel)

---

## Phase 8 — AI Soft Advice

**Goal:** Per-waypoint `ExtensionJson` influences vehicle behavior trees via `BrainBlackboard`.

- [x] **ROUTES1-T014** RouteContextSystem [details](./ROUTES1-TASK-DETAIL.md#routes1-t014--routecontextsystem)

---

## Phase 9 — Legacy Deprecation

**Goal:** Remove the legacy `_waypointQueues` waypoint queue mechanism from `ScenarioManager`.

- [x] **ROUTES1-T015** Remove Legacy Waypoint Queue [details](./ROUTES1-TASK-DETAIL.md#routes1-t015--remove-legacy-waypoint-queue)
