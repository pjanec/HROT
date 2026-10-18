<!--STATUS
state: LIVE
updated: 2026-10-17
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.13 B-11b (the charge, backend's) and §5l (the bomber, behaviors')
-->
# Note — behaviors → backend: a spent charge's replicas stay live (`CE-2247`)

**Found** while building B5 (`DetonateCharge`, `ua-armed-bomber`, [design §5l](../../DESIGN_Civilians_And_Crowd_Control.md)).

| | |
|---|---|
| what | 2 s after the bomber's vest bursts: SimHost (its position owner) — **gone** · CGF — **live charge** · IG — **live charge** (`Charges.IsCharge` true) |
| why it matters | an ask arriving on CGF/IG could burst it twice; every reader (perception, the weapons API) sees a live charge on a dead man |
| what `Charges.End` promises | a replicated charge ends through ONE `DestroyEntityCommand`, so every replica goes |
| lean (unmeasured) | the teardown is not reaching / not applied on the non-owner nodes for a CARRIED thing, or only the owner clears `Charge` |
| the rail | `PostureScenarioTests.CE2247_Bomber_TheSpentVest_LeavesEveryNode` — written, **skipped** on `CE-2247`; unskip it as the acceptance |

Everything else about the burst works live: never armed to anyone before it, his death, everyone within 5 m dead, nobody beyond 25 m hurt (`ua-armed-bomber` live 5/5).

---

**✅ Backend, `2026-10-18` — fixed.** Your lean was right: the teardown never reached the non-owners. The charge bursts on SimHost (its position owner), but only CGF (the master's owner) can dispose it on the wire, and the "ask the owner" forwarder was IG-only (and polled through a hook a direction-routed node never calls). It is now on every node; `CE2247_Bomber_TheSpentVest_LeavesEveryNode` is unskipped and green. As built: [Entity Creation §3.4c](../../DESIGN_Entity_Creation_Unification.md). Ids allocated: none.
