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
