<!--STATUS
state: LIVE — behaviors → backend: an AMENDMENT note to a dispatched frame (rule 1: the frame itself is not edited).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h OWNS it: AW-15…AW-20 (R-2026) and the redrawn decision graph and insurgent sequence.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — the frame this amends (B-7…B-14).
-->

# NOTE — behaviors → backend: the armed-people frame, amended *(R-2026)*

The same day the [frame](HANDOFF_Behaviors_To_Backend_Armed_People.md) went out, the user approved a critical review of the design against
real incidents (*"approved, fold 1-6 into the design with your leans"*, **R-2026**, [Civilians §5h](../../DESIGN_Civilians_And_Crowd_Control.md)
AW-15…AW-20). ⚠ If you had already started B-7, stop that item and read this; the others go on (R-106).

| item | what changed | what is unchanged |
|---|---|---|
| **B-7 — AMENDED** | `Armament` judges **per observer**, by what he can IDENTIFY: `Judge(view, observer, man)` ⇒ identified lethal · unidentified · nothing. Every item gets an **`Appearance`** in its TKB data (looks like Rifle / Pistol / LongObject / SmallObject / None, a size, its lethality and REACH); the identification range comes from size × light × optics (binoculars, night sights). A toy gun looks like a rifle. A knife is lethal within its reach; a Molotov is lethal (AW-15, AW-19) | non-lethal only and a Hidden station still arm nobody; the 27 readers still route through one function — ⭐ readers that are not an observer (the figure, "can I fire") keep a truth form (`LethalInHand`) |
| **B-8 … B-14** | — | unchanged; B-12's pocket items and B-11's types also carry an `Appearance` |
| *(not yours)* | the intent rows, self-defence over the force level and the neutral-in-line hold are behaviors' (`CE-2239`); the hostile act moving from the draw to the aim / fire / attack is behaviors' (`CE-2238`) | — |

**Acceptance for amended B-7:** a man holding a pistol is "identified lethal" to an observer at 20 m and "unidentified" to one at 200 m at
night; with binoculars the 200 m observer identifies it by day; a phone at 150 m is "unidentified", never lethal; a toy gun at 20 m is
"identified lethal".
