# Note: a REMOTE player who chooses Drive sits in the driver seat — and never drives (the hand-over runs only in HROT's local station)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-23 |
| **For** | `ui` (owner of `PossessionStation` and the possession flow, [DESIGN_Entity_Control](../../DESIGN_Entity_Control.md) §8h); FYI the SimNetGateway session (DPS `PlayerSlot` / `OwnUnit`) |
| **User, verbatim** | *"when I go to a vehicle which is empty, no one in there, the first action should be taking a driver's seat … I would like to drive the vehicle. And control its weapon if there is no other person. Then the driver needs to play also the one who controls the weapons, and I guess this has been already working on HROT, but please mention that in the note."* · *"Every piece of logic needs to be handled by HROT, so the client just sent what action was selected and the rest must happen from the HROT."* |
| **Asks** | §3 — the drive hand-over for every station (local and remote) in HROT's shared possession layer; the user's lone-driver-gunner requirement; the offered-verb list on the wire |

## 1. Measured LIVE (scripted, `scripts/unreal/hrot_drive_check.py`; HROT `ui` `a36176252`, gateway `e784e38`, `showcases/player-station`)

The Unreal client possessed **Rifleman 1** (1001) over DPS, walked him to **HMMWV (empty)** (1013), put the crosshair on its body and tapped use
(`ControlFrame.UseSeq`+1, `Verb` None = the primary action, `UseTarget` 1013), then held W 4 s.

| step | the facts (client log of `OwnUnit` / `PersonState`; HROT `GET /entities/{id}`) |
|---|---|
| offered | `OwnUnit`: CanUse, target 1013, Verb **Drive**, label *"Drive HMMWV (M2, no crew)"* — ✅ HROT's first action for an empty vehicle is Drive (`OperatorUse.Vehicle`, `OperatorUse.cs:266-286`) |
| use | `UseOutcome` Running → **Done**; `PersonState`: 1001 → vehicle 1013, seat 5, role **Driver** ✅ |
| after | `PlayerSlot` still plays **1001**; `OwnUnit` now offers *"Get out"* |
| W held 4 s | the HMMWV did **not** move (HROT's `SimTransform` of 1013 unchanged) — the control frames still go to the seated soldier |

## 2. Why — the hand-over lives in the local station's presentation code

After a Drive press HROT's OWN player screen runs three steps (`PossessionStation.ArmDrive` / `StepHandOver`, `PossessionStation.cs:305-349`,
CE-1158): ① wait until the soldier sits in the driver seat · ② `Possess(soldier, Riding)` (his task stays paused, no frame reaches him) ·
③ `Possess(vehicle, Fps)` — the station now drives the VEHICLE. These run in `Hrot.Presentation` for the local station only; the DPS seat has
no equivalent (searched the gateway's `Plugin.DdsPlayer` and `HrotHost`: nothing). ⇒ a remote player gets ① and never ② ③.

## 3. Asks (ours to discuss with the user; our lean first)

| # | ask | our lean |
|---|---|---|
| ① | **the drive hand-over for every station** — the same ② ③ for a DPS seat | ⭐ move the sequence out of `PossessionStation` into the shared possession layer that every station passes through (so a remote seat gets it by construction, not by a second copy in the gateway or in each client); `PlayerSlot.UnitId` then reports the vehicle, the client follows it (that part is ours: today the client drops to free flight when the slot's unit changes — we fix it regardless). Get-out (`TryGetOut`) is the same story in reverse |
| ② | **the lone driver works the weapon** — the user's requirement: with nobody else aboard, the driver also controls the vehicle's weapon (the M2 here) | does HROT's local station do this already? If so, the same must hold for the remote seat after ①; if not, a decision for you and the user |
| ③ | **the offered-verb list on the wire** — `OwnUnit` carries only the FIRST action; HROT's own screen opens the full list on a held F (Board vs Drive, Man, Take …) | append the list (verb, label, available, reason) to `OwnUnit` (or a sibling topic), so a client shows HROT's list and sends the chosen `Verb` — the choice stays a player input, the meaning HROT's |

The guide already says "next seat (G)" is not carried by DPS yet — it belongs to the same seat-switching family as ②.

## 4. On the Unreal side (ours, no HROT dependency)

Follow a change of `PlayerSlot.UnitId` while playing (vehicle controls when the unit becomes a vehicle) instead of dropping to free flight;
the hold-F list once ③ exists. The check script reruns this measurement in ~4 minutes.
