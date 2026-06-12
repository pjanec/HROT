# Quick-Reload (BTree + HSM) — Task Tracker

In-process quick reload for BTree/HSM (PU-09 / EB-E), mirroring the Blueprint `QuickReloadService`.
Design of record: [DESIGN.md](./DESIGN.md). Per-task specs: [TASK-DETAIL.md](./TASK-DETAIL.md).
One line per task. `[ ]` open · `[~]` in progress · `[x]` done (built + tests green + lead-reviewed + committed) · `[!]` blocked.
**Phases are sequential** — QR-01→QR-02 are the foundation; QR-03/04 depend on QR-02; QR-05 depends on QR-03/04.

---

## Working agreement — implementing agent (Zoo) — MANDATORY, restate in every batch

1. **One task per batch.** Do not combine tasks. Do not touch code outside the task's named files/scope. Do not edit
   files owned by other tasks/workstreams (esp. the `ai-hsm-btree-vis-edit-2` thread).
2. **No cheating to pass the build/tests.** NEVER exclude/delete a user asset from compilation, `<Compile Remove>`,
   `#pragma warning disable`, suppress a diagnostic, weaken an assertion, or stub a feature to dodge a hard error.
   If blocked, **STOP and write the blocker in the report** — do not paper over it.
3. **Finish without asking.** Build + run the named test project(s), diagnose root causes, fix, repeat until
   `Failed: 0` and **0 warnings**, then write the report. No permission-asking; nothing "done" while red.
4. **Headless only.** Verify via build + unit tests. Tasks marked **[RUNTIME GATE]** need the lead to confirm the
   actual hot-swap in the running editor — Zoo is NOT responsible for that, but MUST make the logic headless-testable
   (assert real values/enums/offsets — not generated-string `Contains`).
5. **Litter-free.** No scratch files, no leftover `Console.WriteLine`/`File.WriteAllText`. Clean tree.
6. **Report = truth.** The report must match the diffs. The lead reviews diffs + assertions, not prose.
7. **Do NOT use codebase-memory MCP tooling.** Read the actual source.

**Verification baseline (run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`):** the tree builds green post-rebase
(`Hrot.Editor` 0/0, `Hrot.Editor.Tests` 185/0). `Hrot.Blueprints.Tests` has a small pre-existing PRE-1 failure set —
stay a *subset* (0 new). Each batch states its exact build + test commands.

**Folders:** batch → `batches/BATCH-QR-XX-INSTRUCTIONS.md`; report → `reports/BATCH-QR-XX-REPORT.md`;
lead review → `reviews/BATCH-QR-XX-REVIEW.md`.

---

## Tasks

- [x] **QR-01** Multi-source overload on `InMemoryRoslynCompiler` (parse N C# sources → one `CSharpCompilation`). Pure,
      unit-testable. → [details](./TASK-DETAIL.md#qr-01) *(e01b6efb — multi-source Compile; single-source delegates; +2 tests; 5/0)*
- [x] **QR-02** `QuickReloadService.TriggerFromSourcesAsync(sources, assemblyName/assetIdHash, debugMap?)` — extract the
      Roslyn→ALC→scan→coordinator steps; refactor `TriggerAsync(BlueprintAsset)` to call it. Blueprint reload must be
      byte-for-byte behavior-identical. → [details](./TASK-DETAIL.md#qr-02) *(2026-06-13 — TriggerFromSourcesAsync extracted; TriggerAsync delegates; +1 test; 12/0)*
- [x] **QR-03** BTree quick-reload trigger in `EditorSubsystem` (`_btreeQuickReloadTrigger`): active `BehaviorTreeAsset`
      → DTO (reuse save-path mapper) → `BTreeEmitCore.EmitTopologyCore` + `BTreeBridgeEmitCore.EmitBridge` →
      `TriggerFromSourcesAsync`. **[RUNTIME GATE]** → [details](./TASK-DETAIL.md#qr-03) *(2026-06-13 — _btreeQuickReloadTrigger wired; build 0/0; tests 185/0)*
- [x] **QR-04** HSM quick-reload trigger (`_hsmQuickReloadTrigger`), symmetric to QR-03 via `HsmEmitCore` /
      `HsmBridgeEmitCore`. **[RUNTIME GATE]** → [details](./TASK-DETAIL.md#qr-04) *(2026-06-13 — _hsmQuickReloadTrigger wired; build 0/0; tests 185/0)*
- [ ] **QR-05** Widen the `blueprint.compileReload` toolbar command to dispatch by active-doc **kind**
      (Blueprint/BTree/HSM) and be enabled in all three perspectives. (Rename concept → generic "Compile / Reload".)
      → [details](./TASK-DETAIL.md#qr-05)
- [ ] **REVIEW-QR** *(lead/user RUNTIME GATE)* — edit a BTree and an HSM, hit Compile/Reload, confirm the change
      hot-swaps in the running sim (no full rebuild), within target latency; blueprint reload still works.

## Progress
3/5 + REVIEW-QR. Foundation: QR-01, QR-02. QR-03 (BTree) proves the path; QR-04 (HSM) mirrors; QR-05 wires UX.

## Done-definition
Editing a BTree or HSM asset and triggering Compile/Reload hot-swaps the behavior in-process (no MSBuild rebuild),
reusing the blueprint hot-reload coordinator via the `[BlueprintRegistrar]` masquerade; blueprint reload unchanged.
