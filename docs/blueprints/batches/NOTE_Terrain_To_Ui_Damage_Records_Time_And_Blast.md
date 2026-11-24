# Note: terrain → ui — the damage records carry WHEN and WHERE FROM (CE-11051, built)

| | |
|---|---|
| **From** | `terrain`, 2026-11-24 |
| **Answers** | [HANDOFF_Ui_To_Terrain_Damage_Records_Time_And_Blast.md](HANDOFF_Ui_To_Terrain_Damage_Records_Time_And_Blast.md) (ui → terrain) |
| **Design** | [UTM §8.3a](../../DESIGN_Terrain_Import_UTM.md) — the decisions T-1…T-6, the class diagram, the rails; [Glass Windows §5b](../../DESIGN_Glass_Windows.md) |
| **Your half** | the gateway copies the fields below into DPS `BuildingDamage` (append-only) |

## What the wire now carries

| topic | new field | meaning |
|---|---|---|
| `EntityBuildingDamage` | `double CollapsedAt` | sim seconds the building collapsed; 0 = not collapsed, or collapsed in a loaded save |
| `DdsWallBreach` (in `Breaches`) | `double MadeAt` | sim seconds the breach was made or last WIDENED; 0 = loaded from a save |
| | `float BlastX, BlastY, BlastZ` | the blast point, terrain-local metres (the frame of `SimTransform`): the detonation's world point; for a hull's ram / drive-through / bulldozer blade, the hull's centre. Meaningless when `MadeAt` is 0 |
| `DdsWindow` (in `Windows`) | `double HitAt` | sim seconds of the window's LAST hit (a crack or the break); 0 = loaded from a save |

## The rules the gateway can rely on

1. **One clock**: every time is `SimClock.TotalTime` (`GlobalTime.TotalTime`), the clock the recorder uses. The writer stamps the frame the RECORD changed — at most one
   tick after the detonation / the round.
2. **Live vs join**: a value that changed since you last saw the instance is live; a sample at join carries the times of when it happened (compare them with the
   current sim time), and 0 means "was already there when the scenario loaded".
3. **A widening moves the breach's time and blast point**; a second shell into the open hole changes nothing and republishes nothing. A breach a later merge leaves
   untouched keeps its own stamp (its index in the list can change — key a breach by its geometry, not its index).
4. A window's time moves on every crack and on the break; a hit on a broken pane changes nothing.
5. ⚠ The topics' layout changed: recordings made before `CE-11051` read the old layout (no shim — R-11013's precedent).

## Gates (terrain)

See the commit; the suites run are named in [UTM §8.3a](../../DESIGN_Terrain_Import_UTM.md) under "Rails".
