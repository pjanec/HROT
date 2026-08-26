<!--STATUS
state: LIVE
build-state: BUILT `2026-08-25`. Report for HANDOFF_Cgf_AxisB_Egress_And_Cleanups.md (dispatched `2aacffa8a`,
  started-marker `03f92fefe`).
updated: 2026-08-25
current-answer: ⛔ this report is EPHEMERAL. The durable record is DESIGN_Cgf_AxisB_Rotation_Slice.md §12
  (AS-BUILT, with the LIVE classDiagram + sequenceDiagram) and the tracker rows AX-005a/b/c, AX-007…AX-010,
  CE-018/035/036. This file only says what was measured and what was gated.
design-basis: DESIGN_Cgf_AxisB_Rotation_Slice.md §11 (R-134 STRICT NETWORK SEPARATION) — now superseded in
  its §11.3–§11.5 by §12, per obligation ⑤.
-->
# REPORT — **Axis-B cross-node egress + drag gizmo + cleanups**

> 📌 **Dispatched at `2aacffa8a`** · **started-marker `03f92fefe`** *(rule 1b, pushed before any code)*.
> ⚠ **Process deviation, declared:** the handoff asks for a FRESH branch from the coordinator. This session
> is bound by its harness to **`claude/reset-working-branch-qd1qpv`** and cannot create another. The
> coordinator branch was fetched at start and again before the final commit *(rule 4)* — **nothing new
> landed on it during the run**.

## 1. ⭐ IDS ALLOCATED *(rule 5)*

**`AX-005a`** · **`AX-005b`** · **`AX-005c`** · **`AX-007`** · **`AX-008`** · **`AX-009`** · **`AX-010`** —
tracker **Area M — Axis B**. `AX-005` flipped to done. **`CE-018`** · **`CE-035`** · **`CE-036`** flipped to
done in Area M — cgf==editor.

## 2. ⭐⭐⭐ THE DECISION LOG — **every deviation from the handoff, and why**

| # | decision | evidence |
|---|---|---|
| **D1** | ⭐⭐⭐ **`AX-005b`/`c` built NO new intent and NO new translator.** The handoff asks for `EntityAttributeChangeIntent` + a new request egress. 📐 **Measured: both exist.** `UpdateEntityAttributeCommand` is FDP-internal *(`Fdp.Toolkits`, and that assembly has **no reference** to the DDS message assembly — railed)*, and `UpdateEntityAttributeCommandEgressTranslator` is **registered in production** at `Translators/Map/SharedTranslatorPack.cs:79`, already writing the JSON arm to the same topic for the same owner. ⇒ **EXTENDED.** ⛔ A second intent + second translator on one DDS topic is two implementations of one concept *(ruling 9)* | `SharedTranslatorPack.cs:79`; `ExConOrbatAdapter` already publishes the event |
| **D2** | ⭐⭐ **`IEntityComponentWriter` + `EntityWriteRoute` MOVED to `Fdp.Toolkits`.** `AX-007` needs the seam in `EntityDragGizmo`, which lives in `Hrot.Presentation` — an assembly that ⛔ does not reference `Hrot.Network.NED`. Giving it that reference would pull CycloneDDS into the presentation layer to satisfy an interface naming no network type. ⇒ the SEAM moved; the IMPLEMENTATION stayed | railed: `ThePresentationAssemblyCannotSeeTheDdsMessages` |
| **D3** | ⭐⭐ **The interface gained a multi-change `Write`.** A drag commits `GeoLat`+`GeoLon`; as two single writes the owner applies them a round trip apart, so the entity lands on a coordinate pair the operator never chose — and a lost request leaves it there | railed: `ACommitSendsGeoLatAndGeoLonAsOneChange` |
| **D4** | ⭐⭐ **`EntityWriteRouter.For(repo)` — a factory, not five hand-built writers.** 📐 `EntityRotatorGizmo` is constructed in **five** places. Five hand-assembled writers is five chances to omit `publishRequest` — the SILENT-DEFAULT pattern. The dependency is derived, so it cannot be forgotten. All five call sites + all four `EntityDragGizmoDefinition` registrations now pass it | `CgfSubsystem`, `SimHostVisualization`, `SimHostApp`, `EditorSubsystem` ×2; `IgApplication` ×2, `SimHostApp`, `EditorSubsystem` |
| **D5** | ⭐ **`PublishOnto` takes the WORLD, not a bus.** The translator drains `view.ReadManagedEvents<T>()` — the WORLD bus. A bus parameter would let a caller pass the ORCHESTRATION bus: the command publishes successfully, is drained by nobody, and is lost **with no error at all**. Taking the world makes that unrepresentable | — |
| **D6** | ⭐ **`AX-008` — collapsed `RuntimeNetworkIdOf` before writing a third copy.** `CgfSubsystem` and `EditorSubsystem` held private, line-for-line identical copies, each commenting on the other | `NetworkIdResolver.RuntimeNetworkIdOf` |
| **D7** | ⭐ **The drag routes only the COMMIT.** One request per mouse-move would fight replication every tick on an unowned entity. ⚠ Consequence stated rather than hidden: on an unowned entity the preview IS visibly reverted until the request lands | railed: `TheLivePreviewNeverPublishesARequest` |
| **D8** | ⭐ **`CE-018` was FOUR walk-ups, not the two the handoff named** — `EditorSubsystem` ×3 + `EditorApplication` ×1. All routed. ⚠ `EditorApplication`'s gained the output-directory arm it never had *(it searched only `Environment.CurrentDirectory`)*, so a build launched from a bin folder no longer reports "project not found" | railed: `OnlyAssetRootsWalksUpLookingForACsproj` |
| **D9** | ⛔ **The `--mode all` round-trip rail is RED and was NOT skipped** *(`R-131`)*. See `F2` — the blocker is pre-existing and outside this lane. The half that IS provable here is green and railed on the real cluster | see §3 |

## 3. 🔴 FINDINGS

| # | finding |
|---|---|
| **F1** | ⭐⭐⭐ **`EntityRepository.GetSingletonManaged<T>()` THROWS when unset**, despite a `T?` return type that reads as *"null when absent"*. 📐 **Caught by the cluster rail, not by any unit rail**: the **IG never registers `IGeographicTransform`** *(only SimHost, CGF and the Editor do)*, so `EntityWriteRouter.For` threw on the one host where routing matters most. ⇒ guarded with `HasSingletonManaged`. ⭐ The absence is NORMAL — that host has no `Geo*` handlers, which `AttributeCompilerFactory` already models. Filed as `AX-010` |
| **F2** | 🔴🔴 **`SimHost → IG` entity replication does not complete in this environment — PRE-EXISTING.** 📐 Measured on a **CLEAN tree at `03f92fefe`**, 0 build errors, none of Axis-B present: `DragDropIntegrationTests` fails with *"IG did not receive entity (netId=1) within 120 frames"*; the full assembly is **21 failed / 28 passed / 2 skipped of 51** before the host process crashes. ⇒ the full round-trip rail cannot go green on this base. Filed as `AX-009`, kept RED as a live probe |
| **F3** | ⚠ **`CE-035`'s existing rail ENCODED the defect.** `RequestContinue_WhenNotPaused_IsNoOp` asserted `ResumeCount == 0` — which is precisely why *step, look, continue* left the operator halted. Superseded by two rails, with the reasoning in their own remarks |
| **F4** | ⚠ **`CE-036`'s skip reason was wrong in a misleading way.** *"Requires CycloneDDS"* in an assembly whose other tests boot a real domain. Real cause: ports are `7400 + 250 × domainId`, so `domainId = 250` asks for **69900**. Ceiling ≈ **232**; changed to **200** |
| **F5** | ⚠ **`R-134`'s price, stated:** the ingress lost its zero-copy `CollectionsMarshal.AsSpan` over the DDS list and now allocates one array **per request** — an operator gesture, not per tick. The alternative is the wire type BEING the interpreter's record type, which is the coupling the ruling forbids |

## 4. ⭐ GATES *(rule 8 contract)*

⭐ **Base for every "pre-existing" claim: `03f92fefe`** *(the started-marker)*, measured by stashing the whole
working tree, rebuilding with **0 errors**, and running. ⛔ Not inferred from a diff.

| # | gate | command | `--no-build` | result | Δ vs base |
|---|---|:--:|---|---|
| 1 | affected-project builds | `dotnet build <proj> --no-restore` on `Fdp.Toolkits`, `Hrot.Network.NED`, `Hrot.Presentation`, `Hrot.SimHost`, `Hrot.CGF`, `Hrot.Editor` *(pulls IG)* | build | **0 errors, 0 warnings** | — |
| 2 | **`Hrot.SimHost.Tests`** | `dotnet test … --no-build` | ✅ | **699 total · 692 passed · 4 failed · 3 skipped** | base **695 total · 5 failed**; ⭐ **+4 = the new `StrictNetworkSeparationTests`**. ⚠ **The 4 reds are the known `ComponentTypeRegistry` static-order flake — the failing IDENTITY rotates run to run** *(observed 4/5/6/10 across runs, in BOTH the base and this tree)*, and every named one **passes under `--filter`** |
| 3 | **`Hrot.Presentation.Tests`** | same | ✅ | **125 total · 122 passed · 3 failed** | base **120 total · 3 failed** — ⭐ **the SAME three**, all in the pre-existing `EntityDragGizmoTests` *(a `_dragOffset` expectation and a pick-token assertion I did not touch)*; **+5 = the new `TheDragCommitsThroughTheWriteRouterTests`, all green.** ⚠ One run also reddened `VertexEditGizmoTests.OnInteractionStarted_SetsActiveVertex`; it passes in isolation and in two subsequent full runs ⇒ ordering flake, reported rather than hidden |
| 4 | **`Hrot.Editor.AiShared.Tests`** | same | ✅ | **2028 total · 2027 passed · 0 failed · 1 skipped** | **+2 = `TheWalkUpHasOneImplementationTests`.** ✅ green |
| 5 | **`Hrot.Diagnostics.Breakpoints.Tests`** | same | ✅ | **165 total · 165 passed · 0 failed** | **+1 net** *(one rail superseded, two added)*. ✅ green |
| 6 | **`Hrot.Network.NED.Tests`** | same | ✅ | **98 total · 98 passed** | unchanged ✅ |
| 7 | ⭐⭐ **INTEGRATION** *(rule 8 row 8 — this IS a cross-node change)* | `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build --filter …` | ✅ | ⭐ **`AttributeChangeRequestRoundTripTests`: 2 passed / 1 failed** · ⭐ **`HarnessSmokeTests`: 5 passed / 0 skipped** *(was 2 passed + 3 skipped)* | ⛔⛔ **The full assembly CANNOT gate — and that is a reported finding, not an omission:** at base it is **21 failed / 28 passed of 51**, ending in a **test-host crash**. `F2`/`AX-009`. ⇒ the suite is run **filtered**, and the filtered result is stated |
| 8 | `python3 scripts/tracker-counts.py --check` | — | — | **OK — open 102 / done 346 (+1 refuted)** | — |
| 9 | `python3 scripts/rulings-check.py` | — | — | **25/25 verified** | — |
| 10 | `python3 scripts/design-digest.py --check` | — | — | **87 docs OK; every buildable design carries both diagrams** | — |
| 11 | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Cgf_AxisB_Rotation_Slice.md` | — | — | **all 6 blocks parse** | +2 *(§12.2/§12.3)* |
| 12 | **golden movement** | — | — | ⭐ **NONE — zero golden files touched.** This batch moves no asset, corpus or emitter output | — |
| 13 | **working tree clean after every suite** | `git status --short` | — | ✅ only this batch's own edits and new files; **no test regenerated anything** | — |
| ⭐ **T3** | **system / E2E suite** *(the slow lane — backgrounded, never a foreground blocker)* | `bash scripts/run-system-tests.sh --no-build` *(`Category=SystemSmoke\|Category=SystemModes`)* | ✅ | ⭐⭐ **102 total · 102 passed · 0 failed · 0 skipped** · 6 m 56 s | **unchanged** — the Axis-B first cut also reported 102/102. ⇒ ⛔ nothing this batch touched moved the system lane |
| 14 | **quarantine** | — | — | **`Hrot.SimHost.Tests` 3 skipped** *(unchanged)* · **`Hrot.Editor.AiShared.Tests` 1 skipped** *(unchanged)* · ⭐⭐ **`Hrot.ClusterRunner.Integration.Tests` 3 → 0** *(`CE-036`)*. ⛔ **No new skip was added anywhere** | −3 |

### ⭐⭐ RED-PROOFS — **by inverse edit, never `git checkout --`**

| rail | the edit that reddened it | observed |
|---|---|---|
| ⭐⭐⭐ `OnlyTheDeclaredBoundaryMentionsADdsTypeInTheWritePath` | added `internal static Hrot.NED.Messages.AttributeRecord RedProof;` to `EntityWriteRouter` | ✅ **red** *(1 failed / 3 passed)*, then green after the inverse edit |
| ⭐⭐ `AnUnownedWriteLeavesTheNodeAsADdsChangeRequest` | `if (false && cmd.AttributeChanges is …)` in the egress translator | ✅ **red**, with the intended message *("No UpdateEntityAttributeRequest reached the wire…")*, then green |

⚠ **Both scans also carry their own vacuity proof** *(`TheScanActuallySeesTheWritePath`, `TheScanActuallyReachesTheSourceTree`)* — ⛔ a scan that walked the wrong assembly or an empty tree would otherwise pass green forever.

## 5. ⭐ OBLIGATION ③ — **the design's diagrams, checked**

📄 `DESIGN_Cgf_AxisB_Rotation_Slice.md` §11.4/§11.5 carried **1 classDiagram + 1 sequenceDiagram** for this
slice. ⭐ **What was built DEVIATES**, in the one way `D1` names: the diagram draws
`EntityAttributeChangeIntent` and `AttributeRequestEgress` as **NEW**; measured, their equivalents existed
and are registered in production, so they were extended.

⇒ ⭐⭐⭐ **Obligation ⑤ discharged:** §11.3–§11.5 are marked **SUPERSEDED** in place *(the `R-134` ruling in
§11.1 is untouched and still binds)*, and a new **§12 AS-BUILT** carries the LIVE `classDiagram` +
`sequenceDiagram`, every deviation in a table, the findings and the rail inventory. The STATUS block names
§12 as the current answer. ⛔ The design no longer describes something that was not built.

## 6. ⛔ WHAT WAS NOT DONE

| item | why |
|---|---|
| 🔴 **the full `--mode all` round trip green** | **`F2`/`AX-009`** — pre-existing SimHost→IG replication failure, proven on a clean tree at the started-marker. The rail exists, is RED, and is documented in its own remarks as blocked on that defect. ⛔ Not skipped |
| ⚠ **`AttributeCompilerFactory.Build`'s `"Heading"` JSON path** | it contains a **third inline copy of the compass math** *(`(90f − headingDeg) × π/180` + `CreateFromAxisAngle`)*, predating this work *(`877fc7c74`, `2026-07-16`)*. ⛔ Out of scope: it is the JSON compiler, not the binary write path, and routing it is a behaviour-preserving change that deserves its own rail. **Filed here so it is not re-derived** |
| ⛔ **DebugApi/catalog · `Program.cs` · the diagnostics log-sink wiring** | the handoff's lane restriction — the concurrent MCP-diagnostics slice owns them. `EditorSubsystem.cs` was touched **only** in the CE-018 walk-up regions and the two gizmo-construction lines |

## 7. ⭐⭐⭐ ADDENDUM `2026-08-26` — **`AX-009` root-cause narrowing, and FIVE HYPOTHESES TESTED**

> ⭐ The user supplied five candidate root causes with the instruction *"do not believe, verify"*.
> ⛔ **Four are FALSE against this codebase and one is FALSE for the failing path.** All were measured, not read.

| # | hypothesis | verdict | the measurement |
|---|---|:--:|---|
| **RC-1** | `TkbDatabase` instantiated empty on both hosts ⇒ `[NS] Unknown TkbType` silent abort | ⛔ **FALSE** | Both hosts carry an `ITkbDatabase` singleton and `TryGetByType(100)` is **TRUE** on both. `HrotEnvironment.CreateTkb()` calls `NedTkbCatalog.RegisterAll` and both bootstrappers use it. ⚠ The prescribed `BdcTkbCatalog` **does not exist** — the FILE is named that, the class is `NedTkbCatalog` |
| **RC-2** | `SimHostScenarioManager.SpawnVehicle` bypasses the network pipeline via `_repo.CreateEntity()` | ⛔ **FALSE for the failing path** | The failing rails use `TestHook_SpawnEntity`, which already publishes `SpawnEntityCommand` and routes through `NetworkSpawningSystem` — the prescribed fix. Measured result: `NetworkIdentity=True`, `SimTransform=True`, `NetworkAuthority=True`, lifecycle `Active`. ⚠ The claim may still hold for `SpawnVehicle`, a **different** path that no failing test uses |
| **RC-3** | IG ingress translators silently drop unknown NetIDs instead of ghosting | ⛔ **FALSE** | `EntityMasterIngressTranslator.ProcessSample` calls `_ghostCreationSystem.CreateGhost(...)` on an unknown id. ⭐ And it demonstrably works: the IG ghost appears at **frame 2** |
| **RC-4** | replication systems registered in the wrong kernel phase ⇒ six systems dead on arrival; remove `SimWrapper` | ⛔ **FALSE — already fixed** | `SimWrapper` survives in **one test file** and nowhere in production; `ReplicationPhaseExecutionTests` **passes**. 📄 The owning design `docs/designs/replication-fixes/REPL-DESIGN.md` is in the **`_DONE`** tree |
| **RC-5** | SimHost must run a CGF pre-genesis / `PendingAuthorityGrants` handshake or the entity is torn down | ⛔ **FALSE for this failure** | The SimHost entity is `Active` with `HasAuthority(dtWorldPos)=True` and is never deleted. ⛔ No ELM timeout occurs — the entity simply never gets its position onto the wire |

### 🔴 What the measurement DID find — **the symptom names the wrong clause**

⛔⛔ The rail fails on *"IG did not receive entity"*, and **that is misleading**: 📐 the IG receives it at **frame 2**
with `NetworkIdentity`, `NetworkAuthority` and `TkbIdentity`. ⭐⭐⭐ **The clause that actually fails is
`HasComponent<SimTransform>`** — checked to **600 frames**, never true.

⭐⭐ **Two independent stalls, and both are now the next session's starting point:**

| # | stall | measured |
|---|---|---|
| **①** | ⭐⭐⭐ **the IG ghost is never PROMOTED** — lifecycle stays `Ghost` indefinitely | ⛔ **and `GhostPromotionSystem` IS registered**: `pureIgRole=True`, `_tkbDb`/`_lifecycleModule`/`_tkbEntityTranslators` all present, and the ghost carries the `TkbIdentity` that drives promotion. ⇒ ⭐ **promotion is reached and does nothing** |
| **②** | ⭐⭐ **SimHost publishes ZERO `WorldPos` samples** *(independent DDS reader, 300 frames)* | ⚠ despite `SimTransform=True`, `NetworkAuthority=True`, `HasAuthority(dtWorldPos)=True`, lifecycle `Active`. ⭐ The single measured discriminator is **`DescriptorOwnership=False`** |

⭐ **Proven WORKING, so nobody re-investigates it:** SimHost's spawn through `NetworkSpawningSystem` · the
`EntityMaster` egress *(1 sample observed on the wire by an independent participant)* · the IG's
ghost-creation ingress · the replication phase registration.

⚠ **Method note:** this was measured with a throwaway reflective diagnostic test, **deleted after use** —
⛔ it asserted `false` unconditionally to print its output and had no business staying in the tree. The
numbers above are its output; the durable record is the `AX-009` tracker row.

### ⭐⭐⭐ `2026-08-26` — **STALL ① RESOLVED TO A SINGLE ROOT CAUSE, and the "two stalls" reading was WRONG**

⛔⛔ **Correction to §7 above:** it reported *"two independent stalls"*. 📐 Measured — **they are one cause and
its consequence.** `GhostPromotionSystem` is **not** defective; it is correctly refusing an entity whose HARD
requirement is genuinely absent.

| # | link | measured |
|---|---|---|
| **①** | `NedTkbBuilder.DefineVehicle` — the production catalog — declares `EntityInfo` + `SimTransform` mandatory | ⛔ **never `NetworkTransform`**; grep gives **zero** occurrences in `BdcTkbBuilder.cs`/`BdcTkbCatalog.cs` |
| **②** | nothing in production attaches `NetworkTransform` to a SimHost-spawned entity | on the live entity: `SimTransform=True`, `NetworkIdentity=True`, **`NetworkTransform=False`**. The only production writers are `IgApplication:1991` *(IG side)* and an FDP example |
| **③** | `GeoSpatialEgressTranslator.ScanAndPublish` queries `SimTransform` **+ `NetworkTransform`** + `NetworkIdentity` | 📐 **matches 0 entities.** Drop the `NetworkTransform` clause and the same query **matches 1** ⇒ **0 `WorldPos` samples on the wire** |
| **④** | ⇒ the IG ghost never receives `SimTransform` | it holds `NetworkIdentity`, `NetworkAuthority`, `TkbIdentity` and nothing else |
| **⑤** | `SimTransform` is **HARD** mandatory *(`BdcTkbBuilder.cs:38`, `isHard: true`)* | ⇒ `PromoteGhost` hits `if (req.IsHard) return;` ⭐ and declines **forever** — by design |

🔴 **The load-bearing lie is a COMMENT.** The query carries: *"Entities spawned through NedTkbBuilder always
receive this component; older/test entities without it are skipped."* ⛔ **Measured FALSE** — and it is what
makes the omission read as deliberate. 📌 Exactly the *"a claim about CODE became false while the comment did
not change"* failure this programme keeps hitting.

⭐⭐ **Corroboration that this is the real story, not a coincidence:**
`Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs:837` already does
`template.AddMandatoryComponent<NetworkTransform>(isHard: false, softTimeoutFrames: 10)` with the comment
*"entity must have NetworkTransform before going Live"* — ⇒ **a TEST harness patches the production gap**,
which is precisely why that suite passes and `Hrot.ClusterRunner.Integration.Tests` does not.

⚠⚠ **NOT FIXED, deliberately.** ⛔ It is a design call with cluster-wide blast radius — it changes what every
replicated entity carries and would unblock ~21 integration tests — ⭐ and it is the **BACKEND** lane's file
set, not the UI lane's. The three candidate homes for the shadow *(the catalog · `NetworkSpawningSystem` ·
lazily in the translator, dropping the clause)* are recorded in the `AX-009` tracker row for a coordinator
decision.

### ⭐⭐⭐ `2026-08-26` — **`AX-011`/`AX-012` BUILT: the full round trip is GREEN and `F2` is RESOLVED**

⛔⛔ **`F2` above is SUPERSEDED.** Its measurement was right; its verdict *("cannot go green on this base")*
was wrong. 📄 The durable record is **`DESIGN_Cgf_AxisB_Rotation_Slice.md` §13**.

| id | built | where |
|---|---|---|
| ⭐⭐⭐ **`AX-011`** | attach `default(NetworkTransform)` at birth on the node that OWNS `SimTransform`, then grant authority | `SimHostNodeBootstrapper.onEntitySpawned` |
| ⭐⭐⭐ **`AX-012`** | the DDS constructor **builds** the binary interpreter from the `geoTransform` it already takes | `UpdateEntityAttributeRequestSystem` |

⭐⭐ **The placement was CHANGED from what was proposed, on measurement.** `NetworkSpawningSystem` *(the
engine-level first choice)* was implemented and **reverted**: a bare `AddComponent` there **throws**
*"Component NetworkTransform is not registered"*, and 📐 **37** files register `TkbIdentity` while only
`HrotSharedComponentRegistry` registers `NetworkTransform` ⇒ 37 registry edits, two of them FDP examples.
⭐ The shipped hook was **already written for this** — its `if (HasComponent<NetworkTransform>) SetAuthority(...)`
was a grant for a component nothing attached. ⚠ Cost stated: per-host, so the rails assert on a **real spawn**.

| gate | result |
|---|---|
| ⭐⭐⭐ **the `--mode all` round trip** | ✅ **3/3 GREEN** *(was 2 green + 1 red)* — §9.4's open item fully discharged |
| ⭐⭐ **`TheEgressShadowExistsAtBirthTests`** | ✅ **6/6** · red-proved by removing the attach *(all 6 + the round trip reddened)* |
| ⭐⭐ **`TheBinaryArmIsWiredInProductionTests`** | ✅ **3/3** · red-proved by passing `null` |
| `Hrot.Network.NED.Tests` | ✅ **101/101** *(was 98; +3)* |
| `Fdp.Toolkits.Tests` | ✅ **2037/2037** — confirms the engine-level revert is clean |
| `Hrot.SimHost.Tests` | **699 total · 3 failed** — the known rotating `ComponentTypeRegistry` flake *(4–5 before)* |
| `Hrot.Presentation.Tests` | **125 total · 3 failed** — the same pre-existing `EntityDragGizmoTests` three |
| `mermaid-check` · `design-digest --check` · `tracker-counts --check` | ✅ 7 blocks parse · 87 docs OK · counts OK |
| `rulings-check.py` | ✅ **25/25**; ⚠ one staleness WARN on `DataBreakpointManager.cs` from this batch's own `CE-035` edit — 📌 the script compares **commit** timestamps, so it clears once the ledger commit lands. `R-63` was re-read and **REFINED in place** rather than left implying an unconditional restore |

### 🔴 THE COUNT THAT LOOKS LIKE A REGRESSION AND IS NOT

⚠⚠ The integration suite's raw failures moved **21 → 24**, and the suite still aborts on the pre-existing
test-host crash. ⛔ **Not a regression** — 📐 established by diffing the failure SETS, not the counts:

| | |
|---|---|
| ⭐ **FIXED (3)** | `DragDrop_EntityPositionUpdatesOnIgWithinFewFrames` · `DragDrop_SimHostReceivesRequestAndMarksDirty_PublishesWithoutRollingWindow` · `SimHostDrag_IgReceivesPositionUpdateWithinFewFrames` |
| ⚠ **apparent additions (6)** | 📐 **grep: ZERO mentions in the before-log** — the crash truncated that run before reaching them. **All re-measured on a clean tree at the started-marker and fail identically there** *(`EventSerializationHelperTests` ×2 — a JSON-shape assertion; `E1_CognitiveRuntimeModule_RegistersExactlySixSystemsInOrder` — expected 6, actual 7, a stale COUNT assertion of the `CgfLogicPackTests` 18→19 family; `AreaAuthoring…`; `SensorMechanism…`; `ExCon_CommitMissionAsync…` which passes in isolation)* |

⇒ ⭐ **net: 3 fixed, 0 new.** 📌 Lesson worth keeping: **on a suite that aborts, compare failure SETS, never
counts** — a crash that moves later hands you "new" failures that were always there.

#### ⭐ T3 after `AX-011`/`AX-012`

| gate | result |
|---|---|
| ⭐ **T3 system/E2E** *(`run-system-tests.sh --no-build`, backgrounded)* | **102 total · 101 passed · 1 failed** · 7 m 16 s |
| ⚠ the single failure | `ModeStartupRails.EveryMode_StartsAndKeepsRunning(mode: "ig")` — ⛔ **NOT a regression: an X11 DISPLAY flake.** The runner output is explicit: `WARNING: GLFW: Error 65550 … X11: Failed to open display :91` → `GLFW: Failed to initialize` → the process died with **exit code 139 (SIGSEGV)** inside GLFW, before any replication code ran. 📐 **Re-run of the whole `ModeStartupRails` class: 8/8 GREEN.** ⇒ the spawn-path change is not implicated — this rail boots the IG **non-headless** and needs a virtual display that was momentarily unavailable |

⚠ **Stated rather than rounded up to "102/102":** the suite did report a red, and the honest record is
*"one display flake, isolated re-run green"* — ⛔ not a clean sweep.

### ⭐⭐⭐ `2026-08-26` (2) — **`AX-013`/`AX-014`: the `R-134` claim CORRECTED, and the two arms made consistent**

⛔⛔ **`AX-005a`'s claim in §12 was an OVERCLAIM.** It said *"no DDS type survives in the FDP-internal write
path"*. 📐 Measured: **no DDS MESSAGE type survives; a DDS DESCRIPTOR-ORDINAL enum does** —
`Hrot.NED.Descriptors.EDescriptorType`, in four apply-path files. 📄 Design **§14** is the corrected record;
the `AX-005a` tracker row is amended in place.

| # | finding |
|---|---|
| **F6** | ⭐⭐⭐ **THE RAIL COULD NOT HAVE CAUGHT IT, and the reason is structural.** `private const long X = (long)EDescriptorType.Y` is **folded to a literal at compile time** — the assembly holds the number and **no reference to the enum**. 📐 Proven, not assumed: broadening the reflection rail from `Hrot.NED.Messages` to the whole `Hrot.NED.` prefix **left it green**. ⇒ ⛔ **no reflection rail can ever see this class of coupling**; a SOURCE scan is necessary. ⚠ A real limit of the approach I used in this batch, not a tuning issue |
| **F7** | ⭐⭐ **A free cleanup fell out of measuring:** all four apply-path files carried a **dead `using Hrot.NED.Messages;`** — leftovers from `AX-005a`'s retype with zero remaining references. Removed ⇒ the coupling is now exactly `Hrot.NED.Descriptors` |
| **F8** | 🔴 **`AX-012`'s own fix introduced an inconsistency, and it was mine.** The JSON compiler was built by the factory and **passed in**; the binary interpreter was **built in the ctor**. Two siblings of one system, same factory class, same `geoTransform`, two conventions. 📌 **That ambiguity is exactly what let one be forgotten.** ⇒ `AX-014`: the ctor now defaults **both**, either overridable |

| gate | result |
|---|---|
| ⭐⭐ **`StrictNetworkSeparationTests`** *(+1 source-scan inventory rail, now 5)* | ✅ **5/5** · red-proved by adding one `using` |
| ⭐⭐ **`TheBinaryArmIsWiredInProductionTests`** *(+2 consistency rails, now 5)* | ✅ **5/5** |
| `Hrot.Network.NED.Tests` | ✅ **103/103** *(98 → 101 → 103)* |
| `Hrot.SimHost.Tests` | **700 total · 4 failed** — the known rotating `ComponentTypeRegistry` flake *(`StagingEntityExtractorTests` / `EditLoadClusterOpHandlerTests` / `FullBranchPipelineTests`, the baseline family; the count itself rotates)* |
| round trip + shadow + drag-drop, filtered | ✅ **11/11** |
| `design-digest --check` · `tracker-counts --check` · `rulings-check` · `mermaid-check` | ✅ 87 docs · counts OK · 25/25 · 7 blocks |

⚠ **`AX-013` is left OPEN on purpose** — whether the apply path should move out of the DDS assembly is
argued both ways in design §14.3, and *against* includes a real objection: the bus-intent variant adds a
third registration that can be silently absent, which is the exact failure mode `AX-011`/`AX-012` just were.

### ⭐⭐⭐ `2026-08-26` (3) — **`AX-015`/`AX-016`: steps (1) and (2) of the agreed plan; step (3) NOT started**

📄 Durable record: design **§15**. 🔒 Agreed order was *"do 1 and 2, report before 3"*.

| # | finding |
|---|---|
| **F9** | ⛔⛔ **§14.3's "against moving" argument is RETRACTED, on the user's challenge.** 📐 `MarkDescriptorDirty` sets a bit in a **local `ulong`**; nothing serialises `EDescriptorType`; the attribute update carries `AttributeId`. ⇒ *"a descriptor ordinal is wire numbering"* was **false** — **there is no wire-format obstacle to moving the apply path** *(`AX-013`)* |
| **F10** | 🔴🔴 **`AX-015` — the binary path told SmartEgress NOTHING.** `EcsPatchContext.Create`'s ordinal map is empty, so the `FlushDirtyMarks()` that `Apply` already calls flushed nothing; the installers' `MarkDescriptorDirty` set only a local `ulong` **no production code reads**. ⚠⚠ Hidden because the only end-to-end attribute is `GeoHeading` → `SimTransform`, whose translator **diffs every tick**; `EntityInfoEgressTranslator` does not ⇒ **a binary entity RENAME on the owner was never republished** |
| **F11** | ⭐⭐ **`AX-016` — the interpreter was built PER CALL of `EntityWriteRouter.For(repo)`**, i.e. per gizmo, not per network factory. Worse than reported: N scratchpads, and N chances for two interpreters built from different geographic transforms to convert the same attribute differently |
| **F12** | ⚠ **`SetSingletonManaged` cannot host a service** — it throws *"missing a `[ComponentId]` attribute"*. Using it would burn two **global component-id slots** on non-entity-data, and `BinaryInterpreter<T>` is an open generic whose instantiations would share one id. ⇒ `ConditionalWeakTable` instead |

| gate | result | Δ |
|---|---|---|
| ⭐⭐⭐ **`TheAppliersBelongToTheWorldTests`** | ✅ **8/8** | NEW *(replaces `TheBinaryArmIsWiredInProductionTests`, **deleted not weakened** — it pinned a per-network-stack instance as the contract)* |
| ⭐⭐⭐ **`TheBinaryApplyTellsSmartEgressTests`** | ✅ **2/2** · red-proved by removing the forward | NEW |
| `Hrot.Network.NED.Tests` | ✅ **106/106** | 98 → 101 → 103 → 106 |
| `Hrot.SimHost.Tests` | **702 total · 1 failed** | +2 rails; the 1 red is the known rotating flake *(3–4 in earlier runs)* |
| `Fdp.Toolkits.Tests` | ✅ **2037/2037** | ⚠ one run showed 1–2 reds in `GizmoRegistryTests`/`StatelessGizmoRegistryTests` *("expected throw for an UNREGISTERED component")* — 📐 **8/8 in isolation and 2037/2037 on repeat** ⇒ the static-registry order flake, count varying 0/1/2 across identical runs |
| `Hrot.Core.Tests` | **134 total · 5 failed** | ⚠ all `LogArchiveExtractionServiceTests`; 📐 **identical 5 on a clean tree** ⇒ pre-existing. ⭐ `JsonAttributeCompilerTests` — the third `IEntityPatchContext` implementor — **8/8**, so the default interface member is clean |
| round trip + shadow + drag-drop + harness smoke | ✅ **16/16** | unchanged |
| `design-digest` · `tracker-counts` · `rulings-check` · `mermaid` | ✅ 87 docs · counts OK · 25/25 · 7 blocks | |

⛔ **STEP (3) — moving the apply stack out of the DDS assembly — is NOT started**, as agreed. ⭐ `F9` removes
its main objection, and the source-scan rail's allowlist is the ready-made proof: it shrinks to zero when the
move lands.

---

### ⭐⭐⭐ `2026-08-26` (4) — **`AX-017`: step (3), the move — plus the JSON/binary consistency the user asked for twice**

📄 Durable record: design **§16** *(classDiagram + sequenceDiagram; §14.3 marked ANSWERED, its claim
RETRACTED)*. 🔒 *"ok do (3). again, we need consistency between json and binary attribute update path."*

| # | finding |
|---|---|
| **F13** | ⭐⭐⭐ **The consistency ask found a REAL defect, not a tidiness issue.** 📐 `BinaryInterpreter.Apply` ends with `FlushDirtyMarks()` ⇒ **a binary caller cannot forget.** 🔴 The JSON path left the flush to its caller and **three production callers each remembered it on a separate line** *(`UpdateEntityAttributeRequestSystem`, `DebugApiService.PatchEntityAttributes`, `EditorSpawnAdapter`)*. ⇒ **a fourth that forgot reproduces `AX-015` exactly** — applied to local ECS, never republished, **no exception anywhere**. ⭐ Fixed by making `Compile` flush itself *(the `UXI-30`/`AX-001` shape)*; the three explicit calls stay correct and become redundant *(`HashSet` ⇒ flushing twice marks once, railed)* |
| **F14** | ⭐⭐ **The move is bigger on the TEST side than the production side, and that was measurable up front** — 📌 `HN-037`'s lesson applied. 8 production files `git mv`'d, but **17 call-site files** needed re-`using`ing, of which **9 are test files**. ⚠ Two files legitimately needed BOTH usings back *(they still use the DDS-side `AttributeRecordConversion`)* — a blanket sed broke them and the build caught it |
| **F15** | ⚠⚠ **`ForceIdentifier` makes a THIRD copy of the force enum, and the two pre-existing ones agree by COMMENT with no rail.** 📐 `Hrot.NED.Descriptors.eForceIdentifier` and `Hrot.Core.Mission.eForceIdentifier` both exist, both `0,1,2,3`. ⭐ The new rail pins all three. ⛔ **Consolidating the two Hrot copies is out of scope** — filed rather than silently widened into this slice |
| **F16** | ⭐ **A source-scan rail cannot be red-proved the obvious way, and that is a feature.** Adding a real `using Hrot.NED.Descriptors;` to an `Fdp.Toolkits` file **does not compile** — rail ① *(the project graph)* forbids the reference. ⇒ red-proved with a `#if RED_PROOF` block: the compiler ignores it, the line-based scanner does not |

| gate | `--no-build` | result | Δ |
|---|---|---|---|
| ⭐⭐⭐ **`TheDescriptorOrdinalVocabulariesAgreeTests`** | ✅ | ✅ **10/10** · red-proved `WorldPos = 2` → `22` *(2 red, with the exact message `"WorldPos = 22 but dtWorldPos = 2"`)* | NEW |
| ⭐⭐⭐ **`TheJsonAndBinaryPathsAgreeTests`** | ✅ | ✅ **4/4** · red-proved by removing `Compile`'s flush *(**3 red**)* | NEW |
| ⭐⭐⭐ **`StrictNetworkSeparationTests`** | ✅ | ✅ **6/6** · red-proved by `#if RED_PROOF` *(1 red, `["SimTransformHeadingInstaller.cs"] = "Hrot.NED.Descriptors"`)* | 5 → 6; ⭐⭐ **allowlist 6 entries → 3, all four `Hrot.NED.Descriptors` rows GONE** |
| `Hrot.Network.NED.Tests` | ✅ | ✅ **106/106** | unchanged |
| `Hrot.SimHost.Tests` | ✅ | **717 total · 1–5 failed across 3 identical runs** | +15 rails. ⭐ **Only ONE red is stable: `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`** — 📐 **reproduced on a base worktree at `f800ae545`** *(1 red / 702)* ⇒ **PRE-EXISTING**. ⚠ The others rotate *(run 1: +4 `StagingEntityExtractorTests`; run 2: none; run 3: +1 `EditLoadClusterOpHandlerTests`)* — the known static-order flake; `StagingEntityExtractorTests` is **21/21 in isolation** and **35/35 run together with both new files** |
| `Fdp.Toolkits.Tests` | ✅ | **2037 total · 1 failed** | ⚠ `DangerAreaProviderTests.FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup` *("Refresh allocated heap memory")* — 📐 **fails identically on the base worktree, in isolation** ⇒ **PRE-EXISTING**, a GC-noise assertion unrelated to attributes |
| ⭐⭐⭐ **`Hrot.ClusterRunner.Integration.Tests`** — **targeted: the apply path** | ✅ | ✅ **9/9** *(`AttributeChangeRequestRoundTripTests` + `TheEgressShadowExistsAtBirthTests`)* | ⭐ **the suite that would break if the apply path stopped reaching the wire** — row 8's named integration gate |
| ⚠⚠ **`Hrot.ClusterRunner.Integration.Tests`** — **full suite** | ✅ | **34 failed / 76 · Test Run Aborted** | 🔴 **looks like a regression from the earlier 21→24 and is NOT one — proved, not asserted.** 📐 The base worktree at `f800ae545` aborts EARLIER *(18 failed / 38)*, so the two totals are not comparable. ⇒ re-ran the suspicious subsets **filtered, on BOTH trees**: `HarnessSmokeTests` + `SpawnMovingVehicle` + `TimeControl` + `GhostPromotion` = ⭐ **identical 3 failed / 18 on mine AND on base**, the same three names. ⇒ **the 19 "extra" failures are the ABORT and resource pressure, not the diff.** ⚠ `R-131` still applies to this suite *(un-gateable ⇒ a defect to resolve)* — ⛔ **not in this slice's scope, and named rather than filtered around** |
| `design-digest --check` · `tracker-counts --check` · `rulings-check` · `mermaid-check` | — | ✅ 87 docs · counts OK **102 open / 346 done** · **25/25** · **9/9 blocks** | +2 mermaid blocks |

⭐ **IDs allocated this addendum: `AX-017`** *(one row)*.

⚠ **Working tree clean after every suite run** — no golden was regenerated. **Quarantine/skip counts unchanged**
*(3 skipped in `Hrot.SimHost.Tests`, same as base)*; ⛔ **no new skip was added.**

#### ⚠⚠ `F17` — **the integration suite's headline count is NOT comparable between two runs, and this nearly cost a false regression**

📐 **Measured `2026-08-26`:** the suite **aborts** *(test-host crash)* at a **different test count each run** — `38` on the
base worktree, `76` on mine. ⇒ ⛔ **"18/38" versus "34/76" tells you nothing**: the second run simply got
further before dying, so it accumulated more of the same failures.

⭐⭐⭐ **The only sound comparison is a FILTERED re-run of the same names on both trees.** 📐 Done:
`HarnessSmokeTests` + `SpawnMovingVehicleIntegrationTests` + `TimeControlIntegrationTests` +
`GhostPromotionTests` ⇒ **3 failed / 18 on BOTH**, the same three names
*(`OutOfOrder_GeoSpatialBeforeEntityMaster_PositionPreservedAfterPromotion`,
`SpawnMovingVehicle_IgReceivesPositionChangesWithinFewFrames`,
`SpawnMovingVehicle_IgPositionContinuesToUpdate`)*.

⭐ **And the `TimeControl`/`SimTimeSync`/`HarnessSmoke` failures from the full run are GREEN when filtered** ⇒
🔴 **they were resource pressure, not defects** — which is exactly why the raw count misleads.

⇒ ⭐⭐ **Reporting rule for this suite, worth keeping:** quote a **filtered subset run on both trees**, never
the aborted total. ⚠ **`R-131` is unaddressed here and said so** — an un-gateable suite is a defect to
resolve, ⛔ and this addendum does not resolve it; it only stops it manufacturing a false alarm.

---

### ⭐⭐⭐ `2026-08-26` (5) — **`AX-018`: answering "is the JSON path inconsistent?" properly, and it was**

📄 Durable record: design **§17** *(classDiagram; §16.5 marked SUPERSEDED)*. 🔒 *"is then the json path
inconsistwnt with the binary one? can wr make consistent, following network agnostism rules? can we fix the
tests where we know correct asserts?"*

| # | finding |
|---|---|
| **F18** | ⛔⛔ **`AX-017` §16.5 CALLED THE ASYMMETRY "STYLISTIC". THAT WAS THE MIRROR ERROR** — 📌 *"a design ruling tells you what SHOULD exist; it cannot tell you what a diff ACTUALLY DID."* ⭐ I described the SHAPE of the two paths *(implicit vs explicit ordinal)* and never measured their BEHAVIOUR. ⇒ the user's question was the right one and the honest answer is **worse than I had reported**: two silent defects and a ruling-9 violation |
| **F19** | 🔴🔴 **FOUR routing tables, not two — and the PRODUCTION one was the hand-copy.** `IgApplication._edgeCompiler` re-`Register`ed the five paths with a comment saying they must stay in sync with `BuildEdgeCompiler()`, whose only callers are TESTS. ⇒ ⛔ **the comment was the enforcement, and it had already failed** |
| **F20** | 🔴🔴 **`D1` — a heading could be APPLIED but never EMITTED.** `Heading` went into the JSON→ECS table and the binary interpreter *(Axis-B item ②)* and into **neither** edge table ⇒ `{"Heading":90.0}` emitted **ZERO** records, so IG's creation tool could not send a heading at all. ⚠ Silent: no exception, no log |
| **F21** | 🔴🔴 **`D2` — `{"Affiliation":2}` THREW at the edge**, and that is **exactly what ExCon sends** *(its default enum serialisation is the underlying integer)*. ⚠⚠ **Both ends were already built for it** — `MapAffiliationInt` exists *because of* ExCon, and `HandleAffiliation` already branched on `record.Value.Kind == CsInt32`. ⇒ ⭐ **only the edge refused**, because `ExpectedKind` chose the reader getter unconditionally |
| **F22** | ⭐⭐⭐ **The fix needed NO network reasoning, and that is a dividend of `AX-017`.** 📐 All four tables now live in `Fdp.Toolkits`, so the disagreement is entirely FDP-internal and `R-134` is not engaged. ⇒ answering *"following network agnosticism rules?"*: **the rules do not bear on it at all** — before the move, this same fix would have been a boundary argument |
| **F23** | ⭐⭐ **`ExpectedKind`'s real job is the numeric WIDTH, not the token category.** JSON has one number type, so nothing in `32` says `int`/`long`/`double` — that is a schema question. ⛔ But the CATEGORY is in the token, and a record carries its own `AttributeValueKind` that consumers already branch on. ⇒ **the token wins**; a string on a numeric route now throws a **named** diagnostic instead of the opaque BCL message |

#### ⭐⭐ The test question, answered row by row — ⛔ **never change an assert to match the code**

| test | correct assert knowable? | action |
|---|---|---|
| `HsmBehaviorIntegrationTests.E1_…SixSystems…` | ✅ **YES — the answer was already in the repo.** `Fdp.Toolkits.Tests/…/CognitiveRuntimeModuleTests` asserts **7** with `BehaviorFrameSystem` at index 6 **and is green** ⇒ the module is right, this was a **stale duplicate** | ✅ **FIXED** → 7 + index-6 check. ⚠ Duplication **filed, not removed** |
| `DangerAreaProviderTests.…ZeroAlloc…` | ✅ **YES — the INSTRUMENT was wrong.** `GC.GetTotalMemory` is the whole PROCESS heap; xunit allocates on other threads ⇒ **no tolerance value could ever work**. 📐 Its `Flaky` comment claimed *"passes in isolation"* — **it does not (8224 B)** | ✅ **FIXED** → thread-local `GC.GetAllocatedBytesForCurrentThread()`, assert now **EXACTLY 0** *(stricter)*, `Flaky` trait removed. Red-proved at **exactly 24000 B** |
| `FullBranchPipelineTests.BranchedRecording_…` | ⛔ **NO — the assert is CORRECT**; why it fails is unknown | ⛔ **NOT touched.** Needs pipeline instrumentation; pre-existing |
| `GhostPromotionTests.OutOfOrder_…` · `SpawnMovingVehicle_…`×2 | ⛔ **NO** — *"ghost was not promoted"* is the right assert *(`AX-009` family)* | ⛔ **NOT touched.** Identical **3/18 on BOTH trees**; a real investigation, offered not started |
| `StagingEntityExtractor` · `EditLoadClusterOpHandler` · `GizmoRegistry` | ⛔ **NO — not an assert bug.** `ComponentTypeRegistry` global order ⇒ the identity ROTATES | ⛔ **NOT touched.** `R-131` applies; the fix is engine-level global state |

| gate | `--no-build` | result | Δ |
|---|---|---|---|
| ⭐⭐⭐ **`TheFourRoutingTablesAgreeTests`** | ✅ | ✅ **12/12** | NEW — ⭐ **6 were RED before the fix**, which IS the red-proof *(zero records for `Heading`; the exception for `{"Affiliation":2}`; `["IgApplication.cs"]` from the source scan)* |
| ⭐⭐ **`HsmBehaviorIntegrationTests`** | ✅ | ✅ **2/2** | 1 red → 0 |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ✅ | ✅ **2037/2037** | ⭐ **2036/2037 → 2037/2037** — the `DangerArea` red is gone, and no static-order flake this run |
| `Hrot.Network.NED.Tests` | ✅ | ✅ **106/106** | unchanged |
| `Hrot.SimHost.Tests` | ✅ | **729 total · 4 failed** | +12 rails. ⚠ Reds are the rotating static-order set + the stable pre-existing `FullBranchPipelineTests`; ⛔ **none from the edge-compiler change** — `BinaryInstallersTests.BuildEdgeCompiler_*` and `JsonToRecordCompilerTests` all green |
| builds | — | ✅ `Fdp.Toolkits` · `Hrot.IG` · `Hrot.SimHost` · `Hrot.CGF` · `Hrot.Editor` · `Hrot.Network.NED` | 0 errors each |
| `design-digest --check` · `tracker-counts --check` · `rulings-check` · `mermaid-check` | — | ✅ 86 docs · counts OK · **25/25** · **10/10 blocks** | +1 mermaid block |

⭐ **ID allocated: `AX-018`.** ⚠ **On `tracker-counts`:** the header tally is unchanged at **102/346** and that is
CORRECT, not a stale gate — 📐 the script counts only rows naming a **`BP-`** id, so `AX-` rows are outside
its scope by construction.

---

### ⭐⭐⭐ `2026-08-26` (6) — **`Q59` BUILT (`AX-019`): the split, one declaration, FDP's descriptor vocabulary deleted**

📄 Durable record: `Architect_Question_59_…md` **§10** *(as-built, three corrections)*. 🔒 *"ok accepting
recommenaldations."*

| # | finding |
|---|---|
| 🔴🔴 **F24** | ⛔⛔ **I WROTE A DUPLICATE OF AN EXISTING SEAM AND ALMOST SHIPPED IT.** `ComponentDescriptorMap` was built, built clean, and wired — then `IDescriptorTranslator`'s own doc comment named `DescriptorOwnershipMap`, which 📐 **already calls itself *"the Single Source of Truth for the descriptor → component mapping"* and already has `RegisterFromTranslator`.** ⇒ deleted the rival, extended the original. ⚠⚠ **This is the seam law I have documented all session, and the rule did not save me — reading the interface's DOC did.** 📌 Worth recording as the concrete lesson: `search_graph` found the translators, but only the prose said *"this is the single source of truth"* |
| ⭐⭐ **F25** | 📐 **The two gaps that made the existing seam LOOK missing, both real:** ① `RegisterFromTranslator` filled **only** descriptor→components, so `GetDescriptorForComponent` never saw a translator's contribution at all; ② the reverse map was **single-valued** while `SimTransform` is covered by **both** `BdcWorldPosTranslator` and `GeoSpatialEgressTranslator`. ⇒ ⭐ under-adopted, not absent — which is exactly the seam law's shape |
| 🔴 **F26** | **`CycloneNetworkModule` is never instantiated in production** — `grep` for `new CycloneNetworkModule` finds nothing outside `bin`/`obj`. ⚠ It was my first choice of wiring seam. ⇒ the real seam is `CycloneEgressSystem`, the one type holding both the translators and the world; the translator lists are assembled in **4+ host-side places** *(a main pack plus a gizmo pack per host)*, so there is no host-side seam either |
| ⚠ **F27** | **`A1′` delivers less than the design implied, and the difference is worth naming.** 📐 The JSON setters and binary handlers carry per-attribute logic with different delegate signatures ⇒ ⛔ **not redundancy, distinct code**. ⭐ Only the edge table and the schema are pure metadata. ⇒ the honest design is *derive those two, cross-check the rest with rails* — and saying so beats claiming a unification that would produce worse code |
| ⚠ **F28** | **Sequencing: `A1′` and `E` swapped.** Building `E` first showed it cannot drop the routing table's ordinal until the map is wired everywhere, and **two ordinal sources meanwhile is worse than either** |

| gate | `--no-build` | result | Δ |
|---|---|---|---|
| ⭐⭐⭐ **`TheHeadingConversionIsSharedTests`** | ✅ | ✅ **26/26** · red-proved by restoring the old formula | NEW |
| ⭐⭐⭐ **`TheDescriptorMapIsWiredTests`** | ✅ | ✅ **4/4** · red-proved by removing the wiring hook *(**4 red**, incl. both `AX-015` rails)* | NEW |
| ⭐⭐ **`TheFourRoutingTablesAgreeTests`** | ✅ | ✅ **26/26** | 12 → 26 |
| ⭐⭐ **all `Q59` rails together** | ✅ | ✅ **68/68** | — |
| ⭐⭐⭐ **`Hrot.ClusterRunner.Integration.Tests`** — the apply path | ✅ | ✅ **9/9** on a real cluster | ⭐ **the gate that matters**: it proves republication survived swapping the dirty-mark mechanism |
| `Hrot.SimHost.Tests` | ✅ | **761 · 6 failed** | ⚠ all rotating-flake *(`Staging`×4, `EditLoadCluster`)* + the stable pre-existing `FullBranchPipeline`; 📐 **24/24 in isolation** |
| `Hrot.Network.NED.Tests` | ✅ | ✅ **106/106** | unchanged |
| `Fdp.Toolkits.Tests` | ✅ | ✅ **2037/2037** | ⭐ clean this run |
| `Fdp.ModuleHost.Tests` | ✅ | **198 · 6 failed** | 📐 **identical 6, same names, at the previous commit `3f13de914`** ⇒ **PRE-EXISTING** *(`Convoy`/`SoD` scheduling, unrelated)* |
| builds | — | ✅ 11 projects, 0 errors | incl. `Fdp.Network.Cyclone`, all four hosts |
| `design-digest` · `tracker-counts` · `rulings-check` · `mermaid` | — | ✅ · counts OK · **25/25** · **2/2** | |

⭐ **ID allocated: `AX-019`.** ⛔ **`N4` not built** — it was offered as a question needing a ruling, not a lean.

---

### ⭐⭐⭐ `2026-08-26` (7) — **`N4` built (`AX-020`); `CycloneNetworkModule` answered (`AX-021`)**

📄 Durable record: `Architect_Question_59_…md` **§11** *(`N4`)* and **§12** *(the module)*.

| # | finding |
|---|---|
| ⭐⭐⭐ **F29** | **`N4`'s rail caught a regression I had introduced two commits earlier.** 📐 416 bytes on a fully-known numeric patch, traced to `DescriptorOwnershipMap.GetDescriptorsForComponentId` returning `set.ToArray()` — **allocating on every component access during an attribute apply**. ⇒ fixed by storing `long[]` and merging at registration. 📌 **This is the argument for allocation rails**, and it landed within an hour of `AX-019` shipping |
| ⚠⚠ **F30** | **The rail was wrong TWICE before it was right, both times measuring the wrong window.** ① **688 B** — the payload held a **string** attribute, and `GetString()` legitimately allocates *(the zero-alloc mandate only ever covered non-string paths — the sibling rail says so in its name)*. ② **216 B** — a **fresh** `EcsPatchContext` allocates its `HashSet` buckets, i.e. the cost of CREATING a context, not of the diagnostic. ⇒ ⭐ the lesson this session keeps relearning: **an allocation rail measuring the wrong window manufactures either a false alarm or a false green** |
| ⚠⚠ **F31** | **`CycloneNetworkModule` is BYPASSED, not obsolete — and `docs/` is not archived.** 📐 Zero instantiations anywhere, yet `Fdp.Network.Cyclone.md:207` calls it the *"Root `IEcsModule`"* and `DESIGN-IG.md:281` explicitly **forbids** the hand-registration that all four hosts perform. ⇒ 📌 `CLAUDE.md`'s *"unreferenced is not unintentional"* in its purest form: the design doc answers the question, and its answer is *"use it"* |
| ⭐⭐ **F32** | **The bypass cost THIS slice something concrete.** `Q59-E` needed one place where the world and all translators meet — **which is what the module's `RegisterSystems` builds** — so its absence forced the hook onto `CycloneEgressSystem.Execute` and introduced the documented one-frame window |
| ⚠ **F33** | ⛔ **I did NOT measure why the hosts bypass it, and say so.** ⭐ There is a plausible good reason in plain sight: the module takes ONE translator list while each host composes TWO *(main + gizmo)*. ⇒ *"the hosts were sloppy"* would be an assumption, not a finding — so `M1` *(adopt)* is only a **weak** lean and `M2` *(delete + correct the docs)* may be right |

| gate | `--no-build` | result | Δ |
|---|---|---|---|
| ⭐⭐⭐ **`TheUnknownKeyIsWarnedNotThrownTests`** | ✅ | ✅ **7/7** | NEW — each asserts BOTH halves: no throw **and** the known keys still land |
| ⭐⭐ **`Hrot.SimHost.Tests`** | ✅ | **768 · 1 failed** | ⭐⭐ **the cleanest run of the whole batch** — only the stable pre-existing `FullBranchPipeline` red; the rotating static-order flake did not fire |
| `Hrot.Network.NED.Tests` | ✅ | ✅ **106/106** | unchanged |
| `Fdp.Toolkits.Tests` | ✅ | ✅ **2037/2037** | clean |
| ⭐⭐⭐ **integration — the apply path** | ✅ | ✅ **9/9** on a real cluster | the gate that proves republication still works |
| builds | — | ✅ 10 projects, 0 errors | |
| `design-digest` · `tracker-counts` · `rulings-check` · `mermaid` | — | ✅ · counts OK · **25/25** · **2/2** | |

⭐ **IDs allocated: `AX-020`** *(done)* **· `AX-021`** *(OPEN — needs a decision on `M1`/`M2`)*.
