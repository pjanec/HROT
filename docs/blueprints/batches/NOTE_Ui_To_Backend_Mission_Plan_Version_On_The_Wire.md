<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/UX/DESIGN_Mission_Plan_Editor.md — §5.11 P-7 7c (D9: Live = Commit with the base version, Conflict → Reload / Overwrite)
-->

# NOTE — ui → backend: the mission plan's VERSION is not on the wire (a replica cannot detect a conflict)

**Measured (ui, P-7 7c, `2026-11-07`):**

| | where |
|---|---|
| the version is `MissionPlanQueue.Version`, bumped only by the brain's owner on an applied commit | `MissionControlExecutionSystem.cs:304-311` (`VersionOf` / `BumpVersion`) |
| a commit's precondition: `BaseVersion > 0 && BaseVersion != current` ⇒ CONFLICT; `0` = no precondition | `MissionControlExecutionSystem.cs:190` |
| the mission descriptor carries the task states, the current task and halted — **no version** | `MissionProgressWire.Decode` (`Hrot.Network.NED/Replication/Map/MissionProgressWire.cs:41`) |

⇒ on a REPLICA (the editor in Live, watching units whose brains CGF owns) the version stays 0, so its commits carry base 0 — no false
conflicts, but **no detection either**: a Live edit from the editor still overwrites another station's edit silently.

**ui fixed its half (this commit):** the mission facade's snapshot reported a constant 0 even on the OWNER — it now reads `VersionOf`, so the
owner's stations detect a conflict and the Orders tab offers Reload / Overwrite. (Also fixed: the snapshot dropped every task's triggers, so an
Apply from the Orders tab wiped the *then* / *give up* conditions.)

**Asked of backend:** carry `Version` in the mission descriptor and restore it in `MissionProgressWire.Decode`, so a replica's
`MissionPlanQueue.Version` follows the owner's. ⭐ Lean: a field on the existing descriptor (one DDS schema bump), not a new topic. Nothing in ui
changes — the snapshot already reads the queue's version.

---
_ui lane, 2026-11-07_
