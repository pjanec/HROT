# BATCH-38 — BUG-A2/A3: scenario Save via icon + save feedback

**Bugs:** BUG-A2 (scenario Save broken in the "Scenario"/Editor perspective) + BUG-A3 (no save feedback).
**Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP` · From MTB2-T4 (BATCH-33).

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file. 3. Read `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs`
   and the `ShellSaveCommands.Register(...)` call site in `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (search
   `ShellSaveCommands.Register`) including the `isScenarioContext`/`hasLoadedScenario`/`saveScenario`/
   `describeActiveTarget` lambdas wired there.

## ⚙️ RULES (non-negotiable)
1. Touch ONLY: `ShellSaveCommands.cs`, `SaveCommandsTests.cs`, `EditorSubsystem.cs`. No other files.
2. NEVER hide a problem to pass a build (no excluded assets / `[Skip]` / weakened tests / stubs / suppression).
3. Add/update the EXACT named tests; assert real behavior.
4. DO NOT STOP until build = 0 warnings AND the test commands show `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact changes + final summaries. No litter.

## The bugs (observed at runtime)
In the Editor/"Scenario" perspective with a (new) scenario: the Save icon is **disabled** and its tooltip shows the
**stale document** ("blueprint: Count5"); scenario saving via the icon does nothing. Also, **no save gives any
feedback**. Causes:
- `shell.save` `IsEnabled` in scenario context gates on `hasLoadedScenario()`, which is **empty after `NewScenario()`**
  → disabled.
- `describeActiveTarget` falls through to the **document** branch when the scenario is unnamed → shows the stale doc.
- Saves emit no status/log message.

## Part A — `ShellSaveCommands.cs`
1. **`shell.save` `IsEnabled`** → in scenario context, **always enabled** (you can always Save, falling back to
   Save-As when unnamed): `() => (isScenarioContext?.Invoke() == true) ? true : docManager.Active != null`.
   Apply the SAME change to `shell.saveAs` `IsEnabled`.
2. **`shell.save` handler scenario branch** → route by whether the scenario is named (mirror the document
   empty-path→Save-As rule):
   ```csharp
   if (isScenarioContext?.Invoke() == true)
   {
       if (hasLoadedScenario?.Invoke() == true) saveScenarioAction?.Invoke();
       else                                     requestScenarioSaveAs?.Invoke();
       return;
   }
   ```
   (`shell.saveAs` scenario branch keeps calling `requestScenarioSaveAs?.Invoke()`.)
3. **Save feedback (BUG-A3):** after a successful per-kind **document** save (each `case` that calls
   `saveBlueprint`/`saveBTree`/`saveHsm` then `doc.MarkClean()`), call
   `report?.Invoke($"[OK] Saved {doc.Kind}: '{doc.Asset.Name}'.");`. (Scenario feedback is wired in Part B.)

## Part B — `EditorSubsystem.cs` (the `ShellSaveCommands.Register(...)` lambdas)
4. **`describeActiveTarget`** → in scenario context ALWAYS describe the scenario (never the document):
   ```csharp
   describeActiveTarget: () =>
   {
       if (windowManager.CurrentPerspective == "Editor")
       {
           var n = _editorLogic?.LoadedScenarioName;
           return string.IsNullOrEmpty(n) ? "Save Scenario" : $"Save [scenario: {n}]";
       }
       var act = _aiDocumentManager?.Active;
       return act != null
           ? $"Save [{act.Kind.ToString().ToLowerInvariant()}: {act.Name}]"
           : "Save";
   }
   ```
5. **`saveScenario` seam (feedback)** → after saving, set the status:
   `saveScenario: () => { _editorLogic?.SaveCurrentScenario(); _saveAllStatus = $"[OK] Saved scenario '{_editorLogic?.LoadedScenarioName}'."; }`
6. **`report` seam** → ensure the `ShellSaveCommands.Register(...)` call passes `report: msg => _saveAllStatus = msg;`
   (so the Part-A document-save messages surface). If `report:` is already wired, keep it.

Keep all wiring null-safe; do NOT change `ScenarioMenuCommands`.

## Tests — `Hrot/Editor/Hrot.Editor.AiShared.Tests/Documents/SaveCommandsTests.cs`
- **UPDATE** `Save_IsEnabled_ReflectsActiveTarget` — scenario context is now **always enabled** (no longer gated on
  `hasLoadedScenario`); document context still enabled iff `docManager.Active != null`. Update the assertions
  accordingly.
- **ADD** `Save_InScenarioContext_Named_RoutesToSaveScenario` — scenario ctx + `hasLoadedScenario => true` → invoking
  `shell.save` fires `saveScenarioAction`, NOT `requestScenarioSaveAs`.
- **ADD** `Save_InScenarioContext_Unnamed_RoutesToSaveAs` — scenario ctx + `hasLoadedScenario => false` → invoking
  `shell.save` fires `requestScenarioSaveAs`, NOT `saveScenarioAction`.
- **ADD** `Save_Document_ReportsSavedMessage` — document ctx, active Blueprint with a non-empty SourceFilePath +
  a recording `report` spy → invoking `shell.save` calls `report` with a message containing the asset name.
- All other existing `SaveCommandsTests` (incl `NullSeams_PreserveLegacySaveBehavior`) must still pass — with null
  seams, behavior is unchanged (the new scenario IsEnabled branch only differs when `isScenarioContext` is non-null).

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~SaveCommands"
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
All `Failed: 0`.

## Definition of done
- Scenario Save works via the icon: enabled in the "Scenario" perspective; named → SaveCurrent, unnamed → Save-As;
  the dynamic label/tooltip names the scenario (not a stale doc). Document + scenario saves emit a status message.
  Back-compat preserved (null seams). Tests updated/added pass.
- Build 0 warnings; filtered `SaveCommands` + `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/main-toolbar-2/reports/BATCH-38-REPORT.md`: changes, test updates, final summaries.

If something cannot be done as specified, STOP and report why.
