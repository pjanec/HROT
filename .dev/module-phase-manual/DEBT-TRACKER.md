# Technical Debt Tracker: Network Architecture Cleanup and Module Phase Manual

**Project:** MPM (Module Phase Manual)  
**Updated:** 2026-04-22

---

## Format

| ID | Priority | Source Batch | Description | Target Batch | Status |
|----|----------|--------------|-------------|--------------|--------|

---

## Active Debt Items

| ID | Priority | Source Batch | Description | Target Batch | Status |
|----|----------|--------------|-------------|--------------|--------|
| DEBT-001 | P3 | BATCH-01 | `FDP/FDP.sln` references two missing project files (`Fdp.ModuleHost.Core.csproj`, `ModuleHost.Benchmarks.csproj`). Always breaks `FDP/FDP.sln` standalone build. Pre-existing issue. | Deferred | Open |
| DEBT-002 | P3 | BATCH-01 | 4 pre-existing test failures in `Hrot.IG.Tests` (`AdvancedFeaturesIntegration` x2, `GeoSpatialDRTranslator` x2). Unrelated to this batch. | Deferred | Open |

---

## Resolved Debt Items

*None yet.*
