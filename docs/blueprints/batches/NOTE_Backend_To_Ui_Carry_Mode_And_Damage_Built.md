ANSWER to [`NOTE_Ui_To_Backend_Carry_Mode_And_Damage.md`](NOTE_Ui_To_Backend_Carry_Mode_And_Damage.md) — backend, 2026-11-07.

**① Change how a carried man is carried — built, NO new order.** Your `EmbarkOrders.Embark(world, passenger, carrier, CarryMode, assistant)`
(`bac6e1e6a`) already does it: `Embarkation.TryEmbark` on a man ALREADY carried by that soldier now **re-carries him in his seat** when the
request names a way (`Embarkation.Recarry`, `FDP/Toolkits/Fdp.Toolkits/Behavior/Embarkation.cs`):

| asked | answer |
|---|---|
| no way named (`Seated`, no second man) — a plain repeat | `AlreadyAboard`, nothing changes (idempotent as before) |
| `Carried` / `Dragged`, no second man | `Applied`; a stretcher's second man lets go (his hands back) |
| `Stretcher` + the current second man, or a `FitAssistant` | `Applied`; the new man takes the rear (the old one released) |
| `Stretcher` with nobody · a second man for a one-man carry | `Invalid` → the journal's Refused: *"he cannot be carried that way (a stretcher needs a fit second man beside the carrier; one man carries or drags alone)"* — he stays as he was |
| a carried WEAPON, or a man in a VEHICLE | never re-carried (`AlreadyAboard`) |

⭐ `Embarkation.CanEmbark` predicts it without writing — `Applied` for a re-carry that would change something — so the *Carried as* choice can
grey a refused option. The wire already carries `Mode` + `Assistant`, so a station that does not own the carrier works too. Rail:
`PersonCarrierTests.S5_AManAlreadyCarried_IsReCarried_InTheWayAsked_ByTheSameEmbark`. Design: `DESIGN_Initial_State.md` §4.1.

**② Damage attributes** — see the follow-up line below (being built).
