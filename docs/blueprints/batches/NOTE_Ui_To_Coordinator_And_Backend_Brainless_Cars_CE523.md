<!--STATUS
state: LIVE — ui → coordinator + backend: one FINDING from ui's merge of all three lanes, no action taken by ui.
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Ownership_Groups_And_Grants.md — OWNS §5.6 S8 (Health is in the brain group; only the brain node applies damage), the rule the rail guards.
-->

# FINDING — ui → coordinator + backend: the traffic cars have Health and no brain (`CE-523` rail red)

Found while merging `backend` + `behaviors` + `coordinator` into `ui` (`2026-10-18`). ⭐ **Red on `origin/coordinator` alone** — its
catalogue (`UrbanCombatTkbCatalog.cs`) and the rail are byte-identical to the merged tree's; ui changed neither.

| | |
|---|---|
| the rail | `HrotOwnershipGroupsTests.EveryProductionTemplateWithHealth_HasABrain_SoTheBrainNodeAppliesItsDamage_CE523` (`Hrot.Core.Tests`) — 291 pass / **1 fail** |
| what fails | `CivilianCar`, `Hatchback`, `DeliveryVan`, `CityBus`, `BoxTruck` — *"has Health but no brain"* |
| the cause | `CE-4030` (coordinator, `0ca8cb75f`, *civilian cars can be hit*) gave them the soft-skin `CombatPlatformDefDto` |
| why it matters (the rail's own words) | Health is in the BRAIN group and only the brain node runs `HealthApplicationSystem`, so a brainless type *"would keep Health with its creator — a SimHost or IG that applies no damage"* |
| ⚠ against it | `CE-4030`'s live check saw a hit SUMO car reach 150/300 — so either the damage lands by another path (the rail is too strict for cars), or it lands by accident on that topology |

⭐ ui's lean: the rail's owner (backend, `CE-523`) and the catalogue's (coordinator, `CE-4030`) decide between ① a brain group for the car
types, ② the rail narrowed to types that must take damage on a brain node, with the reason written in its doc comment. ⛔ ui did not touch
either; the red is reported in ui's gate as pre-existing on `coordinator`.
