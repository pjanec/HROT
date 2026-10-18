<!--STATUS
state: LIVE — backend → behaviors: one ruling asked (TC-7), and what a thrown blade is.
updated: 2026-10-18
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.17 B-17 (backend, this design) · §5h AW-15 / AW-19 (behaviors, armed people) · §5i (the blow)
-->
# Note — backend → behaviors: thrown cold weapons (Civilians §7.17)

**Approved by the user `2026-10-18` (R-3016):** a thrown hatchet, javelin or throwing star is a **round**,
not a blow — it flies the hand's throw arc, can miss, stops on a wall, strikes the first body on its line, and lies where it lands to be
picked up again. It rides the stone's path unchanged: `PersonHands.Give(world, man, ammo, n)` puts it in his hands and `FireAtPoint`
on the hands index throws it. Rows: `CE-3247` (the three munitions and their looks), `CE-3248` (a hand throw is no longer a gunshot —
today a thrown stone makes a shot report). ⛔ **A knife is never thrown** — user: *"Knife is usually not thrown unless by a circus artist."* It stays the melee item of
your AW-19 (with the baton and machete).

**⏳ One ruling asked — TC-7 (yours, AW-15 / AW-19):** a man holding a throwable is judged **lethal at any identified distance** today
(`Armament.OfHands` gives a held item no reach — a hand grenade reads the same). The user: *"Lethality depends also on what the man is
holding in hands."* ⇒ **Lean: the lethal reach is the HELD ITEM's** — a grenade or a javelin out to the hand's throw range (25 m), a
throwing star to its short range, a stone never lethal (as today), a knife within melee reach only — data on the item (`ReachM`), read by
the existing `Armament.OfItem` rule. What would change it: a doctrine that treats anyone holding a grenade as lethal at any range.

**Demo idea for you:** a rioter throws a hatchet at the police line from 10 m; a second picks it up and throws it back.

**Ids allocated:** `CE-3247`, `CE-3248` (backend).
