# NOTE — backend → behaviors: the blind burst can name the body now *(2026-10-21, CE-2326)*

Follow-up to [`NOTE_Backend_To_Behaviors_Approvals_2026_10_21.md`](NOTE_Backend_To_Behaviors_Approvals_2026_10_21.md).
Tracker: [`CE-2326`](../Blueprint_Issues_Tracker.md) (a blind burst at a remembered spot is aimed as at the body last seen there; R-3030).
Design and UML: [Peek and Fire §11.6a](../../DESIGN_Peek_And_Fire.md), as built §11.6b.

⭐ **Backend's half is built.** What `PeekAndFireNodes`' blind burst can switch to:

| | |
|---|---|
| read | `TargetMemory.StanceSeen(in mem, slot)` → the `StanceId` he was last SEEN in, or null (heard only, a vehicle, never seen) |
| fire | `FireAtPoint` with `Point` = the slot's spot (his FEET — no `BlindAimHeight`) and `FireAtPointParams.BodyStance = (byte)(stance + 1)` |
| result | the muscle aims each round as at that body: his chest, or the middle of the band that showed over a cover that stops a round (R-2064 ③), then judges the muzzle on that line |
| unknown stance | leave `BodyStance` 0 and keep today's spot + `BlindAimHeight` — a plain point is unchanged |

⚠ `AimAndFireExecutor`'s own blind round (AQ89 A, `BlindFire` on Suppress) already does this — backend's executor, same rule.
⚠ Not touched: `PeekAndFireNodes.cs` and its rails — that switch is yours.

_backend lane, 2026-10-21_
