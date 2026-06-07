# MVE-BATCH-05: compile-on-demand — register the opened blueprint so Run resolves it
Closes the live loop. Wire the editor's compile/quick-reload trigger to `QuickReloadService` so an opened/edited blueprint is compiled + registered into the shared `_blueprintRegistry`; then the MVE-03 Run button (which attaches via `BlueprintAttachService` and needs the blueprint registered) resolves it, and `BlueprintTickSystem` (MVE-02) ticks it in the real kernel.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/blueprint-mve/DESIGN.md`; MVE-02/03/04 reviews.
2. Reuse: `Hrot.Blueprints.Editor/Reload/QuickReloadService` (compile `.bp.json` → in-memory Roslyn → ALC load → `[BlueprintRegistrar]` scan → `AiHotReloadCoordinator` stage/commit into the registry); `EditorSubsystem._aiCoordinator` (AiHotReloadCoordinator, ~line 522), `_blueprintRegistry`, and the `_blueprintQuickReloadTrigger = null` seam (~1869–1882) with its "Phase 4 wires QuickReloadService" comment. Existing API reference: `Hrot.Blueprints.Tests/Editor/QuickReloadServiceTests.cs`.
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS.

## VERIFY FIRST (cite file:line)
- `QuickReloadService` constructor deps (does it take the `BlueprintRegistry` + `AiHotReloadCoordinator` + compiler + an output/diagnostics sink?) and `TriggerAsync(...)` signature/return (`QuickReloadResult` with diagnostics?). After a successful Trigger, is the blueprint present in the SAME `_blueprintRegistry` instance the editor's `BlueprintTickSystem` (MVE-02) ticks? (Confirm the coordinator commits into that instance — this is the crux. If QuickReload uses a different registry instance than the one wired into the kernel in MVE-02, that mismatch must be resolved so the run-button sees the compiled blueprint.)
- How the active blueprint's source path / asset is reached (same as MVE-03/04: `AiDocumentManager.Active` → `BlueprintFileAsset.SourceFilePath` / `AiCanvasContext.AssetRef`). QuickReload likely compiles from the `.bp.json` on disk → so the asset may need to be **saved first** (MVE-04) before compile, OR QuickReload can compile from the in-memory asset — determine which and document.

## Task 1 — wire compile-on-demand
- Construct a `QuickReloadService` in `EditorSubsystem` with the shared `_blueprintRegistry` + `_aiCoordinator` (+ whatever it needs), and replace `_blueprintQuickReloadTrigger = null` with a real trigger that invokes `QuickReloadService.TriggerAsync` for the active blueprint, surfacing the result/diagnostics to a status line/log.
- Expose it via a toolbar action (same `CaptureWindowRegistrar`/`RegisterToolbarEntry` + DrawUI path as the Run/Save buttons) — e.g. **"Compile / Reload Blueprint"**. (If QuickReload compiles from disk, the action should Save-then-compile, or require the asset saved; pick the clean option and document.)
- Keep the trigger callback ImGui-free + headless-testable where possible (the compile itself is a service call). Handle async cleanly (the editor loop is synchronous — `TriggerAsync` result handling must not deadlock; mirror how the existing seam/tests invoke it).

## Task 2 — headless compile→register→run integration test
The payoff test (ClusterRunner integration or Blueprints test, whichever can host the real kernel + QuickReloadService): take a simple Instance `.bp.json` that is NOT pre-registered, run it through `QuickReloadService.TriggerAsync`, assert it is now in the registry (`TryGetById`), then **attach it to a self-created entity via `BlueprintAttachService` and `PumpFrames(N)` through the real kernel (the MVE-02 wiring) and assert the blackboard `Count == N`**. This proves compile → register → run end-to-end without `dotnet build`. (If QuickReload requires the registry to be the kernel's instance, this test also guards the instance-sharing.)

## Task 3 — (if clean) Run-button auto-compiles when NotRegistered
Optional but ideal for the loop: if `BlueprintAttachService` returns `NotRegistered`, have the Run command first trigger compile-on-demand then retry the attach (so a single "Run" both compiles and runs an uncompiled opened blueprint). If async/coupling makes this messy, keep Compile and Run as separate actions and document the two-click flow. State the decision.

## Success Criteria
- [ ] Compiling the opened blueprint registers it into the SAME registry the kernel ticks; the Run button then resolves + attaches it (no more spurious NotRegistered for a valid opened Instance blueprint).
- [ ] Headless compile→register→run test: an initially-unregistered `.bp.json` → TriggerAsync → registered → attached → `Count` advances by pumped frames.
- [ ] Build 0 errors; touched projects no new warnings. GizmoMap.Contracts 0.2.2.
- [ ] Green: new test(s); `EditorSubsystemBoot` filter (QuickReloadService constructed at composition — must still boot); `Hrot.Blueprints.Tests` (DEBT-006 unchanged); `Hrot.Editor.AiShared.Tests`.
- [ ] Report at `.dev/blueprint-mve/reports/MVE-BATCH-05-REPORT.md`.

## Execution rules — YOU (the sonnet agent) run the full implement→build→test→fix loop yourself
- Verify the QuickReloadService API + the registry-instance-sharing question against the code FIRST (cite file:line). The single most important correctness check: the compiled blueprint must land in the registry instance the MVE-02 kernel wiring (`BlueprintRuntimeWiring`/`BlueprintTickSystem`) ticks — if not, fix the instance sharing. Reuse QuickReloadService/AttachService/the kernel wiring; do NOT reimplement compile or runtime. Gate ImGui. Build + run suites yourself; reach green; never fake a pass.

## Report
Document: the QuickReloadService wiring (ctor deps, registry instance shared with the kernel — proven), the trigger + toolbar action, save-then-compile vs in-memory-compile decision, the async handling, whether Run auto-compiles, the compile→register→run test + counts, build/suite results, and the next steps (hot-reload MVE-06, debug-observe MVE-07). Suggested commit message. No comprehension questions.
