<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-10-25
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/Architect_Question_104_Area_And_Building_Combat.md — ✅ R-2079: E (sound through walls / floors, door sounds), G (the lean's depth by skill), I (arrival on the target's storey)
  - docs/blueprints/Architect_Question_105_Squad_Callouts.md — ✅ R-2080: D (a shout is a voice the enemy hears)
  - docs/DESIGN_World_Query_Seam.md — OWNS WQ-E (sound through walls and round corners) — item 2 is its v1
  - docs/blueprints/Architect_Question_95_Sound_Signatures_And_Lossless_Hearing.md — OWNS the Band (B3) item 2 reads
-->

# HANDOFF — backend: four engine pieces the area / in-building fight and the squad callouts need

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). **Dispatched at `9c481363c`.** Your scope is FROZEN at that
sha — documents that change after it are FYI only; a later change that invalidates an item ⇒ stop and report. ⭐ The designs are the contract:
[AQ104](../Architect_Question_104_Area_And_Building_Combat.md) (approved R-2079) and [AQ105](../Architect_Question_105_Squad_Callouts.md)
(approved R-2080). Tracker: `CE-2371`, `CE-2372` (behaviors' block); number your own rows from yours.

| # | item | measured / why | acceptance |
|---|---|---|---|
| **1** | ⭐ **a person ARRIVES only on the destination's storey** (AQ104 I) | the person mover judges arrival in 2-D — `Vector2.Distance(pos2D, endXY) ≤ tol` (`CarKinematicsSystem.cs:752`, the 0.5 m straight-path rule `RoutePlanner.cs:101` too). 📐 Live (Peek and Fire §12.7): an attacker hunting a man at z 3 stood on the ground floor under him and was "arrived" for 160 s; the destination's Z reaches the mover intact (`NavigationDestination.cs:27`) | a MoveTo to an upper-floor point inside a building does not report Arrived while the mover stands on another level (`BuildingLayout.LevelAt` or the surface level he stands on); behaviors keeps its own storey check in `PostureNodes` as a belt (`CE-2371` I) |
| **2** | **sound through walls and floors, v1** (AQ104 E ②; WQ-E Q3) | hearing is a pure 3-D distance test (`AcousticPerception.cs:209-213`); the Band is "read by nothing yet" (`SignaturesDto.cs:80-81`) — footsteps behind a wall carry as in the open | per wall or slab the line from source to ear crosses, the audible range of a High-band sound (footsteps, voice) halves (or your WQ-E rule if it is ready); Low-band (engines, explosions) as WQ-E says |
| **3** | **door sounds** (AQ104 E ③) | no door makes a sound (`AcousticEmitter.cs:45`: Movement / Shot / Detonation only) | opening, closing and breaching a door (`DoorCommandSystem`) emit a short Movement-class sound at the door (breach louder) the hearing system answers like any other |
| **4** | **a VOICE sound** (AQ105 D) | a shout must cost something: an enemy within earshot hears it (his own side never — `AcousticPerception.cs:167` already) | a Voice signature class (~40 m, High band) emitted while a man's `Utterance` is a shouted kind (behaviors names which kinds when it writes them — `CE-2372`); heard as an anonymous contact like footsteps |
| **5** | **the lean's DEPTH** (AQ104 G) — FDP Combat, with the ui lane | `Lean.Shift` is one 0.4 m for everyone (`Lean.cs:42`) | `Lean` carries a per-man depth (e.g. a byte 0–255 of a max 0.6 m, default = today's 0.4 m), replicated; the eye, body points and hit boxes read it instead of the constant. Behaviors sets it from proficiency (Regular+ shallow ~0.25–0.3, Militia ~0.5); ui draws it (its own handoff) |

⭐ Report per item: the as-built in the owning design, the rail and its red-proof, and the sha. Questions ⇒ a NOTE to behaviors.
