# BATCH-09 Review — TASK-BT-09 Emit AssetId in [BTreeDefinition] (REVIEW-BT F1)

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED (after lead correction)

## What landed (mirrors HSM)
- `BTreeDefinitionAttribute` gains additive `string? AssetId` (mirrors `HsmDefinitionAttribute`).
- `BTreeEmitCore` emits `[BTreeDefinition("Name", AssetId = "{guid:D}")]` (mirrors `HsmEmitCore:468`).
- `BTreeAssetContributor` uses `defAttr.AssetId` when parseable, else `FromName(treeName)` (mirrors `HsmAssetContributor`).
- Regenerated `CombatShowcase.g.cs` → `[BTreeDefinition("CombatShowcase", AssetId = "aaaaaaaa-…1")]` → JSON + assembly share AssetId → dedupe (fixes the duplicate).

## Verification (independent — caught 2 issues)
- **Scope creep REVERTED:** the worker also re-serialized `CombatShowcase.btree.json` (tabs→spaces, field reorder, `Blackboard.TypeName ""→"BrainBlackboard"`) — unrelated to BT-09. Reverted via `git checkout HEAD`; re-ran tests green without it.
- **"Pre-existing" claim VERIFIED (not trusted):** the 2 `Generators.Tests` failures (`MigrationEquivalenceTests` BTree_SampleScout + Hsm_SampleGuard) — I **stashed BT-09 and re-ran on clean HEAD: same 2 fail** → genuinely pre-existing (a separate persistence-unification issue), NOT a BT-09 regression. (Worker's "$meta byte-stability" label was imprecise but the pre-existing conclusion was correct.)
- Golden rebaseline is legit: `ByteIdenticalGateTests` updated the SampleScout assertion to the `AssetId =` form (not weakened) + added a strong new emit test.
- New `BTreeAssetContributorTests` (2): real `[BTreeDefinition]` fixtures (with/without AssetId) assert attribute-AssetId vs FromName fallback.
- Final (showcase reverted): full `dotnet build` 0 errors; **Persistence.Tests 113/0**, **BTree.Editor.Tests 493/0**.

## Pre-existing failures (not ours; do not chase)
- `Generators.Tests`: 2 (`MigrationEquivalenceTests` BTree_SampleScout, Hsm_SampleGuard) — verified pre-existing on HEAD.
- `Fbt.Tests`: 9 — FastBTree kernel pre-existing (the BT-09 change is a purely additive attribute property; cannot affect runtime/compiler tests).

## Verdict
APPROVED. Duplicate root cause (name-derived AssetId mismatch) fixed by carrying the real AssetId through codegen — also fixes the latent rename-stability issue. "One CombatShowcase in the browser" confirmed at REVIEW-BT-2.

## Commit message
```
fix(btree-editor): carry AssetId through [BTreeDefinition] codegen so JSON/assembly dedupe (BATCH-09 / TASK-BT-09)

BTreeDefinitionAttribute gains an additive AssetId property (mirrors
HsmDefinitionAttribute); BTreeEmitCore emits it; BTreeAssetContributor prefers
it over FromName(treeName). Fixes the duplicate CombatShowcase (REVIEW-BT F1):
the assembly-reflected asset now shares the JSON asset's real AssetId instead of
a name-derived one, so the dedupe (JSON-wins, design D4) collapses them. Also
removes the latent rename-instability of name-derived AssetIds. Rebaselined the
[BTreeDefinition] golden assertion + added contributor/emit tests.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
