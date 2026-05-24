# RB-5.2 — End-to-End Manual Smoke Test Checklist

**Prereq**: A `.fdp` recording file from a scenario run (e.g. `scenarios/hill-attack/`).

## Steps

1. Build and run: `dotnet run --project Hrot\Runner\Hrot.ClusterRunner -- -m replaybrowser`
2. Verify the GUI launches in the ReplayBrowser perspective with 5 docked windows:
   - Timeline panel (bottom)
   - Entity inspector (right, top)
   - Event browser (right, mid)
   - Component diff viewer (right, bottom)
   - Search panel (left or floating)
3. Click `File > Open Recording...` (or use the timeline panel's open button) to load the `.fdp` file.
4. Verify: scrubbing the timeline advances the frame counter; the inspector shows entity components.
5. Select an entity in the inspector; verify the diff viewer shows changed components.
6. In the event browser, scroll through events; click a frame link; verify timeline seeks.
7. In the search panel: run a Component search (e.g. `HarnessPosition.X > 0`); click a result row; verify timeline seek.
8. Switch to any other perspective (e.g. SimHost) and back; verify dock layout is preserved.
9. Click `Save to JSON...` in the timeline export expander; verify the JSON file is created without UI freeze.
10. Confirm no exceptions appear in the console output.

## Pass Criteria

All 10 steps complete without error.
