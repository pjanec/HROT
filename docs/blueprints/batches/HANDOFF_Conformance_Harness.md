<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: dispatch pointer for the cross-host conformance harness — steps 6+7 of
  DESIGN_Headless_Testability.md, now built on the Q54 contract (RESOLVED): a perspective-scoped dispatcher over
  per-subsystem providers + a FULL capability manifest (description × measured matrix + reviewed baseline),
  ack-gated deterministic cluster-wide stepping, the editor-vs-(--mode all) THREE-way conformance suite, and a
  RE-PROOF of the part-C editor goldens under the same stepping seam. ⛔ Carries no design: every item cites a
  section.
known-conflict: none in the harness lane. ⛔ CROSS-LANE BOUNDARY: the LookaheadWallTicks/tickSource seam in
  --mode all lives in OrchestratorSubsystem.cs + Fdp.Toolkits/Time (TIME lane, Area H) and is OUT of this
  batch — correctness comes from the ack-gate, not from zeroing the barrier (§6c).
-->
# HANDOFF — **the cross-host conformance harness** *(steps 6+7, on the Q54 contract)*

> 📌 **Dispatched at `045773154`** *(re-stamped `2026-08-24` — rule 1a, verified unstarted; added the
> PARTICIPATE≠OBSERVE clarification for ExCon, no scope change)*. ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: push the started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`HN-`**/**`MX-`**, tracker **Area J** — 📐 the Area-J series stands at **`HN-025`** *(net)* /
> **`HN-122`** *(MCP-harness)* / **`MX-014`**. ⭐ **Rule 3: you allocate the ids; state them (rule 5).**
>
> ⭐ **Re-issued `2026-08-24`** *(the prior gate is lifted — `Architect_Question_54` is RESOLVED and approved)*.
> The earlier "one minimal `ClusterReadDriveService`" framing is superseded by Q54; item ① below is the new shape.

> 🔒 **User, `2026-08-24`:** *"do not forget also about the goldens based tests (editor showing same stuff as it
> did before). both need to be proven to work. also lets make sure we are using the deterministic stepping …
> should tick the simulation cluster wide no matter if editor or distributed mode."* ⇒ ⭐⭐⭐ **THREE things must be
> true when this closes: (a) `--mode all` answers MCP and its panels/gizmos match the editor's; (b) the part-C
> editor goldens still pass; (c) BOTH driven by the SAME deterministic cluster-wide step.**

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`Architect_Question_54`](../Architect_Question_54_Cluster_Mcp_Contract.md)** *(RESOLVED)* — the `--mode all`
MCP contract: the perspective-scoped dispatcher, the per-subsystem providers, the FULL capability manifest
*(description × measured matrix + reviewed known-absent baseline)*, and the routing/ack-gate rules. Carries the
`classDiagram` + `sequenceDiagram`.
📄 **[`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md)** — §**"Step 6"** *(6a-6d)* and
§**"Cross-host conformance"** *(the THREE-way diff)*.
📄 **[`DESIGN_Perspective_Unification.md`](../../DESIGN_Perspective_Unification.md)** §1b *(perspective→subsystem)*,
§1d *(SAME/DIFFERENT/NOT-PRESENT, declared not inferred)*.
⭐ Report per obligation ③; ⭐⭐ **fold deviations back into the design** *(obligation ⑤)*.

## 1. ⭐⭐⭐ THE ITEMS

| # | task | design | the one thing not to get wrong |
|---|---|---|---|
| 🔴🔴 **①** | ⭐⭐⭐ **`ISubsystemDebugProvider` + `PerspectiveScopedDispatcher`** — each subsystem contributes a provider *(its read surface + its role-correct `ITimeTransportFacade` + a capability descriptor)*; the dispatcher resolves the **current perspective → owning subsystem** *(`perspectiveMap`)* and routes there | Q54 Q54-2, §6a | ⛔ **NOT one monolithic `ClusterReadDriveService`.** ⭐ Reuse the per-role facades that already exist *(`ClusterTimeTransportAdapter` on slaves, editor facade direct)*; ⛔ do not carry editor-only deps *(`IPreviewController`/`IEditorLogic`)* into a provider — a CGF provider simply does not wire them *(D3: accept nulls)* |
| 🔴🔴 **②** | ⭐⭐⭐ **Lift the API to the `ClusterRunner` host** — the **four wiring points**, serving the dispatcher | §6a table | ⛔⛔ **all four, or a `RunMain` route hangs:** `PanelSnapshot.CaptureEnabled=true` · construct+`AttachService`+`Start` · per-frame `MainThreadJobQueue.DrainAll()` · per-frame `PanelSnapshot.ClearCaptured()` **after** the drain *(`HN-007`)*. ⭐ Gate on `HROT_DEBUG_API_PORT` |
| 🔴🔴 **③** | ⭐⭐⭐ **`GET /capabilities` — the FULL manifest** — a complete DESCRIPTION of every endpoint *(what · params · response schema)* **DERIVED FROM the route registrations + DTO types/attributes in code** × a **runtime-MEASURED** availability matrix, plus a committed **reviewed known-absent baseline** | Q54 § Manifest scope | 🔴🔴 **NOTHING hand-authored** *(a table/doc is the green-and-false rot — `CLAUDE.md` §M)*: the **description reflects routes + DTO attributes** *(precedent: `GET /behaviors` emits schema from the registry)* ⇒ a new route grows the manifest itself; the **matrix derives each cell from *is the dependency wired***. ⭐ panel dumps are honestly *"a model per `PanelId`"*. ⭐⭐ **A capability measured-absent that is NOT in the baseline is a FAILURE**, a port is a reviewed one-cell diff |
| 🔴🔴 **④** | ⭐⭐⭐ **Ack-gated cluster-wide `Step()`** — `POST /sim/step` returns **only when the tick is acknowledged cluster-wide** *(`MasterSyncController.IsAwaitingStepAcks == false`)*, SAME contract in editor *(empty roster)* and `--mode all` *(SimHost·IG·CGF ACK)*; issued through the **active perspective's** provider, **gated on the master** | §6b, §6c · Q54 Q54-2 | 🔴🔴 **THE CORRECTNESS ONE.** ⛔ **No `Thread.Sleep`/fixed `Settle` as the sync** — a read between `Step()` and the last ACK captures a HALF-STEPPED cluster. ⭐ *Issue where the user is, confirm where the truth is* — the slave adapter issues, the master gates. ⚠ `GET /sim/state` lacks `awaitingStepAcks` today: gate INSIDE `Step()` *(preferred)* or add the field + poll, ⛔ not both |
| ⭐⭐ **⑤** | ⭐⭐⭐ **The conformance suite — THREE-way** — `ClusterRunnerFixture(mode)`, run `S` in **editor** and **`--mode all`**, switch to the perspective showing `PanelKind K`, deterministic-step, dump `K` **+ the gizmo frame**, verdict **SAME · DIFFERENT · NOT-PRESENT** | §Conformance, §6 · perspective §1d | ⛔⛔ **a two-way diff is WRONG** — *"absent in `--mode all`"* is expected during migration. ⭐⭐ **NOT-PRESENT is read from the manifest baseline (③), never inferred from a missing panel.** ⛔ discover perspectives per-mode *(disjoint sets)*; start from the shared-presentation panels both hosts draw |
| 🔴🔴 **⑥** | ⭐⭐⭐ **PROVE conformance + the manifest FAIL on demand** — inject a panel divergence *(→ DIFFERENT reddens naming the path)* **and** measure a baseline-declared-present capability absent *(→ FAILS)*; revert both | §Conformance · Q54 | ⛔⛔ **Report as a table** *(the `N4` mutation-table standard)*. ⭐ **Rebuild before concluding** *(the stale-binary trap)*. ⛔ A diff/assert never seen red is decoration |
| 🔴 **⑦** | ⭐⭐⭐ **RE-PROVE the part-C editor goldens** — run `PanelGoldenRails`/`GoldenCaptureFixture` on this tree, report GREEN, state *(obligation ③)* that their stepping is the same deterministic seam `--mode all` uses | §6d | ⛔ *"both need to be proven to work"* — the goldens are the **editor half of the parity claim**. ⛔ **Do not re-bless** to go green; a red is a finding. ⭐ they already drive via `SwitchPerspectiveAndSettleAsync` → `POST /sim/step` — confirm, don't rewrite |
| ⭐ **⑧** | ⭐⭐ **A lockstep rail** — after a cluster-wide step, assert all sim nodes agree on sim time | §6b | ⭐ reuse `SimTimeSyncIntegrationTests.AssertAllInSync` / `TestHook_CurrentSimTime`; ⛔ do not copy its `Thread.Sleep` sync — gate on ACKs *(item ④)* |

## 2. ⚠ WHAT WILL BITE

| ⚠ | |
|---|---|
| ⭐⭐⭐ **the step is asynchronous under the hood** | `POST /sim/step` publishes `StepTimeIntent`; the tick completes over several frames as slaves ACK. ⛔ Returning before `!IsAwaitingStepAcks` is the half-stepped-read bug *(item ④)* |
| ⭐⭐ **route by the ACTIVE perspective, not a global stepper** | in `--mode all` every selectable perspective is a SLAVE context *(orchestrator has none)* ⇒ a step goes slave-adapter → DDS → master. ⛔ do not hard-code the master path |
| ⚠ **the matrix must reflect GROUND TRUTH** | ⛔ if you write a static table you have reintroduced the rot D4 exists to remove. ⭐ derive from wiring |
| ⚠ **the 200 ms enter-deterministic barrier** | `LookaheadWallTicks` crossed against the real clock ⇒ pump-until-paused once. ⭐ LATENCY, not non-determinism *(sim frozen during the barrier, §6c)*. ⛔ zeroing it edits TIME-lane files — §3 |
| ⚠ **`ExCon` never ACKs steps — but the MCP still knows completion** | roster is **SimHost·IG·CGF** *(`OrchestratorSubsystem.cs:309-313`)*. ⭐ ExCon uses the **same** `SlaveSyncController` API — it just has **no ECS kernel**, so it can't be a barrier *participant*. ⛔ **PARTICIPATE ≠ OBSERVE** *(Q54 Q54-2)*: completion is OBSERVED by reading the **master's** `IsAwaitingStepAcks` *(the gate reads the master, not the issuer)*, so a step issued from ANY perspective — ExCon included — is confirmed. ⛔ do not wait on an ExCon ACK, and do NOT add ExCon to the roster *(the cluster would stall on an ACK it can't produce)*. ⚠ if you read ExCon's OWN panels, the settle ticks let it catch up *(one frame behind the roster)* |
| ⚠ **capture is perspective-scoped** | switch, **step**, then read *(`HN-007`)*; a same-frame read returns the empty prefix |
| ⚠ **authoring perspectives capture EMPTY** | no debug route opens an AI asset *(`MX-013`)* ⇒ BTree/HSM/Blueprint compare skeletons. ⭐ say so |

## 3. ⛔ LANE & SCOPE

⭐ **Yours** *(harness lane, Area J)*: `Hrot.SystemTests` *(the fixture, the three-way conformance rails, the
lockstep rail, the mutation proofs)* · the new **`ISubsystemDebugProvider`** + **`PerspectiveScopedDispatcher`** +
the per-subsystem providers · the **`GET /capabilities`** manifest *(description + measured matrix + baseline)* ·
the **API wiring at the `ClusterRunner` host** · the **ack-gate inside `Step()`** *(and, if needed, an additive
read-only `awaitingStepAcks` field)*.

⛔⛔ **NOT yours — CROSS-LANE, STOP-and-report if you think you need them:**
- ⛔ **`OrchestratorSubsystem.cs` / `Fdp.Toolkits/Time/**` production files** *(TIME lane, Area H, `TM-`)* — the
  `LookaheadWallTicks=0`/`tickSource` seam. ⭐ You do NOT need it for correctness *(the ack-gate carries
  determinism)*; if 200 ms latency hurts, **report it** → a small TIME-lane follow-up.
- ⛔ the variable/blackboard/`AiShared` panels *(UI-lane freeze)* · CGF production beyond reading its panels /
  adding its provider. ⚠ **A provider is a READ+DRIVE surface, not a feature port** — building CGF's asset
  perspectives *(Part B)* is a different lane/batch.

⚠ **Rule 4: pull the coordinator branch before your final commit** and read any design/handoff that changed.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs `dc7df8b1b`** ·
a `--no-build` column · every RED confirmed pre-existing **by name** · `tracker-counts.py --check` ·
`rulings-check.py` · `design-digest.py --check` · **the ids you allocated** *(rule 5, same commit)*.
⚠ delta is **vs `045773154`** *(the re-stamped dispatch sha)*.

⭐⭐ **Row 8 — this batch IS an integration gate** *(it stands up `--mode all`)*: report
`bash scripts/run-system-tests.sh` *(baseline `58 / 58` + the new conformance/lockstep/manifest cases)*, **item
⑥'s mutation table**, and **item ⑦'s `PanelGoldenRails` result**. 📐 **`--mode all` boots five subsystems over
DDS** — ⛔ if the DDS-allocator crash makes a suite un-gateable, that is a **reported finding with the base-sha
proof**, not a silent skip *(rule 8 row 8)*.

⚠ **Known baseline quirks — not yours:** `tracker-counts.py --check` blind to `HN-`/`MX-` rows ·
`Fdp.Presentation.Tests` ~18–20 pre-existing *(`BP-419`)* · `mermaid-check.mjs` needs `npm install` *(say if
skipped)* · known `rulings-check` staleness WARNs.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into the designs** — **`Architect_Question_54`** *(the provider interface's real members,
the dispatcher, the manifest's final shape + the baseline's home)* and **`DESIGN_Headless_Testability.md`**
*(§"Step 6" made true, §Conformance's coverage answered, sequencing steps 6+7 → BUILT)*. ⛔ Design content in the
design; the report points at it. ⭐ Mark the ids closed in the tracker.
