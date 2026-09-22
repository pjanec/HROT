<!--STATUS
state: LIVE
doc-type: RESUMPTION for P4 — retiring BrainBlackboard and Blackboard1024.
  ⚠ A STATE doc, not canon. Every "measured" line is dated; ⛔ VERIFY against git before acting.
updated: 2026-09-22
build-state: n/a — a build resumption, not a design. The DESIGN is §30 of
  DESIGN_Occurrence_Scoped_Storage.md and it is READY-TO-BUILD.
current-answer: ⭐ START AT §3 (the slices, in order) — but read §1 and §2 FIRST, they are short.
  §1 is the state to verify; §2 is what must NOT be re-litigated. §4 is the traps already paid for,
  §5 the gates and the harness, §6 what is still unexplained.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — §30 IS THE DESIGN for this work (four slices, UML,
    acceptance). §29.12/§29.12a are the CE-304 as-built this builds on. It wins on any disagreement.
  - RESUME_Occurrence_Storage.md — the PROGRAMME-level resume (P0–P4). Its §0c is the path;
    this doc owns the P4 build detail that §0c does not carry.
  - Blueprint_Issues_Tracker.md — CE-303 (three editor surfaces), CE-306 (ExtDeps contamination),
    CE-307 (the 100-byte cap). All three are P4 work.
  - DESIGN_Parameter_Model.md — owns WHAT a parameter is; §4.7 is the hosted/root split.
-->

# RESUME — `P4`: retire `BrainBlackboard` and `Blackboard1024`

> 🔒 **The goal, in the user's words:** *"we will retire it unless we find a true need and do not see
> any. Being part of ABI is no reason, ABI can and must change."*

---

## 1. ⭐ STATE — **verify these, do not trust them**

| | |
|---|---|
| branch | **`behaviors`** @ **`65d62e583`**, clean, pushed |
| `P0`–`P3` | ✅ **DONE AND LIVE-VALIDATED.** `CE-304` closed `2026-09-22`; `hill-attack-close` is **2/2 gold** at the fixed HEAD |
| `P4` | ⛔ **not started.** Designed: 📄 `DESIGN_Occurrence_Scoped_Storage.md` **§30** |
| ⚠ the stash | `stash@{0}: EXPERIMENT: RootParamsBytes always 100 — probe only`. ⛔ **A DIAGNOSTIC. Never commit it** |

### ⭐ The commits this work builds on

| sha | what |
|---|---|
| `5df21c367` | 🔴 **`CE-304`** — `BTreeActionGenerator.cs:655` re-anchored + the rail |
| `9830ca2cb` | the `CE-305` emission golden |
| `3b3ce8bff` | `CE-304` closed — 2/2 gold on the cluster; `P4` unparked |
| `7319bea46` | `P4` re-scoped (`byte` binding, StructEdit offset) — **§30** |
| `65d62e583` | `P4`-②b (the wrapper structs go) + `CE-306`/`CE-307` filed |

---

## 2. ⛔⛔ SETTLED BY THE USER — **do not re-litigate**

| # | ruling |
|---|---|
| **①** | ⭐⭐⭐ **`TBlackboard` is BOUND TO `byte`, not removed.** 🔒 *"cant we simply pass byte reference instead, pointing to the slot's memory region?"* ⇒ **zero FastBTree changes** |
| **②** | ⭐⭐ **StructEdit learns nothing.** 🔒 *"It should now nothing about the dto is inside some ither structure. The thunk calling it should provide the DTO struct reference"* ⇒ the CALLER supplies the base |
| **③** | ⭐⭐⭐ **The curated registrar names NOTHING.** 🔒 *"Why would it need to name the cincrete data type? Ahouldnt it live with the byte regerence as well?"* ⇒ the five wrapper structs die too |
| **④** | ⭐ **The action is HANDED its DTO** *(`2026-09-21`)* — it never looks for it. Already true of every `[SharedAiAction]` body |
| **⑤** | ⭐ **The UI lane fence is LIFTED** — *"and do UI stuff yourself"* |

---

## 3. ⭐⭐⭐ THE SLICES, IN ORDER

### `P4`-① — delete `Blackboard1024` *(pure deletion, do this first)*

📐 **It is on ZERO entities**: every production assignment is `HeavyDtoType = null`
*(`HsmAssetMapper.cs:493`, `BehaviorTreeAssetMapper.cs:443`)*, and ingress only adds the component when
`HeavyDtoType != null`.

| delete | |
|---|---|
| `Renderers/Blackboard1024Renderer.cs` · `Renderers/Blackboard1024ViewProvider.cs` | the two surfaces |
| the `Blackboard1024` arm of `Windows/BlackboardReflection.cs` *(`:45`, and `:50`'s `EditContextFactory`)* | |
| the component + `HeavyDtoType` + its ingress add *(`BehaviorIngressSystem.cs:136-141, :299-304`)* | |
| ⚠ also touches `BlueprintLiveValueWriter.cs` · `EditorSubsystem.cs` · `CgfSubsystem.cs` | registration sites |

> 🔴🔴 **THE TRAP: `Blackboard1024` and `BlueprintBlackboard1024` ARE DIFFERENT TYPES.** The first is the
> legacy heavy blackboard and **dies**; the second is an occurrence-store **tier** and **stays**.
> ⛔ `BlueprintBlackboard1024Renderer.cs` must NOT be deleted.

### `P4`-② — bind `TBlackboard` to `byte`

⭐ `BTreeTickSystem.cs:123` currently does `ref var blackboard = ref repo.GetComponentRW<BrainBlackboard>(entity);`
and hands it to `Tick` at `:159`. ⇒ replace with **one resolve of the root slot base per entity**.

| | |
|---|---|
| ⭐ the kernel | ⛔ **UNCHANGED.** `Interpreter.cs:9` is `where TBlackboard : struct`; 📐 **zero** by-value or sized uses of `TBlackboard` anywhere in `FDP/ExtDeps/FastBTree/src` |
| ⭐ the emitters | `BlackboardParamsExpression`'s **BTree arm reverts to `bb`-relative** — this SIMPLIFIES `CE-304`'s fix and moves the resolve from per-dispatch to per-tick *(which is `CE-301`'s concern, answered without a cache)* |
| ⚠ the HSM arm **stays** `RequireRootBytes(world, self)` | `HsmActionDispatcher` hands its thunks `instance`/`context`, **never a blackboard ref**. ⛔ Two expressions for two dispatch shapes — **NOT the `BP-306` duplication it will look like** |
| ⛔ **the no-params case** | a behaviour with no params has **no root slot**, so `RootRef` THROWS on `Idle`/`WanderMilitary`. Gate on the checkable predicate **`RootParamsBytes(def) == 0`** — ⛔ never a null-guess |

### `P4`-②b — delete the five wrapper blackboard structs

📐 Each is `{ public XParams Params; }` with **exactly one use site**, and exists only to make
`bb => bb.Params` resolve to offset 0:

`CgfNodes.cs:55` `MoveToBlackboard` · `:59` `FollowRouteBlackboard` · `:63` `JoinFormationBlackboard` ·
`HillAttackDtos.cs:240` `HullDownAttackBlackboard` · `PlatoonHillAttackBlackboard`.

```csharp
new BTreeBuilder<byte, BTreeContext>()
    .Action($"{typeof(CgfNodes).FullName}.{nameof(Action_WriteMoveToChannel)}@0");
```

⭐ `BTreeBuilder` **already** has `Action(string methodKey, …)` *(`:236`)* and `Condition(…)` *(`:263`)*
⇒ ⛔ **no ExtDeps change, not even an additive overload.**

> 🔴🔴 **THE HAZARD, AND IT IS THE `CE-304` SHAPE AGAIN.** The selector form is compile-time checked; the
> key form is not. 📐 `Interpreter.BindActions:697-709` binds an unknown key to a **fallback returning
> `NodeStatus.Failure`** after one `Console.WriteLine`. ⇒ a typo is a silent behaviour change.
> ⛔ **MANDATORY:** build keys with `nameof`, **and** add a rail that walks every `FbtTreeCatalog` blob
> against a populated `ActionRegistry` asserting **ZERO fallbacks**.

### `P4`-③ — re-home the three editor surfaces *(`CE-303`)*

⛔ **RE-HOME, not re-anchor** — each is keyed on the component's IDENTITY, so deleting the type deletes
the entry point:

| surface | key | the re-home |
|---|---|---|
| `BrainBlackboardRenderer.cs:19` | `[ImGuiRenderer(typeof(BrainBlackboard))]` | a root-params arm on the tier renderers *(§25.3 already decodes occurrences)*. ⭐ The root slot is the EASIEST label — `ComputeRootParamsKey` is ONE computation, so §25.2's forward search does not apply |
| `BrainBlackboardViewProvider.cs:22` | component + `$.BehaviorParameters` path match | StructEdit takes an **offset**: 📐 `BufferViewRequest.cs:88-102` already composes `NativeOffset + Marshal.OffsetOf(...)`; only `NativeOffset`/`Buffer` being `internal` blocks a provider supplying its own base ⇒ an **additive optional parameter** |
| `LiveBlackboardValueProvider.cs:81,176,188` | `session.GetComponent(…, typeof(BrainBlackboard))` | read the **tier** component from the `IDebugSession`, walk to the key, project at its offset |

### `P4`-④ — retire the 100-byte cap *(`CE-307`)* — ⚠ **WITH or AFTER ②, never before**

📐 `BehaviorConstants.cs:32` *(truth)* · `BehaviorParameterSizeAnalyzer.cs:26,64` *(a Roslyn analyzer
with its own mirror)* · `BehaviorRegistry.cs:319` *(runtime throw)*.
⛔ Both its justifications are already false — see §30.11.

---

## 4. ⛔⛔ TRAPS ALREADY PAID FOR — **do not re-pay**

| | |
|---|---|
| 🔴 **an inventory keyed on a NAME misses a CAST** | `CE-304` was invisible to `grep BehaviorParameters` because the line said `Unsafe.As<BrainBlackboard, TParams>(ref bb)`. ⇒ **enumerate by the storage TYPE** (graph / Roslyn), never by a spelling |
| 🔴 **the tank rails pass a hand-built `p`** | they test the node BODY, never the params ADDRESSING. ⭐ `CE304_ReverseToBaseline_Thunk_ReadsAuthoredParams_FromTheRootSlot` is the only rail that drives ingress → slot → the REAL thunk. ⛔ **It must stay green through `P4`** |
| ⚠ **`Blackboard1024` ≠ `BlueprintBlackboard1024`** | one dies, one stays. See `P4`-① |
| ⚠ **the vendored generator is a SECOND fork** | `Fbt.SourceGen` still carries the pre-`CE-304` bridge at `:616` and hard-codes FDP names at `:40-42` — `CE-306`. ⛔ Nothing in HROT consumes it; do not "fix" `CE-304` there and think it shipped |
| ⚠ **`RW-S` rows are invisible to `tracker-counts.py`** | a known gap filed as `CE-259at`. ⛔ Do NOT "fix" the rows |
| ⛔ **`pkill -f ClusterRunner` kills your own shell** | kill by **PID**: `ps -eo pid,args --no-headers \| awk '$0 ~ /Hrot\.Cluster/ {print $1}' \| xargs -r kill` |
| ⛔ **`git worktree` cannot build here** | ~136 `NETSDK1004`. Baseline by `git checkout <sha>` in the MAIN tree |
| ⚠ **build the TEST project, not the production one** | `--no-build` against a production-only build runs a stale binary |

---

## 5. ⭐ GATES — **the baseline to beat, measured `2026-09-22` at `3b3ce8bff`**

| suite | baseline | note |
|---|---|---|
| `Fdp.Toolkits.Tests` | **2303 / 0** | |
| `Hrot.Blueprints.Tests` | **4017 / 0** *(18 skipped)* | |
| `Hrot.AiEditor.Generators.Tests` | **279 / 4** | the four are PRE-EXISTING: `S3_BehaviorScopedThunkTests`, `S3_SharedSlotProvisioningTests` ×2, `T30_BehaviorScopedShared_ProofTests` |
| `Hrot.SimHost.Tests` | **1003 / 3–5** | ⚠ **FLAKY UNDER LOAD.** In isolation exactly **3**: `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete` · `MapPresentationParityRails…EditorStrideSubsystem.cs` · `FullBranchPipelineTests.BranchedRecording…`. ⭐ The LOAD-FLAKY family is **larger than 1**: `EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule` (**`BP-534`**, appears while a cluster runs concurrently) **and** `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState` (observed `2026-09-22` during `P4`-⑥; ✅ **3/3 twice in isolation**). ⚠ Confirm a 4th red in ISOLATION before calling it a regression |
| `Hrot.IG.Tests` | ⛔ cannot build here | `NETSDK1004`, unrestored |

### ⭐⭐ THE ACCEPTANCE — **`hill-attack-close` must stay 2/2 gold**

```bash
cat > /tmp/launch-all.sh <<'SH'
#!/bin/bash
cd /home/user/HROT
exec env HROT_DEBUG_API_PORT=8111 xvfb-run -a --server-args="-screen 0 1600x1000x24" \
  dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode all
SH
chmod +x /tmp/launch-all.sh
setsid nohup bash /tmp/launch-all.sh > /tmp/cluster.log 2>&1 < /dev/null & disown
until grep -qiE "Debug API asset creation attached|Aborted" /tmp/cluster.log; do sleep 3; done

B=http://localhost:8111
curl -s --noproxy '*' -X POST $B/scenario/load/live -H 'Content-Type: application/json' \
     -d '{"name":"hill-attack-close","waitForReady":true}'      # want sawWorldChange: true
curl -s --noproxy '*' -X POST $B/sim/play -H 'Content-Type: application/json' -d '{}'
# …wait for /sim/state totalTime >= ~70, then:
curl -s --noproxy '*' -X POST $B/perspective -H 'Content-Type: application/json' -d '{"name":"Scenario"}'
curl -s --noproxy '*' $B/entities/1001    # …1004, and 1006/1007 for the targets
```

⭐ **PASS** = `1001`–`1004` at **x ≈ 523–531** with `LocomotionChannel.Status: Success`, **and**
`1006`/`1007` at `Health.Current: 0`. ⛔ **FAIL** = three tanks left at x ≈ 578–590.
⚠ **Dead bodies STAY** (`CE-272`) — the entity count does **not** fall.
⚠ `127.0.0.1` 404s every route — always `--noproxy '*'` and the `localhost` hostname.

---

## 6. ⚠ STILL UNEXPLAINED — **carried forward, not load-bearing**

| | |
|---|---|
| ⚠ **the failing runs still reached the firing line** | the platoon spawns at x ≈ 446–449 yet the broken build ended at 579/587/590, and a stranded tank's `NavState` read a correct `[523, 401]`. An all-zero region from the first dispatch should have commanded `(0,0)`. 📐 There is **no spawn-time value writer** — `BehaviorTkbTranslator.cs:126` adds a **zeroed** component and `BrainBlackboardTranslator.Inject` is a no-op. ⇒ **no mechanism named.** 📄 §29.12 |
| ⚠ **the 100-byte probe** | forcing `RootParamsBytes` to 100 made the gold test pass 2/2 **before** the fix. Widening it cannot revive a component nobody writes ⇒ **unexplained**; §29.11's candidates A/B/C are **unconfirmed, not disproven** |

⛔ **Neither blocks `P4`** — the gold test is green. ⚠ But if `P4` reddens the cluster in a way the unit
rails miss, **re-read these two first**: they are the known-unknowns in this area.

---

## 7. ⭐ THE EXACT FIRST ACTION

1. `git fetch origin behaviors && git status` — expect clean at `65d62e583`.
2. Read **`DESIGN_Occurrence_Scoped_Storage.md` §30** end to end *(it is the design; this doc is only the state)*.
3. Build **`P4`-①** — the pure deletion. ⛔ Mind the `Blackboard1024` / `BlueprintBlackboard1024` trap.
4. Run `scripts/quick-check.sh` on each touched project, then the §5 suites, then the §5 acceptance.
