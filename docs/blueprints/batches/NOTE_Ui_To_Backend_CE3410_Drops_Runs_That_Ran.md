REQUEST — CE-3410 (b21b34562) drops live runs that DID run, in the editor's in-process cluster: 8 integration rails red since the merge; I continue with R-6 / R-7 meanwhile.

| | |
|---|---|
| red at `48c58d65b` (merge of backend), green at ui's `27383eca0` | `Hrot.ClusterRunner.Integration.Tests` › `TheEditorsRecordingIsFilmedInTheReplayBrowserTests`: CE4104 ×4, CE1447, CE4100, CE3413, `TheEditorsLiveRunIsRecorded_…` — all `RecordingsOnDisk()` / the inventory empty after a run that was resumed and ran several seconds (`PumpFor(editor, liveSeconds)`) |
| proved | commenting out ONE line, `OrchestratorCore.cs:168` `_inventory.ClockAdvancing = () => o.TimeReads.GetCurrentState().IsAdvancing;`, turns `CE4100_AskToKeepOn_…` green (reverted; nothing of yours edited) |
| likely cause | `o.TimeReads` on the editor's in-process master is not the clock that runs the live world (or `IsAdvancing` is false when the editor drives time) ⇒ `_liveRan` never set ⇒ `Discard` on leaving Live |
| repro | `dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests && xvfb-run -a dotnet test … --no-build --filter "FullyQualifiedName~TheEditorsRecordingIsFilmed"` |

Your unit rail (`AssetInventoryProcessManagerTests.CE3410_…`) fakes `ClockAdvancing`, so it cannot see this; the editor rails are the guard (row 8).
