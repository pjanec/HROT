<!--STATUS
state: LIVE — a NOTE (protocol §5), FYI · updated: 2026-11-26 · current-answer: the whole file
related-designs:
  - NOTE_Behaviors_To_Terrain_Stechovice_ZU23_In_Forest.md — the CE-5062 finding
  - NOTE_Terrain_To_Behaviors_ShelterFromAir_Thrash.md — the shelter thrash behaviors is on
-->
# NOTE — `behaviors` → `backend`: what behaviors found / is touching, beside your six vehicle scenarios

The user told behaviors you are on `ua-tank-ammo`, `ua-air-defence`, `uc-urban-battle`, `sam-site`, `ua-technical-scoot`, `ua-hill-attack`.
To avoid two lanes on one file:

| | behaviors | ⭐ for you |
|---|---|---|
| **CE-5062** (stechovice ZU-23 never fires) | ✅ root-caused, NO code changed: the gun stands inside a forest — `/perception/los` blocked by woods at `along: 0` — so its memory holds the aircraft only as HEARD (anonymous) contacts and `EngageAircraft` (`AirSense.Newest` → null, `seen` false) has nothing to aim at | if an air-defence row of yours is red, check its memory first: **only anonymous air contacts ⇒ a sight problem, not the fire chain**. `GET /perception/los?observer=&target=` names the blocker in one call |
| **ShelterFromAir thrash** (a vehicle under aircraft re-picks shelter every 2–3 ticks and never moves) | 🚧 **behaviors is on it now** — `Hrot.AI.Behaviors/Brains/ShelterFromAirNodes.cs` | ⛔ please leave that file to behaviors; tell the user if one of your scenarios needs it sooner |
| CE-5051 (a platoon attack's base of fire / the waiting squads) | ✅ built: `SupportByFireBehavior` (fires short of its point only when stuck), `PlatoonAttackBehavior` (D-12 extended) | ⚠ `ua-hill-attack` / `uc-urban-battle` may move by these — a change there since `3463295ed` is behaviors' |
