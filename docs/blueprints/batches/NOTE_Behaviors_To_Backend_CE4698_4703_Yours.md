<!--STATUS
state: LIVE — a NOTE (protocol §5), an owner change · updated: 2026-11-26 · current-answer: the whole file
related-designs:
  - NOTE_Behaviors_To_Backend_Vehicle_Scenarios_Overlap.md — what behaviors touched today
-->
# NOTE — `behaviors` → `backend`: CE-4698 … CE-4703 are YOURS (user-approved), with two conditions

🔒 User, `2026-11-26`: *"ok, give them to backend with your conditions"* — the six rows backend root-caused and marked as behaviors' are
backend's to fix ([tracker on `backend`](../Blueprint_Issues_Tracker.md)): CE-4698 (`ua-tank-ammo` smoke), CE-4699 (`ua-air-defence` Stinger),
CE-4700 (`ua-technical-scoot` exposure), CE-4701 (`sam-site` radar), CE-4702 (`uc-urban-battle` leaderless hold), CE-4703 (`ua-hill-attack`).

| # | condition | why |
|---|---|---|
| ① | ⭐ **merge `behaviors` (≥ `433b9bbec`) before you edit** — and say in the commit when a change is in one of these | behaviors changed today: `SquadContactBehaviors.cs` (CE-2495 / 2496 / 2498-revert), `PlatoonAttackBehavior.cs` + `SupportByFireBehavior.cs` (CE-5051, R-2150 / R-2151), `ShelterFromAirNodes.cs` (CE-2501). ⚠ CE-4702 lands in the squad hold / contact code, CE-4703 next to `PlatoonAttack` |
| ② | ⭐ **CE-4703 is a RULE before it is a fix — take it to the user** | it is CE-2358 (`SopConditions.UnderCommand`: *a squad under a live platoon leaves its contact to the platoon*) for VEHICLES: does a tank under a platoon on an attack react to contact on its own? Behaviors' lean: **no, as for squads — except self-defence (hit)** — but it changes every platoon with vehicles, so it is the user's call |

Also useful for CE-4699: an air-defence weapon whose memory holds an aircraft only as an ANONYMOUS (heard) contact has nothing to aim at —
check its sight first (`GET /perception/los`); `NOTE_Behaviors_To_Terrain_Stechovice_ZU23_In_Forest.md` is that case.
