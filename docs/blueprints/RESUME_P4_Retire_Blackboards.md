<!--STATUS
state: LIVE
doc-type: RESUMPTION for P4 — retiring BrainBlackboard and Blackboard1024.
  ⚠ A STATE doc, not canon. Every "measured" line is dated; ⛔ VERIFY against git before acting.
updated: 2026-09-22
build-state: n/a — a build resumption. The DESIGN is DESIGN_Occurrence_Scoped_Storage.md §30;
  §30.18 is the slice table, and §30.22/§30.23/§30.24 are the P4-③ as-built (they SUPERSEDE
  parts of §30.14 and §30.18 — read them before quoting either).
current-answer: ⭐ START AT §2. FOUR OF FIVE SLICES ARE DONE; the tree is GREEN at 70d40c0d2.
  What is LEFT is `P4`-④ (the 100-byte cap — RESCOPED, it is NOT a deletion) and then the
  BrainBlackboard struct itself, which is BLOCKED on one decision (§2 ③). §1 is what is done.
  §3 traps. §4 gates + the cluster harness. §5 open questions + the pending fork decision.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — §30.18 the slice table · §30.20 P4-① · §30.21 P4-②
    (the zero-fallback rail) · §30.22 CE-312 · §30.23 CE-308 · §30.24 CE-313. It wins on any
    disagreement with this file.
  - RESUME_Occurrence_Storage.md — the PROGRAMME-level resume (P0–P4).
  - Blueprint_Issues_Tracker.md — CE-303/CE-307 open; CE-308, CE-310, CE-311, CE-312, CE-313 DONE.
-->

# RESUME — `P4`: retire `BrainBlackboard` and `Blackboard1024`

> 🔒 **The goal, in the user's words:** *"we will retire it unless we find a true need and do not see
> any. Being part of ABI is no reason, ABI can and must change."*
> 🔒 **And the guard rail:** *"'no real users' does not mean 'not needed', be cautious before deleting
> anything."*

---

## 1. ✅ WHAT IS DONE — **do not redo this**

⭐ **`behaviors` @ `70d40c0d2`, clean. Solution builds 0 `error CS`; all five suites at baseline.**

| slice | |
|---|---|
| **`P4`-①** | ✅ **`Blackboard1024` IS DELETED.** 54 code files. Component id **74 RESERVED**, not reused. **2/2 cluster GOLD** |
| **`P4`-⑤** | ✅ the stale corpus |
| **`P4`-②** | ✅ **`TBlackboard` bound to `byte`** + the **zero-fallback rail** + **2/2 cluster GOLD** *(§30.21)* |
| **`P4`-③** | ✅ **all six identity-keyed surfaces re-homed** *(`CE-303`)*, **`CE-308`** *(the search axis)*, **`CE-313`** *(the builder generic)* — and it uncovered **`CE-312`** |
| **`P4`-④** | ⛔ **NOT STARTED — and it is not what §30.18 says it is.** See §2 ① |

### 🔴🔴 THE BIG FINDING OF `P4`-③ — **`CE-312`, the FOURTH dead-storage instance**

📐 **`BrainBlackboard` is ATTACHED BUT NEVER FILLED.** One site attaches it —
`BehaviorTkbTranslator.cs:125`, `AddComponent(entity, new BrainBlackboard())` — an **EMPTY** one.
**Nothing** has filled `BehaviorParameters` since `P3`; every remaining production mention is a doc
comment. ⇒ four debug surfaces projected a permanently ZERO region, and StructEdit bound **editable**
fields to it.

⭐⭐⭐ **Worst of the four, and the generalisation is the point:** `CE-304` read zeros silently,
`CE-310` returned `null`, `CE-311` would **throw**, **`CE-312` RENDERS PLAUSIBLE ZEROS AND ACCEPTS
EDITS.** 🔒 **An instance's danger is set by WHAT ITS READER DOES with an unfilled region, not by how
central the code is.**

⇒ 📄 **§30.22** has the full as-built. All four are re-homed onto the root params slot, with a
**POSITIVE** rail (`RootParamsProjectionTests`) that writes known values into a real store and asserts
they read back — ⛔ the old six rails asserted only REFUSALS, which a zero-filled component satisfies
by construction.

---

## 2. ⭐⭐⭐ WHAT IS LEFT

### ① `P4`-④ — **the 100-byte cap is NOT a deletion** *(`CE-307`, rescoped `2026-09-22`)*

> 🔒 **User:** *"Capping no longer needed as we allocate as much as we need, no?"* — ⭐ **Correct as
> far as the CAP goes.**

📐 **Where it was needed**, in the analyzer's own words *(`BehaviorParameterSizeAnalyzer.cs:31`)*:
*"exceeding the 100-byte `BehaviorParameters` region. **This would corrupt the SoftAdvice and Interrupt
registers in `BrainBlackboard`**."* ⇒ params lived inline in a fixed-layout struct **with neighbours
after them**; overflow silently overwrote unrelated state. It was a **buffer-overrun guard**, not a
budget. ⭐ Params now land in a slot sized `RootParamsBytes(def)` with `ResolveTier` promoting up the
ladder *(176 / 800 / 3808 / 16096 B)* and `TryAttach` failing structurally. **No neighbours, no cap.**

🔴🔴 **BUT TWO LIVE CONSUMERS READ THE NUMBER AS A *WIDTH*, NOT A LIMIT:**

| site | what it does with 100 |
|---|---|
| 🔴 `BehaviorIngressSystem.cs:57,97,98` | `stackalloc byte[BrainBlackboardByteSize]` — the **shadow buffer**, seeded from the previous slot with `Math.Min(prevLen, 100)`. It exists for **partial-parse semantics** *(an emitted parser writes only the variables the JSON mentions)*. ⛔ **If params ever exceed 100 B the carry-over SILENTLY TRUNCATES** — impossible today **only because the analyzer caps at 100** |
| `RootParamsAccess.RootParamsBytes` | returns 100 as the **reservation width** for a behaviour with `ParseParams` but neither manifest nor layout — the documented escape hatch. Its own comment says returning `0` drops the parse on the floor |

⇒ 🔒 **ORDER MATTERS: make the shadow buffer slot-sized FIRST, then remove the cap.** ⛔ Removing the
guard first converts an impossible case into a silent data bug.

📐 **The cap's surface:** source of truth `BehaviorConstants.cs:32` *(+ `:24` aliases it as
`BrainBlackboardByteSize`)*, **3 mirrors** — `BehaviorParameterSizeAnalyzer.cs:26` *(a real
`private const 100`, the netstandard wall)*, `BlackboardBinPacker.cs:87`,
`BTreeBlackboardPackHelper.cs:19` — pinned by `InlineBudgetConstantAgreementTests`.

### ② THEN THE STRUCT ITSELF — **32 production code lines, mostly strings**

📐 Measured `2026-09-22`: 136 files mention it, **401 lines, 237 code, and only 32 production**.
⛔ **Do not estimate from the file count** *(the `HN-037` trap)*.

| real work | |
|---|---|
| 🔴 **`BTreeTickSystem.cs:88` — `.With<BrainBlackboard>()` in the TICK QUERY** | a **live gate**. Redundant for production *(`BehaviorTkbTranslator` adds it unconditionally beside `BrainBTreeState`)* — ⛔ **but NOT for `BehaviorValidationScenario`**: it attaches `BrainBTreeState` at `:259` and only **registers** `BrainBlackboard` at `:125`, never attaches it ⇒ **that example's agent is excluded from the BTree tick today.** ⚠ Dead storage as a GATE. Worth its own row |
| the mechanical set | `CognitiveComponentRegistry.cs:44` + 3 example `RegisterComponent<>` · `BehaviorTkbTranslator` ×3 · `HrotRoleComponentSets.cs` · `BrainBlackboardTranslator` + its factory site · `GlobalComponentIds.cs` id **23 → RESERVE** · the struct · ~10 diagnostic strings |

### ③ 🔴 THE BLOCKER — **`BrainBlackboard` is a STRING in 26 ASSET FILES**

📐 `AiEmitCoreBase.cs:28` `DefaultBlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard"`,
and **26 asset `.json` files** *(21 authoring + 5 built)* name it explicitly. Only 4 assets use their own
`*_Blackboard` — ⚠ and those four names **do not exist as C# anywhere**, so for HSM assets the field is
inert.

⭐⭐ **`CE-313` already removed one of the three consumers** — the builder generic is now a literal
`byte` *(§30.24)*. **Two remain:**

| consumer | state |
|---|---|
| `BTreeNewAssetService.cs:123,159` | stamps the default into **every new asset** |
| `BTreeEmitCore.cs:304,336` namespace collector | still reads the asset's type to add a `using` |
| `BTreeOrchestratorEmitCore.cs:108` | `ref {bbShort} master` + `master.{VarName}` — **needs real fields**. ⭐ **LATENT:** 📐 **zero** generated `HostedSubtree.Tick` sites exist, so nothing orchestrates today |

⇒ **The decision needed before the struct can go: what does a BTree asset's `BlackboardTypeName` point
at?** ⚠ **Not a blind rename** — §30.19: it mangles into params-layout struct names **and**
`SubtreeSyncIdentity.Derive`, **which MATCHES SUBTREES**.

---

## 3. ⛔⛔ TRAPS ALREADY PAID FOR — **do not re-pay**

| | |
|---|---|
| 🔴🔴🔴 **THE DEAD-STORAGE PATTERN — now FOUR instances** | `CE-304` *(read zeros)* · `CE-310` *(returned null)* · `CE-311` *(would throw)* · 🔴 **`CE-312` (renders plausible zeros AND accepts edits)**. ⛔ No static signal sees it. ⭐ **The check: *"which production site PROVISIONS the storage this reads?"* — never *"who calls this?"*** ⭐⭐ **And the danger is set by what the READER DOES with an unfilled region** |
| 🔴🔴 **THE SILENT-DEFAULT RULE FIRES ON DELETIONS TOO** | 📌 `CE-312`: three hosts wire the inspector; `EditorSubsystem` set BOTH registry accessors, **CGF and ReplayBrowser set only the deleted renderer's** ⇒ removing it would have left two hosts with an inert panel. ⭐ Fixed structurally — **one static cannot be half-set**. ⚠ The usual shape is *a caller that has a dependency and does not pass it*; this is *a caller that STOPS passing it because the callee went away* |
| ⛔⛔ **A RAIL THAT ASSERTS ONLY REFUSALS CANNOT SEE DEAD STORAGE** | 📌 `CE-312`'s six rails were green **by construction** — they handed the renderer a `new BrainBlackboard()`, which IS the broken state. ⭐ **A positive rail that writes known values and asserts they READ BACK is the only shape that catches it** — ⛔ "the lookup returned true" is not evidence |
| ⛔⛔ **NEVER retarget the asset's `BlackboardTypeName`** | it mangles into the params-layout struct name **and** `SubtreeSyncIdentity.Derive`, which **MATCHES SUBTREES** ⇒ 11 structs across 44 files, and matching breaks silently. 📄 §30.19 |
| ⚠ **~~the generated BUILDER keeps the asset's type~~ — SUPERSEDED** | ⛔ §30.18 said `byte` was impossible because selector-form bindings need fields. 📐 **Measured: 26 generated builders, ALL nodes bound by STRING KEY, ZERO selector lambdas** ⇒ the type argument was never read. `CE-313` made it `byte`. ⭐ **Hand-written trees DO use the selector form and still need a struct** |
| ⭐⭐ **the per-test fix depends on WHICH THUNK the test runs** | hand-written thunk ⇒ the test owns a plain `byte[]`; generated thunk resolves via `RootParamsAccess` ⇒ take `RootParamsAccess.RootRef(world, entity)`. 🔒 Why `P4`-②'s tail was NOT batch-substituted |
| ⛔ **borrowing an ECS component as a scratch buffer** | the habit that hid `CE-310`/`CE-311`. ⭐ A plain `byte[]` cannot be mistaken for a storage path |
| 🔴 **`G3` under-measured the asset-driven generator** | there are **TWO** BTree generators. `BTreeActionGenerator` (analyzer) is polymorphic; `BTreeBridgeEmitCore` derived the dispatch type from the ASSET. ⭐ Fixed at `:310`, **not** in the asset |
| 🔴 **ANY COMMAND-LINE PATTERN MATCH KILLS YOUR OWN SHELL** | the pattern is in your own command line *(exit 144)*. 📌 Hit **twice**: `ps \| awk '/Hrot\.Cluster/'` and **`pkill -f Xvfb`**. ⛔ Not an `awk` quirk — **matching on the full command line at all**. ⭐ Filter on `comm`: `ps -eo pid,comm --no-headers \| awk '$2=="dotnet"{print $1}' \| xargs -r kill` |
| 🔴🔴 **A RELOAD IS NOT A RESET** | re-POSTing `/scenario/load/live` on the same process answered **`sawWorldChange: false`** with `totalTime: 84.9` ⇒ the "trial 2" numbers were trial 1 drifting. ⭐ **Restart between trials**; require `sawWorldChange: true` **and** `totalTime ≈ 0` at play |
| ⛔⛔ **`Fdp.Presentation.Tests` CANNOT BE GATED WHOLE** | `BP-419` / `CE-259aa`: a native SIGSEGV aborts the run **and still prints `Passed!`**. ⭐ Gate by `--filter` *(`~ReplayBrowser` ⇒ **93/0**)*. ⚠ **And its `bin` had NO xunit adapter** — the project had never been restored here, so `--no-restore` produced an unrunnable assembly. `dotnet restore` fixes it |
| ⚠ **the LOAD-FLAKY family keeps growing** | `BP-534` · `LiveFromReplayTests.Teardown…` · `SquadInputsP3Tests.AllReaders_ZeroAlloc…` · `T35_SharedWorkingState_ProofTests` *(reddened in a full run, **2/2 in isolation**)*. ⛔ **Confirm any extra red IN ISOLATION before calling it a regression** |
| ⚠ **an over-broad substitution corrupts DOC COMMENTS** | ⭐ Always `git diff --name-only \| grep -v Tests` after a scripted edit |
| ⚠ **`NETSDK1004` × ~60 is PRE-EXISTING** | unrestored `Stride/` projects. ⭐ Filter on `error CS` |
| ⚠ **build the TEST project, not the production one** | `--no-build` against a production-only build runs a stale binary |
| ⛔ **do NOT "fix" `RW-S` tracker rows** | invisible to `tracker-counts.py` — known gap `CE-259at` |

---

## 4. ⭐ GATES — **the baseline, all MET at `70d40c0d2`**

| suite | baseline |
|---|---|
| solution build *(156 projects)* | **0 `error CS`** |
| `Fdp.Toolkits.Tests` | **2307 / 0** ⚠ was 2299; **+8 = `BehaviorParamSlotResolverTests`** |
| `Hrot.Blueprints.Tests` | **4017 / 0** *(18 skipped)* |
| `Hrot.SimHost.Tests` | **1005 / 3** *(the 3 documented)* |
| `Hrot.Presentation.Tests` | **299 / 0** |
| `Hrot.AiEditor.Generators.Tests` | **281 / 4** *(the 4 documented)* ⚠ was 279/4; **+2 = the zero-fallback rails** |
| `Fdp.Presentation.Tests` | ⛔ **filter only** — `--filter "FullyQualifiedName~ReplayBrowser"` ⇒ **93 / 0** |
| docs | `design-digest --check` · `rulings-check` **38/38** · `tracker-counts --check` · `mermaid-check` |

⭐ **Golden regeneration switches:** `BLUEPRINT_REGENERATE_SNAPSHOTS=1` *(Blueprints)* ·
`AI_REGENERATE_SNAPSHOTS=1` *(AiEditor.Generators)* — ⚠ deliberately separate.
⚠ **A regen run itself reports reds** *(the goldens are rewritten mid-run)* — ⭐ **always re-run
without the flag** before believing a count.

### ⭐⭐ THE CLUSTER ACCEPTANCE — **`hill-attack-close`**

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
curl -s --noproxy '*' -m 90 -X POST $B/scenario/load/live -H 'Content-Type: application/json' \
     -d '{"name":"hill-attack-close","waitForReady":true}'      # REQUIRE sawWorldChange: true
curl -s --noproxy '*' -m 20 -X POST $B/sim/play -H 'Content-Type: application/json' -d '{}'
# wait /sim/state totalTime >= 72, POST /perspective {"name":"Scenario"}, then read
#   /entities/1001..1004 -> data.Components.SimTransform.Position[0] + LocomotionChannel.Status
#   /entities/1006,1007  -> data.Components.Health.Current
# teardown: ps -eo pid,comm --no-headers | awk '$2=="dotnet"{print $1}' | xargs -r kill
```

⭐ **PASS** = `1001`–`1004` at **x ≈ 523–531**, all `LocomotionChannel.Status: Success`, `1006`/`1007`
at `Health.Current: 0`, entity count **8** *(dead bodies stay — `CE-272`)*.
📐 **Gold after `P4`-②:** `523.10 524.91 528.32 531.23` · `523.03 525.27 528.46 531.14`.
⭐⭐ **Also check `grep -c "FastBTree] Warning" /tmp/cluster.log` is `0`** — the zero-fallback rail's
claim, observed on the running product.
⚠ **ONE TRIAL PER CLUSTER PROCESS** *(§3)*. ⚠ Always `--noproxy '*'` and the `localhost` hostname.

---

## 5. ⚠ STILL OPEN

| | |
|---|---|
| 🔴 **THE FORK DECISION — awaiting the user** | 🔒 User: *"The vendored fork can be removed"* — ⚠ **given before this measurement.** 📐 `Fbt.SourceGen` is referenced by **3** projects in `FastBTree.sln`: 2 examples and **`Fbt.Tests`**. Of `Fbt.Tests`' 33 unit files, **7** touch the generator *(useless to HROT — it has its own)*; **~26 test `Fbt.Kernel`/`Fbt.Compiler`, which ARE in the root solution and ship in HROT** — and `Fbt.Kernel` is what `P4`-② modified. ⭐ **Lean: delete the generator + its 7 tests + the 2 examples, KEEP the kernel/compiler tests.** ⚠ `SampleTreeDefinitions.cs` is a SHARED fixture using `[BTreeDefinition]`, so whether the kernel tests survive needs **one build**, not a guess. 🔒 **User also ruled: *"in extdeps btree there should be nothing from fdp"*** ⇒ ⛔ **"sync the fork with the analyzer copies" is WITHDRAWN** — it would push FDP *into* a vendored library |
| ✅ **`G4` — CLOSED** | `Idle` registers with no `ParseParams`, no `BlackboardLayoutType`, no manifest ⇒ `RootParamsBytes` = **0** |
| ✅ **`G5` — ANSWERED, and LATENT** | `subBb` is **`master.{VarName}`** — a *field* of the master struct, so orchestration needs a struct, not `byte`. `BTreeOrchestratorEmitCore:108` types `master` on the **asset's** type *(not `byte`)*, which is why the solution builds. 📐 **Zero** generated `HostedSubtree.Tick` sites ⇒ nothing orchestrates today. ⚠ §2 ③ would trip it |
| ⚠ **`BehaviorValidationScenario` is excluded from the BTree tick** | §2 ②. Not yet filed as a row — ⭐ file it when `P4`-④ starts |
| ⚠ **the two §29 unknowns** | the failing runs reaching the firing line with no spawn-time writer; the 100-byte probe passing 2/2 *before* the fix. ⛔ Neither blocks — ⭐ re-read if `P4` reddens the cluster in a way the unit rails miss |

---

## 6. ⭐ THE EXACT FIRST ACTION

1. `git fetch origin behaviors && git status` — expect **clean at `70d40c0d2`**.
2. **`P4`-④, in the order §2 ① gives:** ⭐ **shadow buffer first** *(`BehaviorIngressSystem:57,97,98`
   → slot-sized)*, **then** the analyzer cap + its 3 mirrors. ⛔ Not the other way round.
3. **Settle §2 ③** *(what a BTree asset's `BlackboardTypeName` points at)* — it gates the struct deletion.
4. **Then delete `BrainBlackboard`** — ⛔ **re-run §3's dead-storage check FIRST**; it is what found
   `CE-310`, `CE-311` and `CE-312`.
5. The fork decision *(§5)* is independent and can go any time.
