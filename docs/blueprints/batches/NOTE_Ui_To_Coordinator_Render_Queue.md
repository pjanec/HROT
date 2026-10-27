<!--STATUS
state: LIVE — ui → coordinator: the render queue is built; CE-4060's night run can enqueue every recording's stills in one call (a seam offered, nothing of yours changed)
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §11u (R4: the reel composer and the render queue, as built) · §11r (stills, recording=, MP4 in the RB)
  - ../../DESIGN_Film_Production.md — §4 / §4a: the overnight documentation (CE-4060) this seam serves
-->
# NOTE — ui → coordinator: the render queue (CE-1306) — a seam for CE-4060's night run

**Dispatched at** the commit that adds this file (`ui` worktree, CE-1306). Nothing in CE-4060's code (`DocumentationTakes.cs`) is changed.

**What CE-4060 renders through today:** nothing yet — `DocumentationTakes.From` builds the `_doc` take, and the designed path
([Film Production §4a](../../DESIGN_Film_Production.md) "what renders it") is `POST /director/stills {recording, take:"_doc"}`: ONE recording per call,
answered when done, 409 while another job runs.

**The seam ([Director Timeline §11u](../../DESIGN_Director_Timeline_Editor.md) D3–D5, D9):** one call queues the whole night, each recording opened AT ITS
TURN through the same open `POST /replay` makes, a failure named per job without stopping the rest:

```
POST /director/renders {"renders":[
  {"recording":"<exercise 1>/node_1.fdp","kind":"stills","take":"_doc","dir":"<report>/<scenario 1>"},
  {"recording":"<exercise 2>","kind":"stills","take":"_doc","dir":"<report>/<scenario 2>"},
  {"recording":"<exercise 3>/node_1.fdp","take":"main","capture":{"size":"1280x720","fps":30,"dir":"<report>/<scenario 3>/clip"}}   ← a clip where a scenario opts in
]}
GET  /director/renders          → jobs[{id, state, output (the contact sheet / the MP4), outputs[] (the stills), problems[]}]
DELETE /director/renders/{id}   → cancel one
```

⭐ **Lean: switch the night run to the queue** — one request instead of N waits, and the morning list is the queue's. Same stills job, same plan
(`DirectorStills.Prepare` — the route and the queue plan the one way), same contact sheet. ⚠ It needs a WINDOWED Replay Browser (Xvfb), as stills already
do; a headless one refuses at enqueue. ⚠ A job's default folder is its own (`takes/_doc.stills/<id>/`) — pass `dir` for the report's.

**Found on the way, fixed:** a stills job after the map had been in 2-D failed every still but the first ("the map is switching") — the night run would
have hit it after any 2-D film or view (CE-1390). **Filed, not built:** a reel item naming its own recording (TE-16) — CE-1391.
