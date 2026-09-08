<!--STATUS
state: LIVE
updated: 2026-09-08
current-answer: the whole file — a self-contained handoff for CE-224 (behaviour parameter schemas).
design-basis: docs/blueprints/Blueprint_Issues_Tracker.md (CE-224) · docs/blueprints/RULINGS.md R-132
  (a curated artefact outranks a generated one) · R-142 (find and run the feature's own rails first) ·
  tools/ai-debug-mcp/SKILL.md §4 (the paramSchema contract this breaks).
known-conflict: none.
-->

# HANDOFF — `CE-224`: no behaviour has parameters, on any host

> ⭐ **Paste the prompt at the end of this file into a fresh session.**

## 1. The defect, already root-caused — ⛔ do not re-derive it

`GET /behaviors` returns `paramSchema: {"type":"object","properties":{}}` for **every** behaviour, so
nothing over the debug API or MCP can discover a behaviour's parameters, and
`run_mission {behavior, params}` answers **`committed:true`** while silently dropping them.

📐 **Measured `2026-09-08` on TWO hosts** — this is not host-specific:

| host | behaviours | with a non-empty `paramSchema` |
|---|---|---|
| Stride mode 1 *(`HrotStrideApp`, `HROT_DEBUG_API_PORT=8131`)* | 34 | ⛔ **0** |
| plain editor *(`ClusterRunner --mode editor`, port 8141)* | 40 | ⛔ **0** |

📐 **The chain, every hop measured:**

| # | |
|---|---|
| ① | `DebugApiService.cs:1700` builds the entry with `DtoJsonSchemaExtractor.ExtractParams(definition.ParamsDtoType)` |
| ② | `DtoJsonSchemaExtractor.cs:42-57` returns `{type:"object",properties:{}}` when that type is **null** |
| ③ | ⭐ `CgfCuratedBehaviorRegistrar` **does** set it — `ParamsDtoType = typeof(CgfNodes.MoveToLocationParams)` *(`:61`)*, and likewise `FollowRoute`, `JoinFormation`, … |
| ④ | 🔴🔴 **`CgfCuratedBehaviorRegistrar.Register` HAS ZERO CALLERS.** grep over `Hrot/`, `Stride/`, `FDP/` finds only comments and `typeof(...).Assembly` for assembly identity. `CgfBehaviorSetup.LoadFromAiAssembly` calls **only** `BlueprintRegistrarScanner.Scan` |
| ⑤ | 🔴 the registry is filled by the **generated** registrars alone, and **none sets `ParamsDtoType`** — `grep -rc ParamsDtoType Hrot/Subsystems/Hrot.AI.Behaviors/obj/GeneratedFiles/` returns no non-zero file |

⛔⛔ **This is [`R-132`](RULINGS.md) BY OMISSION** — *"a curated (hand-authored) artefact outranks a
generated one"*. There the generated `ParseParams` won the slot and a platoon drove to `(0,0)`; here the
curated producer **never entered the race**.

## 2. ⛔ THE RAILS ARE GREEN AND THE FEATURE IS DEAD — fix the blindness, `R-142` ③

| rail | asserts | why it passes |
|---|---|---|
| `Hrot.SystemTests/DiscoveryAndHintTests.cs:81` | `paramSchema.type == "object"` | ⛔ **the ENVELOPE, never the contents** — its own comment says *"a schema (possibly empty) to fill"*, so an empty schema is an explicit pass. ⚠ And it is in the **T3 slow lane** (`run-system-tests.sh`), so it rarely runs |
| `Fdp.Toolkits.Tests/Behavior/BehaviorRegistryTests.cs:420` | *"the curated DTO type wins too"* | ⛔ uses a **FAKE** curated registration ⇒ proves the precedence works **IF USED**; nothing asserts it **is** used |
| `Hrot.Editor.Tests/DebugApiCompositionTests.cs:33` | — | ⛔ only a comment naming the field |

⇒ ⭐⭐ **The new rail must assert a NAMED behaviour has a NAMED parameter** *(e.g. `MoveToLocation`
exposes the properties of `MoveToLocationParams`)*, from the **live registry**, not from a fake.
⛔ An "is an object" assertion is what let this ship.

## 3. ⚠ WHAT IS **NOT** ESTABLISHED — measure before you build

| ⛔ do not assume | why |
|---|---|
| **that wiring the curated registrar fixes movement** | it fixes the SCHEMA. Whether the runtime then READS the params from a mission task is a **separate, unmeasured hop** |
| **that the curated registrar covers every behaviour** | it names a handful; the generated ones would still have `ParamsDtoType == null`. ⭐ The generator may be the real fix — decide with a measurement, not a preference |
| **that calling it is safe where it is not called today** | ⚠ it registers *interpreters* too. Registering twice may throw or overwrite — 📐 check `BehaviorRegistry.Register`'s duplicate policy FIRST |
| **that `run_mission` is the only consumer** | the editor's Mission panel authors the same params; a schema change is visible there too |

## 4. The acceptance

1. ⭐ `GET /behaviors` on **both** hosts shows a non-empty `properties` for at least the curated behaviours.
2. ⭐⭐ `run_mission {behavior:"MoveToLocation", params:<per the schema>}` on a spawned vehicle produces a
   **non-`None` `NavigationIntent.Mode`** — read it back with `GET /entities/{id}`, per `RUNBOOK` §8.1
   *(🔒 user: "you have the way of dumping any entity so pls use it instead of theoretizing")*.
   ⚠ **If it still does nothing, that is hop ③ above and a SEPARATE finding** — report it, do not widen scope.
3. ⭐ A rail asserting a named behaviour has named parameters, from the live registry.
4. ⛔ **`CE-225` is NOT yours** — `spawn_entity` places entities at the origin, so to get a vehicle at a
   known position use a **scenario load** (`hill-attack-close`), not `spawn_entity`.

## 5. Fences

| | |
|---|---|
| ⭐ **yours** | `Hrot.AI.Behaviors` · `Hrot.CGF/Configuration/CgfBehaviorSetup.cs` · `DtoJsonSchemaExtractor` · the behaviour registry · the generators, if that is where the fix lands |
| ⛔ **NOT yours** | `Stride/**`, `Hrot.Editor/DebugApi/DebugApiService.cs`'s spawn path, `BulletPhysicsBodyService` — a Stride session holds those *(`CE-222`, `CE-225`, and the vehicle-body work)* |
| branch | ask the user; ⛔ do not push to a branch another session is on |

---

## ⭐ THE PROMPT

```
RELEARN. Then read docs/blueprints/HANDOFF_CE224_Behaviour_Parameter_Schemas.md in full
and do it.

CE-224: every behaviour reports an empty paramSchema on every host, so the debug API and
MCP cannot discover behaviour parameters and run_mission silently drops them. The handoff
carries the measured chain (CgfCuratedBehaviorRegistrar sets ParamsDtoType and has zero
callers; generated registrars never set it), the three rails that are green because they
assert the envelope rather than the contents, and four things it explicitly does NOT
establish - read §3 before writing code.

Verify the fix over the live API, not from source: GET /behaviors must show real
properties, and a move order shaped to that schema must produce a non-None
NavigationIntent.Mode read back off the entity. Use a scenario load to place a vehicle -
spawn_entity puts entities at the origin (CE-225, not yours).
```
