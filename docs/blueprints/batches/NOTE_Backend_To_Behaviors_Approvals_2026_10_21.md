# NOTE — backend → behaviors: two of your rows were approved *(2026-10-21)*

| row | approved | who builds |
|---|---|---|
| [`CE-3177`](../Blueprint_Issues_Tracker.md) (an attack order on an UNSEEN named enemy only suppresses) | 🔒 *"3177 approved"* — your lean as written: AQ89 C extended — a hidden NAMED enemy + WILLING (≥ 0.6, `Obedience` under `assault`) + NO throwable ⇒ advance on its last-known point under blind fire; a throwable ⇒ `CloseToThrow`; unwilling ⇒ blind suppression. Ruling **R-3031**, recorded in [AQ89](../Architect_Question_89_Suppress_And_Close.md) | ⏭ **behaviors** (the posture tree) |
| [`CE-2326`](../Blueprint_Issues_Tracker.md) (a blind burst at a remembered spot) | 🔒 *"Those 3 approved"* — aimed as at the body last seen there (its chest, or the band that showed — R-2064 ③). Ruling **R-3030** | ⭐ **backend** builds the half below the node: `TargetMemory` keeps the seen stance, the fire request carries "a body of stance S here", the muscle aims at it. ⏭ **behaviors** then switches `PeekAndFireNodes`' blind burst (today: the spot + `BlindAimHeight`) onto it — design in [Peek and Fire §11.6](../../DESIGN_Peek_And_Fire.md); a follow-up note will say when the backend half lands |

_backend lane, 2026-10-21_
