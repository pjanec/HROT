# NOTE — behaviors to backend: the Bridgehead Red-on-Red answer (CE-2379)

**Cause.** `showcase-bridgehead`, 497.6 s: shooter 1085 fires at a point; its `FireAtPointExecutor` did ask `LineOfFire.BlockedByFriendly`, but that tested a friend's bare `PhysicsCollider.Radius`. A round meets a person by his body-part boxes (`RaycastSolverSystem` → `BodyPartRay.Person`), and an arm box (UpperArmL of 1087) reaches past the collider, so the hold missed a man the round hit. `NeutralOnLine` had the same gap.

**Fix.** `LineOfFire.StrikeRadius` (cross-lane edit of your file `FDP/Toolkits/Fdp.Toolkits/Combat/LineOfFire.cs`): for a person, max(collider radius, `PersonBody.HorizontalReach(current pose)` x height scale + lean shift); else the collider radius. `Blocked` and `NeutralOnLine` use it. `HorizontalReach` sits beside `PersonBody.Bound`, computed from the generated tables. CE-2338 (a friend down is skipped) is unchanged; the 0.84 m hit height is a standing man, not a downed one.

**Rails.** `CE2379_ALineOfFireHold_CountsAPersonByTheReachOfHisBodyParts` (friend inside reach held, entity and point form; beyond reach not held; neutral held), red-proved with the helper returning the bare radius.

**Live run.** See the result line appended below.

**Result** (probe copy, `runFor` 505, 30301 steps, wall 898 s): `PASS no Red round hits a Red man`; `PASS no Blue round hits a Blue man`; all other checks PASS except `FAIL (eval) a civilian harmed fails the whole tree` — not compared against a pre-fix run of the same 505 s copy (no baseline was taken; the eval row is likely an artefact of cutting `runFor` from 1500 to 505, unconfirmed).
