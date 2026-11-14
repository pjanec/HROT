<!--STATUS
state: LIVE
updated: 2026-10-13
doc-type: STEER — amends a RUNNING handoff (rule 1c). Handoff: HANDOFF_Params_Finish_Backend.md, dispatched at d92f57081, started by the backend lane at 75ba5b75b. The user relays this note.
current-answer: §1 what changes · §2 what is unchanged
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Orders_Params_Entity_Classes.md — §13.0 (the user's words), §13.1 F8 (replaced) and F12 (new)
  - docs/blueprints/HANDOFF_Params_Finish_Backend.md — the handoff this steers
-->

# Steer 1 — finish the params story (backend)

⭐ **This is not a correction of your work.** The user ruled two things after the dispatch; this note carries them so you do not
have to adapt to a document that changed under you. Ledger: R-2004, R-2005.

> 🔒 **User, 2026-10-13, verbatim:** *"Blueprints should use param dto either from asset or hardcoded struct as anyone else, no own
> declaration, getparameters node probably already provides the parameter fields as pins so single-field parameter either need to refer
> to the field of the single param dto or gets retired as redundant. Json must support fields everywhere, and in case insetive way,
> sharing the json option settring."*

## 1. What changes

| item | before (handoff) | now |
|---|---|---|
| **P4** blueprint half | 4 Behavior blueprints get C# DTOs; `ParameterDecl` and Get Parameter STAY as a view — "confirm F8 first" | ⭐ **F8 is decided the other way (R-2004): no blueprint declares Parameters.** All **14** Parameter-declaring blueprints name a params DTO (`ParamsDtoTypeId`; a hand-written struct is fine): 4 Behavior, 2 resolvers, 8 AiPrimitive actions / conditions — the list is in design §13.1 F8. `ParameterDecl` (as a blueprint declaration kind) retires. **Get All Parameters** is retargeted to one data-out pin per FIELD of the named DTO. **Get Parameter** retires (lean in F8; the user offered "refer to a field" or "retire") — its 3 graphs (`HsmGuardDemo`, `DangerCrossingBp`, `PlatoonHillAttackBp`) move to Get All Parameters. ⚠ the compiler needs the DTO's fields: reflection in the editor, Roslyn / `GeneratedTypeCatalog` in the generator. ⚠ If the AiPrimitive conversion turns out to need more than the params DTO (e.g. the host binding emitters), STOP that part and report it — the behaviour and resolver blueprints still convert |
| **P8** JSON | "lean: one shared options instance for params JSON" | ⭐ **ruled (R-2005), and the instance EXISTS:** `Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed` — field-aware, case-insensitive, frozen. Every params / order JSON path, parse AND send, uses it. Beyond params: the other ad-hoc `new JsonSerializerOptions` in production (42 measured) move to it or carry a one-line justification, listed by a rail as a shrinking allow-list — take this as its own item after P8 if it grows large, and allocate it an id. ⚠ `DefaultRelaxed` reads enums as strings only (`StrictStringEnumConverter`, `FdpJsonOptionsRegistry.cs:77`) and omits nulls on write — measure the params JSON that sends enums as numbers before switching |

## 2. What is unchanged

P1, P2, P3, and the rest of P8 (fields, the readers, the rail) and of P4 (deleting `Role`, `StatefulSlotInfo.Role`, `StatefulSlotRole`,
the empty allow-list) stand exactly as dispatched. The order and the two-session split stand.
