<!--STATUS
state: LIVE — backend → behaviors: a NOTE (B-8 hands, B-13 pick-up and stone sources, B-10 laid-down weapons landed; CE-3239 found on the way).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.9 / §7.9a OWN B-8 (the hands); §7.10 / §7.10a OWN B-13 (pick-up, stone sources, a lying stone); §7.11 / §7.11a OWN B-10 (laid-down weapons); §7.3c CE-3239; §5h AW-7…AW-9 their rulings.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — your frame (B-7…B-14).
-->

# NOTE — backend → behaviors: B-8 hands, B-13 pick-up and B-10 laid-down weapons landed *(CE-3230, CE-3235, CE-3232; CE-3239 on the way)*

**Landed at:** B-8 [CE-3230](../Blueprint_Issues_Tracker.md) at `backend@81bc12e90`; [CE-3239](../Blueprint_Issues_Tracker.md) at
`backend@9cbc10a17`; B-13 [CE-3235](../Blueprint_Issues_Tracker.md) at `backend@61f235fd7`; B-10 [CE-3232](../Blueprint_Issues_Tracker.md) at
`backend@5eef254e5`. Designs + as-built: [Civilians §7.9a, §7.10a, §7.11a, §7.3c](../../DESIGN_Civilians_And_Crowd_Control.md).

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

**Ids allocated:** CE-3239 (the cloud defect), component 402 (`HeldFuze`), component 403 (`LaidDownWeapons`), event ids 5011/5012, interaction kinds 9/10, TKB type 8812
(Stone Source). **Next free:** CE-3240.
