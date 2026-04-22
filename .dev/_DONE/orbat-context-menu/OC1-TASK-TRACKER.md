# Task Tracker — ORBAT Context Menu

**Reference:** See [OC1-TASK-DETAIL.md](./OC1-TASK-DETAIL.md) for detailed task descriptions.  
**Design:** See [OC1-DESIGN.md](./OC1-DESIGN.md) for architecture and rationale.

---

## Phase 0: Bug Fixes (Priority)

**Goal:** Fix broken authoring and deletion lifecycle flows that block verification of the new features.

- [x] **OC1-B001** IOS Draw Route — no entity created  [details](./OC1-TASK-DETAIL.md#oc1-b001--ios-draw-route-no-entity-created)
- [x] **OC1-B002** No map layer for routes in IOS layer panel  [details](./OC1-TASK-DETAIL.md#oc1-b002--no-map-layer-for-routes-in-ios)
- [x] **OC1-B003** Tactical shape authoring — shape position wrong  [details](./OC1-TASK-DETAIL.md#oc1-b003--tactical-shape-authoring-shape-position-wrong)
- [x] **OC1-B004** Entity deletion not reflected in IOS inspector  [details](./OC1-TASK-DETAIL.md#oc1-b004--entity-deletion-not-reflected-in-ios-inspector)
- [x] **OC1-CORRECTIVE-01** Fix BATCH-01 edge cases (IOS inspector selection, Route zero-points, Edit shape drift)
- [ ] **OC1-CORRECTIVE-02** Fix BATCH-02 unresolved regressions (IG Delete Entity Request, PickEntity for Routes, Canvas Y-to-Z math)
---

## Phase 1: Shared Contracts

**Goal:** Introduce the new DDS command type so all projects can reference it.

- [x] **OC1-C001** Add `CMD_DRAW_PERSONAL_ROUTE` to `CommandType`  [details](./OC1-TASK-DETAIL.md#oc1-c001--add-cmd_draw_personal_route-to-commandtype)

---

## Phase 2: SimHost Route-Assignment Fix

**Goal:** Allow the network layer to assign routes by entity ID instead of an internal ephemeral index.

- [x] **OC1-S001** SimHost FollowRoute: translate network ID at ingress boundary  [details](./OC1-TASK-DETAIL.md#oc1-s001--simhost-followroute-translate-network-id-at-ingress-boundary)

---

## Phase 3: IG Command Handling Extensions

**Goal:** Teach the IG to respond to selection, view, and personal-route commands issued over the network.

- [x] **OC1-G001** IG handles `CMD_SET_SELECTION`  [details](./OC1-TASK-DETAIL.md#oc1-g001--ig-handles-cmd_set_selection)
- [x] **OC1-G002** IG handles `CMD_SET_VIEW` (entity-centric)  [details](./OC1-TASK-DETAIL.md#oc1-g002--ig-handles-cmd_set_view-entity-centric)
- [x] **OC1-G003** IG orchestrates `CMD_DRAW_PERSONAL_ROUTE`  [details](./OC1-TASK-DETAIL.md#oc1-g003--ig-orchestrates-cmd_draw_personal_route)

---

## Phase 4: IOS — ORBAT Context Menu

**Goal:** Surface all five context menu actions in the IOS ORBAT panel, gated correctly by entity type.

- [ ] **OC1-I001** OrbatPanel context menu infrastructure + `IsSimulatedEntity` helper  [details](./OC1-TASK-DETAIL.md#oc1-i001--orbatpanel-context-menu-infrastructure)
- [ ] **OC1-I002** Select Entity action  [details](./OC1-TASK-DETAIL.md#oc1-i002--select-entity-action)
- [ ] **OC1-I003** Center on Entity action  [details](./OC1-TASK-DETAIL.md#oc1-i003--center-on-entity-action)
- [ ] **OC1-I004** Delete action  [details](./OC1-TASK-DETAIL.md#oc1-i004--delete-action)
- [ ] **OC1-I005** Edit Route action (physical entities only)  [details](./OC1-TASK-DETAIL.md#oc1-i005--edit-route-action-physical-entities-only)
- [ ] **OC1-I006** Abort Mission action (physical entities only)  [details](./OC1-TASK-DETAIL.md#oc1-i006--abort-mission-action-physical-entities-only)

---

## Next Batch Selection (OC1-BATCH-03)
Scheduled for OC1-BATCH-03:
- **OC1-CORRECTIVE-02** (Fixes)
- **OC1-I001** (Phase 4 Setup)
- **OC1-I002**, **OC1-I003**, **OC1-I004**, **OC1-I005**, **OC1-I006** (Phase 4 Functionality)
