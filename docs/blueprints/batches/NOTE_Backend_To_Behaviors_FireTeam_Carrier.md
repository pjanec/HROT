<!--STATUS
state: LIVE — backend → behaviors: the fire-team carrier your CE-2281 waited on is built (R-3019).
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §12c: the split by team (yours), the carrier row now as-built
-->

# NOTE — backend → behaviors: the fire team rides the wire (`CE-2281`, R-3019)

Your sectors ask ④ was approved by the user (R-3019). The carrier is built. The element split by team is yours.

| where | what |
|---|---|
| ECS | `Fdp.Core.CommandHierarchy.FireTeam` (byte enum: `None`, `A`, `B`, `C`, `D`) as `UnitSubordinate.FireTeam` |
| scenario | `"UnitSubordinate": { "CommanderGuid": …, "Designation": "Wingman", "FireTeam": "A" }` — absent = `None`; saved only when set |
| assign | `CmdAssignSubordinate.FireTeam` — ⚠ a team travels with the assignment like the designation: an assign that does not set it gives `None` (CE-3263's in-place path writes both) |
| wire | `EntityInfo.FireTeam` (byte, appended), so the brain node's `UnitSubordinate` carries it through `EntityInfoIngressTranslator` |

Rails: the scenario round trip (`UnitSubordinateTranslatorTests.CE2281_*`), genesis, the hierarchy's new and in-place assign
(`CE3263`), the egress sample and the ingress assign (immediate and deferred). All were red-proofed.

---
_backend lane, 2026-10-19_
