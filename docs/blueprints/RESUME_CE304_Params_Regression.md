<!--STATUS
state: LIVE
doc-type: DEBUGGING RESUMPTION for CE-304 — the live regression P3-C introduced.
  ⚠ A STATE doc, not canon. Every "measured" line is dated; ⛔ VERIFY against git before acting.
updated: 2026-09-22
build-state: n/a — a debugging snapshot, not a design.
current-answer: READ §1 (what is proven), then §2 (the CORRECTION — a claim in CE-304 and
  DESIGN §29.10 is WRONG and must be fixed), then §5 (the exact next action).
  ⛔ §3 lists the DEAD hypotheses — do NOT re-test them.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — §29.10 owns the design defect and the intended fix;
    this doc owns the LIVE-DEBUGGING state that §29.10 does not carry.
  - RESUME_Occurrence_Storage.md — §0d is the programme-level stop sign that points here.
  - Blueprint_Issues_Tracker.md — CE-304 is the filed issue; this doc is its working notes.
-->

# RESUME — `CE-304`: `P3-C` regresses `hill-attack-close`

> 🔒 **The one-line situation.** `P3-C` (behaviour params moved from `BrainBlackboard` into the root
> occurrence slot) is **committed, pushed, unit-green, and BROKEN ON THE LIVE PRODUCT**. The cause is
> **not yet found**. `P4` is parked behind it.

---

## 0. ⛔⛔ FIRST MOVES — **the repo is NOT in a clean resumable state**

| # | do this | why |
|---|---|---|
| **①** | 🔴 **`git checkout behaviors`** | ⛔ The tree was left on a **DETACHED HEAD at `9e20d3f97`** (the baseline binary was built from it). `git status -sb` will say `## HEAD (no branch)` |
| **②** | ⚠ **`git stash list`** — there is one entry: *"EXPERIMENT: RootParamsBytes always 100 — probe only"* | ⛔ **NOT a fix.** Drop it or keep it as a probe; it must never be committed |
| **③** | ⭐ **Rebuild before trusting any binary** — `dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj --no-restore` | ⚠ `bin/` currently holds whichever of the four builds ran last; the timestamp is the only tell |
| **④** | ⭐ `git log --oneline -5` should show `1b347e3bb` *(the `CE-304` filing)* on `behaviors` | that is the last pushed commit |

### ⭐ The commits this programme landed today *(all pushed to `behaviors`)*

| sha | what |
|---|---|
| `e9d124326` | `CE-302` — tier demand reserves the root slot, **plus two live defects it exposed** (the HSM sweep ate the slot; a BTree slot leak) |
| `3d4547a8d` | 🔴 **`P3-C`** — re-anchor every params reader + cut the blackboard write. **THIS IS THE CULPRIT** |
| `cc4132859` | `P3-C` fallout — goldens, four test fixtures, one registration mirror collapsed |
| `620bff7a5` | runbook correction — link ⑤ was stale since `CE-272` |
| `1b347e3bb` | **`CE-304` filed** + `DESIGN` §29.10 + `RESUME_Occurrence_Storage` §0d |

---

## 1. ⭐⭐⭐ WHAT IS PROVEN — **do not re-measure this**

### 1.1 The regression is REAL, DETERMINISTIC, and BISECTED to `P3-C`

📐 `clusterrunner --mode all`, `hill-attack-close`, acceptance per `CE-296`
*(both targets `Health 0`; all four members back on the baseline, x ≈ 523–531)*:

| build | trials | end positions (x) |
|---|---|---|
| `9e20d3f97` *(session start)* | ✅ **3/3 PASS** | `523 525 529 531` |
| `e9d124326` *(`CE-302` only)* | ✅ **2/2 PASS** | `523 525 529 531` |
| `3d4547a8d`+ *(`P3-C`)* | 🔴 **3/3 FAIL** | `579 587 524 590` |

⭐⭐ **Identical to the decimal across trials on every build** ⇒ ⛔ this is NOT the scenario's documented
non-determinism. ⇒ **`CE-302` is EXONERATED**, and with it the tier-demand bump, the attach/sweep
ordering and `DetachRoot`.

### 1.2 The failure shape

⭐ Tanks advance, acquire, fire, **both targets die** — links ①–④ of the runbook's chain are GREEN.
⛔ Then three of four **park on the firing line** and never return to the baseline. Positions frozen
from t ≈ 90 (verified unchanged at t = 337). Ammo frozen at 41. **Zero exceptions** in any run.

⚠ **Dead bodies STAYING is CORRECT** — `CE-272` reverted `CE-267` on a user ruling. The runbook's
link ⑤ said otherwise and was corrected in `620bff7a5`. ⛔ **Do not wait for the entity count to fall.**

---

## 2. 🔴🔴🔴 THE CORRECTION — **a claim recorded in `CE-304` AND `DESIGN` §29.10 IS WRONG**

⛔⛔ **Both documents currently state, as measured fact:**

> *"**Parameter delivery is correct END TO END** … the authored value survives the whole chain into
> `NavState.FinalDestination: [523, 401]` … ⇒ the supply chain, the key derivation and the thunk-side
> read are all FINE."*

🔴 **That was inferred from ONE downstream field, and the field is STALE STATE.** 📐 A full component
dump of a stranded tank (`/tmp/dump-HEAD.txt`, t = 140) shows:

```
NavState          FinalDestination [523, 401, 0]   TargetSpeed 15    ← CORRECT, but CARRIED OVER
NavigationIntent  FinalDestination [0, 0, 0]       TargetSpeed 0
                  IntentId 50   Mode DirectPoint                     ← ISSUED, and issued with ZEROS
LocomotionChannel Params [0, 0, 0, …]   Status Running               ← ZERO
VehicleState      Speed 0   Accel 2.5                                ← commanded, not integrating
```

⭐⭐ **`NavState` is the LAST INTENT THAT CARRIED VALUES. `NavigationIntent` is the CURRENT one, and it
is zeros.** ⇒ 🔒 **the live chain is `thunk → LocomotionChannel.Params → MoveToExecutor →
NavigationIntent`, and `channel.Params` is ZERO ⇒ THE THUNK WROTE ZEROS** — on the LATER dispatches
only, since the early ones carried the tanks to the firing line.

| ⛔ obligation on resume | |
|---|---|
| ⭐⭐⭐ **EDIT `CE-304` and `DESIGN` §29.10 to retract the "delivery is fine" claim** | ⚠ as written they send the next reader AWAY from the fault. Do this BEFORE any code |
| ⭐ **the lesson** | a targeted read of a field that *looks* right is not proof of a chain. ⛔ `NavState` is downstream STATE; `NavigationIntent` is the live COMMAND. The full dump was two commands away and would have shown this hours earlier |

---

## 3. ⛔⛔ DEAD HYPOTHESES — **measured and refuted; do NOT re-test**

| # | hypothesis | killed by |
|---|---|---|
| **①** | the 16-slot `MaxKindSlots` ceiling is exceeded | 📐 `PlatoonHillAttack` declares **1** manifest slot. Nowhere near 16 |
| **②** | `DetachRoot`'s `TryDetach` compaction corrupts a neighbour | 📐 disabled it → **still fails** (`577 587 525 588`) |
| **③** | a projection OVERRUNS its slot into the neighbour | 📐 both corpus behaviours measure **52 ≤ 52** — `HullDownAttackRun` and `PlatoonHillAttack` each declare `("Params", <T>, 0)` and their thunks project `<T>` at offset 0. ⚠ **The HOLE is real** (nothing bounds it — §29.10) but there is **no instance in this corpus** |
| **④** | per-dispatch `GetComponentRW` floods replication via chunk-version churn | 📐 `/diagnostics/architecture`: `WorldPos` 2817 sent / 60 s is ordinary, and **there is NO `BlueprintBlackboard` translator at all** — the tier component is not replicated |
| **⑤** | the thunk THROWS and something swallows it | 📐 `BTreeTickSystem` has no `try`/`catch`; nothing in the tick path swallows. And zero exceptions logged |

⚠ **Two of these (② and ④) produced PARTIAL improvements** — `DetachRoot` off gave `577 587 525 588`;
the RO-fetch probe gave `527 588 536 585` (**2** home instead of 1). ⛔ **Do not read a partial as
support.** ⭐ All three probes shift **slot layout**, so they shift WHICH dispatch lands on good bytes —
that is a property of the bug, not evidence for the probe.

---

## 4. ⭐⭐ THE LIVE LEAD — **the thunk writes zeros on later dispatches**

📐 From §2: `LocomotionChannel.Params` is zero while the behaviour is live and `Status: Running`.

| ⭐ what this points at | |
|---|---|
| the emitted thunk projects its DTO through `RootParamsAccess.RootRef(ctx.World, ctx.Self)` | 📄 see any `*.Registrar.g.cs` — e.g. `HullDownAttackRun`, four sites, all `(nint)0` |
| ⇒ either `RootRef` resolves the WRONG bytes on a later dispatch, or the region it resolves has been **zeroed/detached/reallocated** underneath | ⚠ the subordinates re-assign repeatedly — `BehaviorState.InstanceId` reaches **6** |
| ⭐⭐ **the prime suspect is the RE-ASSIGN path**, because early dispatches work and later ones do not | 📄 `BehaviorIngressSystem` — the shadow is seeded from the PREVIOUS root slot, then `DetachHostedOccurrenceSlots`, then `DetachRoot`, then attach+fill |

### 4.1 ✅ CONFIRMED `2026-09-22` — **the baseline/HEAD DIFF, same tank, same sim-time (t = 140)**

📐 `/tmp/dump-BASE.txt` vs `/tmp/dump-HEAD.txt`, entity `1001`, **Scenario** perspective:

| field | `9e20d3f97` — PASSES | `P3-C` — FAILS |
|---|---|---|
| `LocomotionChannel.Params` | `[1,192,2,68, 0,128,200,67, …]` ⇒ floats **523.0, 401.0** | 🔴 `[0,0,0,…]` **ALL ZERO** |
| `LocomotionChannel.Status` | `Success` | `Running` |
| `NavigationIntent.FinalDestination` | `[523.000061, 401, 0]` | 🔴 `[0,0,0]` |
| `NavigationIntent.TargetSpeed` / `ArrivalRadius` | `15` / `5` | 🔴 `0` / `0` |
| `NavigationIntent.IntentId` | `14` | ⚠ **`50`** — it keeps re-issuing, because it never arrives |

⇒ 🔒 **PROVEN: at `P3-C` the emitted thunk writes ZEROS into `LocomotionChannel.Params`.** The params
region it projects is zero AT DISPATCH TIME, while the **translator reading the same slot moments later
returns the CORRECT values** (§2). ⇒ ⭐⭐ **the slot is right when ingress fills it and wrong when the
thunk reads it** — a TIMING or ADDRESSING divergence between the two readers, not a storage-content bug.

### 4.2 ⭐⭐ THE TWO READERS, AND WHAT SEPARATES THEM — **where to look first**

| reader | path | result |
|---|---|---|
| `BrainBlackboardTranslator` *(the debug dump)* | `RootParamsAccess.TryGetRootBytes(repo, entity)` | ✅ correct values |
| the emitted thunk | `RootParamsAccess.RootRef(ctx.World, ctx.Self)` → same `TryGetRootBytes` | 🔴 zeros |

⭐⭐⭐ **They call the SAME function.** ⇒ the divergence must be in the ARGUMENTS or the MOMENT:

| # | candidate — ⛔ none yet tested | how to settle it |
|---|---|---|
| **①** | ⭐⭐ **`ctx.World` / `ctx.Self` are not what the translator uses** — a different world instance (`--mode all` runs several nodes), or a stale `Self` | log/assert the entity id and world identity inside the thunk for one dispatch |
| **②** | ⭐⭐ **the thunk runs on a node where the entity has NO root slot** — `RootRef` would THROW there, so more likely it runs where the slot exists but is EMPTY | read `LocomotionChannel` on **every** perspective, not just `Scenario` |
| **③** | ⭐ **the tick runs BEFORE ingress fills the slot** in some frames | `BehaviorIngressSystem` is `SystemPhase.Input`; the BTree tick is Simulation. ⚠ Check the re-assign frame specifically |
| **④** | ⭐ **`BTreeContext.Self/World` are not populated on every dispatch path** | 📌 `T10`'s proof tests used `new BTreeContext()` with NO world — that shape compiled fine and is exactly this hazard |

⚠ **④ is the cheapest to check and the most likely**: before `P3-C` the thunk never touched `ctx.World`
or `ctx.Self` for params — it used the `ref bb` the tick system handed in. ⇒ **any dispatch path that
leaves `ctx` partly unset was HARMLESS before and is FATAL now.**

---

## 5. ⭐⭐⭐ THE EXACT NEXT ACTION, in order

| # | | |
|---|---|---|
| **①** | ✅ **DONE — see §4.1.** The diff is complete and names the fault: the thunk writes zeros. ⇒ **the next action is §4.2's candidate ④**, then ①–③ `/tmp/dump-HEAD.txt` exists (t = 140, 5 entities, every component). Produce the baseline twin and diff them | ⛔ **This is the measurement in flight when the session ended.** It names the fault mechanically instead of by hypothesis — and 5 of my hypotheses have now died |
| **②** | ⭐⭐ **RETRACT the §2 claim** in `CE-304` and `DESIGN` §29.10 | ⛔ before any code — the docs currently misdirect |
| **③** | ⭐⭐⭐ **WRITE THE RAIL FIRST** *(§29.10's own demand)* | attach a root slot, attach an occurrence AFTER it, write through the params `ref`, assert the neighbour is **byte-unchanged**. ⚠ **AND** a rail for the re-assign path: assign → dispatch → re-assign → dispatch, assert the thunk still reads the authored values. 📐 **No rail anywhere asserts either** — that is the gap that let this ship with 2303 + 4017 + 420 + 299 green |
| **④** | fix, then re-run §1.1's table — **all three builds, 2 trials each** | ⭐ the reproducer is deterministic, so 2 trials suffice |
| **⑤** | only then unpark **`P4`** | 📄 `RESUME_Occurrence_Storage.md` §0c |

---

## 6. ⭐ THE HARNESS — **how to reproduce, in full**

⚠ **These scripts live in `/tmp` and do NOT survive a container restart.** Recreate them from here.

```bash
# 1. launch  (⛔ setsid+nohup+disown — a plain & is killed with the tool call's process group)
cat > /tmp/launch-all.sh <<'SH'
#!/bin/bash
cd /home/user/HROT
exec env HROT_DEBUG_API_PORT=8111 xvfb-run -a --server-args="-screen 0 1600x1000x24" \
  dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode all
SH
chmod +x /tmp/launch-all.sh
setsid nohup bash /tmp/launch-all.sh > /tmp/cluster.log 2>&1 < /dev/null & disown
until grep -qiE "Debug API listening|Aborted" /tmp/cluster.log; do sleep 3; done

# 2. run the scenario
B=http://localhost:8111
curl -s --noproxy '*' -X POST $B/scenario/load/live -H 'Content-Type: application/json' \
     -d '{"name":"hill-attack-close","waitForReady":true}'      # want sawWorldChange: true
curl -s --noproxy '*' -X POST $B/sim/play -H 'Content-Type: application/json' -d '{}'
# …wait for simTime >= 140…

# 3. read the verdict  (⛔ perspective FIRST — Scenario/CGF is the brain owner)
curl -s --noproxy '*' -X POST $B/perspective -H 'Content-Type: application/json' -d '{"name":"Scenario"}'
curl -s --noproxy '*' $B/entities/1001     # …1004
```

⭐ **PASS** = each of `1001`–`1004` within ~8 m of its own `NavState.FinalDestination`
*(x ≈ 523, 525, 529, 531)*. ⛔ **FAIL** = three of them at x ≈ 578–590.

| ⚠ traps paid for, all measured `2026-09-21/22` | |
|---|---|
| ⛔⛔ **`pkill -f 'ClusterRunner'` KILLS YOUR OWN SHELL** *(exit 144)* | the bracket trick does **not** save you when your own command line contains the path — e.g. a heredoc writing the launch script. ⭐ Run the `pkill` **alone**, in its own tool call |
| ⛔ **a `setsid nohup … &` started INSIDE a backgrounded tool call dies** with that task | ⭐ launch from a FOREGROUND call that returns immediately, then poll in a separate background call |
| ⛔ **`sawWorldChange: false`** ⇒ the clock did NOT reset — you are stepping a world that already ran | ⭐ restart the process for a clean `t = 0` |
| ⛔ `127.0.0.1` 404s every route | ⭐ the listener binds the `localhost` **hostname**. Always `--noproxy '*'` |
| ⛔ a fresh `git worktree` **cannot build** in this container *(~136 `NETSDK1004`)* | ⭐ baseline by `git checkout <sha>` in the MAIN tree, and check out back afterwards |

---

## 7. ⚠ COLLATERAL — **true, and not part of `CE-304`**

| | |
|---|---|
| **`CE-303`** | the two `BrainBlackboard` inspector surfaces render **zeros** between `P3-C` and `P4`. Keyed on `typeof(BrainBlackboard)` and the StructEdit path `$.BehaviorParameters`, so they cannot be re-anchored — they must be re-homed onto the occurrence inspector |
| **`P4` scoping** | 📐 measured: **78** production files mention `BrainBlackboard`; **46** are type-parameter-only (`Interpreter<BrainBlackboard, BTreeContext>`). Making the type vanish means dropping `TBlackboard` from `Interpreter`/`ActionRegistry`/`ITreeRunner` in **`FDP/ExtDeps/FastBTree`** — a vendored dependency. ⚠ **The user has not ruled on that**; it was raised and never answered |
| **`Blackboard1024`** | ⭐ genuinely dead — production adds it to **no entity** (`HeavyDtoType` is only ever assigned `null`), and all 28 mentions in generated code are comments saying *"NOT Blackboard1024"*. The easy half of `P4` |
| **pre-existing reds** *(do not chase)* | `Hrot.AiEditor.Generators.Tests` 4 · `Hrot.SimHost.Tests` 3 — **all proven pre-existing**, two of them *exhaustively* (their whole input is source files, and none of the 75 files this programme touched is in either input set) |
| **`Hrot.IG.Tests`** | cannot build in this container — `NETSDK1004`, unrestored |
