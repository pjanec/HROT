<!--STATUS
state: LIVE — behaviors → backend: a NOTE (an enum B-16 should reuse, and why it exists before B-16).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h.6 OWNS the known combatant (behaviors); §7.16 OWNS B-16, the replicated HostileAct (backend).
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_Armed_People_Combatants.md — the note that framed B-15 / B-16.
-->

# NOTE — behaviors → backend: `HostileActKind` exists now; B-16 should reuse it *(CE-2238 part 3, CE-3238)*

**What landed (behaviors, design [§5h.6](../../DESIGN_Civilians_And_Crowd_Control.md)):** AW-22's knowledge half. A civilian's LETHAL act makes
him a known combatant to the units that witnessed it and their squad (`Fdp.Toolkit.Combat.KnownCombatants`, a sticky list in
`TargetMemory`); `Armament.Judge` then reads him on what he carries. It is brain-local and needs nothing from B-16.

**What that put in your area:**

| file | change |
|---|---|
| `Civilians/HostileActs.cs` | ⭐ `public enum HostileActKind : byte { None = 0, NonLethal = 1, Lethal = 2 }` — the enum your §7.16 draws (HA-1); `Commit(repo, actor, kind)` now takes it with NO default (the stone and the blow pass `NonLethal`); a `Lethal` act calls `KnownCombatants.MarkWitnesses` every time — HA-2's upgrade |
| `Perception/Components/PerceptionComponents.cs` | `TargetMemory` grows `KnownCombatantIds[8]` + `KnownCombatantCount` (brain-local, never on the wire; the scenario translator is keyed, so saved files are unchanged) |
| `Combat/Armament.cs` | `Judge`: not a combatant by status but known to THIS observer ⇒ `LethalInHand` ⇒ IdentifiedLethal / Nothing, before `Conduct` (AW-24's counter does not move for him) |

**Asked of you, only when you build B-16:** ⭐ give `HostileAct.Kind` the type `HostileActKind` from `HostileActs.cs` (do not declare a
second enum), and stamp it inside `HostileActs.Commit` — it already has the kind in hand. Nothing else changes for you.

**Ids:** none allocated (rows `CE-2238`, `CE-3238` mentioned).
