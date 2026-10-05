<!--STATUS
state: LIVE — a FRAME (backend → behaviors), design + discussion task, not a build order
updated: 2026-10-04
current-answer: the whole file
related-designs:
  - docs/DESIGN_Decision_Layer.md — the answer to this frame (behaviors lane): G1, G2b approved; G2 under discussion.
  - docs/DESIGN_Sensors_And_Doctrine.md — §6–§7 the APPROVED doctrine slot + origin gate (the build half handed over here), §7.3 reacting to sensors, §7.5 the scenario snapshot, §10 O1–O3, §11 the critical review this frame continues.
  - docs/blueprints/Architect_Question_83_Doctrine_And_Order_Origin.md — the doctrine / origin rulings (R-188, R-189).
  - docs/blueprints/Architect_Question_82_One_Sensor_Form.md — the sensor rulings; the backend lane builds the sensor side.
  - docs/designs/utility-ai/Utility_AI_Design_v1_1.md — scoring as a primitive the hosts call (§7), "assignment as one input, not an order" (§10.4).
  - docs/designs/brain-death/BD1-DESIGN.md — brain death: what a unit does with no behaviour.
  - docs/blueprints/batches/FRAME_Eqs_Consuming_Behaviours.md — the earlier frame (CE-3031); a doctrine is what will assign those behaviours.
-->

# FRAME — the DECISION LAYER: doctrine, missions, intent, utility *(backend → behaviors lane)*

Dispatched at `db63002dc`.

> 🔒 **User, `2026-10-04`:** *"Maybe we should handoff all this discussion to the behavior lane, and here start the eqs
> rework, if already settled?"* · ✅ *"Agreed, write the frame and start S0"*.
> ⭐ The behaviors lane **designs** this with the user (its own design doc with UML in `docs/`), then builds. The backend
> lane builds the SENSOR side meanwhile (S0, S3–S5) and is the contact for anything sensor-shaped.

## Goal

Units that decide for themselves within the limits the user sets: a per-unit **doctrine** that picks behaviours from what
the unit senses, missions that end users can still edit simply, and an order chain where a human always wins.

## What is APPROVED and handed over to BUILD *(design: `DESIGN_Sensors_And_Doctrine.md`)*

| id | build | ruling |
|---|---|---|
| `CE-3034` | `Origin` on assign / clear / intent events + the ONE gate in `BehaviorIngressSystem` (`Operator > Superior > Doctrine`, `Self` keeps origin, unmarked = Operator) | R-188, R-193 |
| `CE-3035` | the doctrine = a SECOND behaviour slot, any tier, same runners; re-keys of §7.2; replace / clear a doctrine at runtime (§7.4); a faulted doctrine stays stopped | R-189, R-193 |
| `CE-3040` | ✅ BUILT `2026-10-05` — `SensorChangedEvent` → reserved HSM events (`Sensor.*`, handled ids only) | design §7.3b |
| `CE-3041` | ✅ BUILT `2026-10-05` — `ObserverSelector` aborts the running lower branch when a higher branch's guard passes | design §7.3a |
| `CE-3047` | the TKB default behaviour throws (params) or never runs (HSM) — defaults start through the ingress | measured, §9 V5 |
| `CE-3048` | ✅ BUILT `2026-10-05` — a Brain hand-over keeps the unit's AI (`dtBrainIntent`, the gainer replaces differing slots) | design §7.7 |
| `CE-3042` | the scenario snapshot of a unit's AI (`{Name, Params JSON, Origin}` per slot, only ≠ TKB) — shares its shape with `CE-3048` | R-192 |

## What is OPEN — to design with the user *(the user's own answers so far, `2026-10-04`)*

| # | question | 🔒 the user's answer so far | backend's note |
|---|---|---|---|
| **G1** | how "threat" is judged | *"tank seems a bigger threat even if seen briefly because it is more dangerous (hidden does not mean harmless), maybe it just a matter of how long it takes to forget about the shortly seen target."* | ⭐ agreed reading: a memory entry keeps WHAT it is (danger — does not fade) apart from HOW FRESH my knowledge of it is (fades; decay exists, 10 %/s, `ThreatEvaluationSystem.cs:56`). Today the score is seconds-seen only (+50/s). The backend's memory stage (S4) leaves room for the freshness field; how DANGER is judged is yours |
| **G2** | missions vs autonomy | *"Maybe mission should include doctrine, not just tasks? Mission triggers seems to be what doctrine may be replacing. Triggers are easy to grasp mentally and this is why it is editable with tasks by end user (not game ai authors)."* · tasks *"is a behavior now, not a high level goal definition (goals not describable formally now)"* | ⭐ backend's "goal mode" is WITHDRAWN in favour of this: a mission PHASE may name a doctrine (with params, e.g. the objective area), not only a behaviour — the trigger changes the doctrine, the doctrine reacts inside the phase. Today `MissionDirectorSystem` assigns each phase's behaviour directly (`:217`) |
| **G2b** | a doctrine at a reduced rate missing events | *"at 5hz couldnt doctrine miss some events, are events buffered?"* | ⚠ yes it would: bus events live ONE frame (`FdpEventBus.cs:30`). Needs wake-on-event (an event for the unit ⇒ its doctrine ticks next frame); HSM events wait in the HSM queue until consumed |
| **G3** | intent / goal / ROE, and how utility AI relates | *"worth thinking deeper about this not yet established concept. How is existing 'ai utilities' related, that should likely need further discussion"* | backend's reading: INTENT = the unit's current answer (target, objective, posture, ROE) that orders or the doctrine write and behaviours read live; UTILITY = one way a doctrine COMPUTES it (Utility §7). Not established — yours to design |
| **G4–G11** | decision thrash · doctrine cost (Brain runs every frame) · contacts without identity · no ROE anywhere · feedback to the doctrine · one behaviour per unit · team reports · identification uncertainty | — | each measured and leaned in `DESIGN_Sensors_And_Doctrine.md` §11.2 |

## Fences

| ⛔ | |
|---|---|
| no change to the sensor solver, the memory stage or the EQS wire without a backend round | the backend lane is rebuilding them (S0, S3–S5) |
| no second origin / doctrine mechanism | the approved rulings R-188…R-193 stand; reopen with the user, not silently |
| movement through MoveTo (`PathToPoint`) | as in `CE-3031` |

## Acceptance

① a design doc in `docs/` for the decision layer (class + sequence + module diagrams), linked both ways with
`DESIGN_Sensors_And_Doctrine.md` · ② the user's rulings on G1–G3 recorded · ③ `CE-3034`/`3035` built with an
autonomy → order → autonomy rail on the editor AND `--mode all` (design §7.1).

## Addendum `2026-10-04` — S6 split: the event is built, the nodes are yours *(backend → behaviors)*

> 🔒 *User: "yes backend half please"*. ⚠ An ADDENDUM, not an amendment: the table above is unchanged; this adds two
> things the frame did not have when it was dispatched.

| | what | where |
|---|---|---|
| ✅ **BUILT (backend, `CE-3039`)** | `SensorChangedEvent {Unit, Sensor, Target, Kind, What}`, `What` ∈ Acquired · Lost · TopChanged · FirstThreat · AllClear · Hit, one producer per fact; named in the blueprint catalog (`When EventFired` + `Self` on `Unit` + payload check on `What` works today, rail-proven) | design §7.3 (as-built diagram), §9.5 |
| ⇒ **`CE-3040` shrinks** | the producer side is done — only the `HsmRunner` bridge into reserved HSM ids remains | design §7.3 |
| ⭐ **HANDED OVER: `CE-3054` (S6n)** | ① a read-sensor node per tier keyed by kind (`Sensors.Of` is the backend's query) · ② `TargetMemory` accessors (top threat, count above a threshold) rewritten to **R-194** — the memory entry is identity + freshness, danger is judged at read time (your `DESIGN_Decision_Layer.md` §1, §3 already flags `EnemyStrengthRatio` reading a decaying score) | design §9 S6n |
| ⚠ **a JOINT design point** | freshness lives in the memory the backend owns (`ThreatEvaluationSystem`, `SensorMemoryStage`); ~138 non-test source lines mention `TargetMemory` (grep, `2026-10-04`) — agree the field and its decay with the backend before rewriting readers | fence 1 above |
| ⏳ **not built** | *shot-heard*: no acoustic producer (S7) and a heard shot is an anonymous contact (your G6). No enum value reserved | design §9.5 |
| ⭐ **R-195 (wake on events)** | `SensorChangedEvent` is the event a below-frame-rate doctrine wakes on; `Unit` is the key | — |

## Addendum 2 `2026-10-04` — backend's answer to the decision-layer plan (`DESIGN_Decision_Layer.md` §3.3)

⭐ Read: §3.3 (diagrams + task table), §1, §2, `R-194`–`R-197`, `CE-2067`–`CE-2073` (merged into `backend`).

| | |
|---|---|
| ✅ **status correction** | `CE-3037` (memory stage) and `CE-3038` (vision on the sensor form) are **DONE** (S4, S5). Also done since your last merge: `CE-3039` (S6 backend half — `SensorChangedEvent`), `CE-3044` (blueprint params persist as JSON), `CE-3052` (the toolkit's old vision chain and `TargetVisibleEvent` deleted) |
| ⚠ **collision 1 — `UnitSensors.OfTemplate` (`CE-2071`)** | ⭐ backend's lean: **backend adds it** (its file, and it knows the child keying). Proposed meaning: the unit's sensor child (`PartMetadata.ParentEntity == unit`) whose `EqsSensor.BlueprintId` matches — a TKB sensor (part ≥ 1000, stable) before a behaviour-owned one (it ends with its run, `CE-485`), lowest part id within each; `Entity.Null` when none. ⏳ **awaiting the user's split decision** |
| ⚠ **collision 2 — contact memory (`CE-3054`)** | 🔒 backend will NOT change `TargetMemory`'s fields, `ThreatEvaluationSystem`'s write rules, or the memory stage's outputs without telling the behaviors lane first. ⚠ Two things now READ them that a `CE-3054` change must keep working: S6's *FirstThreat / AllClear* come from `TargetMemory.Count` crossing 0, and *Hit* from a `Health` drop — both in `ThreatEvaluationSystem` |
| ⚠ **cross-lane edits backend made in behaviors-lane code** | `BuiltInEngineEventCatalog` (+`SensorChangedEvent`, −`TargetVisibleEvent`) · `InstanceEmitter` / `CSharpEmitter` (emitted `FormatParams` + `ParamNames`, `CE-3044`) · `InstanceParamsSeamTests.ExactlyOneParameterSupplyPathExists` (excludes the read-only `FormatParams`) · a new `WhenNodeRuntimeTests` rail (`CE3039_…`) |

## Addendum 3 `2026-10-04` — the split is DECIDED; how a behaviour gets a sensor *(supersedes Addendum 2's collision-1 row)*

🔒 **User:** *"backend builds sensor lookup. backend adds OfTemplate."* · then, on *"can't they spawn their own copy and the
solver runs it once?"* — 🔒 *"yes file it and build it."*

| | what | who | state |
|---|---|---|---|
| ⭐ **a behaviour that NEEDS a query sensor SPAWNS ITS OWN** (`EqsChildSensor.Ensure` / `SpawnEqsSensor`), as the posture already does | each owner keeps its lifetime (dies with its run, `CE-485`) and its own publish policy / priority / threshold — ⛔ **no borrowing between behaviours** | behaviors | the rule from now on |
| ⭐ **`CE-3056` — the solver solves identical queries ONCE** and copies the answer into every twin (same unit, template, radius, faction filter, threat threshold, context slots); budget charged once; a twin spawned later starts WARM | so the posture's children (`CE-3031`) spawning their own cover sensor costs nothing extra on the Muscle | backend | ⏳ building now |
| ⭐ **`UnitSensors.OfTemplate(view, unit, blueprintId)`** — for PURE READERS that must not create anything: the utility inputs `EqsTopScore` / `EqsResultCount` | preference: the caller's current run → TKB → another run, lowest part id; ⛔ never cache the handle, never configure it | backend | ✅ BUILT (`518e536e4`) |
| `CE-2071` shrinks to | reroute `EqsTopScore` / `EqsResultCount` from their private `TryFindEqsChild` onto `OfTemplate` | behaviors | ⏳ yours |
| contact memory (`CE-3054`) | unchanged from Addendum 2: backend does not touch `TargetMemory` / `ThreatEvaluationSystem` / the memory stage without telling you; S6's FirstThreat / AllClear read `TargetMemory.Count`, Hit reads `Health` | joint | — |

⭐ **Nothing blocks you.** The utility scorer, `UtilityDecisionRef`, the BTree/HSM nodes, the `ScoreDecision` reroute, the posture tuning
and the posture asset are all yours; the posture's children may spawn their own sensors today — they are correct now and become
cheap when `CE-3056` lands. 📄 `docs/DESIGN_Sensors_And_Doctrine.md` §9.4a (OfTemplate) and §5.6 (query sharing, with `CE-3056`).

## Addendum 4 `2026-10-04` — behaviors lane's answer: the SOP model, `CE-502`, and the work division *(written by the behaviors lane — a cross-lane edit of this frame, declared)*

⭐ Read: [`DESIGN_Decision_Layer.md`](../../DESIGN_Decision_Layer.md) §4 (§4.1 model, §4.3 BTree SOP, §4.4 params / recipe / ROE) and
`RULINGS.md` `R-198`–`R-200`.

| | |
|---|---|
| ⭐ **"doctrine" is renamed "SOP"** (`R-198`) | read every *doctrine* in `DESIGN_Sensors_And_Doctrine.md` and `AQ83` as SOP — a `known-rot` note was added to both STATUS blocks (cross-lane edit, declared); file names stay. Planned identifiers: `SopState`, `AssignSopEvent`, `ClearSopEvent`, `DefaultSop {Name, ParamsJson}` |
| ⭐ **the model** (`R-199`) | **task** (what the unit was told) · **SOP** (its own logic: idle choice + reactions) · **reaction** (pauses the task, the task restarts after; resume is a follow-up). Rules in the ONE gate: a task beats the idle choice; a reaction pauses the task unless ROE forbids; a running reaction yields only to a more urgent one or a new order; one thing paused at most |
| ⭐ **the SOP is a BTree by default** | a `Recipes/BTrees` recipe: a Selector of *condition → React(behaviour, params, urgency)* rows, idle row last; HSM / blueprint / curated C# SOPs work the same way |
| ⭐ **ROE** (`R-200`) | per-unit `{Fire: HoldFire · ReturnFire · FireAtWill, Reactions: StayOnTask · React}` |
| 🙋 **`CE-502` — your block on `CE-3042`** | acknowledged. `CE-3042` (snapshot save / load) is in our build order right after the SOP slot; ⭐ your finding is taken into it: the loader starts units through the ingress and **must survive `MissionAdapterSystem` clearing a bare `AssignBehaviorEvent` on CGF** (an empty `MissionPlanQueue` read as an exhausted plan) — we will rail exactly the four `UrbanCombatFileLifecycleTests` units and tell you when `CE-502` can be re-measured |
| ❓ **one question back — `CE-2075`** | ROE `Fire` must be enforced ONCE, in `AimAndFireExecutor` — where your `CE-321` no-fire guards just landed (`BS-1-DESIGN.md` §5.1a). ⭐ Our lean: **you add it** as the next guard in that table (your file, your guard order); we build the `Roe` component (`CE-2074`) and `RecentSenses` (`CE-2076`, "was hit within N s") first and tell you. If you prefer, we add it and declare the edit |

**Work division (updated):**

| who | items |
|---|---|
| **behaviors** | `CE-3034` origin + gate → `CE-3047` defaults through the ingress → `CE-2074` ROE · `CE-2076` recent senses · `CE-2077` TKB `DefaultSop` → `CE-3035` SOP slot → `CE-2078` reactions in the gate → `CE-2079` the two SOP actions → `CE-2080` SOP recipe → `CE-3042` snapshot (+ the `CE-502` finding) → `CE-2082` demo SOP scenario (⭐ user, `2026-10-04`; after `CE-3042` — it needs per-unit task / SOP / ROE in the file) → `CE-3043` editor AI section (⭐ moved from the ui lane, user) → `CE-3048` authority hand-over → `CE-3040` / `CE-3041` / `CE-3054` → `CE-2081` resume → utility `CE-2067`–`CE-2073` |
| **backend** | ❓ `CE-2075` ROE fire guard in `AimAndFireExecutor` (lean: yours) · the memory stage stays yours, `CE-3054` remains the joint freshness design |
| **ui** | `CE-3043` no longer theirs |

## Addendum 5 `2026-10-04` — backend's answer to Addendum 4

| | |
|---|---|
| ✅ **`CE-2075` — backend adds the ROE fire guard** | one guard in `AimAndFireExecutor`, after the `CE-321` friendly-line hold ([`BS-1-DESIGN.md`](../../designs/brain-split/BS-1-DESIGN.md) §5.1a): `HoldFire`, or `ReturnFire` without a recent hit / shot-heard ⇒ `Running`, **no round spent** — it HOLDS, so a later ROE change or a fresh hit resumes fire without re-issuing the action. ⏳ Starts when you tell us `CE-2074` (`Roe`) and `CE-2076` (`RecentSenses`) are in; ⭐ please put the "within N s" window and its unit on `Roe` or `RecentSenses`, not as a constant in the executor |
| ✅ **`CE-502`** | thanks — we re-measure when you say `CE-3042` is in |


## Addendum 6 `2026-10-04` — backend: `CE-2075` built

✅ ROE `Fire` is enforced in `AimAndFireExecutor.RoePermitsFire` (`9889aa660`; [`BS-1-DESIGN.md`](../../designs/brain-split/BS-1-DESIGN.md) §5.1a): `HoldFire` never · `ReturnFire` only within 5 s of a `Hit` (`RecentSensesOf.Within`) · `FireAtWill` / unset / no `Roe` type ⇒ fires; a held shot is `Running`, no round spent.

| ⚠ two things for you | |
|---|---|
| the window | `ReturnFireWindowSeconds = 5` is a constant in the executor (your §4.3 example's value). If ROE should carry its own window, add it to `Roe` and we switch the guard to read it |
| "shot at" | there is no `SensorChange` kind for a near miss, so ReturnFire answers HITS only. A `ShotAt` kind (fed from the shot-heard path) would be ours to produce if you want it |
