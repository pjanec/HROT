<!--STATUS
state: LIVE
updated: 2026-10-13
doc-type: HANDOFF — behaviors lane → the backend lane (one or two sessions). Dispatched at d92f57081 (branch `behaviors`).
current-answer: §1 scope and the two sessions · §2 session A · §3 session B · §4 traps · §5 gates and the report
stale-below: nothing
known-rot: none
known-conflict: ⚠ STEERED after the start — read docs/blueprints/STEER_Params_Finish_Backend_1.md (2026-10-13): P4's blueprint half is REPLACED (no blueprint declares Parameters, R-2004) and P8's JSON options are ruled (FdpJsonOptionsRegistry, R-2005); everything else unchanged
related-designs:
  - docs/DESIGN_Orders_Params_Entity_Classes.md — THE design: §13 (finishing the params story: decisions F1–F11, work P1–P8, the docs list §13.4), §5c (B3 / B3b re-measured), §12 (the params DTO asset)
  - docs/blueprints/RULINGS.md — R-2000..R-2003 (2026-10-13), R-273..R-282 (behaviors)
  - docs/blueprints/HANDOFF_Orders_Track_B_Params_Internals.md — the previous Track B dispatch (B1–B5), built
  - docs/blueprints/DESIGN_Parameter_Model.md — the start pipeline; its §1 / §P.7 are rewritten when B3b lands
-->

# Handoff — finish the params story (backend)

RELEARN

⭐ This file points at the design; it is not one. Draw no UML here — extend
[DESIGN_Orders_Params_Entity_Classes.md](../DESIGN_Orders_Params_Entity_Classes.md) §13 when a task needs it.
⛔ Your scope is FROZEN at `d92f57081`; documents that change after it are FYI. A later document that invalidates an item ⇒ STOP that
item and report it (R-106), do not adapt.

🔒 **User, 2026-10-13:** *"I would like to finish this parameter story before continuing with embark and other squad development …
backend lane can work on the param infrastructure refactor and asset conversion, you can lead this effort making handoff to other
sessions."* The decisions you build on are in design §13.0 and ledger R-2000..R-2003.

## 1. Scope and the two sessions

Yours: the params infrastructure (attributes, registry, caches, DTO shape) and the asset conversion (B3b, deleting `Role`).
NOT yours: `[Required]`, the menu's fast path, `GET /orders`, designations (behaviors lane, P5 / P6); the order form (ui lane, P5b).

| session | items, in order | why this split |
|---|---|---|
| **A — registry and DTO shape** | **P8 → P1 → P2** | all touch the DTO declarations and the registry; ⭐ P8 first — the behaviors lane's P5 waits for fields |
| **B — assets and `Role`** | **P3 → P4** | assets, generators, the blueprint compiler; barely overlaps A |

One session may do both, A first. Two sessions: each merges the other's branch before its final commit (rule 4) and names it.

## 2. Session A

### P8 — params DTOs are plain public FIELDS *(F11, R-2002)*

- Convert every hand-written params DTO from properties to public fields. Measured at dispatch: 21 files, 87 properties — mostly
  `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/*ParamsJsonDto.cs`, plus the order DTOs (`Hrot.AI.Behaviors/Orders/OrderDtos.cs`).
  Let the rail below find the set; do not trust the count.
- Every READER must read fields: `OrderMenu.PickOf` (`Hrot.Common/Orders/OrderMenu.cs:77-87`, properties only today) and
  `OrderMenu.Json`, `DtoJsonSchemaExtractor` (⚠ not measured whether it reads fields), `BehaviorParamsForm` / `ComponentEditDrawer`,
  anything that reflects a DTO. ⚠ `PickOf` is the behaviors lane's file — the change is a mechanical "fields too" and is yours here
  because P8 breaks the menu without it; say it in the commit (cross-lane edit).
- ⚠ **Every `JsonSerializer` call on a params DTO must include fields.** System.Text.Json ignores fields unless `IncludeFields = true`
  (the trap is documented at `Hrot.Blueprints.Compiler/Assets/GraphTypes.cs:176-279`). The behaviour parse already handles fields (B4a's
  `T41Params` parses flat); the SEND side (`EntityOrders`, the HTTP route, mission tasks, blueprint Send Intent) is not measured. Lean:
  one shared options instance for params JSON, used by every site — find the sites with `search_code` + grep.
- Rail: params DTOs declare no properties (in CE-2130's suite, `Hrot.Editor.Tests/DebugApi/ABehaviourAdvertisesItsRealParametersTests.cs`).
- Acceptance: the rail is red-proved; every order still sends its value end to end (menu move, RTS attack, HTTP `order`), proven by
  the existing order rails (T-1).

### P1 — one `[Dto]` base, the origin, one registry *(F1, F2, F9, R-2000)*

- A base `DtoAttribute` in `Fdp.Core` (the one assembly both `Fdp.Toolkits` and `Hrot.Core` see; `Fbt.Kernel` references nothing). Its
  constructor takes `[CallerFilePath] string file = "", [CallerLineNumber] int line = 0` — the compiler fills them at the use site
  (✅ verified with a probe, design §13.1 F1) — and an optional `AssetId` (a GUID string) set only by the params generator.
- `ParamsDtoAttribute`, `OrderAttribute`, `UnitMemoryAttribute` derive from it. Move `[UnitMemory]` and `[BlackboardDtoStruct]` out of
  the vendored `Fbt.Kernel` (`FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BlackboardAnnotations.cs:12,37`; the kernel never uses them) —
  ⛔ by Roslyn, never text replace (CLAUDE.md ②). `[BlackboardDtoStruct]` retires: every registered struct is a variable type
  (`BlackboardTypeChoiceBuilder.cs:99-133` lists `[Dto]` types; 13 production files name the old attribute).
- Every DTO a behaviour names carries `[ParamsDto]` — including the asset-named ones the registry misses today: `ThreatCountParams`
  (`Fdp.Toolkits/Behavior/SensorNodes.cs:44`), `EqsTacticsParams`, `HullDownAttackParams`. Rail: a behaviour whose DTO lacks it fails.
- `ParamsDtoRegistry` (`Hrot.Core/MapDefinitions/Behavior/ParamsDtoRegistry.cs:95-150`) becomes ONE scan of `[Dto]` types recording
  each one's purposes and origin (file + line, or `AssetId`). ⚠ Its assembly filter keeps only assemblies that reference `Hrot.Core`
  (`:174-181`) — widen it to `Fdp.Core`, or `Fdp.Toolkits`' DTOs stay invisible. It refuses two `[ParamsDto]` types with the same
  simple name (load error).
- The `.params.json` asset gets an `AssetId` (as `.bp` / `.btree` / `.hsm` already do); the generator emits it into
  `[ParamsDto(AssetId = …)]` and reports a diagnostic when the file name ≠ `Name`; `AssetBaseNameCollisionGuard` covers `.params.json`.
- Acceptance: the registry lists every params DTO with its origin (rail over a hand-written and a generated one); `AssetCatalog.FindByAssetId`
  finds the generated one's asset; the CE-2130 suite and `ParamsDtoRegistryTests` green. The "open source / open asset" BUTTONS are UI
  (P5b) — expose the origin on the registry entry only.

### P2 — no stale type after a rebuild *(F4, R-2001)*

- A DTO shape change needs a full rebuild (accepted). Fix the three caches that keep the old `Type` after one:
  `BindJsonParamsDtoTypes` runs at CGF load only and `MergeFrom` re-applies the stored Type (`BehaviorRegistry.cs:911,925`;
  `CgfBehaviorSetup.cs:64`) · `BlackboardTypeHelper.ResolveClrType` returns the first match across loaded assemblies (`:64-87`) ·
  `IntentContractPaletteEntries` is cached with `??=` and never invalidated (`:31`).
- Quick reload (`AiAssetReload.cs:101-124`) refuses an asset whose DTO changed with "full rebuild needed" instead of running stale.
  How to detect it is yours to design (lean: compare the DTO's layout hash in the loaded registrar with the re-emitted one).
- Acceptance: a rail that loads a second copy of a DTO assembly and sees the newest type everywhere; the quick-reload refusal rail.

## 3. Session B

### P3 — B3b-1: the 14 allow-list entries that need no editor *(design §5c "B3b re-measured")*

| entries | conversion |
|---|---|
| `BlueprintBehaviourDemo`, `Demo_Advance`, `Demo_Engage`, `Demo_Retreat`, `Demo_TakeCover` | declare no Parameters ⇒ `ParamsDtoTypeId = NoParams` in the `.bp.json` (the compiler reads it: `BlueprintAsset.cs:36`, BP1689 `Stage2_Validate.cs:2824`; no `.bp.json` sets it yet) |
| `OperatorControl` | a curated C# BTree (`OperatorControlBehavior.cs:116`, `ParamsType = OperatorControlBlackboard`): a named DTO or `NoParams` — measure its callers |
| `HsmResolverDemo`, `T40_BehaviorResolverAsset` | the resolver asset's parameters → a named DTO (supersedes Parameter Model §P.7) |
| `T09`, `T10`, `T20`, `T35`, `T39`, `BTreeCuratedBindingDemo` | test fixtures → one named DTO each, or `NoParams` where no caller sends JSON (B3a's T31–T34 precedent) |

Remove each entry from the CE-2130 allow-list as it converts (`ABehaviourAdvertisesItsRealParametersTests.cs:294`). Allow-list 18 → 4.

### P4 — B3b-2: the 4 Parameter-declaring blueprints, then delete `Role` *(F8, §5c)*

- `DangerCrossingBp` (4 parameters), `Demo_MissionPlan` (3), `Demo_TaskChain` (2), `PlatoonHillAttackBp` (6) get hand-written C# DTOs
  (fields, `[ParamsDto]` if P1 has landed — merge session A first if it has). 🔒 User: *"It is no problem writing dto class in csharp for now."*
- F8 lean: `ParameterDecl` and the Get Parameter nodes STAY as the graph's view of the DTO's fields (24 / 8 / 7 production files) —
  ⚠ **confirm the user approved F8 before building this half** (asked in chat 2026-10-13); if not, build the C# DTOs and STOP before
  touching the blueprint compiler.
- Then delete `Role`: `BlackboardVariableRole` (`Hrot.AiEditor.Persistence/BlackboardVariableEnums.cs:7-13`; 23 production + 33 test
  files), the generated JSON mirror emission, the variables panel's legacy path, AND the runtime twin that is write-only:
  `StatefulSlotInfo.Role` (`BehaviorRegistry.cs:91`, written by every registrar and `HostedSubtree.cs:369`, read by nothing) and
  `Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotRole`. ⚠ The persisted `Role` key in asset JSON: decide migrate-on-load vs a
  one-time corpus rewrite, and say which in the as-built.
- Acceptance: the CE-2130 allow-list is EMPTY; generator / blueprint goldens move as a reported diff shape; the AQ76 and hosted-child rails green.

## 4. Traps

- Build the TEST project, never the production one, then `--no-build` (the second stale-binary trap). Never two overlapping builds at once.
- Generators are `netstandard2.0` and see JSON text only — a DTO is named by FQN string, never by `typeof` at generate time.
- Killing `dotnet test` orphans `testhost`; kill by PID, never `pkill -f` a pattern that matches your shell (RESUME_Orders_Programme §0.4).
- Goldens: report the diff SHAPE (files, added / removed lines), never just "regenerated".
- Docs (design §13.4): when B3b lands, rewrite `DESIGN_Parameter_Model.md` §1 / §P.7 and add known-rot to
  `Behavior_Parameter_Resolver_Detailed_Design.md` and `EXPLAINER_Where_Parameters_And_State_Live.md`; when `[BlackboardDtoStruct]`
  retires, fix the docs that name it. The rest of §13.4 is the behaviors lane's (P7). Fold every as-built into design §13 / §5c.

## 5. Gates and the report

- T-1 first — the feature suites: CE-2130's suite (`Hrot.Editor.Tests`), `ParamsDtoRegistryTests` (`Hrot.Core.Tests`),
  `ParamsDtoJsonGeneratorTests` / `ParamsDtoAssetTests`, the generator goldens (`Hrot.AiEditor.Generators.Tests`, incl.
  `BTreeGoldenCorpusTests`), the blueprint emit snapshots (`Hrot.Blueprints.Tests`), the order / menu / DebugApi order rails, the
  hot-reload rails. Find each with `scripts/find.sh`.
- `python3 scripts/tracker-counts.py --check` · `python3 scripts/design-digest.py --check` · `python3 scripts/rulings-check.py`.
- Tracker: P3 / P4 are `CE-2135` (B3); allocate P1, P2, P8 from YOUR block (`CE-3000`–`CE-3999`), append rows at the end of your area.
- Report per the gate-report contract (CLAUDE.md rule 8): one row per gate with its verbatim command, counts and delta; the
  `--no-build` column; goldens as a diff shape; every red proven pre-existing at a named base sha; a clean tree; the ids you allocated;
  the design sections you updated.
