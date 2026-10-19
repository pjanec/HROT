# NOTE — backend → behaviors: the muscle now judges the shot; `Masked` arrives as a refusal *(2026-10-19)*

**What changed** ([`CE-3269`](../Blueprint_Issues_Tracker.md) (Q94 A — the muscle judges the shot, the brain's ammo follows its answer), R-3018;
design: [Q94 §4.1](../Architect_Question_94_Brain_Geometry_Sites.md)):

| before | now |
|---|---|
| `AimAndFireExecutor` checked `LineOfFire.MuzzleMasked` itself and held `Masked` BEFORE asking | it asks; `FireProcessingSystem` (the muscle, live terrain) REFUSES a masked direct round — at a unit or at a point — and the refusal comes back in the `WeaponFireNotification` (`Refusal`, `RequestId`) |
| a round was spent when the brain raised the intent | the round is RESERVED at the ask and SPENT when the muscle's answer says it flew; a refusal spends nothing |
| — | after a refusal the weapon HOLDS 1 s with the muscle's reason — `ActionStatus.Weapon.Reason` = `Masked` (or `TooClose`, `OutOfRange`, `AwaitingRelease`, `NotDeployed`), Running, no new ask |

**What it means for behaviours — the one timing difference:**

- `Masked` is still the reason a behaviour reads (`PeekAndFireNodes.MaskedSince`, `VehicleFightFromCover`'s burn-the-firing-point) —
  unchanged words, unchanged hold semantics.
- ⚠ It now arrives **one or two frames after the first ask**, not before it. A **one-round action** (`Rounds = 1`, an RPG) has already
  returned **Success** by the time the refusal lands; the NEXT fire action on that weapon then holds `Masked` for the 1 s refusal hold.
  So a behaviour that reads `Masked` only while the action runs may need one more ask to see it. Measured: the SimHost
  `CE2218_*` window-burn rails and the six cross-node twins (operator fire, window duel, U6 squad fire, heard shot, CE2073, CE1078) are
  green, so nothing shipped regressed — this is a heads-up for new plays, not a defect.
- The action's round count still counts ASKS; a refused round is not given back to a running burst.

**Nothing to do unless** a play needs the refusal inside a one-round action — then ask, and backend can make the executor stay Running
until the last ask is answered.
