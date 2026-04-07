# Debt Tracker — Module Init

**Workstream:** `mod-init`

---

## Legend

| Priority | Meaning |
|---|---|
| P1 | Blocking — must fix before next batch (becomes Corrective Task 0) |
| P2 | Important — schedule in next available batch |
| P3 | Low — address when convenient |

| Status | Meaning |
|---|---|
| 🔴 Open | Not yet addressed |
| 🟡 In-Progress | In current batch |
| ✅ Resolved | Fixed |

---

## Debt Items

| ID | Priority | Source | Description | Target Batch | Status |
|---|---|---|---|---|---|
| DEBT-001 | P2 | Pre-existing (`SimHostApp.cs`) | `// TODO (P2 debt): wire NedReplicationModule once it moves to Hrot.Common` — private `_nedReplicationModule` field retained as dead state | BATCH-03 (MODINIT-S301) | 🔴 Open |
| DEBT-002 | P2 | BATCH-01 report (Q5) | `NedReplicationModule.cs` still imports `using Hrot.SimHost.Network;` for `BrainPerceptionTranslatorPack`, `SimPerceptionTranslatorPack`, `SimPathfindingTranslatorPack`, `BrainPathfindingTranslatorPack` which remain in `Hrot.SimHost/Network/`. Module cannot be moved to `Hrot.Network` until these are resolved. Must be addressed in BATCH-02 before MODINIT-S201 can compile. | BATCH-02 (MODINIT-S201) | 🔴 Open |
| DEBT-003 | P3 | BATCH-01 report (Q2) | Hardcoded DDS domain IDs (`209u`, `210u`) in `TranslatorPackTests.cs` — parallel test collision risk as more packs are tested | Backlog | 🔴 Open |

---

## Notes

- DEBT-001 is the primary motivation for the entire `mod-init` workstream. It will be resolved when MODINIT-S301 is implemented.
