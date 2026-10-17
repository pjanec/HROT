<!--STATUS
state: LIVE — backend → behaviors: a NOTE (B-8 hands, B-13 pick-up and stone sources, B-10 laid-down weapons, B-9 the pistol, B-11 carry stations and the charge, B-12 the held item, B-14 the venting canister landed; CE-3239 found on the way).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.9 / §7.9a OWN B-8 (the hands); §7.10 / §7.10a OWN B-13 (pick-up, stone sources, a lying stone); §7.11 / §7.11a OWN B-10 (laid-down weapons); §7.12 / §7.12a OWN B-9 (the pistol); §7.13 / §7.13a / §7.13b OWN B-11 (stations, pick-up weapons, the charge); §7.14 / §7.14a OWN B-12 (the held item); §7.15 / §7.15a OWN B-14 (the venting canister); §7.3c CE-3239; §5h AW-7…AW-9 their rulings.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — your frame (B-7…B-14).
-->

# NOTE — backend → behaviors: B-8 hands, B-13 pick-up, B-10 laid-down weapons and B-9 the pistol landed *(CE-3230, CE-3235, CE-3232, CE-3231; CE-3239 on the way)*

**Landed at:** B-8 [CE-3230](../Blueprint_Issues_Tracker.md) at `backend@81bc12e90`; [CE-3239](../Blueprint_Issues_Tracker.md) at
`backend@9cbc10a17`; B-13 [CE-3235](../Blueprint_Issues_Tracker.md) at `backend@61f235fd7`; B-10 [CE-3232](../Blueprint_Issues_Tracker.md) at
`backend@5eef254e5`; B-9 [CE-3231](../Blueprint_Issues_Tracker.md) at `backend@95b9812f6`. Designs + as-built: [Civilians §7.9a, §7.10a, §7.11a, §7.12a, §7.3c](../../DESIGN_Civilians_And_Crowd_Control.md).

| what you can call now (on the man's BRAIN node) | what it does |
|---|---|
| `PersonHands.Give / Rounds / HasRoom / Take / HoldsLethal` | the hands' loadout (up to 4 kinds); a throw is `FireAtPoint` on the hands mount (`PersonHands.MountIndex`) |
| `ItemSources.Refill(world, man)` | ⭐ the STONE SOURCE: tops his hands up to 5 stones when he stands inside one (returns how many). Call it when your 1.5 s bend is done — there is nothing to ask anyone (a source is unlimited) |
| `RoundPickUp.CanAsk(world, man, round)` · `RoundPickUp.Ask(world, man, round)` | ⭐ a LYING round he stands at (≤ 1.5 m): a stone (lies 60 s), a grenade during its fuze, a stun / gas / smoke grenade. `Ask` goes to the node that flies the round; the grant arrives in his hands a few frames later. ⚠ No "denied" reply — **the round vanishes either way; read `PersonHands.Rounds` to know whether it was yours** (two men reaching at once: one gets it) |
| ⭐ `WeaponLaying.LayDownAll(world, man)` · `LayDown(world, man, mount, down)` | B-10 — SURRENDER's armed half (AW-5): lay every built-in weapon mount down (the hands excepted); `down: false` takes them up again. Brain node only |
| `WeaponLaying.IsLaidDown(view, man, mount)` · `AllLaidDown(view, man)` | read on ANY node (replicated through `WeaponAmmo`, mounts 0…3). A laid-down mount makes no round, `AimAndFire` / `FireAtPoint` hold with `NotDeployed`, and it arms nobody — all down ⇒ `Armament.ArmedByStatus` false, the figure unarmed. ⚠ "out of the fight" (AW-21) stays YOURS: laid down AND hands up |
| `RoundPickUp.IsLying(world, round)` · `RoundPickUp.IsHandItem(world, round)` | to FIND one: a lying round is a lightweight replicated entity (`LightweightRound` + `TkbIdentity` = its munition) whose speed is ~0 |

**① A live round keeps its fuze.** A picked-up grenade keeps burning in his hand (`HeldFuze`); `FireAtPoint` throws it with what is left;
if your behaviour holds it too long it is thrown at his own feet and bursts there (the one firing path). `PersonHands.HoldsLethal` is true
while he holds it — AW-9 — so `Armament.LethalInHand` / `Judge` see him armed.

**② The canister cannot be picked up yet.** CE-3239 (a cloud round never burst on the ground — it raised no cloud) is fixed: a canister now
bursts where it lands and is gone, so `ua-armed-gas-throwback` waits on B-14 (the venting canister), as the frame orders.

**③ What a thrown stone does now** — it LIES 60 s where it stopped (a hit on a man still ends it), and every node sees it (the stone,
the stun / gas / smoke grenades and the canister got a look). ⚠ That is one lightweight entity per lying stone on the wire; the civilian
perf gate (CE-3222) will measure a riot — tell me if your riot demo throws far more than a stone per man every few seconds.

**④ `ua-armed-surrender` is unblocked** (your §5h.3 "waits on B-10"): lay down → no round, re-arm (`down: false`) → fires again.

**⑤ The pistol (B-9) is data only** — InfantrySoldier (2002) mount 1, Grenadier (2004) mount 2 (the grenades stay mount 1; the hands moved to 2 / 3). Nothing to call: `AimAndFire` with `MountAuto` keeps the rifle while it has ammo and takes the pistol once it is empty. ⚠ **Measured: an empty rifleman then fires the pistol at ANY range** (50 m is its reach; aimed fire has no range gate for any weapon, and the chooser takes any score above 0) — if a behaviour should not empty his pistol at 150 m, gate the engagement on range. Your four soldier rails re-ran unchanged (U1, U2, WindowDuel, UniversalSoldier, plus CE2073 and AW20).

**⑥ B-11 — carry stations and the charge** ([CE-3233](../Blueprint_Issues_Tracker.md), B-11a at `backend@fdffec282`, B-11b at `backend@7eff97ff7`; design [§7.13a/§7.13b](../../DESIGN_Civilians_And_Crowd_Control.md)). Every person now has seats 1 HANDS, 2 BACK (stowed), 3 HIDDEN (stowed + concealed).

| what you can call | what it does |
|---|---|
| `CarryStations.Hands / Back / Hidden(view, man)` · `SeatOf(view, thing, out carrier)` · `IsStowed` · `IsConcealed` | the seat indices and where a carried thing sits — read on any node |
| `CarryStations.Move(world, man, thing, seat)` (on the man's list owner) · `CarryStations.Ask(world, man, thing, seat)` (any node, I-1 kind 11) | DRAW (→ Hands), SLING (→ Back), HIDE (→ Hidden). A stowed weapon cannot fire; a slung one still reads ARMED to observers, a hidden one arms nobody (status, conduct) — but `Armament.LethalInHand` still counts it: he knows he has it |
| pick-up rifle **616**, pistol **617** | weapon systems a man carries and fires (`Embarkation.TryEmbark(world, weapon, man)` → his hands, then his back; never hidden unasked) |
| charge **618** · `Charges.Ask(world, charge, by)` (any node, I-1 kind 12) · `Charges.Detonate(world, charge, by)` (its position owner) | a vest worn HIDDEN, a charge placed on the ground, or loaded in the civilian car 1002's boot (`TryEmbark(world, charge, car)` fills the boot). It is never fired; set off, it bursts WHERE IT STANDS — worn, at the man; in the car, at the car — as one 8 kg detonation named after `by`, and is gone |

⚠ The bomber's own death is the blast's business (it bursts at him) — nothing extra to do. The ui draws Back (slung) and Hidden (not drawn); until they do, every carried weapon is drawn in his hands.

**⑦ B-12 — what is in his hand** ([CE-3234](../Blueprint_Issues_Tracker.md), at `backend@9ef1ba306`; design [§7.14 / §7.14a](../../DESIGN_Civilians_And_Crowd_Control.md)).

| what you can call | what it does |
|---|---|
| `HeldItems.Set(world, man, kind)` (brain node) · `HeldItems.Of(view, man)` (any node) | `HeldItemKind` None · Weapon · Throwable · Phone · Binoculars. It is a byte on `WeaponPosture` beside `ActiveMount` (so "Weapon" IS the active mount) and crosses on `dtWeaponPosture`. `Set` refuses a phone or binoculars his type does not carry, a throwable with no hands, a weapon with none. Lowering the weapon (`WeaponPostureOf.Idle`) keeps it |
| pocket items (type data, `PocketItemsDto`) | a **phone** on every civilian 1101–1104 and the insurgent 2003; **binoculars** on the mortar team 2005. ⚠ The officer 201 has no template, so no binoculars yet |
| binoculars up | his visual sensor reaches ×2 in a 10° cone, and he identifies ×4 further (`SeeingConditions.Of`) — put them down to see around him again |
| a phone in hand | an observer SEES it (`Armament.Judge`: a small object — Unidentified far off, Nothing near, never lethal): your D4 cue |
| ⚠ `Charges.Ask(world, charge, by)` | a man who does NOT carry the charge (a remote IED) must hold a phone, else it returns false; his own vest and `by = null` (a timer) need none |

⚠ Nothing in the brains sets the held item yet — the frame left it to you (`ua-armed-status`, the spotter, the bomber).

**⑧ B-14 — the venting canister** ([CE-3236](../Blueprint_Issues_Tracker.md), at `backend@3e50adfc7`; design [§7.15 / §7.15a](../../DESIGN_Civilians_And_Crowd_Control.md)) — ⭐ **`ua-armed-gas-throwback` is unblocked** (② above).

| what happens now | what you call |
|---|---|
| a gas / smoke canister or grenade that bursts on the GROUND lies there and VENTS for its burn (90 s for the 40 mm canister); its cloud sits on it | nothing — `RoundPickUp.IsLying` / `CanAsk` find it like a stone |
| picked up: the cloud lets go and drifts on the wind; the canister keeps burning in his HAND — an Irritant hit on him each second; burnt out it is gone from his hands | `RoundPickUp.Ask(world, man, canister)` (as B-13) · `RoundPickUp.BurnLeft(world, man, ammo)` reads what is left |
| thrown back: it lands and raises a NEW cloud for the burn it had left, sitting on it again | `FireAtPoint` on his hands mount (as a stone) — the burn rides along by itself |

⚠ An AIRBURST (a gas grenade whose fuze runs out in flight) still makes a free cloud and nothing to pick up.

**Ids allocated:** CE-3239 (the cloud defect), component 402 (`HeldFuze`), component 403 (`LaidDownWeapons`), munition 6042 (`TkbPistol9mm`), event ids 5011/5012/5013/5014, interaction kinds 9/10/11/12, munition 6043 (the charge round), TKB types 616/617/618 and 8812
(Stone Source). **Next free:** CE-3240.
