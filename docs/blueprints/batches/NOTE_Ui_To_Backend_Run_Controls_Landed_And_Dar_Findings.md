<!--STATUS
state: LIVE
updated: 2026-10-29
current-answer: whole file
related-designs:
  - ../../DESIGN_Cluster_Load_Phase.md — §11.8a (CE-1445 as built) · §11.9 (R-1046, the map step seam)
-->
# NOTE ui → backend — CE-1445 landed · two DAR findings for you · merged backend@`5c6c04f7c`

ui merged `backend` at `9df8f3c04` (your CE-1446 design + note, CE-3346 admission + the late-join rail).

| | |
|---|---|
| ✅ **CE-1445 landed** (`0df1b7d11`) | `NodeRunControls.For(HrotNodeContext, NodeRunProfile)` (`Hrot.Presentation/RunMode/NodeRunControls.cs`) is the ONE construction of an ECS host's adapter + run choice — CGF, SimHost, the editor and now the IG. ⚠ **For CE-1446 B1/B2:** when CGF / the editor move onto `CgfNodeBootstrapper` / `EditorNodeBootstrapper`, keep calling `NodeRunControls.For` (it needs only the node context + a profile) — the CE-1445 rails (`TheRunChoiceIsOnEveryHostTests.CE1445_*`, `TheStartGateIsComposedOnceTests.CE1445_*`) are worth adding to B1/B2's gates. The IG now offers `IProvidesRunControls` (Player profile, time control suppressed); its debug provider still has no drive (Q54/D4) |
| 🔴 **CE-1447 — yours** | a SECOND DAR in the same live run reports `LivePointSimSeconds` = the DAR's own position (0.315 s vs live end ~8.1 s), and ■ then returns live AT the DAR position. The first DAR is right. Suspect: the master's review live point, `ReplaySessionProcessManager.cs:66-69` (`_review.LivePoint`). Reproduced through the real headless editor (CE-4104 rails); ui's rails run one DAR per run to stay clear of it |
| ℹ **CE-1448 — ui's, FYI** | after a DAR return the node's live timeline index restarts (297+ frames → ~41) — likely `LiveTimelineSource.Step` `:69` resetting on a frame number behind the last indexed one (the restored world's). ui investigates; it touches `Hrot.Core` timeline code |
| ⏳ **R-1046 map step** | ui is building the `IMapBuildStatus` + the background builder now (agreed seam, §11.9); a NOTE follows with where the status is reachable from the node |
| ⚠ **Stride (CE-1449)** | `StrideNodeShell.cs:740` builds its adapter by hand with no run choice — fix needs a Windows build; flagging since CE-1446 B3's "no host builds its node inline" rail may want Stride in its set too |
