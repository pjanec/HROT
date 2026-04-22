# Window Manager & Icon System — Technical Debt Tracker

**Project:** `win-mgr-1`  
**Updated:** 2026-04-01

---

## Debt Registry

| ID | Priority | Source Batch | Description | Target Batch | Status |
|----|----------|-------------|-------------|--------------|--------|
| DEBT-001 | P3 | BATCH-01 | `GetUvCoordinates` parses string every call — callers should cache UV pair if called per-frame. | Future | Open |
| DEBT-002 | P3 | BATCH-01 | Pre-existing `CS5001` in `FDP/ExtDeps/FastCycloneDds/debug_tool/DebugOffsets.csproj` — prevents clean `dotnet build FDP/FDP.sln`. Not introduced by this workstream. | Future | Open |
| DEBT-003 | P2 | BATCH-04 | `ImGui.AddSettingsHandler` not accessible via ImGui.NET 1.91.0.1 managed bindings. JSON fallback implemented. Pure `imgui.ini` integration deferred. | Future | Open |
| DEBT-004 | P3 | BATCH-05 | `PerspectiveUpdateSubsystem.Coordinator` uses deferred-set property pattern. Cleaner approach: constructor injection with late binding. | Future | Open |
| DEBT-003 | P2 | BATCH-04 | `ImGui.NET 1.91.x` does not expose `ImGuiSettingsHandler` or `ImGui.AddSettingsHandler` in its managed bindings (confirmed via reflection). WM-S401 is implemented with JSON-based fallback persistence (`fdp_windows.json`). When the bindings expose the native hook, `SaveSettings`/`LoadSettings` can be wired to the ImGui ini pipeline instead. | Future | Open |

---

## Resolved Debt

| ID | Description | Resolved In |
|----|-------------|-------------|

---

## Notes

- **P1:** Must be fixed before next batch (becomes Corrective Task 0).
- **P2:** Should be fixed within 2 batches.
- **P3:** Track and fix when convenient / opportunistic.
