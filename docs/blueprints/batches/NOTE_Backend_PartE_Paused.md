# NOTE — backend, PART E (Native American warrior, CE-4650) PAUSED

Base: `8f7ba4956` (part D, accepted). Nothing of part E is committed: the steps below are in the WORKING TREE, uncommitted, because none of their rails has run yet.

| NA step | state | files (working tree) |
|---|---|---|
| ① NA-1 type | written, `Hrot.Core` builds (0 errors), no rail run | `Hrot/Engine/Hrot.Core/Tkb/FunTkbCatalog.cs` (new; type **2016**, bow + tomahawk mount, `UrbanCombatTkbCatalog.Insurgent` made `internal`, silent when a thrown hatchet), `HrotEnvironment.cs` (registers it), `UrbanCombatTkbCatalog.cs` |
| ② NA-2 wardrobe | enums + `OutfitCatalog.NativeWarrior` + `OutfitDto.FacePaint` slot written; presentation stand-ins NOT done | `OutfitKinds.cs` (Buckskin, Breechcloth in `OutfitOver`, Headband, Feather, hair Braids, `OutfitFacePaint`), `OutfitCatalog.cs`, `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/OutfitDto.cs` |
| ③ NA-3 riding | nothing to build (the type + horse 703, as `bw-mounted-archers`) | — |
| ④ NA-4 circle raid | written, NOT built | `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CircleRaidBehavior.cs` (mirrors `SwarmRaidBehavior`), `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/CircleRaidParamsJsonDto.cs`, `BehaviorNames.CircleRaid` |
| ⑤ NA-5 scenarios | not started | `scenarios/fun/western/{war-party,archer-ambush}` |

## Exact next step
1. `dotnet build Hrot/Subsystems/Hrot.AI.Behaviors --no-restore` — fix `CircleRaidBehavior` compile errors (suspects: `PassengerSlots` indexer, `EntityRef.IsNone/NetworkId`, `Fixed` buffers need `unsafe`).
2. Presentation stand-ins in `Hrot/Engine/Hrot.Presentation/Map3D/Outfits.cs` (+ `HairStyle.Braids` drawn as Long, `OutfitOver.Breechcloth` = the short-skirt box, `OutfitHead.Headband` = the police-cap band, `Feather` / face paint not drawn); list them in the report.
3. Add the ONE new line to `EntityTypeCatalogTests` BuiltInGolden (`2016 Native American warrior | dis 3.1.0.1.0.0.0 | family Person | …`, copy the shape of line 2013) and run `Map3DTests`/`EntityTypeCatalogTests`.
4. Rails (red-proved, under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`): the warrior template; the circle raid legs (`CircleRaidBehavior.RingPoint/BearingOf`, break-off). Declare `Health` / `PassengerBuffer` reads in `AiBrainReads` if the read check asks.
5. Scenarios `war-party`, `archer-ambush`, goldens; `stepped-hash.py --threads 1,4,8` on `war-party` (or the 1/4/8-thread scenario substitute); bows scenarios + `rabbit-hunt` + five `ua-animal-*` still pass.
