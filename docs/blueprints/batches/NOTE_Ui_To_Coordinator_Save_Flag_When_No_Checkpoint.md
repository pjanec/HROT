QUESTION — the Save slot's amber flag where no checkpoint CAN be taken (AAR, a Live not recorded): lean **greyed with the reason** (built); R-5 part built, nothing skipped.

| | |
|---|---|
| the picture | `run-indicator-visual.svg` draws the flag AMBER in the AAR row and in *Live · running · NOT recorded* |
| D27 | "the amber flag (take a checkpoint) in Live / DAR / AAR" — and R-5 "greyed with its reason when not saveable" |
| the code | a checkpoint needs a recording: the master / `ReferenceCheckpointHandler` refuse one in an AAR or an unrecorded run (`RunModeChoice.CheckpointDisabledReason`) |
| built (ui, `9348c80ec`) | the flag is GREYED there, its tooltip says why (*"a recording cannot take a checkpoint — go live from a moment first"* / *"this run was not recorded"*); Ctrl+S greys with it |
| the alternative | amber, and a click toasts the refusal |

Lean greyed: the same rule as the floppy (greyed with its reason), and a control that looks live but always refuses is the LC-17 trap the lifecycle review removed.
