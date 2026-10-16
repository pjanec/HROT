<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file (a frame — the design carries the substance)
stale-below: nothing
related-designs:
  - docs/DESIGN_Entity_Control.md — §8h, the owning design (U-1..U-7, O-1..O-6, items §8h.4, the verdict path §8h.6)
  - docs/DESIGN_Entity_Interactions.md — the interaction executors (doors, carry, embark) and their params
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — §6, WS-6: the Gunner seat and controlled mounts (32+k)
  - docs/blueprints/batches/HANDOFF_Use_Key_To_Backend.md — the backend half this one builds on
-->

# Handoff: the FPS use key, behaviors half (`CE-1154`, `CE-1155`, `CE-1157`)

**Dispatched at `2dfec7a37` (branch `ui`).** 🔒 User, `2026-10-16`: *"pls records into items and write handoff to the backend node and
behavior node to implement"*. **Your scope is FROZEN at `2dfec7a37`.** Documents that change after it are FYI only; if a later document
invalidates an item, stop and report it. ⛔ Do not adapt to it.

**First step:** merge `ui` at or after `2dfec7a37`, **and `backend` once it has pushed CE-1151 / CE-1152 / CE-1153** (CE-1156 for
CE-1157). The backend lane will name the sha. Then push a `chore: started use-key at <sha>` commit (rule 1b).

⚠ **Dependency:** CE-1154 needs the frame fields, the rule and the verdict component from backend. Until they land, only the measurement in
CE-1155 can start.

## What it is

In FPS mode, `E` acts on what the crosshair rests on. The station sends `UseSeq` / `UseVerb` / `UseTarget` in the control frame. The
possessed soldier's **brain** asks the shared rule `OperatorUseActions.For` (backend CE-1152) what is possible, puts the chosen action on
its `InteractionChannel`, and reports the result in `OperatorUseStatus` (backend CE-1153). Read
[`DESIGN_Entity_Control.md`](../../DESIGN_Entity_Control.md) §8h end to end, especially the door sequence, the driving sequence and §8h.6.

## Your items: build against §8h's diagrams, report any deviation, and fold it back into §8h (obligation ⑤)

| id | build | design | feature suite to extend first (T-1) |
|---|---|---|---|
| **`CE-1154`** | `OperatorControlBehavior.OperateUse` (`Hrot.AI.Behaviors/Brains/OperatorControlBehavior.cs`), next to `OperateWeapon`, same shape: a counter delta (`UseSeq`) starts the chosen verb's action on the `InteractionChannel` with its params, as `OperateWeapon` writes the weapon channel. It covers doors (ids 4–8, `OpenDoorParams`) and bodies (pick up 9 Dragged / Carried, put down 10; `E` again = put down). It writes `OperatorUseStatus`: Running on start, Done / Refused + Reason at the end. A stale frame (`Seq` going back) resets the counter exactly as for `ShotSeq` (`:141`). | §8h U-1, U-2, U-6, U-7, door sequence | the operator suites around `OperatorControlBehavior`, `CgfLogicPackTests`, `EntityOrdersAcrossNodesTests` |
| **`CE-1155`** | Standalone weapon: `E` = embark into its Gunner seat (embark 1, role Gunner). Possession stays on the man, and his weapon keys reach the controlled mount through `AimAndFireExecutor`'s operator path (index 32+k: `WeaponSystems.IsControlledIndex`, `ControlledAt`). ⚠ **Measure first** that a POSSESSED man (operator behaviour, not `ManWeapon`) fires a controlled mount end to end; if not, that gap is the item. | §8h U-5; Weapon Systems §6, WS-6 | `AimAndFireExecutorTests`, `WeaponMountTests`, `EmbarkExecutorTests` |
| **`CE-1157`** | Driving: `E` on a vehicle = embark as Driver (Pilot on a flyer). While the soldier is **Riding** (`PossessionMode.Riding`, backend CE-1156) the operator brain holds still and stays aboard. Get out = disembark (2), sent with a frame after the station re-possesses him. | §8h U-4, O-2, O-3, driving sequence | `EmbarkExecutorTests`, `PassengerReplicationTests`, `EmbarkNodesTests` |

## Key decisions, made with the user. Do not reopen them; report if a premise fails.

- ⭐ **The brain decides with the SAME rule the station uses for the prompt** (`OperatorUseActions`). No second copy of "what can I do here"
  in the behaviour.
- ⭐ **Out of reach ⇒ the executor refuses; no walk-to** (U-3). The operator's body is the human's.
- ⭐ **The brain never moves possession.** On Drive the **station** swaps possession after the boarding shows in replicated state, because
  the soldier and the vehicle may be brained on two different nodes (§8h driving-sequence caption). Your part ends at "the soldier is aboard".
- ⭐ **Crew stays abstract** (B5): the vehicle's brain drives. Separate human driver and gunner wait (O-5). Stretchers and two-man weapons
  for a human wait (O-6).
- ⭐ The carrier can't shoot while carrying (C7). The executors and `Embarkation` already enforce it; the HUD says the rifle is slung
  (ui, CE-1158).

## Fences

- ⛔ Don't change the frame or wire fields, the rule, or the verdict component: they are backend's (CE-1151…1153, CE-1156). If one is missing
  something, report it to backend.
- ⛔ `PossessionStation`, `FpsCameraDriver`, `EntityOrders` and the HUD belong to the ui lane (CE-1158).
- ⭐ Any cross-lane edit is allowed but must be stated in the commit and report.

## Acceptance

- [ ] CE-1154: one `E` press opens a closed door within 2 m, and a second press closes it. Out of reach gives `OperatorUseStatus` Refused
      with reason TooFar, and the door is unchanged. `E` on a man who is down drags him; `E` again puts him down. Each is a rail; one is
      also shown across nodes (`EntityOrdersAcrossNodesTests`).
- [ ] CE-1155: a possessed man in a ZU-23's Gunner seat fires it with his weapon key (rail), or the measured gap is reported.
- [ ] CE-1157: `E` on a BMP seats the soldier as Driver. While he is Riding, his paused task doesn't resume and he stays aboard. Get out
      disembarks him.
- [ ] Red-proof each new rail.
- [ ] Gate report per CLAUDE.md "THE GATE REPORT CONTRACT", including **row 8** (the cross-node suite).
- [ ] §8h folded with the as-built, prior state marked superseded. Tracker rows moved, ids stated.
- [ ] Pushed to `behaviors`. Tell the ui lane the sha: CE-1158 (station, HUD, hand-over) is built against it.
