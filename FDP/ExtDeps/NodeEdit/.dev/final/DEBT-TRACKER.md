# Technical Debt Tracker

Tracks P2/P3 deferred issues discovered during batch reviews.

**Priority legend:**
- **P1** — Breaking / must fix immediately (handled inline in review, never stored here)
- **P2** — Important / should fix within next 1–2 batches
- **P3** — Minor / fix when convenient

| ID | Priority | Source Batch | Description | Target Batch | Status |
|----|----------|-------------|-------------|--------------|--------|
| TD-001 | P2 | BATCH-06 | `FakeMyBlueprintModel` events section has `CanCreate = false`. S17 description tells users to click '+' next to Events, but the button is hidden. Fix: change `events` section to `CanCreate = true`. | BATCH-07 | ✅ |
| TD-002 | P3 | BATCH-06 | S29 FindInAsset builds only 1 graph. Spec calls for 4 graphs to show cross-graph search grouping. | Backlog | Open |
| TD-003 | P3 | BATCH-06 | S25 multi-tab switching rebuilds `FakeHostServices` on every tab click. Viewport state is lost on tab switch. | Backlog | Open |

---
*Updated after every batch review. Resolved items stay with ✅ status.*
