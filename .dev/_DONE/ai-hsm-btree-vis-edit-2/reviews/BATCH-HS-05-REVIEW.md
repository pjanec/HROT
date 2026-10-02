# BATCH-HS-05 Review — TASK-HS-05 initial-state arrows [VISUAL GATE]

**Reviewer:** Dev Lead · **Date:** 2026-06-13 · **Status:** ✅ APPROVED (headless) · **Impl:** Zoo

## Verification (independent — read diff, re-ran suite)
- **CollectInitialMarkers** (pure logic): skips synthetic root; parallel → one marker per region with non-null `InitialChild` (carries `RegionIndex`); normal composite → first child with `IsInitial` (`RegionIndex = -1`); simple/leaf/no-initial → nothing. Matches spec.
- **ComputeMarkerGeometry** (pure math): circle floats `MarkerGap (24)` above child top-center; arrow down to child top edge; `arrowStart == circleCenter`. Graph-space, no container-bounds dependency (works for composite + region children alike).
- **Render:** marker loop added above the existing LCA loop (LCA path untouched); GraphToScreen + `ctx.Zoom` scaling consistent with `DrawLcaOutline`; filled circle + arrow line + two-line "v" arrowhead (no existing arrow helper in ImDrawListExtensions — confirmed). Defensive, no per-frame heavy alloc.
- **No cheating:** touched only the renderer + new test file.
- **Tests (6, behavioral):** composite-with/without-initial; parallel two-regions (RegionIndex + child); region-with-null-initial skipped; synthetic-root skipped; exact geometry (`arrowEnd (160,200)`, `circleCenter (160,176)`, `arrowStart == circleCenter`). Values, not strings.
- **Re-run (no regenerate flag):** `Hrot.Hsm.Editor.Tests` **423/0** (6 new, 0 pre-existing failures). Build 0 errors.

## Issues
None. **[VISUAL GATE]** — actual circle/arrow pixels (radius, color, arrowhead, parallel-region placement) confirmed by lead at REVIEW-HS.

## Verdict
APPROVED (headless). Initial-pseudostate markers are computed for every composite + parallel region and drawn; the LCA highlight still works.

## Commit message
```
feat(hsm-editor): initial-state arrow markers (BATCH-HS-05 / TASK-HS-05)

HsmInitialArrowRenderer.Render had a TODO for the initial-child marker. Add two
pure internal helpers — CollectInitialMarkers (composite -> IsInitial child;
parallel -> each region's InitialChild; skip synthetic root) and
ComputeMarkerGeometry (filled circle floating above the child top-center, arrow
down) — and draw circle + arrow + arrowhead (ctx.Zoom-scaled), keeping the
existing LCA-highlight loop. +6 headless tests (marker collection + exact
geometry). Pixel appearance is the lead's visual gate (REVIEW-HS).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
