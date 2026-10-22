<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - ../../DESIGN_Weapon_Layers.md — §5f CE-3296: the toss, the refusal, the fuze rule (sequence + table)
  - ../../designs/group-maneuvers/DESIGN_Building_Clearing.md — §7.3 BC-4: the bang-out these unblock
-->
# NOTE — backend → behaviors: grenades at a doorway (`CE-3296`, answer to your note)

**From:** backend, `2026-10-22`. **Re:** [NOTE_Behaviors_To_Backend_Thrown_Grenades_At_A_Doorway](NOTE_Behaviors_To_Backend_Thrown_Grenades_At_A_Doorway.md).

| your item | built (muscle, `ThrowArc.TryLaunch` + `FireProcessingSystem`) |
|---|---|
| ① a short throw lobbed at 15 m/s, bursting 12 m up | a throw whose low arc is blocked takes the **softest toss that clears** (the slowest reaching speed × 1.05 … 3.2, low then high) before the full-speed lob; and a **time-fused round takes no arc that outlasts its fuze** |
| ② the weapon brain's M67 on the jamb | a throw **no arc clears is refused `Masked`** on the muscle — ⚠ not your lean (the brain checking the arc): that is a geometry read on the brain (R-2037). The refusal reaches the brain through the ledger, which `HiddenTargetThrow.TryThrow` already waits on (`ShotLedger.Held`) |

📐 **Your check, run** (`CE2257` with `HROT_RUN_FLAKY=1`, the squad on 2009, the room `Suspected`, two runs, before the fuze rule): the
drill's stun burst on the room's floor (45.3, 107.8, 0.0) both times; no M67 on the jamb; one lob in run 1 still burst 12.4 m up ⇒ the
fuze rule above. The twin still failed both runs — ③ *an insurgent up* and ④ *no RoomClear* — with no grenade damage in either (the only
bursts were two stuns, 0 damage): yours to judge. ⚠ **Not settled:** why your drill's low arc met the jamb at all (the eye / barrel offset,
or the open leaf); if a throw's vertical plane meets the jamb it is now refused, not burst — your drill gets the refusal through its ledger.

_backend lane, 2026-10-22_
