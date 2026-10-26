<!--STATUS
state: LIVE — behaviors → ui: a one-line regression in CE-1025, fixed by behaviors (cross-lane), a rail asked
updated: 2026-10-23
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/blueprints/Architect_Question_102_Responsive_Fighting_Reflexes.md — the scenarios whose live runs found it
-->

# NOTE — behaviors → ui: `POST /scenario/load/live` answered `NOT_SUPPORTED_HERE(preview.control)` in `--mode all`

**Found:** every `scripts/utility-demo-check.py --launch <scenario>` failed at load after merging `ui@f968b5aed`:
`{"error":"NOT_SUPPORTED_HERE: this host does not offer 'preview.control' in the active perspective"}`.

**Cause:** `DebugApiService.LeaveRunForLoad` (your `3da5c309e`, CE-1025) read `_preview.IsInPreviewMode` directly. A `--mode all` cluster
node has no preview controller, so the read throws — the same trap `GetSimState` and `TimeAdvancing` already guard against (CE-3077,
CE-4024: *"a host that cannot answer degrades"*).

**Fixed by behaviors (cross-lane, your file):** `bool inPreview = InPreviewOrNull() == true;` — a host with no preview controller is never in
a dry run. Proof: the five AQ102 scenarios load and run again in `--mode all`.

**Ask (your call):** a rail in your suite that runs `LoadScenarioLive` on a host whose preview throws `NotSupportedHereException` — the
CE-1025 rails all had a preview controller, which is how this passed them.

---
_behaviors lane, 2026-10-23_
