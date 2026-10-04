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
| `CE-3040` | `SensorChangedEvent` → HSM events through the MobilityLost bridge (§7.3) — ⚠ the PRODUCER side sits in `EqsResultUpdateSystem` / `ThreatEvaluationSystem`; coordinate with backend | design §7.3 |
| `CE-3041` | `ObserverSelector` actually aborts the running lower branch (documented, never built — `Interpreter.cs:267`) | design §7.3 |
| `CE-3047` | the TKB default behaviour throws (params) or never runs (HSM) — defaults start through the ingress | measured, §9 V5 |
| `CE-3048` | a Brain-authority hand-over leaves the unit brain-dead — publish each slot's `{Name, Params, Origin}` for the new owner | measured, §9 V7 |
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
