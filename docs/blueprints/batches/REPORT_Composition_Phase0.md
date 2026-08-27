<!--STATUS
state: LIVE
updated: 2026-08-27
current-answer: this whole file — the phase-0 return report.
design-basis: docs/DESIGN_Subsystem_Composition_Unification.md §5 (phase 0), with §5.6/§5.7/§5.8 added by
  THIS batch as the as-built (obligation ⑤). Architect_Question_63 §9/§10 are the user rulings this batch
  is bound by. DESIGN_UI_Observability_Snapshot.md STATUS ③ is where BP-487 was already filed.
known-conflict: none. ⚠ This report is EPHEMERAL by policy — the durable record is the design's §5.6–§5.8
  and the tracker rows. Do not quote this file as intent.
-->
# REPORT — **Composition unification, phase 0** *(the UI-parity rail)*

> ⭐⭐⭐ **Headline: phase 0 was specified as *"no production change"*. That premise was FALSE, twice — and
> finding out why produced the user's reported crash, root-caused.**

## 1. ⭐ WHAT WAS ASKED vs WHAT HAPPENED

| §5.3 item | asked | outcome |
|---|---|---|
| **①** the 8 drift instances | *"extend the two-host comparison"* | ⭐⭐ **DISCHARGED BY INVENTORY** — all eight were already railed by the preceding batch. ⛔ No new code: a T3 comparison would duplicate what T0 rails prove faster and *at the line* |
| **②** map parity via `get_gizmo_frame` | *"the highest-value item"* | ✅ **BUILT** — and it needed **`BP-487`** first, because the channel answered **404** on every cluster host |
| **③** the two `--mode cgf` symptoms | root-cause each | 🔴 **(2) center-on-entity CRASH: CONFIRMED and FIXED (`CE-065`)** · ⚪ **(1) empty map: DOES NOT REPRODUCE** |
| **④** *"nothing in production"* | — | ⛔ **FALSE.** `BP-487` *(reachability)* + `CE-065` *(a live crash)*. ⭐ The parity **comparison** itself still adds no production code — that half survives |

## 2. 🔴🔴🔴 THE FIND — **`CE-065`: the shared SYSTEM was routed; its EVENT REGISTRATION was not**

📐 Reproduced over MCP on `--mode all`:
```
POST /entities/1000/focus → 500
Strict Mode Violation: Unmanaged event type 'CenterOnEntityCommand' (ID: 8104)
was published without being explicitly registered.
```
⭐⭐ In the UI that publish happens inside CGF's ImGui **context-menu callback**, where the throw is
unhandled ⇒ **the process dies.** That is precisely what the user saw.

| | |
|---|---|
| ⭐⭐⭐ **enabling condition** | `ClusterRunner/Program.cs:52` sets `FdpConfig.EnforceExplicitEventRegistration = true` — **PROCESS-WIDE** |
| ⚠⚠ **and I had measured that wrong** | an earlier session recorded *"defaults false, and ClusterRunner does not set it"* and **dropped this exact hypothesis on that basis.** ⛔ The measurement was false; the hypothesis was right |
| 🔒 **the seam already existed** | `PresentationComponentRegistry.RegisterAll` **already registered `SelectEntityCommand`** ⇒ ⭐⭐ **that is why *"Select entity"* worked on CGF while its SIBLING menu item crashed.** Two items, one slice, one central and one inline |
| ⭐ **the fix** | the two missing events join that ONE list *(enumerated from all three systems `ScenarioEditorModule` registers, not just the one that crashed)*; the editor's inline pair **deleted** — it reaches the registry via `EditorSubsystem:905`. Reach: CGF · SimHost · Stride · editor transitively |

⚠ **Paid for TWICE now:** `HrotNodeBuilder:101-112` registers `OrchestrationEventRegistry` on the NODE bus
for the identical reason — *"pressing pause on a CGF/SimHost/IG toolbar throws instead of pausing."*

### 2.1 ⛔⛔ Why every existing rail was green — **rail-blindness, 4th instance**
| existing rail | proved | ⛔ never asked |
|---|---|---|
| `TheViewportInteractionIsSharedTests` source scans | CGF publishes the shared command, not a parallel | — |
| its behavioural rails | the shared system reacts correctly | — |
| ⛔ **neither** | — | 🔴 **whether the event was REGISTERED on the publishing host's bus** |

⭐⭐⭐ **And the reason is exact: unit rails run with strict mode OFF (the default), where `Publish` creates
the stream lazily.** ⇒ they published these very events and stayed green. The new rails turn it **ON**.
📌 Joins `CE-049` *(presence, not substance)* · `CE-053` *(supplied its own input)* · `CE-064` *(asserted
over an empty set)*.

## 3. ⭐ `BP-487` — the map feed, and a manifest that lied

📐 `GET /panels/_gizmo` reads `_primitiveBuffer`, passed **only** by `EditorSubsystem:1901`;
`ClusterRunner/Program.cs:429` built the cluster service without one ⇒ **404** — while **CGF, IG and
SimHost each drive a buffer of their own** *(ExCon: none)*. ⭐ Textbook silent default.
🔴 **And `CapabilityManifest` hard-coded `panels.gizmo = true` on every perspective row**, so the cluster
**advertised a feed that did not answer** — the *"present cell that silently no-ops"* `R-133` calls worse
than an absent one. ⭐ Fixed: `GizmoBuffer` joins `World`/`EntityMap`/`Drive` on `ISubsystemDebugProvider`
*(`Func`-backed — CGF builds its buffer in `Initialize`)*, and the cell is now measured.

🔒 **The id already existed** — `DESIGN_UI_Observability_Snapshot.md` STATUS ③ ⇒ ⛔ no new id allocated.
⛔ **Not `MX-011`** *(registering the buffer INTO `PanelSnapshot`)* — still the MCP lane's, still open.

## 4. 📐 MEASUREMENTS worth keeping

| | editor | `--mode all` *(CGF/Scenario)* |
|---|---|---|
| map primitives | **828** | **739** |
| entity anchors | ids 1000–1007 ×3 | ids 1000–1007 ×2 |
| cluster-only shapes | — | ⭐ **none** *(subset holds)* |

```
panels.gizmo:  SimHost claims=True answers=200 | IG claims=True answers=200
               ExCon   claims=False answers=404 | Scenario claims=True answers=200
```

⛔⛔ **`--mode cgf` ALONE CANNOT BOOT** — `DdsIdAllocator` waits 30 s for `Hrot.Orchestrator`, then throws;
exit **134** before `/status`. ⇒ CGF is exercised via `--mode all` + the `Scenario` perspective.

## 5. ⭐⭐ GATES *(the report substitutes for the coordinator's run — rule 8)*

| # | gate | command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|---|
| 1 | T0 seam | `quick-check.sh Hrot.Presentation.Tests TheGizmoFeedIsPerPerspectiveTests` | builds *(T0 always does)* | ✅ **5 / 0 / 0**, 7 ms | +5 new |
| 2 | T0 E3 file | `quick-check.sh Hrot.Editor.Tests TheViewportInteractionIsSharedTests` | builds | ✅ **17 / 0 / 0**, 191 ms | +2 new |
| 3 | T1 | `dotnet test Hrot.Presentation.Tests --no-build` | ✅ yes | ✅ **131 / 0 / 0** | 0 |
| 4 | T1 | `dotnet test Hrot.Editor.Tests --no-build` | ✅ yes | ⚠ **337 / 1 / 1** | 0 — the 1 red is `CE-050` |
| 5 | T1 | `dotnet test Hrot.IG.Tests --no-build` | ✅ yes | ⚠ **411 / 4 / 1** | **0 — all 4 PRE-EXISTING** |
| 6 | T1 | `dotnet test Hrot.ClusterRunner.Tests --no-build` | ✅ yes | ⚠ **270 / 3 / 0** | **0 — 2 pre-existing, 1 parallel flake** |
| 7 | **T2 integration** *(contract row 8)* | `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build --filter BreakpointSubsystemWiringTests` | ✅ yes | ✅ **25 / 0 / 0**, 18 s | 0 |
| 8 | T3 | `run-system-tests.sh --no-build TheMapsAgreeOnBothHostsRails` | ✅ yes | ✅ **3 / 0 / 0** | +3 new |
| 9 | T3 | `--filter The_manifest_tells_the_truth_about_each_hosts_map_feed` | ✅ yes | ✅ **1 / 0 / 0**, 7 s | +1 new |
| 10 | T3 | `--filter The_manifest_describes_this_host_truthfully` | ✅ yes | 🔴 **0 / 1 / 0** | **0 — PRE-EXISTING, see §6** |
| 11 | ledger | `python3 scripts/rulings-check.py` | n/a | ✅ **25/25**, no staleness warnings | — |
| 12 | designs | `python3 scripts/design-digest.py --check` | n/a | ✅ **98 docs**, UML present on buildable | — |
| 13 | tracker | `python3 scripts/tracker-counts.py --check` | n/a | ✅ **OK** *(101 open / 346 done)* | header corrected for `CE-065` |
| 14 | mermaid | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <design>` | n/a | ✅ **2/2 parse** | +1 `classDiagram` |

⭐ **Contract row 3 — golden movement: NONE.** ⛔ No golden file is touched by this batch; `git status` was
clean after every suite run *(contract row 5)*.
⭐ **Contract row 6 — quarantine:** `Hrot.Editor.Tests` **1 skip**, `Hrot.IG.Tests` **1 skip** — ⭐ **both
unchanged; no new skip was introduced.**
⭐ **Contract row 7 — ids allocated: `CE-065` only.** `BP-487` and `CE-010` were **existing** rows, updated.

### 5.1 ⭐ Red-proofs — **every new rail reddened on the pre-fix code, by INVERSE EDIT**
| rail | inverse edit | red |
|---|---|---|
| 3 dispatcher facts | resolve *"any provider with a buffer"* instead of the ACTIVE perspective | ✅ 3 red |
| 2 provider facts | capability back to hard-coded `true` | ✅ 2 red |
| `Centring_on_an_entity_does_not_kill_the_cgf_host` | ⭐ **no edit needed — it was RED on the real pre-fix tree** with the `500 Strict Mode Violation` | ✅ |
| `TheSharedViewportEventsArePublishableAfterOnlyTheSharedRegistry` | ⭐⭐ **caught a line I dropped while editing the registry, on its FIRST run** | ✅ |

## 6. ⛔⛔ PRE-EXISTING REDS — **named, with base-sha proof** *(contract row 4)*

**Base commit: `a0e77788a`** *(this batch's parent)*, verified in a detached worktree, same filters:

| suite / test | this tree | base `a0e77788a` | verdict |
|---|---|---|---|
| `Hrot.IG.Tests` · `EntityInfoTranslatorTests` *(4: `CS011_*`)* | 4 failed / 10 passed | **4 failed / 10 passed** | ⭐ **identical ⇒ pre-existing** |
| `Hrot.ClusterRunner.Tests` · `DataDrivenGizmoPredicateTests` *(2: `D003_*`)* | 2 failed / 0 passed | **2 failed / 0 passed** | ⭐ **identical ⇒ pre-existing** |
| `Hrot.ClusterRunner.Tests` · `TheLayoutIsOneUnitTests` *(1)* | 1 failed in the full suite | ⭐ **9/0 in isolation** | ⚠ **parallel/order flake, not a regression** |
| `Hrot.Editor.Tests` · `TwoReloadCycles_OldAlcIsCollected` | 1 failed | — | ⚠ the known rotating ALC flake, `CE-050` |
| ⛔ **`The_manifest_describes_this_host_truthfully`** | 🔴 red | — | ⛔ **`unclassifiedRoutes = [/missions/{networkId}, …/run, …/task, …/tasks]`** — a missing prefix in `CapabilityManifest.CapabilityFor`. **`missions` appears nowhere in that file and this batch's diff never touched `CapabilityFor`** ⇒ pre-existing, **MCP lane, THIRD report** |

⇒ ⭐ **contract row 8, honestly:** that rail **cannot gate** the new `panels.gizmo` claim, so the claim was
**moved** into `TheMapsAgreeOnBothHostsRails` *(gate 9, green)*. ⛔ Both rails carry a pointer: when
`/missions` is classified, move it back beside `time.drive` and keep **one** copy.

## 7. ⚠⚠ PROCESS DEVIATIONS — **declared, not hidden**

| # | deviation | why |
|---|---|---|
| **①** | ⚠ **branch**: developed on `claude/reset-working-branch-qd1qpv` *(harness-bound)*, not a fresh branch | unchanged from previous batches; declared each time |
| **②** | ⚠⚠ **CROSS-LANE FILE TOUCH** — this batch edited **`DebugApiService.cs`**, **`DebugApiService.Panels.cs`** and **`CapabilityManifest.cs`**, which the lane table assigns to the **MCP lane** | ⛔ **Unavoidable for `BP-487`**: the feed is *resolved* inside `DebugApiService`, so the reachability fix cannot live anywhere else. ⭐ The edits are narrow *(one resolved property, two call sites, one hard-coded cell)* and add **no route and no MCP verb**. 🔒 **Flagged for the coordinator to confirm rather than assumed acceptable** |
| **③** | ⭐ **item ① built no code** | it was already done; see §1. ⛔ Reported rather than padded with a duplicate rail |

## 8. ⭐ WHAT IS STILL OPEN

| id | state |
|---|---|
| **`BP-487`** | `[~]` **half done** — the FEED is reachable; ⛔ `PanelSnapshot.ClearCaptured()` still has one production caller, so a cluster host's `captured` list is latest-wins-forever. ⇒ belongs with `MX-011` |
| **`MX-011`** | MCP lane — register the buffer INTO `PanelSnapshot` so one `DumpAll()` carries it |
| **`/missions` prefix** | MCP lane — **third report**; blocks the manifest rail entirely |
| ⚪ **symptom ③(1)** *(empty map)* | ⛔ **NOT reproduced, NOT fixed.** The rail now stands to catch it. ⚠ Do not record as closed — the user said *"on some scenarios"*, and `hill-attack` is not one of them |
| `CE-062` · `CE-063` · `CE-047` · `CE-048` · `CE-050` | unchanged |

⭐ **Phase 1 is unblocked** *(the bundle seam + menus/toolbar)*, and phase 0's rail is now the scaffold it
was meant to be: 📌 **it caught a real crash on its first real run.**
