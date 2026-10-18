<!--STATUS
state: LIVE — behaviors → backend: CE-2282 (DA-L, sectors) is built brain-local; two asks to make it work across nodes and precisely.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §13c DA-L in detail (DA-26…DA-30) and its as-built
  - docs/DESIGN_Entity_Control.md — §2.3a the tactical-intent road kinds 2 (ROE) and 3 (reporting) that ask ① copies
-->

# NOTE — behaviors → backend: sectors are built; a kind 4, the round's origin, and two roles asks

**On `behaviors`, in the commit that carries this note.** Row: [`CE-2282`](../Blueprint_Issues_Tracker.md).
Design: [Defend & Attack §13c](../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md).

**What landed (FYI, touches your combat files — said here):** a man's sector `EngagementArea` (component 417 — 416 went to your `Lean` the same hour; behaviors merged second and moved) set by
`SetEngagementAreaEvent` (event 3107, the `SetRoeEvent` twin) through `EngagementAreaSystem`; one reader `EngagementAreas`.
`Allow` filters what a man CHOOSES to shoot (posture fire step, `LockTarget`, the threat matrix, the Suppress order and step).
`NoFire` holds the round at the gates with a new reason `ActionReason.HoldNoFireArea` (appended last): `AimAndFireExecutor.HoldOf`
(aimed + turret), `FireBlind`, `FireAtPointExecutor.ShootAtPoint` (when `obeyRoe`), `HiddenTargetThrow.TryThrow`. ⚠ The IDL for
`ActionReason` regenerates on build — the wire enum gains one value at the end.

| ask | why | the shape |
|---|---|---|
| ① **a kind 4 on the tactical-intent road** for `SetEngagementAreaEvent` | ⛔ today a superior whose brain is on another node than the man's sets a sector with no effect — exactly CC-2 before your kind 3. `EngagementAreaSystem` already leaves a networked replica to its owner (`ForwardedCount`), as `ReportingRequirementsSystem` does | copy kind 3 (`ForwardReporting` / the ingress branch): the event's fields one for one; area ids are already NETWORK ids |
| ② **a commander's `EngagementArea` on his men's brain nodes** | `NoFire` is looked up through the man's chain (`UnitSubordinate.Commander`, ≤ 8) — one write by the platoon reaches every man. A man whose brain runs in another process than his platoon's does not see it | the same answer you gave for ROE (BrainIntentIngress writes the replica's `Roe`) — or tell me the cheaper road |
| ④ (`CE-2281` roles) **a fire-team index on the wire** — a `FireTeam` byte beside `TacticalDesignation` on `EntityInfo` | the brain node gets `UnitSubordinate` from the wire only (`UnitSubordinateTranslator` is not extraction-safe), so a scenario's team never reaches the plays that split by it ([Defend & Attack §12c](../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md)) | ⚠ a lean awaiting the user; I add the ECS field and the scenario key when you say the wire takes it |
| ⑤ (`CE-2281`) **succession's designation change is never on the wire** | `SquadSensing.Succession` writes the successor's `Designation` without marking `EntityInfo` dirty; and a second `CmdAssignSubordinate` for the same commander appends the man to the roster again (`UnitHierarchySystem.cs:117-122`) — so marking it dirty today would corrupt the other nodes' rosters | your file, your call on the order: the duplicate guard first, then the dirty mark |
| ③ (DA-14, already in your frame) **the round's origin on `NearMissEvent`** | self-defence (DA-28) lets a man answer a contact within 50 m while he was hit or near-missed in the last 5 s — any such contact, because nothing says who shot. With the origin it becomes "the one who shot at me" | `BallisticsSystem.cs:345-370` already has `proj.Shooter`; carrying its position (or the entity) is enough |

---
_behaviors lane, 2026-10-19_
