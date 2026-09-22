<!--STATUS
state: LIVE
doc-type: RESUMPTION for P4 — retiring BrainBlackboard and Blackboard1024.
  ⚠ A STATE doc, not canon. Every "measured" line is dated; ⛔ VERIFY against git before acting.
updated: 2026-09-22
build-state: n/a — a build resumption. The DESIGN is DESIGN_Occurrence_Scoped_Storage.md §30,
  and §30.18 is the CURRENT slice table (§30.5's is SUPERSEDED).
current-answer: 🔴 START AT §2 — `P4`-② IS IN FLIGHT AND THE TREE DOES NOT BUILD.
  §1 is what is DONE (do not redo it). §2 is the exact remaining work and the pattern that fixes it.
  §3 is what must still happen before `P4`-② can be called landed. §4 traps. §5 gates. §6 open.
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

| commit | |
|---|---|
| `38e52845d` | **`CE-310`** — routed the AiPrimitive working-state **WRITE** path onto occurrences |
| `05d15ec5f` | **`CE-311`** — routed the **inline AiPrimitive emitter** off `Blackboard1024` |
| `0b25c595d` | ⭐⭐ **`P4`-① — `Blackboard1024` IS DELETED.** 54 code files, 17 prod + 37 test |
| `fadc2e205` | `hill-attack-close` **2/2 GOLD** on the post-deletion build |
| `746c1b123` | ⭐ **`P4`-⑤ — the stale corpus.** 2 STATUS blocks, 9 crefs, and a no-op builder deleted |
| 🔴 `3ac1af9f4` | ⛔⛔ **`P4`-② WIP — THE TREE DOES NOT BUILD.** See §2 |

⭐ **`Blackboard1024` no longer exists.** Component id **74 is RESERVED**, not reused
*(`GlobalComponentIds.Reserved_WasBlackboard1024`)* — a recording written before the retirement still
names 74, and binding it to a different component would decode those bytes as the wrong type.

⛔ **Deliberately NOT deleted, and both are recorded where a reader will look:** `BlackboardTarget`
*(the replay-browser predicate axis — `CE-308` **RE-POINTS** its members in `P4`-③, because "which
memory region?" still has two answers)* and `BlackboardTier.Blackboard1024` *(the Blueprint compiler's
tier selector, which only shares the spelling)*.

---

## 2. 🔴🔴 WHERE `P4`-② STOPPED — **production compiles, ~76 TEST errors remain**

⛔ **`3ac1af9f4` DOES NOT BUILD. Do not branch from it expecting green.**

### ⭐ Done (production, compiling)

`95` type arguments across 29 files · the **4 authored declarations** *(`CgfNodes.cs:417,609`,
`HillAttackTankNodes.cs:560,580` → `ref byte`)* · the 4 Blueprints-compiler emission lines
*(`AiPrimitiveEmitter:513,530`, `CSharpEmitter:204,438`)* · 🔴 **`BTreeBridgeEmitCore.cs:310`
`bbShort = "byte"`** *(the `G3` correction — §4)* · **`BTreeTickSystem`** resolving the root slot
**once per entity per tick**, gated on `RootParamsBytes(def) == 0` · `CgfNodes.BuildWanderMilitaryTree`
→ `BTreeBuilder<byte>`.

### ⛔ THE REMAINING WORK — **two shapes, both enumerated**

| shape | count | where |
|---|---|---|
| `ref var bb = ref world.GetComponentRW<BrainBlackboard>(e);` | **16** | `T39`×2 · `PlatoonHillAttack2_Integration` · `T37` · `HillAssault2I_Smoke` · `T20`×2 · `T34` · `T31` · `S3_BehaviorScopedThunk` · `T35` · `T30` · `StatefulPrimitiveTests`×2 · `BehaviorValidationScenario`×2 |
| `new BTreeBuilder<BrainBlackboard, BTreeContext>()` | **4** | `NetworkDemoPatrolAndEngageTests:427` · `MoveToAndFire_EndToEndTests:181` · `HostedSubtreeCursorTests:420` · ⚠ `BTreeJsonGeneratorTests:264` is a **STRING ASSERTION**, not code |

### ⭐⭐⭐ THE PATTERN — **measured on `StatefulPrimitiveTests`**

📐 Those tests register a **hand-written thunk that reads the HANDED-IN base**
*(`ref byte bb` → `Unsafe.AddByteOffset(ref bb, paramOffset)`)* — ⛔ **not** `RootParamsAccess`. ⇒ the
test needs no occurrence slot at all; it needs **a byte region it owns**:

```csharp
// P4-②: the interpreter takes the params region as a ref byte. This test owns its own region
// because its thunk reads the HANDED-IN base — which is what makes it a unit test of DISPATCH
// rather than of ingress.
var paramsRegion = new byte[64];          // wide enough for this test's packed table
ref byte bb = ref paramsRegion[0];
```

⭐⭐ **Strictly better than what was there:** borrowing an ECS component as a scratch buffer is exactly
the habit that hid `CE-310` and `CE-311`.

⚠⚠ **BUT CHECK EACH ONE FIRST.** If a test's thunk is a **generated** one it resolves through
`RootParamsAccess.RootRef(ctx.World, ctx.Self)` and **ignores the handed-in base** — that test needs a
real root slot *(assign a behaviour, run ingress)*, not a buffer.
🔒 **That per-test judgement IS `G1`, and it is why this was not batch-substituted.**

---

## 3. ⚠ WHAT `P4`-② STILL NEEDS AFTER IT COMPILES

| # | |
|---|---|
| **①** | ⭐⭐ **Regenerate the goldens and ASSERT THE DIFF IS A PURE TYPE-NAME SUBSTITUTION.** 📐 124 files / 356 lines. ⛔ **A changed `(nint)` offset, a changed `@N` key, or a renamed `{Asset}_…` struct is a STOP** — §30.19 predicts none of those, so any of them means a premise broke |
| **②** | ⭐⭐⭐ **THE ZERO-FALLBACK RAIL.** Walk every `FbtTreeCatalog` blob against a populated `ActionRegistry` and assert **ZERO** fallbacks. ⛔ It is the ONLY thing that catches the reflection hazard: `BTreeActionRegistryFactory.cs:64`, `BlueprintRegistrarScanner.cs:126` and `AiHotReloadCoordinator.cs:433` compare `typeof(ActionRegistry<…>)` and **`continue` on mismatch** ⇒ a disagreement silently skips `RegisterAll` and **every action falls back to `Failure`**. ⚠ The compiler cannot see it |
| **③** | the §5 suites, then the §5 cluster acceptance |

⭐ **Then `P4`-③** *(re-home the **six** identity-keyed surfaces — `CE-303`; and `CE-308` **RE-POINTS**
`BlackboardTarget` onto {root params, node working state}, it does **not** delete it)* **and `P4`-④**
*(the 100-byte cap — `CE-307`; the real structural ceiling is **16 096 B**, so the cap is 160× low)*.
⛔ **`P4`-②b is WITHDRAWN** — the wrapper structs stay *(§30.18)*.

---

## 4. ⛔⛔ TRAPS ALREADY PAID FOR — **do not re-pay**

| | |
|---|---|
| 🔴🔴 **THE DEAD-STORAGE PATTERN — 3 instances, and it is this lane's whole lesson** | `CE-304`, `CE-310`, `CE-311`: **a capability whose STORAGE moved, whose CALLERS were never re-anchored, and whose RAIL kept it green by building a world production stopped building.** ⛔ No static signal sees it — `CE-311`'s in-degree looked healthy: the emitter is called, its output compiles, its rail passes. ⭐ **The check: ask *"which production site PROVISIONS the storage this reads?"* — never *"who calls this?"*** |
| 🔴 **`G3` UNDER-MEASURED the asset-driven generator** | there are **TWO** BTree generators. `BTreeActionGenerator` (the analyzer) is polymorphic and needed **zero** changes; **`BTreeBridgeEmitCore` derives the dispatch type from the ASSET** and threads it into **10** sites. ⭐ Fixed at `:310`, **not** in the asset |
| ⛔⛔ **NEVER retarget the asset's `BlackboardTypeName`** | it also mangles into the generated params-layout struct name **and** into `SubtreeSyncIdentity.Derive`, which **MATCHES SUBTREES** by (name, dto type, dto ns) ⇒ renaming 11 structs across 44 files and breaking matching **silently**. 📄 §30.19 |
| ⚠ **the generated BUILDER keeps the asset's type** | `BTreeEmitCore.cs:408` — selector-form bindings need a struct with fields, and `byte` has none. Same reason `P4`-②b was withdrawn |
| 🔴 **`ps \| awk '/Hrot\.Cluster/'` KILLS YOUR OWN SHELL** | the awk pattern appears in your own command line, so it matches itself *(exit 144)*. ⛔ The documented *"kill by PID"* does **not** prevent this. ⭐ Filter on **`comm == "dotnet"`** instead |
| ⚠ **an over-broad substitution corrupts DOC COMMENTS** | `ref bb.BehaviorParameters[0]` → `ref bb` hit two comments that deliberately described the **OLD** anchor *(`RootParamsAccess`, `AiPrimitiveEmitter`)*. Both restored. ⭐ **Always `git diff --name-only \| grep -v Tests` after a scripted edit** |
| ⚠ **the SimHost LOAD-FLAKY family is >1** | `BP-534` **and** `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState`. ⛔ Confirm any extra red **IN ISOLATION** before calling it a regression |
| ⚠ **`NETSDK1004` × ~60 is PRE-EXISTING** | unrestored `Stride/` projects. ⭐ Filter on `error CS` to see the real result |
| ⚠ **an inventory keyed on a NAME misses a CAST** | the `CE-304` lesson. ⇒ enumerate by the storage TYPE (graph / Roslyn), never by a spelling |
| ⚠ **build the TEST project, not the production one** | `--no-build` against a production-only build runs a stale binary |
| ⛔ **do NOT "fix" `RW-S` tracker rows** | they are invisible to `tracker-counts.py` — known gap `CE-259at` |

---

## 5. ⭐ GATES — **the baseline to beat**

| suite | baseline | note |
|---|---|---|
| `Fdp.Toolkits.Tests` | **2299 / 0** | ⚠ was 2303 before `P4`-①; **−4 = the deleted `Blackboard1024Tests`** |
| `Hrot.Blueprints.Tests` | **4017 / 0** *(18 skipped)* | |
| `Hrot.SimHost.Tests` | **1005 / 3** | the 3 documented pre-existing; a 4th is the flake above |
| `Hrot.AiEditor.Generators.Tests` | **279 / 4** | the 4 documented pre-existing |
| solution build | **0 `error CS`** | |
| docs | `design-digest --check` · `rulings-check` 38/38 · `tracker-counts --check` · `mermaid-check` | |

### ⭐⭐ THE CLUSTER ACCEPTANCE — **`hill-attack-close`, 2/2 gold**

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
curl -s --noproxy '*' -m 60 -X POST $B/scenario/load/live -H 'Content-Type: application/json' \
     -d '{"name":"hill-attack-close","waitForReady":true}'          # want sawWorldChange: true
curl -s --noproxy '*' -m 20 -X POST $B/sim/play -H 'Content-Type: application/json' -d '{}'
# wait for /sim/state totalTime >= 72, POST /perspective {"name":"Scenario"}, then read
#   /entities/1001..1004  -> data.Components.SimTransform.Position[0]  and LocomotionChannel.Status
#   /entities/1006,1007   -> data.Components.Health.Current
# teardown:  ps -eo pid,comm --no-headers | awk '$2=="dotnet"{print $1}' | xargs -r kill
```

⭐ **PASS** = `1001`–`1004` at **x ≈ 523–531** with `LocomotionChannel.Status: Success`, **and**
`1006`/`1007` at `Health.Current: 0`, **and** entity count **8** *(dead bodies stay — `CE-272`)*.
📐 **The `P4`-① gold, to compare against:** `523.03 525.25 529.14 531.37` and
`523.03 525.26 529.44 531.37`. ⚠ A retirement must not move the simulation at all.
⚠ Always `--noproxy '*'` and the `localhost` hostname — `127.0.0.1` 404s every route.

---

## 6. ⚠ STILL OPEN / UNEXPLAINED

| | |
|---|---|
| ⚠ **`G1`** | the per-test judgement in §2 — partly paid, the rest is the remaining work |
| ⚠ **`G4`** | the complete no-params set. 📐 `WanderMilitary` confirmed *(no `BlackboardLayoutType`, `CgfCuratedBehaviorRegistrar.cs:88`)*; **`Idle` unconfirmed** |
| ⚠ **`G5`** | where a hosted subtree's `ref subBb` comes from — `HostedSubtree.Tick<TChildBb>` + `BTreeOrchestratorEmitCore.cs:151,182` are a **second `TBlackboard` axis** |
| ⚠ **`CE-306`** | the vendored `Fbt.SourceGen` fork still carries the pre-`CE-304` bridge at `:616` and hard-codes FDP names at `:40-42`. ⛔ Nothing in HROT consumes it |
| ⚠ **the two §29 unknowns** | the failing runs reaching the firing line with no spawn-time writer; the 100-byte probe passing 2/2 *before* the fix. ⛔ Neither blocks anything — ⭐ but **re-read them first if `P4` reddens the cluster in a way the unit rails miss** |

---

## 7. ⭐ THE EXACT FIRST ACTION

1. `git fetch origin behaviors && git status` — expect clean at **`3ac1af9f4`**.
2. `dotnet build IOS-IG-SimHost.sln --no-restore 2>&1 | grep "error CS"` — expect **~76, all in tests**.
3. Work §2's two shapes, **checking each test against the pattern note** *(handed-in base vs generated
   thunk)*. ⛔ Do not batch-substitute — that is where §4's doc-comment trap came from.
4. Then §3 ① ② ③, in order.
