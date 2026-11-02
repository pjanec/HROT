# NOTE — behaviors to backend: the Bridgehead Red-on-Red answer (CE-2379)

**Cause.** `showcase-bridgehead`, 497.6 s: shooter 1085 fires at a point; its `FireAtPointExecutor` did ask `LineOfFire.BlockedByFriendly`, but that tested a friend's bare `PhysicsCollider.Radius`. A round meets a person by his body-part boxes (`RaycastSolverSystem` → `BodyPartRay.Person`), and an arm box (UpperArmL of 1087) reaches past the collider, so the hold missed a man the round hit. `NeutralOnLine` had the same gap.

**Fix.** `LineOfFire.StrikeRadius` (cross-lane edit of your file `FDP/Toolkits/Fdp.Toolkits/Combat/LineOfFire.cs`): for a person, max(collider radius, `PersonBody.HorizontalReach(current pose)` x height scale + lean shift); else the collider radius. `Blocked` and `NeutralOnLine` use it. `HorizontalReach` sits beside `PersonBody.Bound`, computed from the generated tables. CE-2338 (a friend down is skipped) is unchanged; the 0.84 m hit height is a standing man, not a downed one.

**Rails.** `CE2379_ALineOfFireHold_CountsAPersonByTheReachOfHisBodyParts` (friend inside reach held, entity and point form; beyond reach not held; neutral held), red-proved with the helper returning the bare radius.

**Live run.** See the result line appended below.

**Result** (probe copy, `runFor` 505, 30301 steps, wall 898 s): `PASS no Red round hits a Red man`; `PASS no Blue round hits a Blue man`; all other checks PASS except `FAIL (eval) a civilian harmed fails the whole tree` — ⭐ **measured against the pre-fix run of the SAME 505 s copy: that row is a CONSEQUENCE of the fix, not of the shorter run.** Before: the eval row PASSED at 282.5 s (a villager harmed ~270 s, as in your 1500 s run). After: **no civilian is harmed at all** ("No harm to civilians" stays Active) and Blue clears the crossroads at 260 s instead of 308 s — the villager's harm was the same gap in the NEUTRAL hold (an arm outside the collider), which the fix closes.
⇒ ⚠ **your check `(eval) a civilian harmed fails the whole tree` relied on an accidental harm.** It is your scenario content, so I have not touched it. Lean: prove the line deterministically instead — an inject that harms a civilian on purpose (e.g. a trigger at a fixed time that damages one villager, or a rail), or make the row conditional (`if a civilian harmed ⇒ the tree is Failed`).
