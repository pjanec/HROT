QUESTION — in Edit, does the mode indicator show the PEN (presets D10, CE-4252) or the dimmed ARMED mode (AQ108 D1)? lean: the pen. Part P (the pen) of CE-3394 skipped; the rest built at `264f51b95`.

**The conflict, measured.** `RunModeChoice.Indicator` (`Hrot/Engine/Hrot.Presentation/RunMode/RunModeChoice.cs:693`) draws, in Edit, the ARMED mode DIM (AQ108 D1 — what ▶ will start). [Presets](../../UX/DESIGN_Scenario_Presets.md) §2 / D10 and CE-4252 (user, 2026-11-03: *"before I press play … I would like to see the yellow pencil"*) want a yellow PEN in Edit when Save is on, and a steady EYE for a loaded variant preset. Both are approved; they cannot both own the one glyph.

**Lean: the pen wins the indicator in Edit; the armed mode moves to ▶.** ▶ already takes the armed mode's colour (`MainToolbarTimeControlSection.PlayColour`, LC-14) and its tooltip names it (*"▶ Preview — Night attack"*, presets §5c), so the armed mode stays visible without the indicator. Edit: yellow pen ⇔ `IScenarioSession.SaveDisabledReason` is null; a variant world: the eye, steady; any running Preview: the eye, blinking. Blast radius: the indicator's Edit branch + `ColourIconsFrameRail` / `MainToolbarTimeControlTests` expectations; AQ108 §1a gets the old D1 rule under HISTORY.

**What would change the lean:** if the user wants the armed glyph to stay visible beside the pen (two glyphs), the indicator grows a second slot instead.

Built already (CE-3394 minus the pen): the eye replaces ◐ for Preview everywhere; Save / Save As greyed with the gate's reason; `SaveCurrent` / `SaveAs` refuse; `POST /scenario/save` answers 409.

— backend, 2026-11-07 (struct editor overnight)
