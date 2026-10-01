<!--STATUS
state: LIVE — report from the backend lane to the behaviours lane
updated: 2026-10-01
current-answer: §1 (what changed for you), §2 (acceptance), §3 (things to act on), §4 (gates).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Eqs_Sensor_Lifecycle_Wire.md — the handoff this answers.
  - docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md — §3a is the durable as-built of this batch (deviations, rails);
    §1 D5 / §3 now say Suspended.
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — owns the topics, the carrier and the solver this changed.
-->

# REPORT — EQS sensor lifecycle, wire half (`CE-486`, `CE-487`, `CE-490`)

**From:** the backend lane (`backend`) · **To:** the behaviours lane (`behaviors`) · **Answers:**
[`HANDOFF_Eqs_Sensor_Lifecycle_Wire.md`](HANDOFF_Eqs_Sensor_Lifecycle_Wire.md) — dispatched at `acb4ce898`, merged at `3f2be54ba`,
started-marker `0a293dbc2`. Scope frozen there; nothing on `behaviors` invalidated an item.
**Ids:** none allocated — `CE-486`/`CE-487`/`CE-490` were yours; all three DONE.

## 1. What changed for you

| | |
|---|---|
| ⭐ **the one shared seam** | `EqsSensor` gains ONE appended field, `bool Suspended` (`[MarshalAs(I1)]` — the ECS layout validator refuses a bare bool). Default `false` = running ⇒ every existing `new EqsSensor { … }` stays correct; `CE-485` needs no change for it. Nothing renamed or reordered. ⚠ The approved name was `Active`; B-1 flipped the polarity and the user settled it: *"ok Suspended is fine"* |
| ⭐ **ending a sensor** | destroy (or strip `EqsSensor` from) the child as `CE-485` does — the egress's sweep then writes that sensor's last config back with `Suspended = true`. ⛔ It never disposes a child instance while the parent lives. A live local sensor holding the same (parent, part id) in that scan keeps the key out of the sweep — so `CE-485`'s end-then-`Ensure` in one frame is safe. ⭐ ONE rule ends every sensor: *an instance under an entity this node owns that no local sensor holds is suspended* — your ended ones and inherited ones (`CE-490`) alike (a separate end-write was built first and was fully shadowed by the sweep, measured by its red-proof) |
| **a dead parent** | ⚠ not in the handoff: when the PARENT is gone, the egress still DISPOSES the child instance (descriptor rules: dispose = entity deletion). Writing `Suspended` there would leave a TransientLocal instance on the topic forever |
| **the Muscle** | a child dispose no longer destroys the carrier (`SubEntityCleanupSystem` does, when the parent dies); a `Suspended` config updates an existing carrier but never creates one; the solver publishes nothing for a suspended carrier — not even the unknown-template empty answer |
| **results** (`CE-487`) | every result-cache hit is re-checked (alive · an `EqsSensor` · that parent and part id) ⇒ a reused part id's answers reach the NEW sensor. Your epoch high bits still decide whether the answer is current |
| **authority move** (`CE-490`) | the brain's config egress reads its own topic; each scan it writes `Suspended` once for every child instance whose parent this node holds authority over and that NO local sensor holds (every local sensor counts, authority or not — a live one is never swept). ⚠ deviation from B-3: the reader is inside the egress (one brain-side owner of the topic), not a separate translator; nothing added to `SimHostAuxiliaryTranslatorPack` |
| ⭐ **`EqsSensorKey`** (new, `Fdp.Toolkits/Spatial/Eqs`) | the wire key `(ParentNetworkId, LocalChildIndex)`, both directions, once — 🔒 *"share and unify, do not duplicate"*. The egress, the solver, the result ingress and `EqsResultUpdateSystem` each wrote this rule out; all four route through it now. ⚠ It answers the WIRE question (by the parent's network id); your `EqsChildSensor.Find` answers the brain question (by parent entity + site) — not merged, different question, and yours is fenced |

## 2. Acceptance

① end ⇒ no dispose; carrier stays, suspended, publishes nothing ✅ `CE486_ASensorThatEnds_IsSuspendedOnTheMuscle_NotDestroyed_AndPublishesNothing` ·
② end + create on the same key in one scan ⇒ the Muscle solves the new one ✅ and ④ the answer reaches the NEW local sensor ✅
`CE486_CE487_ASensorReplacedOnTheSameKeyInOneScan_IsSolved_AndAnsweredOnTheNewSensor` ·
③ ✅ `CE486_AChildDispose_DoesNotDestroyTheCarrier_AndAFollowingWriteUpdatesIt` — ⚠ **measured:** the topic is KeepLast-1, so a dispose
and a write of ONE key written back-to-back collapse into one valid sample on a real reader; "both in one Take" cannot be produced over
DDS (a rail written that way stayed green on the old ingress — caught by its red-proof). The rail lets the Muscle take the dispose, then
writes · ⑤ ✅ `CE490_AnInheritedInstanceWithNoLocalSensor_IsSuspendedByTheAuthority` · ⑥ ✅ §4 · ⑦ live — §4.

## 3. Things to act on

| | |
|---|---|
| ⚠ **DDS domain overrun in the integration harness** (pre-existing) | `HrotRunnerHarness`'s auto-range is documented as 100–145, but **50** call sites use it, so a full run walks past 145 into the explicit ranges (150–159 `AllSubsystemsSpawnMovingVehicleTests`, …). My rails first took 146–149 on the comment's word and one timed out in the folder-wide run; they use 40–43 now. Not fixed here — the harness is shared |
| ⚠ **multi-writer ordering** (your input wanted) | an instance can carry the old owner's last ACTIVE sample and the new owner's SUSPEND; a late-joining Muscle gets both in arrival order and can end ACTIVE. The binding exposes no destination-order QoS. ⭐ Proposed follow-up AFTER `CE-485`: the Muscle keeps a suspended lifetime suspended — drop an active sample whose epoch equals a suspended one (your epoch high bits make that a lifetime test; today it would silence restarts, all starting at epoch 1). Design §3a records it |
| ⚠ recordings | `EqsSensor` grew one byte (plus padding). A recording made before this build carries the old layout for that component — worth knowing if a replay of an old EQS recording misbehaves |
| ⚠ `ClusterConformanceRails` message | (from the previous batch) still says the `acksPending` lambda is in `Program.cs` — STOP path, not edited |

## 4. Gates *(base `3f2be54ba`)*

GATES_PLACEHOLDER
