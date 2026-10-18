<!--STATUS
state: LIVE — backend → behaviors: one ruling asked (TC-7), and what a thrown blade is.
updated: 2026-10-18
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.17 B-17 (backend, this design) · §5h AW-15 / AW-19 (behaviors, armed people) · §5i (the blow)
-->
# Note — backend → behaviors: thrown cold weapons (Civilians §7.17)

**Decided by backend (leans TC-1…TC-6, for the user's approval):** a thrown knife, hatchet, javelin or throwing star is a **round**,
not a blow — it flies the hand's throw arc, can miss, stops on a wall, strikes the first body on its line, and lies where it lands to be
picked up again. It rides the stone's path unchanged: `PersonHands.Give(world, man, ammo, n)` puts it in his hands and `FireAtPoint`
on the hands index throws it. Rows: `CE-3247` (the four munitions and their looks), `CE-3248` (a hand throw is no longer a gunshot —
today a thrown stone makes a shot report). Your melee half of AW-19 (the fighting knife, baton, machete as items) is separate; throwing
your one fighting knife is later.

**⏳ One ruling asked — TC-7 (yours, AW-15 / AW-19):** a man holding a throwable is judged **lethal at any identified distance** today
(`Armament.OfHands` gives a held item no reach — a hand grenade reads the same). **Lean:** its lethal reach is the hand's throw range
(25 m), in AW-19's own words *"lethality is an item's reach."* What would change it: a doctrine that treats anyone holding a grenade as
lethal at any range.

**Demo idea for you:** `ua-armed-knife`'s thrower variant — a man throws knives at a checkpoint from 8 m.

**Ids allocated:** `CE-3247`, `CE-3248` (backend).
