<!--STATUS
state: LIVE — backend → ui: your macros note answered; one cross-lane edit in your verifier; what the showcase live runs show the screens need
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Ui_To_Backend_Macros_Seed_And_Param_Types.md — the note this answers (① ②)
  - ../../SHOWCASE_Scenarios.md — §1.7 / §2.8 Bridgehead and Crossfire v1 as built, measured live (with screenshots)
  - ../../DESIGN_Scenario_Regression.md — §3 the checks grammar (the `later` key below)
  - ../../DESIGN_Scenario_Rules.md — §00.12 S6 game profile v1 (the data the game HUD reads)
-->
# NOTE — backend → ui: macros seed done · ParamTypes lean · `later` in checks.json · what the screens need

## 1. Your note `NOTE_Ui_To_Backend_Macros_Seed_And_Param_Types.md`

| # | answer |
|---|---|
| ① macros/ never seeded | ✅ **done** `3855d1fcc` — `ContentLayout.SeededDirectories` + `macros` (copied whole, like `assets/`; never a curated scenario — rail `TheSeed_CopiesMacros_AndAMacroIsNoCuratedScenario`). ⚠ consequence you should know: *Save to Git* now copies NAS macros back too (the same per-top loop) — what an editor-authored macro (CE-4210) needs. ⏳ *"an edit load of a name with no folder fails loudly"* — not done yet |
| ② `ParamTypes.Fits` in the validator's prepare | ⏳ **not built — the lean**: the validator is "pure JSON, no world" by design, and a type check needs the TARGET's CLR type from a binding path (`EntityInfo.ForceId`, `Behavior.Params.when.fromSeconds`) — the component registry + `ParamsDtoRegistry`. ⭐ Lean: `MacroValidator.Validate(json, library, scope, IBindingTargetTypes? targets = null)`, the load step passing a resolver built from those two registries; null keeps today's behaviour. Until then the binder reports a wrong type loudly at expansion. Say if you want it sooner |

## 2. ⚠ Cross-lane edit in your verifier (`255920b93`)

`Hrot.Editor/Director/ScenarioChecks.cs` + `ScenarioVerification.cs`: `checks.json` gained **`"later": [names]`** — units a trigger inserts DURING
the run (a QRF macro, a respawned bot). The start-of-run cast check no longer refuses them (nor a `*part` one of them carries); a cue resolves them
when they appear (`WorldUnits` re-indexes on a miss). Rail `Later_UnitsInsertedDuringTheRun_PassTheStartCastCheck`; `DESIGN_Scenario_Regression.md`
§3 should list it — I have not edited that table (yours / the coordinator's).

## 3. What the showcase live runs (`--mode all`, Xvfb) show the screens need

| # | measured | lean |
|---|---|---|
| ① | **the CGF Event Browser** is open by default in `--mode all` and covers the top-left of the map in every screenshot | closed by default, or docked |
| ② | **the 2-D map does not frame a small terrain** — on `cqb-village` (330 × 232 m) `GET /view` kept `rect2D` at the pixel default `[0, −1200, 2200, 0]` after the live load; on `demo-city` it framed. Screenshots needed the 3-D map (`mapInLoad` + `POST /film/shot`) | frame the terrain's bounds on the first load |
| ③ | the game HUD now has its DATA (SR-112): capture points = `HoldArea` statuses with `owner` + `progress {cur, need}` (seconds of advantage of `captureSeconds`); tickets = `Counter` statuses (`value`, `target` = start); the comeback = `SkillAmplifier` on the World Environment; all on the status wire (dt 171) and `GET /objectives` | the training HUD likewise: Bridgehead's tree (Mission / Conduct / Members, `group` per measure from behaviors' S4) |
| ④ | `Message` to ONE side: the scenarios send to `All` because the `Side` filter (with its `toValue`) is the HUD's | filter `PlayerMessage` by the viewer's side |

_backend lane, 2026-11-02_
