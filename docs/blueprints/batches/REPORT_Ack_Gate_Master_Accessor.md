<!--STATUS
state: LIVE
build-state: BUILT — all of HN-028 plus a defect the wiring exposed.
updated: 2026-08-24
current-answer: §1 the accessor's final shape · §2 THE FINDING (the gate was level-triggered and confirmed
  nothing) · §3 obligation ③/⑤ · §4 §Gates · §5 the mutation table · §6 the ids · §7 lane note.
design-basis: 📄 docs/blueprints/Architect_Question_54_Cluster_Mcp_Contract.md § AS-BUILT-2 (written by this
  batch) · docs/DESIGN_Headless_Testability.md §6c/§6e · docs/blueprints/batches/HANDOFF_Ack_Gate_Master_Accessor.md
  (dispatched 3d5743a84) · tracker HN-028.
known-rot: none. ⚠ EPHEMERAL — the durable record is Q54 § AS-BUILT-2 and DESIGN_Headless_Testability §6c/§6e.
-->
# REPORT — **HN-028: the ack-gate confirms cluster-wide** *(and the gate was wrong)*

> ⛔⛔ **This report is EPHEMERAL.** ⭐⭐⭐ The durable record is
> **[`Architect_Question_54` § AS-BUILT-2](../Architect_Question_54_Cluster_Mcp_Contract.md)** and
> **[`DESIGN_Headless_Testability.md` §6c/§6e](../../DESIGN_Headless_Testability.md)** — both written before
> this batch closed *(obligation ⑤)*.

⭐⭐ **Headline:** the accessor was the easy half. ⛔⛔ **Wiring it revealed that the ack-gate had never gated
anything in `--mode all`** — it waited on a flag that reads `false` *before the barrier begins*. ⇒ the batch
is one accessor **plus a corrected gate condition**, and the second part is the one that mattered.

## 1. ⭐ THE ACCESSOR — final shape, and why this one

```csharp
// OrchestratorSubsystem.cs, beside the existing TestHook_ accessors
public bool? IsAwaitingStepAcks => _masterSync?.IsAwaitingStepAcks;
```

| choice | ⭐ why |
|---|---|
| ⭐⭐ **`bool?` rather than two members** | `null` ⇒ **no master on this node**; `true`/`false` ⇒ the master's answer. ⇒ `HasMaster` falls out of the same read, and **absence is assertable** — the same idiom as charter `D3`/`D4` that the rest of this harness is built on |
| ⛔ **NOT `MasterSyncController`** *(the handoff's first suggestion)* | that type also exposes `Step`/`SetTimeScale`. Handing it to the debug host invites driving time directly, **bypassing the perspective-scoped drive facade `Q54-2` established** *("issue where the user is, confirm where the truth is")*. ⚠ Narrowness here prevents an architectural wrong turn — it is not tidiness |
| ⛔ **`public`, not `internal` + `InternalsVisibleTo`** | the consumer is `Hrot.ClusterRunner`, a third assembly. One `bool?` read-only property is a smaller public surface than an `InternalsVisibleTo` grant that opens **every** internal in the assembly |
| ⚠ **read LIVE, through a `Func<bool?>`** | 📐 `_masterSync` is created in `Initialize` and **set to `null` in `Shutdown` (`:378`)** ⇒ a captured value outlives the master and lies. 📌 **This is deviation ③ of the conformance batch repeating** *(a value-captured provider LIES — it cost a wrong `time.drive:false` last time)*; caught before shipping this time |

⭐ `PerspectiveScopedDispatcher`'s ctor param changed `MasterSyncController? master` → `Func<bool?>? acksPending`.
📐 One caller in the tree, so no migration.

## 2. 🔴🔴 THE FINDING — **the gate was LEVEL-triggered and confirmed NOTHING**

⭐ The handoff expected *"a one-line accessor, then the coordinator wires it and the rail flips."* ⛔ It flipped —
and the gate still did not gate.

📐 **Measured, `--mode all`, paused, one step, via `curl`:**

| observation | value |
|---|---|
| `isAwaitingStepAcks`, 2 ms after `POST /sim/step` was issued | ⛔ **`false`** |
| `totalTime` at that moment | ⛔ **unchanged** |
| `totalTime` ~0.5 s later | ⭐ **+0.016667 s = exactly one tick** |

⛔⛔ **`false` means two different things** — *"the barrier drained"* and *"the barrier has not begun"*. A step
is published as an **intent that crosses DDS**, so when the old gate first polled, the master had not entered
`Stepping` at all. ⇒ ⭐⭐ **a wait on `!IsAwaitingStepAcks` returned immediately, having confirmed nothing —
which is worse than no gate, because it looks like a guarantee.**

📌 **This is the same level-vs-edge defect as the scenario-load readiness race** *(`DebugApiService.cs:841-849`,
fixed one batch ago)*, found the same way. ⭐ Two instances now makes it a shape worth naming, not a one-off.

### ⭐⭐ The fix — an AND with a monotone observable, **not** an edge on the flag

```
return when   !IsAwaitingStepAcks   &&   totalTime > (totalTime read before the step)
```

⛔ **An edge-trigger *("wait for awaiting to go true, then false")* cannot work in both hosts:** the editor's
standalone master has an **empty roster** and is never observably awaiting, so phase one would always time
out and every editor step would pay it. ⭐ **Clock progress is the one signal that means the same thing in the
editor and in the cluster** — a step that landed moved it.

⚠ **Degrades deliberately:** a host that offers no clock *(IG/ExCon — `TotalTimeOrNull()` is `null`)* falls
back to the flag alone rather than hanging 20 s. Those perspectives `501` before reaching the gate today; the
fallback exists so a future clockless-but-drivable host degrades instead of stalling.

⚠ **`count > 1`** is gated as *"at least one tick, and the barrier drained"* — the master stays in `Stepping`
until every requested tick is acknowledged, so the flag covers the remainder without the gate needing to know
the tick length.

## 3. ⭐ OBLIGATIONS ③ AND ⑤

**③ — the design's UML was checked.** `DESIGN_Headless_Testability.md` §6b's sequence diagram carries the step
seam; §6c carries the gate. **Two deviations from it:**

| # | the design said | what was built |
|---|---|---|
| **①** | §6c + the §6b diagram: *"gate on `IsAwaitingStepAcks == false`"* | 🔴🔴 **insufficient — §2 above.** The condition is now `!awaiting && clock advanced`; the diagram line was corrected to *"gate: not awaiting ACKs AND clock advanced"* |
| **②** | the handoff: *"fold nothing into a design — this is a mechanical exposure"* | ⛔ **it was not mechanical.** ⇒ obligation ⑤ applies after all |

**⑤ — folded into the owning designs before this batch closed:**

| doc | what changed |
|---|---|
| 📄 **[`Architect_Question_54`](../Architect_Question_54_Cluster_Mcp_Contract.md)** | new **§ AS-BUILT-2** *(the accessor's shape and why, the finding, the fix, and what the rail does and does not prove)*; **deviation ② marked SUPERSEDED** with its prior state kept inline as history; STATUS gains `stale-below` naming it |
| 📄 **[`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md)** | §6c's gate row now states the flag alone is **not** a gate; §6b's diagram line corrected; §6e's *"BLOCKED CROSS-LANE"* row replaced by **CLOSED** + a new row for the wrong condition; STATUS build-state updated |

## 4. ⭐⭐⭐ §GATES

| # | gate | verbatim command | `--no-build`? | result · delta vs `3d5743a84` / base `259220e84` |
|---|---|---|---|---|
| 1 | build | `dotnet build IOS-IG-SimHost.sln --no-restore` | must build | ⭐ **0 errors** *(rebuilt before every conclusion — the stale-binary trap)* |
| 1 · 8 | ⭐⭐⭐ **the integration gate** | `bash scripts/run-system-tests.sh` | builds | ⭐⭐ **81 / 81 pass, 0 fail, 0 skip** *(baseline `80/80` ⇒ **+1**, the new ack rail)*. 🔴 **This is the gate that mattered: the new condition sits under EVERY `StepAsync` in the suite** |
| 8 | ⭐⭐ **the TIME-lane time suites** *(the invariant: nodes stay time-synced)* | `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build --filter "FullyQualifiedName~SimTimeSyncIntegrationTests"` | `--no-build` | ⭐ **6 / 6 pass** |
| 8 | ⭐⭐ *(same)* | `… --filter "FullyQualifiedName~TimeControlIntegrationTests"` | `--no-build` | ⭐ **9 / 9 pass** |
| 8 | the kernel suite | `dotnet test FDP/Engine/Fdp.ModuleHost.Tests --no-build` | `--no-build` | ⚠ **192 / 198, 6 fail — ALL PRE-EXISTING, and the base is WORSE**: on the stashed base tree the same run fails **7**, a strict superset *(it adds `ModuleHostKernelTests.ModuleDeltaTime_AccumulatesCorrectly`)* ⇒ the suite is also **rotating-flaky**, `DEBT-AIB-030`'s shape. Named in §4b |
| 2 | out-of-solution / stale bin | — | — | ⭐ every project gated here is in `IOS-IG-SimHost.sln`; every `--no-build` run followed a full build of the same tree |
| 3 | golden movement | `git status --short` | — | ⭐ **ZERO goldens moved** *(0 created, 0 modified, 0 deleted)* — this batch adds a rail and a field, not a baseline |
| 4 | every RED pre-existing, by name | *(§4b)* | — | ⭐ all 6 named and proven against base `259220e84` by stash |
| 5 | working tree clean after every suite | `git status --short` | — | ⭐ clean; both mutation probes reverted by **inverse edit** and verified *(`grep -c "MUTATION PROBE"` ⇒ **0**)* |
| 6 | quarantine counts | — | — | ⭐ **0 skips before, 0 after.** ⛔ No new filter — `R-131` respected: the integration suites were run by `--filter` on the CLASS *(the documented isolation), not filtered AROUND* |
| 7 | doc gates + ids | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` | — | ⭐ **OK (open 99 / done 333)** ⚠ *(the script is blind to `HN-`/`MX-` rows — known quirk, so closing `HN-028` moves no count)* · **24/24 verified, 3 staleness WARNs** *(2 pre-existing + `DESIGN_Headless_Testability.md`, which THIS batch edited; its only citing ruling is `R-131`, whose quote and substance are untouched)* · **84 designs OK, UML present** |

### ⚠ 4b — the six pre-existing reds, named

`ConvoyAutoGroupingTests.AutoGrouping_SameTierAndFreq_SharesProvider` ·
`ConvoyIntegrationTests.ConvoyIntegration_5Modules_ShareSnapshot` ·
`ConvoyIntegrationTests.ConvoyIntegration_MemoryUsage_Reduced` ·
`HonestSodGdbTests.BatchInstall_SodModules_ActivatedAtomically` ·
`HonestSodGdbTests.UnionMask_Expansion_NewSodModule_ExpandsSharedProvider` ·
`ProviderAssignmentTests.ProviderAssignment_AsyncSoD_MultipleModules_Convoy`

⭐ All six are convoy / shared-of-demand / provider-assignment cases — **no time-control surface among them**.
⭐⭐ **Proven pre-existing by stashing to `259220e84` and re-running: 7 fail there, a strict superset of these 6.**
⛔ **No DDS-allocator crash** in either integration run on this machine.

## 5. ⭐⭐⭐ THE MUTATION TABLE — **what makes the green mean something**

⚠⚠ **Stated first, because it is the honest part:** the new rail
`A_cluster_step_is_ack_confirmed_before_it_answers` asserts a **postcondition**. An un-wired gate can satisfy
it too, by racing *ahead* of the intent instead of waiting *behind* the ACKs. ⇒ ⭐⭐ **the mutations, not the
rail, are what prove the gate is load-bearing.**

| # | mutation *(reverted by inverse edit)* | what reddened | expected? |
|---|---|---|---|
| **M1** | ⭐⭐⭐ **pin the master to "always awaiting"** — `IsAwaitingStepAcks => _masterSync is null ? null : true` | ⭐⭐ `POST /sim/step` answered **504** after 20 s: *"the master still reports awaitingStepAcks, so a roster node (SimHost/IG/CGF) is not advancing"* ⇒ **the gate genuinely consults the master, waits, and diagnoses the right thing** | ✅ yes |
| **M2** | ⭐⭐ **pin it to `null`** *(no master)* | ⭐ `The_manifest_describes_this_host_truthfully` reddened with *"`--mode all` reports NO master…"* ⇒ **the inverted assertion catches a silent unwiring** | ✅ yes |

⭐ Both rebuilt the full solution before drawing any conclusion, and both were restored and re-verified.

## 6. ⭐ RULE 5 — the ids allocated

| id | |
|---|---|
| ✅ **`HN-028`** | **CLOSED** — the gap it tracked |
| ✅ **`HN-031`** | the code: the accessor · the `Func<bool?>` dispatcher param · the `Program.cs` wiring · `AwaitStepLandedAsync` · `isAwaitingStepAcks` on `GET /sim/state` · the new rail · the inverted `hasMaster` assertion |

⛔ **No `TM-` id allocated, and no row written to Area H** — see §7.

## 7. ⛔ LANE NOTE — **read this before merging**

⚠ **The handoff addressed HN-028 to the TIME lane** *(`OrchestratorSubsystem.cs` is Area H)*, and told this
lane explicitly **not** to touch `Program.cs` or `ClusterConformanceRails.cs`. ⭐ **All three were edited
here, under an explicit user instruction to take the ack handoff and the scenario-load handoff together.**

| ⭐ what was done to keep it clean | |
|---|---|
| ⭐⭐ the TIME-lane edit is **one read-only property**, additive, touching no time behaviour | ⇒ the smallest possible merge surface in Area H |
| ⭐⭐ **no tracker row was written to Area H** — the record is an `HN-` row in Area J | ⇒ the tracker partition is intact; ⭐ the coordinator may move it to a `TM-` row if preferred |
| ⭐ the gate condition change is **entirely in this lane** *(`DebugApiHost`/`DebugApiService`)* | ⛔ nothing in `Fdp.Toolkits/Time` production was touched |

⚠ **The one thing a reviewer should check:** whether the TIME lane has an in-flight change to
`OrchestratorSubsystem.cs`. 📐 Its branch last moved `2026-08-23`, before this dispatch, so no conflict was
visible at merge time — ⛔ but that is corroboration, not proof *(rule 1b's blind window, in the other
direction)*.
