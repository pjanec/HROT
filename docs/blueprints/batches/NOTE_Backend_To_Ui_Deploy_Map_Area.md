<!--STATUS
state: LIVE — backend → ui, a small item for your lane
updated: 2026-11-05
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Player_Join.md — §11 S3-d (the item), §11.1 (what is built)
-->
# NOTE — backend → ui: Player Join S3-d, choose the spawn area on the Player's map (CE-3370)

Player Join is built on `backend` through S3 (live-tested). The one piece left is yours: **clicking a spawn area on the Player's 2-D
map**. It was split to ui in the design (§11 S3-d) but never handed over. 🔒 User, `2026-11-05`: *"yes, file it"*.

| what exists | where |
|---|---|
| the areas offered to the Player — `DeployFlowSystem.Offers` entries with `IsArea` (`UnitNetId` = the area's net id, `Side`, `SlotName` = its name) | `Hrot/Engine/Hrot.Gameplay/DeployFlowSystem.cs` |
| the request that carries the choice — `DeployFlowSystem.Request(side, unitNetId, kit, areaNetId)`; `RequestedAreaNetId` reads it back | same file |
| the screen's area list ("Any area" first, selected by default) — the click should select the same entry | `Hrot/Engine/Hrot.Presentation/Panels/DeployScreen.cs` |
| the area itself — a `SpawnArea` entity with an outline (`WeatherField.TryOutline`) | `Hrot.Core` components; `SpawnPoints.Pick` uses the same outline |

⭐ **Ask:** while the flow is Choosing, highlight the chosen side's area outlines on the map and accept a click on one ⇒ that area is
selected (the screen's list follows) and rides the next `Request`. ⛔ No backend change is needed; the server already validates the area
(`NoSuchArea`, `WrongSide`).

⭐ **Done when:** the click selects the area, the spawned man lands inside it, and a rail in the map's suite proves click → `areaNetId`.
