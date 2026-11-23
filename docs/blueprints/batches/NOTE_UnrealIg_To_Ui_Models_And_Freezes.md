# Note: grey boxes, the Unreal client's freezes, the civilians' tilt — measured; one small ask (the SUMO cars' DIS)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-23 |
| **For** | `ui` (TKB / entity types); FYI the traffic (SUMO) session for §1 |
| **User, verbatim** | *"send HROT note about whatever model mapping you need and whatever editor freezes investigations - and how to reproduce it"* |
| **Asks** | §1 only (small, a lean). §2 and §3 are FYI — measured to be the Unreal client's, fixed on our side |

## 1. Models — HROT sends what a client needs; one ask on the DIS

**The grey boxes the user saw were ours.** Measured in HROT's code (graph + grep):

| entity | what HROT sends | the client (today → fixed in `unreal-ig`) |
|---|---|---|
| a SUMO car HROT takes over when shot (`Hrot.Traffic/TrafficPanic.cs:246-256`) | the SAME network id as the SUMO car, `TkbType` = the car's own vType type (`TrafficSession.cs:69-71`): 1002 CivilianCar, 1050 Hatchback, 1054 Sedan, 1055 Estate, 1056 Suv, 1051 DeliveryVan, 1053 BoxTruck, 1052 CityBus | no model for these types → a box ⇒ now: the SUMO movers' own car / van / bus models by `TypeId`, the same pick as the mover had |
| the M2 HMG on the HMMWV (`NedTkbCatalog.cs:459`, TKB 600) | a weapon system, DIS `1.1.0.50.1.0.0` | a gun-sized box ⇒ now: a heavy-MG mesh |

**Ask (lean, small):** every SUMO car kind carries the same DIS **`1.1.0.81.0.0.0`** (`UrbanCombatTkbCatalog.cs:216`, `:557`). A client that keys on DIS
only (a DIS / HLA image generator — the usual case outside our own client, which can use the TKB `TypeId`) cannot tell a bus from a sedan.
⭐ Lean: give each kind its own DIS subcategory (e.g. the SISO-REF-010 civilian-car subcategories: sedan, hatchback, estate / wagon, SUV, van,
truck, bus), set where the template is built. Nothing on our side waits for it.

## 2. The Unreal client's freezes — the editor building assets on first use (ours; no HROT involvement found)

**How to reproduce (scripted, ~5 min, nobody at the keyboard):**

```
# HROT ui (Release ClusterRunner, --mode editor) + the SimNetGateway extension (DPS domain 5, partition ig-live), then the editor:
UnrealEditor.exe D:/Work/HrotUnreal/HrotUnreal.uproject -ExecutePythonScript=<HROT>/scripts/unreal/hrot_freeze_check.py -unattended -nosplash -abslog=<log>
#   HROT_FREEZE_JOB = { domain 5, partition "ig-live", map "/Game/Maps/demo_city_look", out, ready, go, soldier 1001 }
# load showcases/player-station live, /sim/play, write the "go" file
```

The script possesses Rifleman 1, idles 12 s, turns to the nearest person and fires one round, waits 12 s, fires 5 more, and records every
frame over 0.25 s; the editor log's own lines at each hitch say what the frame waited for.

**Measured (2026-11-23, one run):**

| when | hitch | what the editor log shows at that moment |
|---|---|---|
| the scenario's people first appear | **19.8 s** | `Waiting for skinned assets to be ready (SK_US_Soldier_Base / m_006_nrw_FaceMesh / m_tal_unw_jeans / f_tal_nrw_body)` + `AssetCompile memory estimate is greater than available … [SkinnedAsset]` — the editor BUILDS each character mesh the first time it is drawn |
| possession, the first fighting | 1.1 – 2.9 s (3 times) | `FlushAsyncLoading` beside first-time sounds (`rifle_08`, `body_04`, `dirt_05` …) and first-time clips — synchronous first loads |
| the first shot itself | **none** | — |

⇒ Both causes are the Unreal client's (the editor's on-demand mesh builds; our own synchronous first loads), not HROT's data. Ours to fix:
preload the sound sets and the person kits at start, and check a PACKAGED client (cooked meshes need no build). The mesh builds were also
re-triggered today by the project's new CitySample settings (16-bit bone indices, unlimited influences), and the machine was short of memory.

## 3. The civilians' tilt — not HROT's

Measured: HROT sends the civilians upright (earlier `ddsmonitor` capture: pitch / roll 0, Held None), and in two live runs today every
civilian body stood upright: actor and mesh pitch / roll 0, pelvis → head within 7–11° of vertical, in idle and walk (`hrot_person_pose_check.py`,
`hrot_freeze_check.py`). The clips themselves are upright (`hrot_clip_pose_audit.py`). The likely cause of what the user saw is skinning:
the CitySample bodies need 16-bit bone indices, which the project lacked until today's settings — fixed on our side; confirmed by a picture from 3 m (the Passer-by, upright, empty-handed).

## Unreal side, for the record

`unreal-ig`: the car / van / bus and the heavy-MG models, a shot civilian's death clip (the civilian kits have only `Dying`), the hold-F
action list (`OwnUnit.Actions`), the missile launch sound for ATGM / SAM / air-to-air fire, demo-city's roads (HROT's road scene,
`Hrot.Tools.RoadScene`).
