<!--STATUS
state: LIVE
updated: 2026-10-13 (§P — the params-finish state, written after a compaction)
doc-type: RESUME — the orders / params DTOs / entity classes programme (any lane picking up a task). ⚠ A STATE doc: verify every "built" / "dispatched" line against git before acting.
current-answer: ⭐⭐ §P below (params finish — what is in flight, what is next, by lane) · ⭐ design §13 (2026-10-13, finishing the params story: decisions, work P1–P8 by lane, backend handoff HANDOFF_Params_Finish_Backend.md) — then §0 the overnight handoff · then §2 how to pick a task · §3 the lanes
stale-below: nothing
known-rot: §1's table predates the overnight run — its "DangerCrossing (next)", "B4 may run" and CE-2131 🟡 are DONE (see §0); §0 wins where they differ
known-conflict: none
related-designs:
  - docs/DESIGN_Orders_Params_Entity_Classes.md — THE design and its task list (§6); start there
  - docs/blueprints/Architect_Question_90_Unit_Orders_Carry_Capacity.md — the rulings R-273..R-281 (behaviors) and how they were reached
  - docs/blueprints/HANDOFF_Orders_Track_B_Params_Internals.md — Track B dispatch
  - docs/blueprints/HANDOFF_Orders_Track_C_Classes_And_Dis.md — Track C dispatch
  - docs/blueprints/HANDOFF_Params_Finish_Backend.md — 2026-10-13: finishing the params story, backend P1/P2/P3/P4/P8 (design §13)
  - docs/blueprints/HANDOFF_Embark_I2d_And_Unit_Orders.md — embarkation I-2d; NOT deferred (R-282), runs in parallel — see §1a
-->

# Resume — orders, params DTOs and entity classes

RELEARN

## P. Params finish — live state *(behaviors lane, 2026-10-13, day)*

**Design:** [`DESIGN_Orders_Params_Entity_Classes.md`](../DESIGN_Orders_Params_Entity_Classes.md) §13 (decisions F1–F12, work P1–P9 by lane, §13.3 designations, §13.4 docs list, §13.5 the P9 retirement + node-coverage plan). **Rulings:** `R-2000`..`R-2007` (`RULINGS.md`). **Next free ids:** `CE-2149`, `R-2008`.

| item | lane | state |
|---|---|---|
| P8 fields-only DTOs → P1 one `[Dto]` registry → P2 | backend session A | dispatched by [`HANDOFF_Params_Finish_Backend.md`](HANDOFF_Params_Finish_Backend.md) (at `75ba5b75b`) + [`STEER_Params_Finish_Backend_1.md`](STEER_Params_Finish_Backend_1.md) (R-2004/R-2005) + [`STEER_Params_Finish_Backend_2.md`](STEER_Params_Finish_Backend_2.md) (R-2006/R-2007). ⚠ the steers reach backend only when the USER relays them |
| P3 full-rebuild caches → P4 blueprints name a DTO, Get Parameter retires | backend session B | same handoff |
| P9 retire `PlatoonHillAttackBp`, keep the node checks | behaviors | ✅ done `79adf99f3` + `5f544644f` (replacement `SubordinateForEachDemo`) |
| P5 `[Required]`, RTS fast path, `GET /orders`, the `OrderDraft` / `IOrderForm.Open` seam | behaviors | ✅ built `CE-2148` (design §13.9) |
| P5b the modeless order form, entity source for `PickerWindow`, 3D map picking | ui | 📤 handoff written: [`HANDOFF_Order_Form_Ui.md`](HANDOFF_Order_Form_Ui.md) — ⚠ the USER passes it to the ui lane |
| P6 designations stage 1 (§13.3) · P7 the docs of §13.4 | behaviors | after P5 |
| G3 (designations stage 2) | user | architect question offered, unanswered |
| later | behaviors | I-2d + convoy ambush ([`HANDOFF_Embark_I2d_And_Unit_Orders.md`](HANDOFF_Embark_I2d_And_Unit_Orders.md)) — after the params story (user: *"finish this parameter story before continuing with embark"*) |

**How to resume:** `git fetch origin behaviors backend ui` → merge whatever backend pushed (P1 + P8 merged `80ffb3ad9`; P2 / P3 / P4 next — review the diff against the handoff, do not re-run their gates, rule 8) → P6 (designations, §13.3) → P7 (docs, §13.4).

## 0. Overnight handoff *(behaviors lane, 2026-10-12 → 13, solo; backend + ui queues taken over)*

**State:** `behaviors` @ `475509f35`, clean, pushed. `backend` (`a5a917d5b`, CE-3182) and `ui` (`f0a08b621`, CE-1023) merged; neither pushed since. ⚠ Verify against git before acting (`git fetch origin backend ui` → merge what moved, name it in the started note).

**Start here:** read `RULINGS.md` → this §0 → `DESIGN_Orders_Params_Entity_Classes.md` §11 / §12 for the decisions below.

### 0.1 Done overnight (all red-proved; designs + tracker updated)

| id | what | where |
|---|---|---|
| CE-2131 · CE-2145 · CE-2132 | NoParams for take-nothing behaviours · FollowRoute resolved on the receiver · DangerCrossing on the params model | design §5c/§5d; tracker |
| CE-2136 (B4a) · CE-2135 (B3b prep) | `*.params.json` → `[ParamsDto]` struct at build · 15 store-nothing assets → `NoParams`, allow-list 33 → 18 | design §12.1 / §5c |
| CE-1019 | the SOP row offers only SOPs; task pickers leave them out | Sensors & Doctrine §7.6 |
| CE-2126 | UrbanCombatNew green: the fixture's AimAndFire never stamped `BehaviorInstanceId` (arbitration cleared it every tick; the CE-3136 aim clock never finished — bisect: `d271ec488`), eject applier in the wrong stage, a trivially-true halt latch; BallisticsAndHit stale assertion | `DESIGN_Entity_Interactions.md` §7.1b |
| CE-2107 | squad pool folds a heard contact into another member's sighting (no double-counted enemy) | `DESIGN_Thermal_And_Acoustic_Sensing.md` §8 |
| CE-1055 | EQS crash: the map's sensor gizmo rebuilt the generator's broadphase under the solver ⇒ `BroadphaseCache`, one per thread | Sensors & Doctrine §5.5 · Peek & Fire §9.8 |
| CE-1047 (prereq) | attribute ack receipt 512 bits (ids ≥ 256 threw) | tracker |
| CE-1036 · CE-1037 · CE-2147 · CE-2063 | stale fixtures: cross-host danger test loads roads · editor harness passes its creation pack + starter obstacles + bake system (11/11) · FindThrowingPosition golden · 5 blueprint `$meta` envelopes + v2 expectation | tracker |

### 0.2 Waiting on the USER (each has a lean)

| # | decision | lean |
|---|---|---|
| 1 | **CE-2144 E1** — where `HealthApplicationSystem` runs (it sits before the cognitive runtime, `CgfLogicPack.cs:231`) | **A**: Simulation tail + dispatchers also gate on Health — design §11.1 |
| 2 | **B4b** — the editor for params assets (a new `AssetKind`, the first non-graph document) | design §12 |
| 3 | **B3b proper** — delete `Role`; blueprint Parameters → a named DTO; empties the last 18 allow-list entries | design §5c |
| 4 | **UrbanCombat cluster run** (`UrbanCombatFileLifecycleTests`) red BY DESIGN: Peek & Fire D4 needs the target seen; the demo's hand-written trees configure no sensor | add a vision-sensor step to the demo's Ambush + InfantryCombat trees (CE-2126 row) |
| 5 | **CE-1047 ①/②** — `EntityAppearance` descriptor arm + `"Color"` attribute | build in an ATTENDED session: a new DDS type that becomes mandatory for ghost promotion on every node |

### 0.3 Open, no decision needed (next candidates)

- **CE-3180** — fixture component ids vs production ids: census recorded per assembly (7 assemblies, ~190 fixtures); next = one reflection rail per test assembly, fixed assembly by assembly (cross-lane, daylight).
- **CE-2049** — `Fdp.Presentation.Tests` 8 red, causes recorded; each is a ui UX call (singleton row count, id-search semantics, perspective names).
- **CE-2090 / CE-2091** — blueprint EQS round-out pins; unscheduled, no demand.

### 0.4 Traps measured overnight

- ⛔ Killing `dotnet test` (its `vstest.console`) leaves the **`testhost` orphaned** — it held memory for an hour and made `Hrot.Editor.Tests` hang / go red once. `pgrep -fa testhost` and kill by PID.
- ⛔ `pkill -f <pattern>` that matches your own command line kills your shell (exit 144) — kill by PID.
- ⛔ Two builds of overlapping projects at once ⇒ spurious reds; serialise builds that share projects.
- ⭐ Build the TEST project before `--no-build` (CLAUDE.md "second stale-binary trap") — and check a red-proof's mutated build actually succeeded.

## 1. Where it stands *(2026-10-12)*

| | |
|---|---|
| design | ✅ [DESIGN_Orders_Params_Entity_Classes.md](../DESIGN_Orders_Params_Entity_Classes.md), READY-TO-BUILD; rulings R-273..R-281 (behaviors) |
| built | ✅ CE-2130 (rules rail, 48 allow-listed) · ✅ CE-2140 (order DTOs) · 🟡 CE-2131 (FollowRoute contract; the NoParams switch is unblocked by CE-2134) · ✅ **CE-2141 D2** (InfantrySquadMove/Hold/Attack; a commander's run releases its members' orders — design §7) · ✅ **CE-2142 D3** (the receiver resolves `Order.<K>` by its class — design §8) · ✅ **CE-2143 D4** (menu / RTS / HTTP send orders; the menu lists what the unit's class fulfils — design §9) — behaviors lane · ✅ **CE-2133 B1** + ✅ **CE-2134 B2** (backend lane): a BTree/HSM with ONE struct Input parses flat; any tier may name its params DTO (`ParamsDtoTypeId`); the contract is declared on the behaviour — design §5.1, §5b ⇒ **A2 (CE-2132) is unblocked**, **D4 (CE-2143) is unblocked** · ✅ **Track C** (ui lane, CE-2137/2138/2139) |
| embarkation (CE-3110 I-2d, the S3 dismount) and the other behaviour work planned before | ▶ **NOT deferred any more** — 🔒 user, 2026-10-12: *"if the embarkation and other behavior stuff … can continue in some session, it does not need to stay deferred anymore"* (R-282 (behaviors), superseding R-277's postponement). Hand a session [HANDOFF_Embark_I2d_And_Unit_Orders.md](HANDOFF_Embark_I2d_And_Unit_Orders.md) §2 items 1–2 (its item 0 / AQ90 and item 3 are done or absorbed here) plus the note below |
| Track B / Track C | assigned 2026-10-12 by the user: Track B → the `backend` lane, Track C → the `ui` lane. Track B: B1 (CE-2133), B2 (CE-2134) and B3a (CE-2135, R-3000) built by the backend lane; A2 (CE-2132, behaviors): four of its assets go to `NoParams` (no JSON caller, measured by behaviors); DangerCrossing waits for CE-3182. CE-2129 B5 ✅ built (backend). ⇒ **order, user 2026-10-12:** B5 ✅ → CE-3182 field binding ✅ built (backend; a binding targets `Variable.Field`, e.g. `Params.walk`; one target resolver; editor picker / rename — design §5d) → **behaviors converts DangerCrossing (next)** (a resolver filling a config struct in `Params`, nodes bound to `Params.<field>`) → B3b (backend; it can only empty the CE-2130 allow-list after DangerCrossing). B4 (CE-2136) may run in parallel. ⭐ **Track C ✅ built 2026-10-12 by the ui lane** — CE-2137 C1 (`DisTypeMask`), CE-2138 C2 (the hard-coded `EntityClassTable` + `CountryTable`; `DisNames.json` deleted; CE-2127 fixed), CE-2139 C3 (units DIS Kind 11: `UnitDis`, `NedTkbBuilder.AsLandUnit`). As built: design §5a. ⇒ **CE-2142 D3 is unblocked**: its dependencies C2, C3 and D1 are all built |

### 1a. A note for a session doing embarkation or other behaviour work in parallel

- New nodes and behaviours (`Embark(vehicle)`, `Disembark`, the squad Dismount drill) follow the design's §1 rules from the start: ONE params DTO each, flat JSON; a behaviour that takes nothing names `NoParams` (or an empty DTO of its own until CE-2140 adds `NoParams`).
- Do NOT name them `<Class><Order>` yet and do not add order DTOs; Track D (CE-2140 … CE-2143) does that.
- The handoff's §3 (orders to a unit, carry by vehicle type, carrying a person) is superseded by this programme: R-273 … R-281, and R-274 for carrying. Carry capacity and seats stay out of scope until asked.

## 2. How to pick a task

1. Open the design's §6. Its graph shows what each task waits for, and its table gives the tracker id and the acceptance.
2. Take a task whose dependencies are `[x]` in the [tracker](Blueprint_Issues_Tracker.md); push a `chore: started CE-xxxx at <sha>` commit first (rule 1b).
3. Read the design's §1 rules: the conformance rail (CE-2130) enforces them on every behaviour.
4. Before closing: rails red-proved; the as-built folded back into the design (§2–§4 diagrams, §5 table); the tracker row and this file's §1 updated.

## 3. The lanes (lean)

| lane | tasks | first |
|---|---|---|
| behaviors | CE-2130 R0 · CE-2131 A1 · CE-2140 D1 · CE-2141 D2 · then CE-2132 A2 (after B1) · CE-2142 D3 (after C2/C3) · CE-2143 D4 (after D3 + B2) | **CE-2130** |
| Track B (`backend` lane) | CE-2133 B1 · CE-2134 B2 · CE-2135 B3 · CE-2136 B4 · CE-2129 B5 | **CE-2133** (A2 waits for it) |
| Track C (`ui` lane) | CE-2137 C1 · CE-2138 C2 · CE-2139 C3 | **CE-2137** |
| any | CE-2144 E1 (Health in the cross-role set) | — |
