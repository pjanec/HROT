<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-14); scope frozen there.
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: STEERED 2026-10-14 (before any item was built) by STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets.md — WS-1 StartWith on any seat, WS-4 SeatPresets seat the crew (104 / 105 get a gunner), WS-3 + one rail. STEERED again 2026-10-14 by STEER_Behaviors_To_Backend_Team_Carry.md — adds slice T-a, built with WS-2. The items below are as dispatched; the steers win where they differ.
related-designs:
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — THE design (module picture §2, classes §3, sequences §4, TKB §5, rules §6, decisions §7, slices + rails §8)
  - docs/blueprints/batches/STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets.md — the W-I steer (presets seat their crew)
  - docs/blueprints/batches/STEER_Behaviors_To_Backend_Team_Carry.md — the second steer: slice T-a (team carry — a stretcher, a two-man weapon), with WS-2
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Weapon_Systems.md — the ui half (drawings, placing a weapon into a host station)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Air_Operations.md — the air-operations handoff; its E3 / E4 come FIRST
-->

# HANDOFF — backend: weapon systems (separable weapons as entities, stations, deploy, one controller)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**:
your scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report
it**; do every item that is not blocked (R-106).

## 0. Why

🔒 User, `2026-10-14` (R-2016): HMGs, miniguns and the missile launchers (Javelin, NLAW, LAW) can be placed on the ground as entities, mounted
on a pickup or a helicopter door, carried by a soldier, deployed or stowed, and manned. *"Automatic turret is required. This is no different
from a tank or something, Entity having a weapon."* · *"the host's gunner seat operates the weapon."* ⭐ **Build against
[DESIGN_Weapon_Systems.md](../../designs/weapons/DESIGN_Weapon_Systems.md)**: its §3 class diagram and §4 sequences are the contract, §6 the rules.
Report any deviation and fold it into the design (CLAUDE.md obligation ⑤).

⏳ **Order with your queue:** finish the air-operations handoff's **E3** (helicopter) and **E4** (plane + bomb) first. This batch starts after them.

## 1. Before anything

1. Merge `behaviors` at the dispatch commit or later. Push a `chore: started …` marker (rule 1b).
2. Ids: new findings from **your** block (`CE-3xxx`); progress on our row **CE-2174** as appended dated lines (append, never insert above).

## 2. The items — in this order

| # | item | design | acceptance |
|---|---|---|---|
| **WS-1** | **the data**: the `Gen.WeaponSystem` descriptor (`WeaponSystemDto`: `NeedsOperator`, `Portable`, `CarrySpeedFactor`, `FireWhileCarried`, `DeploySeconds`, `PackSeconds`, `MountKinds`) + its translator · `SeatRole.Station` and the APPENDED seat fields (`Accepts`, `OperatedBy`, `NeedsGunner`, optional arc limits, `StartWith`) · the class `WeaponSystem` (Kind 1, Domain 1, **Category 50**, subcategories HMG · Minigun · Atgm · Launcher · Sentry) in `EntityClassTable` · `WeaponSystemState` (`Stowed` / `Deploying` / `Deployed` / `Packing`, `Until`) + `dtWeaponSystemState` (owner-written, R-136) · `WeaponDeploySystem` on the owner | §3, §5, §6 W-C | `NedTkbBuilder…` round-trip of the descriptor and the seat fields; `CarrierTkbTranslatorTests` (station seats); a new `WeaponDeploySystemTests` (Deploying ⇒ Deployed after `DeploySeconds`, Packing ⇒ Stowed); the state's translator round-trip |
| **WS-2** | **boarding a weapon** (W-B): `Embarkation` accepts a weapon system into a `Station` seat whose `Accepts` fits its `MountKinds`; the person-carrier rule widens from "a man who is down" to "or a portable weapon system" (the carry station); a soldier boards a weapon's `Gunner` seat · `EmbarkedStateSystem` puts a station weapon AT its seat's place and facing (today every passenger stands at the carrier's reference point, `EmbarkedStateSystem.cs:115-121`) | §6 rows 6–7 | `EmbarkationTests`: an HMG into a pickup's bed station; a Javelin onto a soldier; a non-portable weapon refused onto a soldier; a pintle-only weapon refused from a door station · `EmbarkedStateSystemTests`: the HMG stands at the station's place and facing as the pickup drives and turns |
| **WS-3** | **one controller** (W-D, W-E, W-H): `WeaponSystems.IsWeaponSystem` / `ControllerOf` / `CanFire` / `Controlled` (Fdp.Toolkits Combat) · `WeaponMountQuery.EnumerateMounts` = own mounts + the weapon systems the unit controls · the turret path on the WEAPON's own turret part (`TurretParts.Find(weapon, 0)`) and the muzzle from the weapon entity · a weapon's own brain fires only while it controls itself | §6 rows 1–4, §4a–c | `WeaponMountTests` (the host lists its station weapon; a manned tripod's soldier lists it; an automatic unmanned one lists itself; a manual unmanned one lists nobody) · `AimAndFireExecutorTests.WS_ThePickupFiresItsStationHmg_OnTheHmgsTurret` · `WS_AnAutomaticTurret_FiresOnItsOwn_AManualOneDoesNot` · `WS_AStationWeapon_WithAnEmptyGunnerSeat_DoesNotFire` |
| **WS-4** | **the catalogue** (W-F): the weapon types (M2, DShK, M134, Javelin, NLAW, LAW, Kornet on a tripod, TOW on a tripod, a sentry gun with a sensor and `WithBehavior`) · a bare Toyota pickup (driver, gunner, one bed station `OperatedBy` the gunner) · the doors-off UH-60 (404) gains a left and a right door station, each operated by a door-gunner seat · `StationPresets` (a placed host fills its `StartWith` stations ONCE on the creating node, R-138; then marked filled, saved) · presets **104** (pickup + M2), **105** (pickup + Kornet), **2006** (soldier + Javelin), **2007** (soldier + NLAW) | §5, §6 row 9 | `UrbanCombatCatalogRails` / `NedTkbBuilderCombatTests` for the new types; ⭐ the existing technical rails stay green through the presets: `M9_TheTechnicals_APickupWithAPintle_ShootAndScoot`, `V8_AnHmg_FiresAtASeenDrone_ATankGun_IsOutOfArc`; a save / load that does not refill an emptied station |

WS-1 and WS-2 may go together; WS-3 after both; WS-4 last.

## 3. Fences

- ⛔ The behaviour lane owns `ManWeapon`, `SetUpWeapon`, `PackUpWeapon`, `CarryWeapon`, `WeaponSystemAttack` / `Hold`, the automatic weapon's
  SOP and `ua-weapon-systems` (WS-6) — a finding there is a REPORT, not an edit.
- ⛔ One firing path: `AimAndFireExecutor`, the dispatcher and `FireProcessing` gain no weapon-system branch beyond the mount list and the
  `CanFire` gate. Built-in mounts (tank gun, coax, rifle, RPG, drone warhead) stay type data — `WeaponMountDto` is unchanged.
- ⛔ No ground combat number moves: the same rounds, loadouts and guidance (6008, 6013–6015, 6019).
- ⚠ Seats per type stay ≤ 8 (`PassengerBuffer.Capacity`, the wire).
- Wire changes are exactly `dtWeaponSystemState` (new) and the APPENDED seat fields if they cross the wire — report each (R-169); any other
  wire change is a STOP-and-report.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · `--no-build` column · every red confirmed pre-existing against the
base sha · golden movement as a diff shape · tree clean after each suite · `tracker-counts.py --check` · every id allocated · ⭐ for WS-2 / WS-3
the INTEGRATION run where a station weapon fires on a split cluster (owner on one node, the muzzle seen on another), or why it cannot gate.
The design folded to the as-built (classes, sequences, deviations). Then the sha of each item on `backend`.
