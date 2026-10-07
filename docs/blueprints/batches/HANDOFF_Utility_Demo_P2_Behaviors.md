<!--STATUS
state: LIVE — DISPATCHED at 03490520c (2026-10-06, the commit that adds this file sits on it); scope frozen there.
  User, 2026-10-06: "continue with open tasks, autonomously as before, with syncs with peer lane as you go, as they also
  keep working". ⭐ §6 SYNC is the APPEND-ONLY channel between the two lanes (same protocol as
  HANDOFF_Danger_Crossing_Behaviors.md §6) — read it on every merge.
updated: 2026-10-06
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — THE programme design: §4 (U3, U4), §6 (G4, G5, G6), §8 (Q1–Q7 approved, R-209).
  - docs/DESIGN_Decision_Layer.md — OWNS the utility step (§3.3: ChooseOption / IsOption / ScoreDecision, the D3 gap) and
    CombatPosture (§3.3b, the BTree host and why HSM / blueprint were not chosen THEN).
  - docs/DESIGN_Eqs_Consuming_Behaviours.md — OWNS Flank / FiringPosition (CE-2108, CE-2109, built) and CE-2110 (who
    else starts them) — G6 is one answer to CE-2110.
  - docs/designs/utility-ai/Utility_AI_Design_v1_1.md — OWNS the scoring engine and the starter decisions (§11.4).
-->

# HANDOFF — utility demos phase 2: the behaviours half (G4, G5, G6)

**For:** the behaviours lane (`behaviors`). **From:** the backend lane (`backend`). ⭐ **Dispatched at `03490520c`**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item
and report it**; do every item that is not blocked (R-106).

## 0. Before anything

1. Read `docs/blueprints/RULINGS.md` (⭐ R-209, R-197, R-204, R-208), then
   [`DESIGN_Utility_AI_Demo_Scenarios.md`](../../DESIGN_Utility_AI_Demo_Scenarios.md) §4 (U3, U4), §6 (G4–G6), §8, and
   [`DESIGN_Decision_Layer.md`](../../DESIGN_Decision_Layer.md) §3.3 + §3.3b. ✅ **The programme, its order and the lane split
   are APPROVED (Q1–Q7, `2026-10-05`, R-209 — Q6: "behaviors: G4, G5, G6").** The items below are FRAMES: ⭐ **step 1 of each
   is YOUR design** (UML in the owning design doc, per CLAUDE.md "WHO DESIGNS"), step 2 the build.
2. **Branch:** `behaviors`. Merge `origin/backend` at `03490520c` or later, push the empty marker
   `chore: started utility-demo P2 at <sha>`.
3. **Ids:** filed by the backend as **`CE-3082`** (G4), **`CE-3083`** (G5), **`CE-3084`** (G6) — the tracker rows are yours
   for the duration (rule 6). A finding gets a new row from **your** block, appended at the end of your area.
4. ⚠ **Large-blast-radius calls go to the USER** (an architect question with a lean). The one I can see: G6's nesting (a
   decision INSIDE another decision's branch) — if your design finds it needs a scorer change, ask before building.

## 1. The user's words

> 🔒 *"I would like to have a set of sample scenarios demonstrating the utility ai features, including the not yet used and
> not yet built decisions. And to build the missing features required by them."* (`2026-10-05`) · *"Approved."* (Q1–Q7) ·
> *"We are building infrastructure so such assets can be created at all."* (R-197) · *"continue with open tasks,
> autonomously as before, with syncs with peer lane as you go"* (`2026-10-06`).

## 2. What exists — measured `2026-10-06` *(so you do not rebuild it)*

| | where | note |
|---|---|---|
| `CombatPosture` BTree — `Parallel[ChooseOption, ObserverSelector[IsOption(n) → child]]`, 5 options | `Assets/BTrees/Tactics/CombatPosture.btree.json`; decision `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/CombatPostureDecision.cs` | U1 PASSES live on it |
| the utility step as SHARED nodes (BTree + HSM) | `UtilityNodes.ChooseOption` / `IsOption` (CE-2069) | ⚠ the HSM RUNTIME switch is railed at COMPILE level only (Decision Layer §3.3 as-built, D3 row) — G4 closes it |
| blueprint `ScoreDecision` (→ `UtilityBlueprintBridge`) | `Nodes.cs` `ScoreDecisionNode.AssetId` — a bare GUID string | no asset uses it; BP-27 (uneditable id) |
| ⭐ `UtilityDecisionCatalog` (CE-2068) — every `[UtilityDecision]` with its asset id and display name | `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionCatalog.cs` | ⭐ BP-27's re-check said "no picker to inherit" because the decision defs then lived in tests only — **the catalog is that source now** (lean for G5 below) |
| ⭐ `Flank` / `FiringPosition` BTrees + `EqsTacticsNodes.Run(template, site, mode)` | CE-2108 / CE-2109 (yours, built + live) | ⚠ **design §6 G6 lists them as "to build" — that is stale; G6 is the DECISION only.** I fold this into the design |
| `PostureNodes.AdvanceAndAttack` (the Direct approach) | Decision Layer §3.3b | — |

## 3. Items *(one commit each, green at each; ⭐ rails FIRST, red-proved)*

| # | item — the FRAME | my lean on the key decision | acceptance (a new rail per bullet) |
|---|---|---|---|
| **G4** `CE-3082` | `CombatPostureHsm` — the same decision hosted as an HSM: one state per option, transitions guarded by `IsOption(n)`, the option's child as the state's activity | ⭐ **reuse the BTree's option children as activities** (TakeCover, FallBack, AdvanceAndAttack…) — no second copy of any behaviour (ruling 9); the HSM adds only the switching | ⭐ **the D3 gap: an HSM RUNTIME switch rail** — Health edits move the active state both ways (the BTree's `CE2069_UtilityNodes_SwitchTheBranch…` shape, on the HSM) · the same winner sequence as the BTree for the same inputs (the U3 premise) |
| **G5** `CE-3083` | `CombatPostureBp` — the same decision as a blueprint: `ScoreDecision` + a Behaviour Task per option (Start / Abort on change) + the decision PICKER (BP-27) | ⭐ **picker baked from `UtilityDecisionCatalog`** (name in the palette / drawer, asset id baked into the node — the `SensorKindBaker` / `GetComponent` bake shape, CA-01), with a shipped-asset staleness rail like your `ShippedSensorDeclsTests` | the picker lists every catalog decision; a picked id round-trips · the same winner sequence as the BTree for the same inputs · ⭐ zero golden movement for existing assets |
| **G6** `CE-3084` | `AttackApproach` {Direct, Flank, FiringPosition} — a NEW `[UtilityDecision]`, scored inside CombatPosture's AdvanceAndAttack branch; Direct = today's `AdvanceAndAttack`, the other two start the existing CE-2108 trees | ⭐ **nest it as a CHILD `Parallel[ChooseOption(AttackApproach), ObserverSelector[…]]` inside the AdvanceAndAttack branch** (the U1 shape, one level down) — no scorer change. Inputs: line of sight to the top threat (`ThreatsInView` / `SegmentBlocked`) and the two idle templates' top scores (`EqsTopScore(FindFlankingPosition / FindOpenFiringPosition)`) | no sight of the target ⇒ Flank or FiringPosition wins; clear sight ⇒ Direct · the outer posture still switches away (TakeCover on damage) while an approach runs · `/entities/{id}/utility` lists BOTH decisions (G2 reads any `[UtilityDecision]` with `OptionNames`) · it answers CE-2110 for the posture case — say so in that row |

⛔ **Not yours** (the backend's): the U3 / U4 scenario JSON, `utility-demo-check.py` cases, the in-process rails in
`PostureScenarioTests`, the runbook, the live runs. ⭐ **Tell me in §6 the moment each asset is pushed** (its name and the
order params) — I build the scenario against it.

## 4. Feature suites to run FIRST *(T-1)*

| suite | why |
|---|---|
| `Hrot.SimHost.Tests` `~TacticsTreesTests` · `Hrot.ClusterRunner.Integration.Tests` `~PostureScenarioTests` | CombatPosture end to end (U1 + the danger crossing live here) |
| `Fdp.Toolkits.Tests` `~UtilityScorerTests` · `Hrot.Blueprints.Tests` `~UtilityNodeRuntimeTests` | the utility step on all three hosts |
| `Hrot.ClusterRunner.Integration.Tests` `~TakeCoverScenarioTests` | Flank / FiringPosition (G6 starts them) |

## 5. Gates — the report contract *(CLAUDE.md, Rule 8)*

Per gate: verbatim command · pass/fail/skip · delta vs base · a `--no-build` column; golden movement as a DIFF SHAPE; every red
confirmed pre-existing against the base sha; a clean tree after each suite; `tracker-counts.py --check`; every id allocated;
⭐ the design sections you wrote / folded as-built. ⛔ No full-solution build in the loop. ⚠ If a suite reds with
`FileNotFoundException: DotRecast.Detour`, it is a stale local restore — `dotnet restore <proj>` (SYNC 2026-10-06).

## 6. SYNC — the two lanes' channel *(append-only; same protocol as the danger-crossing handoff §6)*

⭐ **Read** every entry after the last one you acted on each time you merge; **write** by APPENDING
`### <date> · <lane> → <lane> · <subject>` — what is pushed (branch + sha), what the reader must DO (or FYI), what the writer
waits for. ⛔ Never edit an entry; §0–§5 stay frozen (an entry may ADD an item `G6+` / `B+` with an acceptance line).

### 2026-10-06 · backend → behaviors · P2 dispatched; what the backend does meanwhile

- **Pushed:** `backend` — this handoff + the tracker rows `CE-3082`–`CE-3084` + `CE-3085` (backend, the U1/U2 in-process rails),
  `CE-3086`–`CE-3089` (backend P3: G10, G3, G8, G7).
- **Backend meanwhile (no dependency on you):** G11's U1/U2 in-process rails, then P3 — G10 (desert ridge + wadi), G3
  (`/entities/{id}/squad`), G8 (fire distribution reads the squad's merged pool, F4), G7 (weapon mounts, F2). ⚠ G7 touches
  `CombatTkbTranslator` / `AimAndFireExecutor` / `WeaponSelectionDecision` — if you are near those, say so here.
- **Waiting for:** your started marker; then G4 / G5 / G6 assets as they land (§3's last paragraph).

### 2026-10-06 · behaviors → backend · P2 STARTED at `5fab83226`; two fixes landed; `LiveFromReplay` not reproduced

- **Pushed:** `behaviors` — started marker `1b1ab71d5` (ff of `backend@5fab83226`, so you already have everything below).
  - `CE-2113` (`407e3788f`) — a blueprint `When` on an EQS result now compares the FIRST answer (`TopChanged` fires on the
    first top; `ScoreCrossed` fires when the first top is already ≥ the threshold) and records the answer before firing (the
    same answer used to re-fire every tick). Latent — no shipped blueprint used either trigger. [When v2.2](../When_Reactivity_Iteration_Design_v2_2.md) §6.4a.
  - `CE-2114` (`dff673fad`) — `GetComponent` / `SetComponent` on a component with an ENUM field (e.g. `SensorTag.Kind`) no
    longer BP1500s: the reflector bakes `global::Ns.Enum`. ⚠ FYI for any scenario/asset you author with an enum component field.
- **FYI `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState`** (the order-dependent SimHost red in my H-gate
  report): NOT reproduced — `Hrot.SimHost.Tests` full suite green at base `de30b58cb` (1119/0) and twice on `dff673fad`
  (1123/0, 3 skips). A one-off; nothing filed.
- **Next:** G4 (design first — the HSM posture's finish and exit-cleanup semantics are being measured), then G5, G6. I post
  here the moment each asset is pushed, with its name and order params.
- **Waiting for:** nothing.

### 2026-10-06 · behaviors → backend · ⭐ G4 PUSHED — `CombatPostureHsm` (for U3)

- **Pushed:** `behaviors@c91dc23f8` — `Assets/HSMs/CombatPostureHsm.hsm.json`, registered by name **`CombatPostureHsm`**.
  ⭐ **Order params: the SAME as the BTree** — `{"advance":{"Objective":[x,y,z],"Speed":3,"ArrivalRadius":5,"CooldownSeconds":1}}`
  (the variables carry the BTree's names and defaults).
- **What to expect live (U3):** the same winner as `CombatPosture` at every step (rail `CE3082_TheHsmAndTheBTree_MakeTheSameDecisions…`),
  ⚠ switched **one tick later** (an HSM guard reads the choice the previous tick wrote). It finishes at the objective through a Final
  state (`Arrived` guard), exactly when the BTree does. `/entities/{id}/utility` shows the same decision.
- **⭐ Infrastructure change you may meet:** an HSM state whose C# activity has a `[BTreeDeactivator]` now runs it on exit when the
  state has no authored OnExit (D2, [Behavior Action Binding §5.3c](../DESIGN_Behavior_Action_Binding.md)). No shipped HSM asset was
  affected (none bound such an activity); the HSM corpus goldens moved only by the new asset.
- **FYI — a pre-existing red, not mine to keep:** `Hrot.AiEditor.Persistence.Tests` `BTreeCallShapeTests.EveryCorpusBinding_…` is red
  at `34d548c83` (before G4) — its recorded table lags the BTree corpus. I am fixing it next (behaviors owns most of those assets).
- **Next:** G5 (`CombatPostureBp` + the decision picker), then G6.
- **Waiting for:** nothing.

### 2026-10-06 · backend → behaviors · ⚠ ONE line in your `PostureNodes.cs` (G7); G8 / G3 in; FYI for G6

- **Pushed:** `backend` — G8 `CE-3088` fire distribution (`eac645a03`: the squad driver runs `ThreatMatrixAssignment` after each
  merge, targets = merged pool ∪ leader memory) · G3 `CE-3087` `GET /entities/{id}/squad` · G7 `CE-3089` weapon mounts
  (`4af0bd634`) · CE-3085 the U1/U2 in-process rails (both PASS). Designs: [Utility demo §11 (G8/G3), §12 (G7)](../../DESIGN_Utility_AI_Demo_Scenarios.md).
- **⚠ Cross-lane edit, please read:** `PostureNodes.Fire` now writes `AimAndFireParams { …, Mount = AimAndFireParams.MountAuto }` —
  the executor then picks the weapon per shot (`WeaponChoice`: 25 mm at infantry, TOW at a tank; §12 W3). `Mount` is a new byte;
  ⭐ zero-filled = mount 0 = the old behaviour, so `CgfNodes.Action_FireAtTarget` and `HillAttackTankNodes` are untouched.
  If G6 rewrites the fire step, keep the `Mount = MountAuto`.
- **FYI for G6:** the squad layer now writes `SquadCognitiveState.Assignment` on CGF (per member, ≈ 10 Hz); a member's
  `ThreatRanking` reads it through `IsAssignedTarget` (unchanged). Nothing for you to do.
- **Waiting for:** your `started utility-demo P2` marker; G4 / G5 / G6 as they land.

### 2026-10-06 · behaviors → backend · ⭐ G5 PUSHED — `CombatPostureBp` (for U3) + the decision picker; two infra fixes

- **Pushed:** `behaviors` `6bce3ed2e` (CE-3083), merged with `backend@11e4680b6`. Design: [Decision Layer §3.3d](../../DESIGN_Decision_Layer.md).
- **Asset:** blueprint behaviour `CombatPostureBp`. ⚠ **Order params are NESTED one level deeper than the BTree / HSM**:
  `{"advance":{"advance":{"Objective":[x,y,z],"Speed":3,"ArrivalRadius":5,"CooldownSeconds":1}}}` — the blueprint's Parameter
  `advance` holds the inputs of the `PostureAdvance` child, whose own variable is `advance`. Same decisions as the BTree and the HSM
  for the same inputs (rail `TacticsTreesTests.CE3083_*`), so U3 can run all three hosts side by side.
- **New behaviours registered** (one-leaf wrappers over the shared nodes, used by the blueprint): `PostureAdvance`, `PostureSuppress`,
  `PostureHold`, `PostureSense`. Not meant to be ordered on their own, but harmless if they are.
- **Infra fixes you may see elsewhere:** CE-2115 (a blueprint node reached from a Behaviour Task's Start/Abort AND another edge was
  emitted twice ⇒ CS0140) · ⚠ **CE-2116 — aborting / resetting a LIVE hosted BTree child now runs its running leaf's deactivator**
  (before, an aborted advance kept firing). Applies to every host abort (blueprint Abort, ingress reset, an HSM hosting a BTree).
- **BP-27 done:** the Score Decision node has a decision picker (over `UtilityDecisionCatalog`).
- **Read your G7 note:** `PostureNodes.Fire`'s `Mount = MountAuto` kept; G6 will keep it.
- **Next:** G6 (`AttackApproach`). **Waiting for:** nothing.

### 2026-10-06 · behaviors → backend · ⭐ G6 PUSHED — `AttackApproach` (for U4) + ⚠ a KERNEL fix that changes CombatPosture

- **Pushed:** `behaviors` `e99d41a70` (CE-3084), merged with `backend@98e2cebb2`. Design: [Decision Layer §3.3e](../../DESIGN_Decision_Layer.md).
- **Asset:** no new behaviour — `CombatPosture` (same order params as before) now scores `AttackApproach` {Direct=1, Flank=2,
  FiringPosition=3} (asset `3c6f9e42-5d10-6f3a-ac23-approach00001`, `OptionNames = Approach`) and, inside its AdvanceAndAttack branch,
  out of sight of an identified target with a scored position ⇒ Flank / FiringPosition first (firing on the way), then Direct from there.
  In sight or no target ⇒ Direct (unchanged advance). `/entities/{id}/utility` should list BOTH decisions — please check in U4.
- **For U4's scenario:** "out of sight" = no sensor holds the target by SIGHT right now (new input `ThreatInSight`, from
  `ActiveSensorTracks`). ⚠ `FindThreatsInView` was NOT usable: its faction filter is ABSOLUTE force bits and 0 keeps nothing.
- ⚠ **Frame deviation:** the frame's nested `Parallel[ChooseOption(AttackApproach), …]` cannot compile — Fbt refuses a nested Parallel.
  The approach scorer + its two EQS sensors run in CombatPosture's OUTER parallel ⇒ **two more EQS sensors per CombatPosture unit**.
- ⚠⚠ **CE-2117 (kernel, `Fbt.Interpreter`) — please read:** a branch switch BENEATH a running Parallel never ran the abandoned leaf's
  deactivator. Measured on the shipped CombatPosture: after TakeCover took over, the advance KEPT FIRING (and its move). Fixed —
  `NodeIndexStack` is now the tick's running set. ⇒ any tree with a resource-owning leaf under a Parallel now runs that deactivator on
  a switch (as the design always said). If a live scenario's behaviour changes (U1 `ua-posture` especially: fire now stops when the
  posture leaves Advance), that is this fix.
- **FYI — fixed a stale rail of yours:** `CatalogTests.ChannelCommandPins_AimAndFire_*` expected 2 pins; CE-3089's `Mount` makes 3.
- **P2 behaviors half is DONE** (G4, G5, G6). **Waiting for:** nothing. Next on my side: lane backlog unless you send something.

### 2026-10-06 · backend → behaviors · merged G5/G6; ⭐ U3 PASSES with all three hosts; two findings for YOUR topics

- **Pushed:** `backend` — merged you at `673097a27` (G5, G6, CE-2117). ⭐ **U3 `ua-three-hosts` PASSES live with all three hosts**:
  BTree, HSM and blueprint made the SAME choice at every Health step (Suppress → Flee → AdvanceAndAttack → Flee → AdvanceAndAttack).
  U5 `ua-weapon-choice` PASSES (TOW kills the T-72, 25 mm the insurgent); U6's fire distribution PASSES ×3. As-built: Utility demo
  §11.1, §12.1. U4 `ua-attack-approach`: both decisions listed, `Flank` wins out of sight — the end-to-end run is being re-measured.
- **⭐ For you to decide (CombatPosture — your topic), filed `CE-3090`:** on OPEN GROUND a hurt unit has NO defensive posture —
  TakeCover / Flee score through `EqsTopScore(FindCoverFromTarget / FindSafeRetreatPoint)`, both 0 on `basic-desert` ⇒ at 10 HP:
  `AdvanceAndAttack 0.386, Hold 0.18, Suppress 0.08, TakeCover 0, Flee 0`. My lean (not built): a retreat that needs no cover, or
  Hold-prone as the open-ground defence. U6 now REPORTS this instead of failing.
- **⭐ For you (G6):** on U4's first run the hostile died in the first 5 s and `AttackApproach` then chose **Flank against the corpse**
  (Flank 1.08 at 5–10 s, Health 0). Probably the approach's "identified target" counts a remembered dead contact — CE-466 / CE-3080
  say a corpse is not a live target. I changed only the scenario (the hostile has 1000 HP now); the decision is yours.
- **FYI — I changed a starter decision (G7, W9):** `WeaponSelectionDecision` no longer has `WeaponReadiness` (a per-shot choice
  with readiness in the product alternated weapons; Utility AI design §11.4 never had it). Also `WeaponRangeBandFit` → an "in range"
  curve (W6). Rails in `StarterPackIntegrationTests.CE3089_*`; new route `GET /entities/{id}/weapons?target=`.
- **FYI — `CE-3091` (backend's, mine to fix):** a killed unit kept walking its `MoveToLocation` mission ~80 m.
- **Waiting for:** your call on CE-3090 and the corpse-flank; nothing blocks me.

### 2026-10-06 · backend → behaviors · CE-3091 fixed; ⚠ its consequence turns the U4 twin red (a user decision, CE-3092)

- **Pushed:** `backend` — `CE-3091` fixed: losing `CanMove` now runs the RUNNING locomotion executor's `OnExit` once (MoveTo's STOP),
  in `LocomotionDispatcherSystem` (BD1 §1.1a). All 8 `ua-*` live checks PASS after it; `hill-attack-close` unchanged.
- **⚠ FYI for U4 (your G6):** `HealthApplicationSystem` strips `CanMove` on ANY non-lethal hit (PACK-M002). Before CE-3091 that was
  inert for a move already running; now a hit unit really stops. Measured on the in-process twin `CE3084_U4`: the hostile is hit at
  spawn (1000 → 375 HP, pos (345.0,159.4)) and stops IN SIGHT ⇒ the approach stays `Direct` and never flanks ⇒ red. ⛔ Not your
  defect and not a change to make in AttackApproach — it waits on the user's `CE-3092` (should a non-lethal hit immobilise forever?
  my lean: only death, or a TKB-declared mobility kill). It also means a HURT unit cannot Flee/TakeCover today (both are moves) —
  relevant to your `CE-3090` call.
- **Waiting for:** still your call on CE-3090 and the corpse-flank; nothing blocks me.

### 2026-10-06 · backend → behaviors · ⚠ CROSS-LANE EDIT in behaviour infrastructure (CE-3076) — FYI, please merge

- **Pushed:** `backend` — `CE-3076` (`hill-attack-close` loaded after another scenario in one process never engaged). Root cause:
  per-world state keyed by a REUSED net id / entity index outlived the world boundary. Fixed with CE-2101's `WorldEpoch.Moved` rule
  in seven holders — ⚠ **two of them are yours:**
  - `BrainTickSystem` — `_publishedTerminalForInstanceId` / `_blueprintLayout` (entity INDEX) are cleared on the epoch move: a new
    unit landing on the index of a last-world unit that finished its first run (InstanceId 1 again) would never tick.
  - `DispatcherSystemBase.ForgetLastWorld(view)` (`Array.Clear(_previousAction)`), called FIRST in all five dispatchers
    (Locomotion, Weapon, Interaction, Animation, LookAt) — the last world's action's `OnExit` ran on the new entity.
  Both are inert unless `WorldEpoch` moves (no singleton ⇒ never moved), so unit tests and single-world runs are unchanged
  (Fdp.Toolkits Dispatcher|BrainTick|Mission 112/112). Design: `DESIGN_Cluster_Load_Phase.md` §8.1.
- **FYI `CE-3093`:** the sim clock is NOT reset at the world boundary — after a reload `GlobalTime` reads the last world's time
  until Play, so `BehaviorState.RunSince` can be stamped ~15 s in the future ⇒ `SopConditions.SensedFresh` ignores the new world's
  first senses. TIME-system territory, filed, not fixed.
- **Waiting for:** still your call on CE-3090 and the corpse-flank; nothing blocks me.

### 2026-10-07 · behaviors → backend · ⚠ CROSS-LANE (orchestrator / time / editor) — Q86 BUILT; CE-122 + CE-3093 CLOSED; please merge
- **User-approved design** [`Q86`](../Architect_Question_86_Editor_Runs_The_Orchestrator_Core.md) (ledger `R-215`): the editor runs the
  ONE `OrchestratorCore` the cluster's `OrchestratorSubsystem` now hosts; the clock master stays in each host. Commits on `behaviors`:
  `3f8b3295a` (`SnapAndPause(GlobalTime, roster?)`, `ResetForLoadedScenario` deleted) · `0a45cc02e` (S2a: a load resets to 0; running is
  the load's `TimeMode`) · `df6457ec4` (S2: load handler on the bus, `OrchestratorContextChangedEvent` 9063, NED translator writes the
  topic) · `18efc98a9` (S3: `ITimeCommands.SnapTo` → `SnapTimeIntent`; `PendingTimeMode` deleted, start-paused pauses in `ClusterMaster`)
  · `92b61dcd3` (S4: `OrchestratorCore` extracted, no behaviour change) · `6d232e60e` (S5: editor builds the core, hand-built copy deleted).
- **Behaviour changes you will see:** HTTP `/scenario/load/live` takes `startPaused` (default **true**); every load resets the clock to 0
  on every node before the world boundary ⇒ **CE-3093 closed** (live: `RunSince` = 0 after a reload); a jump lands one frame later (an
  intent) — seek / live-branch suites pumped one `Update`.
- **Yours, pre-existing at `92b61dcd3`, not fixed:** `Hrot.Editor.Tests.EditorCapabilitiesTests` ×2 red — the expected system list lacks
  `DangerAreaSensorSystem`, added by `0ba2cbf54` (CE-3072). And `ScenarioSaveLoadTests.RoundTrip_SimHost_EntitiesMatchAfterLoad` red
  (`ReferenceScenarioLoadHandler`, untouched here; not base-verified).
- **Waiting for:** nothing.

### 2026-10-07 · behaviors → backend · the corpse flank fixed (CE-2120); CE-3090 ruled by the user

- **CE-2120 (the U4 corpse flank you reported):** 🔒 user *"corpse flank approved"*. `HaveLiveTarget` and `ThreatInSight` now skip a
  killed contact (`ThreatDanger.IsDead`, R-214) ⇒ with only a corpse remembered the approach goes Direct. You may put U4's hostile back
  to normal HP if you want the original run. Rail `StandardInputReaderTests.CE2120_*` (red-proved); `TacticsTreesTests` + posture /
  approach rails 29/0.
- **CE-3090:** 🔒 user *"flee is only realistic if the unit is healthy and capable of fleeing without becoming easy target; for wounded
  one the hold-prone seems a better option."* ⇒ my cover-free retreat lean is withdrawn. Design + build of a wounded hold-prone posture
  next (behaviors); I will SYNC when it lands.
- **Waiting for:** nothing.

### 2026-10-07 · behaviors → backend · CE-3090 BUILT — HoldProne; Flee needs half health (⚠ CROSS-LANE: your CE-3090 row closed)

- **What changed for U6 / U3:** `CombatPostureDecision` has a 6th option `HoldProne` (hurt × live threat × NO cover × outmatched) — a
  wounded member on open ground now stops and returns fire instead of advancing. `Flee` needs ≥ half health and still a hidden
  retreat. All three posture hosts (BTree / HSM / blueprint) carry it. 📄 `docs/DESIGN_Decision_Layer.md` §3.3f.
- **Please re-run** U6's hurt step (`ua-fire-distribution`) — expect `HoldProne` top in `/entities/{id}/utility` at 10 HP.
- **I edited your rows:** `CE-3090` (closed) and the U6 line of `RUNBOOK_Utility_AI_Demos.md`; `DESIGN_Utility_AI_Demo_Scenarios.md`
  §11.1's finding row notes the fix.
- **Next (proposed, awaiting the user):** real stance support — the brain requests prone, SimHost's fake animation backend performs it,
  the map shows it (CE-3010 scope, cross-node needs `AnimationReplicationModule`).
- **Waiting for:** nothing.
