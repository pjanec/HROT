<!--STATUS
state: LIVE — ui → coordinator: "still keys captured by Verify" (TE-10, CE-1305) — ui's half built; the night run that enqueues the stills is CE-4060's (yours)
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §11v S1–S3 (what TE-10 means now, as built) · §11u (the render queue) · §11r (stills) · §11s D3 (on a recording the index leads)
  - ../../DESIGN_Film_Production.md — §4 / §4a: the overnight documentation (CE-4060) that renders through this
  - ./NOTE_Ui_To_Coordinator_Render_Queue.md — the queue seam this note builds on
-->
# NOTE — ui → coordinator: still keys captured by Verify (TE-10) — what ui built, what stays yours

**Dispatched at** the commit that adds this file (`ui` worktree, CE-1305). Nothing in CE-4060's code (`DocumentationTakes.cs`, the verify runner) is changed.

## What "still keys captured by Verify" means now

TE-10's first wording was *"the Verify run captures each still key as a PNG"*. CR-10 / CR-12 (approved, R-4091) moved rendering to the Replay Browser:
the verify run records itself (`--verify-replay`) and the stills are rendered **from its recording**. §11r R3 refuses stills on the editor for that reason.
⇒ TE-10 is: **a verify run's recording renders the still keys of a take into the report folder** — the `_doc` take your `DocumentationTakes.From` drafts
(every key a `Still`), and any scenario take whose keys name a `still`.

## What ui built (Director Timeline §11v S1, S2)

- **The stills plan resolves keys through the run's ONE resolver** (`DirectorRunner.AnchorTimesOf` → `AnchorBook`: a bake made for the scenario file as it is,
  with the recording's index leading). Before, the plan read the take's BAKE only — a promoted take carries another run's bake or none (§11s D6), so every
  anchored still key on a verify recording was refused (*"needs an anchor that is not baked"*) or placed at the authoring run's time. Rail
  `ReplayBrowserSubsystemTests.CE1305_TE10_AVerifyRunsRecording_PlansItsStillKeys_AtItsOwnEvents_NotAnotherRunsBake`.
- **A stills render job waits for its recording's index** after the open (it is scanned in the background), so the night's jobs never plan on a half-read
  index. Rail `DirectorRunnerTests.CE1305_TE10_AStillsRenderOfARecording_WaitsForItsIndex_BeforeItPlans`.

⚠ Your `_doc` take uses FIXED times (Film Production §4a: *"a documentation take is of THIS recording, never promoted"*), so it was never hit by the bake
problem; the fix matters for scenario takes with still keys rendered on each night's recording.

## What stays yours (CE-4060)

1. Per scenario, after its verify run: save the `_doc` take beside the recording, then enqueue — one request for the whole night:
   ```
   POST /director/renders {"renders":[
     {"recording":"<verify run 1>/node_1.fdp","kind":"stills","take":"_doc","dir":"<report>/<scenario 1>"},
     {"recording":"<verify run 1>/node_1.fdp","kind":"stills","take":"main","dir":"<report>/<scenario 1>/takes"},   ← only when that take names stills
     …]}
   ```
   A take with no still keys fails its job with *"the take has no still keys"* — harmless, but you can skip it (a key's `still` is in the take file).
2. ⚠ It needs a WINDOWED Replay Browser (Xvfb); a headless one refuses at enqueue.
3. ⚠ CE-1374 (your file): `DocumentationTakes.cs:95` writes `" ✅"`, which no baked font carries — the burned-in caption would draw `?`.
