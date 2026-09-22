<!--STATUS
state: LIVE
doc-type: RESUMPTION for P4 — retiring BrainBlackboard and Blackboard1024.
  ⚠ A STATE doc, not canon. Every "measured" line is dated; ⛔ VERIFY against git before acting.
updated: 2026-09-22
build-state: n/a — a build resumption. The DESIGN is DESIGN_Occurrence_Scoped_Storage.md §30,
  and §30.18 is the CURRENT slice table (§30.5's is SUPERSEDED).
current-answer: ⭐ START AT §6 — `P4`-② IS COMPLETE (rail green, cluster 2/2 gold, 2026-09-22).
  What is LEFT is `P4`-③ (the six identity-keyed surfaces) and `P4`-④ (the 100-byte cap), then
  `BrainBlackboard` itself. §2 has the slice detail, §1 what is done, §3 traps, §4 gates, §5 open.
  ⛔ The rail's AS-BUILT shape is DESIGN_Occurrence_Scoped_Storage.md §30.21, which supersedes
  §2 ①'s sketch — the sketch was measured and was wrong on both halves.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — §30.18 the CURRENT slice table · §30.19 G2/G3 and its
    correction · §30.20 the as-built + the three-instance dead-storage pattern. It wins on any
    disagreement with this file.
  - RESUME_Occurrence_Storage.md — the PROGRAMME-level resume (P0–P4). ⚠ Its §0c predates the
    2026-09-22 re-scope; this doc owns the P4 build detail.
  - Blueprint_Issues_Tracker.md — CE-303, CE-307, CE-308, CE-309 open; CE-310, CE-311 DONE.
-->

# RESUME — `P4`: retire `BrainBlackboard` and `Blackboard1024`

> 🔒 **The goal, in the user's words:** *"we will retire it unless we find a true need and do not see
> any. Being part of ABI is no reason, ABI can and must change."*
> 🔒 **And the guard rail:** *"'no real users' does not mean 'not needed', be cautious before deleting
> anything."*

---

## 1. ✅ WHAT IS DONE — **do not redo this**

⭐ **`behaviors` @ `a62507e3d`, clean, all four suites at baseline, solution builds with 0 `error CS`.**

| commit | |
|---|---|
| `38e52845d` | **`CE-310`** — routed the AiPrimitive working-state **WRITE** path onto occurrences |
| `05d15ec5f` | **`CE-311`** — routed the **inline AiPrimitive emitter** off `Blackboard1024` |
| `0b25c595d` | ⭐⭐ **`P4`-① — `Blackboard1024` IS DELETED.** 54 code files |
| `fadc2e205` | `hill-attack-close` **2/2 GOLD** on the post-deletion build |
| `746c1b123` | ⭐ **`P4`-⑤ — the stale corpus.** 2 STATUS blocks, 9 crefs, a no-op builder deleted |
| `3ac1af9f4` | `P4`-② WIP *(superseded by the next commit — it did not build)* |
| `0ec86d864` | this doc, rewritten mid-flight |
| `a62507e3d` | ⭐⭐⭐ **`P4`-② — `TBlackboard` IS BOUND TO `byte`.** The tree tick takes the root slot |

⭐ **`Blackboard1024` no longer exists.** Component id **74 is RESERVED**, not reused
*(`GlobalComponentIds.Reserved_WasBlackboard1024`)*.

⛔ **Deliberately NOT deleted** — `BlackboardTarget` *(the predicate axis; `CE-308` **RE-POINTS** it in
`P4`-③)* and `BlackboardTier.Blackboard1024` *(the compiler's tier selector, shares only the spelling)*.
⛔ **`P4`-②b is WITHDRAWN** — the wrapper structs stay, and deleting them is unsafe for `HideInCover`
*(§30.18)*.

### ⭐⭐ `P4`-②'s KEY RESULT — **`G2`'s prediction held**

Both golden families regenerated and inspected: **Blueprints 28 files 60+/60−**, **AiEditor emit 20
files 78+/78−**. Perfectly symmetric; every changed line is one of **three shapes** *(the thunk
lambda's ref param · the `Register` signature's `ActionRegistry<>` · the `Interpreter<>` construction)*
⇒ **pure type-name substitution.** ⭐ **None of §30.19's STOP conditions fired** — the `@0` keys are
unchanged, no `(nint)` offset moved, no `{Asset}_…` struct was renamed.

---

## 2. ⭐⭐⭐ WHAT IS LEFT

### ⚠ `P4`-② still owes TWO things

| # | |
|---|---|
| **①** | ✅ **THE ZERO-FALLBACK RAIL — BUILT AND GREEN** *(`2026-09-22`)*. ⛔ **Its shape is NOT the one sketched below — see `DESIGN_Occurrence_Scoped_Storage.md` §30.21**, which supersedes it. The sketch was measured and failed: `BuildFromAssembly` is the wrong registry *(6 `PlatoonHillAttack` keys come from the generated `[BlueprintRegistrar]`)* and `FbtTreeCatalog` is a superset of what production ticks *(9 `HideInCover` keys belong to trees no registrar registers)*. ⭐ Built instead as: `Interpreter.UnboundMethodNames` *(a new production API — the miss list, populated by the same branch that installs the fallback)* + `BlueprintRegistrarScanner.Scan` to enumerate the trees production actually ticks + a **negative control**. 4/4 green in `BTreeActionRegistryFactoryTests` |
| ~~①~~ | ⛔ **HISTORY — the sketch, SUPERSEDED by §30.21.** 🔴🔴 **THE ZERO-FALLBACK RAIL — not written yet.** ⛔ It is the ONLY thing that catches the reflection hazard: `BTreeActionRegistryFactory.cs:64`, `BlueprintRegistrarScanner.cs:126` and `AiHotReloadCoordinator.cs:433` each compare `ps[0].ParameterType != typeof(ActionRegistry<byte, BTreeContext>)` and **`continue` on mismatch** ⇒ if the generator and these ever disagree, `RegisterAll` is **silently skipped** and **every action falls back to `Failure`**. ⚠ The compiler cannot see it; `Interpreter.BindActions:697-709` binds an unknown key to a fallback returning `Failure` after one `Console.WriteLine`. ⭐ `Stage7Tests`' `ActionRegistry<byte, …>` assertion is only HALF the guard |
| **②** | ✅ **the cluster acceptance is RE-RUN and 2/2 GOLD** *(`2026-09-22`)* — `523.10 524.91 528.32 531.23` and `523.03 525.27 528.46 531.14`, all `Success`, both targets `Health 0`, entity count 8, **and zero `[FastBTree] Warning` lines in the live log**. ⚠ **One trial per cluster PROCESS** — see §3's new trap |

#### ⭐ HOW TO WRITE THE RAIL *(the design, measured — not yet built)*

⛔ **Do NOT capture `Console` output** — that is the fallback's symptom, not its definition.
⭐ **Ask the registry directly**, which is deterministic:

1. Build the real registry: `BTreeActionRegistryFactory.BuildFromAssembly(typeof(CgfNodes).Assembly)`.
2. Enumerate every generated `FbtTreeCatalog.Get*()` — each returns an untyped `Fbt.BehaviorTreeBlob`.
3. For every blob, for every leaf node, take its `blob.MethodNames[…]` key and assert the registry
   resolves it *(`TryGetAction` / `TryGetCondition`)*.
4. ⭐⭐ **Assert ZERO unresolved keys**, and name every miss in the failure message.

⚠ **`FbtTreeCatalog` is GENERATED** *(`obj/GeneratedFiles/…/BTreeDefinitionGenerator/FbtTreeCatalog.g.cs`)*
— enumerate its `Get*` methods by reflection so a new tree is covered automatically. ⛔ A hand-written
list of trees is the wrong shape: it goes stale exactly when a new tree is added, which is when the rail
is most needed.

### ⭐ THEN THE REMAINING SLICES

| slice | |
|---|---|
| **`P4`-③** | re-home the **SIX** identity-keyed surfaces *(`CE-303`)*: `BrainBlackboardRenderer` · `BrainBlackboardViewProvider` · `LiveBlackboardValueProvider` · `HillAttackGizmo`'s `[GizmoProjector]` gate · `PredicateCompiler` + `BlackboardTarget` *(`CE-308` — **RE-POINT** its members onto {root params, node working state}, ⛔ do not delete the axis)* · `BrainBlackboardTranslator`'s gate. 📄 §30.14 has the per-surface table |
| **`P4`-④** | retire the 100-byte cap *(`CE-307`)* — `BehaviorConstants.cs:32` · `BehaviorParameterSizeAnalyzer.cs:26,64` · `BehaviorRegistry.cs:319`. ⭐ The real structural ceiling is **16 096 B** *(§30.15)*, so the cap is **160× low** and guards a region that no longer exists |
| ⚠ **then `BrainBlackboard` itself** | once ③ lands nothing is keyed on it. ⛔ **Re-run §3's dead-storage check before deleting** — that is what found `CE-310` and `CE-311` |

---

## 3. ⛔⛔ TRAPS ALREADY PAID FOR — **do not re-pay**

| | |
|---|---|
| 🔴🔴 **THE DEAD-STORAGE PATTERN — 3 instances, and it is this lane's whole lesson** | `CE-304`, `CE-310`, `CE-311`: **a capability whose STORAGE moved, whose CALLERS were never re-anchored, and whose RAIL kept it green by building a world production stopped building.** ⛔ No static signal sees it — `CE-311`'s in-degree looked healthy. ⭐ **The check: ask *"which production site PROVISIONS the storage this reads?"* — never *"who calls this?"*** |
| ⭐⭐ **the per-test fix depends on WHICH THUNK the test runs** | a **hand-written** thunk reads the HANDED-IN base ⇒ the test owns a plain `byte[]`; a **generated** thunk resolves through `RootParamsAccess` and ignores it ⇒ the test takes `RootParamsAccess.RootRef(world, entity)`. 🔒 **That judgement is why `P4`-②'s tail was NOT batch-substituted** |
| ⛔ **borrowing an ECS component as a scratch buffer** | the habit that hid `CE-310`/`CE-311`. ⭐ A plain `byte[]` cannot be mistaken for a storage path |
| 🔴 **`G3` under-measured the asset-driven generator** | there are **TWO** BTree generators. `BTreeActionGenerator` (analyzer) is polymorphic; **`BTreeBridgeEmitCore` derived the dispatch type from the ASSET** and threaded it into 10 sites. ⭐ Fixed at `:310`, **not** in the asset |
| ⛔⛔ **NEVER retarget the asset's `BlackboardTypeName`** | it also mangles into the params-layout struct name **and** `SubtreeSyncIdentity.Derive`, which **MATCHES SUBTREES** ⇒ renames 11 structs across 44 files and breaks matching silently. 📄 §30.19 |
| ⚠ **the generated BUILDER keeps the asset's type** | `BTreeEmitCore.cs:408` — selector-form bindings need a struct with fields; `byte` has none |
| 🔴 **ANY COMMAND-LINE PATTERN MATCH KILLS YOUR OWN SHELL** | ⛔ the pattern appears in your OWN command line, so it matches itself *(exit 144)*. 📌 Hit **twice**: `ps \| awk '/Hrot\.Cluster/'` and — `2026-09-22` — **`pkill -f Xvfb`**. ⛔ "kill by PID" does **not** prevent it, and it is NOT an `awk` quirk: it is **matching on the full command line at all**. ⭐ Filter on the **`comm`** field only: `ps -eo pid,comm --no-headers \| awk '$2=="dotnet"{print $1}' \| xargs -r kill`. ⚠ Xvfb dies with its `xvfb-run` child anyway — do not chase it |
| 🔴🔴 **A RELOAD IS NOT A RESET — `sawWorldChange` IS THE CHECK** | 📌 `2026-09-22`: trial 2 was run by re-POSTing `/scenario/load/live` on the SAME process. It answered **`sawWorldChange: false`**, `/sim/play` reported **`totalTime: 84.9`**, and the "results" were trial 1's end state drifting. ⛔ **Two trials on one process are ONE trial.** ⭐ **Restart the cluster between trials** and require `sawWorldChange: true` **and** a `totalTime` near 0 at play time |
| ⚠ **an over-broad substitution corrupts DOC COMMENTS** | `ref bb.BehaviorParameters[0]` → `ref bb` hit two comments that deliberately described the **OLD** anchor. ⭐ **Always `git diff --name-only \| grep -v Tests` after a scripted edit** |
| ⚠ **the LOAD-FLAKY family is >1** | `BP-534` · `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState` · `SquadInputsP3Tests.AllReaders_ZeroAlloc_After1MillionCalls`. ⛔ Confirm any extra red **IN ISOLATION** before calling it a regression |
| ⚠ **`NETSDK1004` × ~60 is PRE-EXISTING** | unrestored `Stride/` projects. ⭐ Filter on `error CS` |
| ⚠ **build the TEST project, not the production one** | `--no-build` against a production-only build runs a stale binary |
| ⛔ **do NOT "fix" `RW-S` tracker rows** | invisible to `tracker-counts.py` — known gap `CE-259at` |

---

## 4. ⭐ GATES — **the baseline, all MET at `a62507e3d`**

| suite | baseline |
|---|---|
| `Fdp.Toolkits.Tests` | **2299 / 0** ⚠ was 2303 pre-`P4`-①; **−4 = the deleted `Blackboard1024Tests`** |
| `Hrot.Blueprints.Tests` | **4017 / 0** *(18 skipped)* |
| `Hrot.SimHost.Tests` | **1005 / 3** *(the 3 documented)* |
| `Hrot.AiEditor.Generators.Tests` | **279 / 4** *(the 4 documented)* |
| solution build | **0 `error CS`** |
| docs | `design-digest --check` · `rulings-check` 38/38 · `tracker-counts --check` · `mermaid-check` |

⭐ **Golden regeneration switches:** `BLUEPRINT_REGENERATE_SNAPSHOTS=1` *(Blueprints)* ·
`AI_REGENERATE_SNAPSHOTS=1` *(AiEditor.Generators)* — ⚠ deliberately separate so one cannot silently
regenerate the other.

### ⭐⭐ THE CLUSTER ACCEPTANCE — **`hill-attack-close`, 2/2 gold**

```bash
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj --no-restore
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
curl -s --noproxy '*' -m 60 -X POST $B/scenario/load/live -H 'Content-Type: application/json' \
     -d '{"name":"hill-attack-close","waitForReady":true}'          # want sawWorldChange: true
curl -s --noproxy '*' -m 20 -X POST $B/sim/play -H 'Content-Type: application/json' -d '{}'
# wait for /sim/state totalTime >= 72, POST /perspective {"name":"Scenario"}, then read
#   /entities/1001..1004 -> data.Components.SimTransform.Position[0] and LocomotionChannel.Status
#   /entities/1006,1007  -> data.Components.Health.Current
# teardown:  ps -eo pid,comm --no-headers | awk '$2=="dotnet"{print $1}' | xargs -r kill
```

⭐ **PASS** = `1001`–`1004` at **x ≈ 523–531** with `LocomotionChannel.Status: Success`, **and**
`1006`/`1007` at `Health.Current: 0`, **and** entity count **8** *(dead bodies stay — `CE-272`)*.
📐 **The `P4`-① gold:** `523.03 525.25 529.14 531.37` and `523.03 525.26 529.44 531.37`.
⚠ **A retirement must not move the simulation at all** — that is the actual assertion.
⚠ Always `--noproxy '*'` and the `localhost` hostname; `127.0.0.1` 404s every route.

---

## 5. ⚠ STILL OPEN

| | |
|---|---|
| ⚠ **`G4`** | the complete no-params set. 📐 `WanderMilitary` confirmed *(no `BlackboardLayoutType`, `CgfCuratedBehaviorRegistrar.cs:88`)*; **`Idle` unconfirmed**. ⭐ `BTreeTickSystem` gates on `RootParamsBytes(def) == 0`, so a miss is a throw, not silent |
| ⚠ **`G5`** | `HostedSubtree.Tick<TChildBb>` + `BTreeOrchestratorEmitCore.cs:151,182` are a **second `TBlackboard` axis**; where a hosted subtree's `ref subBb` comes from was never measured |
| ⚠ **`CE-306`** | the vendored `Fbt.SourceGen` fork still carries the pre-`CE-304` bridge at `:616` and hard-codes FDP names at `:40-42`. ⛔ Nothing in HROT consumes it |
| ⚠ **the two §29 unknowns** | the failing runs reaching the firing line with no spawn-time writer; the 100-byte probe passing 2/2 *before* the fix. ⛔ Neither blocks anything — ⭐ but **re-read them first if `P4` reddens the cluster in a way the unit rails miss** |

---

## 6. ⭐ THE EXACT FIRST ACTION

⭐⭐ **`P4`-② IS COMPLETE** — the rail is built and green, and the cluster acceptance is 2/2 gold.

1. `git fetch origin behaviors && git status` — expect **clean**.
2. **`P4`-③** *(the six identity-keyed surfaces — §2's table, per-surface detail in `DESIGN` §30.14)*.
   ⛔ **`CE-308` RE-POINTS `BlackboardTarget`; it does not delete it.**
3. **`P4`-④** *(retire the 100-byte cap — `CE-307`)*.
4. Then `BrainBlackboard` itself — ⛔ **re-run §3's dead-storage check first.**

#### ⛔ HISTORY — the first action as it stood before `2026-09-22`

1. `git fetch origin behaviors && git status` — expect **clean at `a62507e3d`**.
2. Write **the zero-fallback rail** *(§2 ①)* — it is the one guard `P4`-② is missing.
3. Run **the cluster acceptance** *(§4)* and compare against the `P4`-① numbers.
4. Then `P4`-③ *(the six surfaces)* and `P4`-④ *(the cap)*.
