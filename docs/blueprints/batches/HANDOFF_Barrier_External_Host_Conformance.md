<!--STATUS
state: LIVE — dispatch (frame-delegation). Owning design: ../../DESIGN_Cross_Node_Construction_Barrier.md.
build-state: READY-TO-BUILD (H-ui authors the §3d design detail + UML as step 1, then builds).
updated: 2026-09-16
current-answer: this is the dispatch frame. Build against DESIGN §3b.6/§3c + AQ-70 §Q70-A/B; author the
  external-host conformance detail (mode table + state/sequence UML) INTO DESIGN §3d as step 1.
related-designs:
  - ../../DESIGN_Cross_Node_Construction_Barrier.md — THE OWNING DESIGN (§3b/§3c degradation; §3d to be authored).
  - Architect_Question_70_Host_Capabilities_And_Reliable_Init_Degradation.md — §Q70-A (degradation) / §Q70-B (token semantics).
  - ../../RUNBOOK_Cluster_Debugging_Over_Http.md — §5a ddsmonitor wire capture (the proof instrument); §1.2 multi-process launch.
-->
# HANDOFF (FRAME) — reliable-init barrier: EXTERNAL-HOST CONFORMANCE (fake external host) + fake-waiting role check

> 📌 Scope is FROZEN at the coordinator HEAD named in the dispatch message. Documents that change after it
> are FYI only. ⭐ Rule 7: re-sync from `claude/blueprint-authoring-status-6sr5ld` FIRST. ⭐ Rule 1b: push an
> empty `chore: started external-host conformance at <sha>` marker before code. ⛔ No PR. ⛔ Continue `CE-`
> ids (plain increments, rule 3a-id) — you allocate them. ⛔ If a premise here is false, STOP that item and
> report — do not adapt silently. This is a FRAME: you own the inventory, the UML, and the item breakdown.

## 0. 🎯 GOAL
Prove — **on the wire, against a genuinely EXTERNAL process** — that the reliable-init barrier degrades
gracefully when a peer does not honour the reliable-init extension. The merged C5 fake
(`SimulatedInitReadinessParticipant`) is *in-process* and speaks HROT's own protocol, so it can only fake a
supporting-but-slow/stuck peer. This frame adds the missing half: a **standalone CycloneDDS.NET fake host**
(a foreign process on the DDS bus, following base DDS/BDC rules but NOT the reliable-init extension), run
next to a **live cluster** as a T3 e2e test. Plus a small correctness check on the fake-waiting roles.

⭐ **User rulings baked into this frame** (`2026-09-16`): (a) *"NED and BDC are not different in principle —
both follow the BDC/NED rules, they differ in implementation details"* ⇒ one external fake host exercises the
interop contract for both. (b) *"fake the waiting should happen for Map2d and MuscleGround roles for the
purpose of testing reliable init."* (c) **SEPARATE PROCESS, not in-process** — the fake host is a real
foreign process next to a live cluster (the e2e live test); the in-process variant is redundant with the
existing `NetworkGatewaySystemTests` prune unit rails, so do NOT build it.

## 1. ⛔ STEP 0 — DESIGN DETAIL FIRST (frame-delegation): author §3d INTO the design
Before code, fold a new **§3d "External-host conformance"** into
`DESIGN_Cross_Node_Construction_Barrier.md` (per NO-IMPLEMENTATION-WITHOUT-UML; diagrams live in the design,
not here). Do your own INVENTORY first (`search_graph` + grep) — the existing raw-participant test pattern is
prior art (`DragDropIntegrationTests.cs:112` — `new DdsParticipant(domainId)` + `DdsReader<Hrot.NED.Descriptors.WorldPos>`;
MiniExCon/CgfSubsystemHeadless likewise). Draw:
- a **state diagram** of the fake host's three modes (below);
- a **sequence diagram** per mode showing the creator-side outcome it forces (wait-set exclude / short-prune
  self-heal / long abort-dispose);
- confirm which NED descriptor types it must read/write (`Hrot.NED.Descriptors.EntityMaster`,
  `NodeCapabilities`, `EntityLifecycleStatusDescriptor`) and that it reuses them read-only (no new wire types).
Report the inventory + the design § you authored as your FIRST message (as slice A / piece C did).

## 2. 🔧 BUILD — you own the slicing; these are the required outcomes

### 2a. The fake external host (standalone, separate process) — ALL THREE MODES
A minimal CycloneDDS.NET app/harness that joins the cluster's DDS domain as a foreign participant. Placement
is your call (a tiny console project, or a test-support launcher) — argue it in the design; "unit-test-driven
but a separate process" is the target. Modes:

| mode | behavior | creator-side outcome to ASSERT |
|---|---|---|
| **UNAWARE** | never advertises `fdp.reliable-init` (no `NodeCapabilities`, or caps without the token) | C1 capability filter EXCLUDES it from the wait-set → it never blocks; creator completes normally |
| **AWARE-BUT-SILENT** | advertises caps but, on a `WaitForAcks` `EntityMaster`, NEVER publishes phase-1 `EntityLifecycleStatusDescriptor` | C2 short-prune drops it (`onPeerUnsupported` + `RecordUnsupported` self-heal) → creator completes Success, NO deadlock |
| **STUCK** | publishes phase-1 then never phase-2 | C3 long abort → `EntityMaster` `NotAliveDisposed` on the wire (the foreign-process version of the C5 in-process abort) |

### 2b. Fake-waiting role check (user ruling b)
Verify the merged `SimulatedInitReadinessParticipant` fake-waiting is actually exercised for the **Map2d**
(IG) and **MuscleGround** (SimHost) roles specifically — not just "some type on some host." If it is not
wired/registered for those two roles, fix it (minimal), and say so. If it already is, report that it is (a
finding, not a change). ⛔ Do not otherwise touch the merged production barrier code — it is done.

## 3. ✅ ACCEPTANCE — the proof, not a green gate
- ⭐⭐ **T3 live, on `--mode all` (or §1.2 multi-process), async — NEVER a foreground blocker.** Boot the
  cluster, spawn a reliable entity (via the CE-292 ai-debug `reliable:true` knob) whose wait-set includes the
  fake host, run the fake host in each mode, and prove the creator-side outcome above — via HTTP diagnostics
  and a **ddsmonitor** wire capture (RUNBOOK §5a; `--AppSettings:TopicSources` at the build output;
  defaults for discovery — no `CYCLONEDDS_URI`). Capture the decisive samples per mode (wait-set membership,
  the absent/late ack, the `NotAliveDisposed` for STUCK).
- ⭐ Map2d + MuscleGround fake-waiting confirmed (or fixed).
- T-1: this is the barrier's own feature — run its existing suites first (`NetworkGatewayIntegrationTests`,
  `NetworkGatewaySystemTests`, `ConstructionResultsTests`, `SimulatedInitReadinessParticipantTests`); add the
  new external-host rails INTO the feature's suite, not a parallel class.

## 4. ⭐ REPORT BACK TO **H - coord** (gate contract — substitutes for my re-run)
Two messages: (1) after Step 0 — the inventory + the §3d design/UML you authored. (2) when done/blocked —
`CE-` ids allocated · the design § folded (as-built) · one row per gate (verbatim cmd · pass/fail/skip ·
`--no-build` · delta vs base) · every RED confirmed pre-existing against the base sha · tree clean · the
three-mode wire-proof results · the Map2d/MuscleGround finding · `rulings-check.py` + `design-digest.py --check`
+ `mermaid-check.mjs` verdicts. Deliver by trigger to session H - coord.
