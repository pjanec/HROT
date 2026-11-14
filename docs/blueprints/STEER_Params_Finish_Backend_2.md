<!--STATUS
state: LIVE
updated: 2026-10-13
doc-type: STEER — amends a RUNNING handoff (rule 1c). Handoff: HANDOFF_Params_Finish_Backend.md (dispatched d92f57081, started 75ba5b75b), after STEER_Params_Finish_Backend_1.md. The user relays this note.
current-answer: §1 what changes · §2 what is unchanged
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Orders_Params_Entity_Classes.md — §13.0 (the user's words), §13.1 F8 / F12, §13.5 (P9, the retirement)
  - docs/blueprints/STEER_Params_Finish_Backend_1.md — the first steer; this one settles its two leans and narrows P4
-->

# Steer 2 — finish the params story (backend)

⭐ Not a correction of your work: the user settled steer 1's leans. Ledger: R-2004 (confirmed), R-2006, R-2007.

> 🔒 **User, 2026-10-13, verbatim:** *"All blyeprint types, retire getparameter node, feel feee to retire PlatoonHillAttackBp with its
> scenario (too complex to maintain, we prefer csharp hardcoded version), json always should read enums as strings not numeric constants,"*

## 1. What changes

| item | now |
|---|---|
| **P4** blueprints | ⭐ ALL blueprint kinds convert (steer 1's lean is now the ruling), and **Get Parameter retires** (no longer a lean). ⭐ **SKIP `PlatoonHillAttackBp`** — the behaviors lane retires it with its scenario `hill-attack-close-bp` (P9, design §13.5), so P4 converts **13** blueprints, not 14, and the Get Parameter graphs to migrate are 2 (`HsmGuardDemo`, `DangerCrossingBp`). ⚠ If you already touched `PlatoonHillAttackBp`, leave the change uncommitted and say so in the report; do not delete it yourself — the retirement re-points feature suites that use it (§13.5) |
| **P8** JSON enums | ⭐ ruled (R-2007): enums are STRINGS in every JSON. `FdpJsonOptionsRegistry.DefaultRelaxed`'s `StrictStringEnumConverter` is the rule, never relaxed; any data or code that sends an enum as a number is CONVERTED to the name. Report each one converted |

## 2. What is unchanged

Everything else in the handoff and steer 1: P1, P2, P3, the rest of P8 (fields, readers, the shared options for every params / order
JSON path, the ad-hoc options allow-list), the rest of P4 (the 13 conversions, Get All Parameters over the DTO's fields, deleting
`Role`, `StatefulSlotInfo.Role`, `StatefulSlotRole`).
