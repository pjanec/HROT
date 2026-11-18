<!--STATUS
state: LIVE — the protocol EVERY lane follows on a handoff that cites it (from `2026-11-18`)
updated: 2026-11-18
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - PROTOCOL_Two_Lanes_Self_Coordinating.md — the two-lane form this generalises (ui ⇄ backend, `2026-11-06`); its loop, status table and R-4156 skip rule are kept here
  - ../../../.claude/CLAUDE.md — the lane protocol, the gate contract (rule 8), T-1, the hot path, the scenario rule (R-4277)
-->

# PROTOCOL — autonomous lanes: keep busy, keep in sync, survive a stop, never block on a decision

🔒 User, `2026-11-18`: *"I need the handoffs to contain more bugs so the lanes keep themselves busy for longer. I also need them to sync with others as
they go, merging the others, leaving notes for others if needed, and being aware of token rate limit that can stop them any time, watching for new
handoffs for them and automatically taking them, working autonomously, skipping stuff needed decisions (reporting those in written so coordinator can
see and respond with decisions)"* · R-4156 (`2026-11-06`): *"if something turns out to require users decision it should be skipped"*.

**The lanes:** `backend` · `behaviors` · `terrain` · `ui` · `convoy` · the `exp/agg-*` lanes · any new one. ⛔ Nobody is watching: never stop to ask,
never wait idle, never end a turn on a question.

## 1. ⭐ A STOP CAN COME AT ANY MOMENT (the token rate limit) — so the branch is ALWAYS resumable

| rule | why |
|---|---|
| ⭐⭐ **commit and push after EVERY row** (and after every half-day's worth of a big row) | a stop loses only the unpushed part |
| ⭐⭐ **the status file is rewritten at every push** — what is done, what is IN PROGRESS and exactly where (file, next step), what is next | the resumed session (or a compacted one) starts from it, not from memory |
| ⭐ **unverified work never goes on the lane branch**: push it to `<lane>-wip-<topic>` and say so in the status | `coordinator` merges lane branches as they are — a red there costs every lane (📌 backend's `backend-wip-part-e` is the model) |
| ⭐ **long runs in the background**, and a run's result written into the status the moment it lands | a stop mid-wait otherwise loses the result |
| ⭐ **on resume: read your status file FIRST**, then `git fetch`, then continue the IN PROGRESS row | ⭐ a resume is usually your own self-wake-up (§1a) |

## 1a. ⭐⭐ The hourly SELF-WAKE-UP — so a rate-limit stop does not end the run *(user, `2026-11-18`, R-4291)*

🔒 User: *"when their token rate limit stops them, and when it resets, then the lanes do not continue automatically … Would it help if they set up some
periodic wake-up trigger that asks them to continue …"* · *"pls write that to the handoffs, no testing first"*.

📐 Why it is needed: a rate-limit stop ENDS the lane's turn; when the limit resets nothing inside a stopped session starts a new turn — only a message
from outside does (the user, another session, a scheduled task). All sessions share one account's limit, so no session can wake the others.

| ⭐ the rule | |
|---|---|
| ⭐⭐ **at the START of a handoff run, create ONE recurring scheduled task into YOUR OWN session** — `create_trigger` (claude-code-remote) with no `persistent_session_id` and no new session, `cron_expression` `0 * * * *` (hourly — the minimum; the server spreads it across the hour), name `<lane> self-wake-up` | ⭐ check `list_triggers` first: if one named `<lane> self-wake-up` exists, keep it — ⛔ never two |
| ⭐ its message, verbatim: *"Self-wake-up (PROTOCOL_Autonomous_Lanes §1a): if you are in the middle of work, ignore this. Otherwise: if your queue has rows left, read `docs/blueprints/batches/STATUS_<Lane>_Queue.md` and continue under the protocol. If it is empty, git fetch and look for new HANDOFF_* / STEER_* / NOTE_* files addressed to you and new DECIDED answers in your DECISIONS file (§2 ④ ⑤) and take them. If there are none, write 'idle — waiting for a handoff' in the status and end the turn. Never delete this scheduled task."* | ⭐ harmless while you work: a message that arrives during a running turn waits for the turn's end |
| ⭐ **on a wake-up**: read your status file, `git fetch`, continue the IN PROGRESS row (§1) | a firing during the limit just fails; the next hour retries |
| ⭐⭐⭐ **NEVER delete it** *(user, `2026-11-18`, R-4292: "i want to keep that trigger as coordinator will issue new follow up tasks and that trigger should make the lane check for new handoff if the lane is finished with the previous one")* — when the queue is empty, a wake-up checks for new handoffs (§2 ④ ⑤) and takes them; with none it writes `idle — waiting for a handoff` and ends the turn | ⭐ the coordinator dispatches follow-up work by pushing a handoff — the next hourly wake-up picks it up, nobody has to message the lane. ⛔ SUPERSEDED: the first version (R-4291) deleted it when the queue emptied |
| ⛔ **never** a trigger to watch or wake ANOTHER lane, and never a second schedule | CLAUDE.md "NO SCHEDULED CHECK-INS" still holds for everything else |
| ⚠ if creating it asks the user to approve, or a firing does: note it once in your status (`self-wake-up: needs approval`) and carry on | the user then knows the platform's behaviour without a separate test |

## 2. The loop — after EVERY row

```
① commit the row (only your own files, by path) and push your branch
② rewrite your status file (§4) and push it
③ git fetch origin coordinator behaviors backend terrain ui convoy
④ ⭐ NEW WORK FOR YOU? — git diff --name-only HEAD...origin/coordinator -- docs/blueprints/batches/ | grep -E "To_<Lane>|To_All|DECISIONS_<Lane>"
   a new HANDOFF_*_To_<Lane>_* or STEER_* ⇒ append its rows to your queue at the priority IT states (a STEER pre-empts; a handoff goes
   after your current one unless it says "first"); record "taken" in your status;
   a new **DECIDED** answer in DECISIONS_<Lane>.md ⇒ the SKIPPED row goes back into your queue (it counts as new work, idle or not)
⑤ read every NOTE_*_To_<Lane>_* new since your last loop; answer a REQUEST (do it next if small, else queue it and say when)
⑥ merge what moved: git merge origin/coordinator, and every lane branch whose work yours touches (MERGE, never rebase); then
   `dotnet restore` the test projects you run (a merge can add a package — the stale-binary trap); conflicts keep BOTH sides
⑦ pick the next row that is NOT blocked (§3); a blocked or decision row is skipped, never waited on
```

## 3. ⭐⭐ DECISIONS ARE SKIPPED, NEVER GUESSED — and written where the coordinator sees them

The moment a row (or a part of it) needs a choice no design or ruling makes — a behaviour two designs disagree on, a premise that measured false,
a fix with a large blast radius, a look not in a mockup:

1. ⭐ write it in **`docs/blueprints/batches/DECISIONS_<Lane>.md`** (one file per lane, newest at the top): the row id · the question in one line ·
   **your lean** and why (a claim table row or two) · what each option costs · what you measured;
2. mark the row **SKIPPED — needs decision** in your status;
3. build every part that does NOT need it; go on with the next row.

The coordinator reads every `DECISIONS_*.md` at each lane check, puts the questions to the user, and answers in the same file
(**DECIDED `<date>`: …**) — the lane picks the row up again at its next loop. ⛔ Never guess the answer into code; ⛔ never wait for a reply.

## 4. The status file — `docs/blueprints/batches/STATUS_<Lane>_Queue.md`

```
<!--STATUS state: LIVE · updated: <time> · current-answer: §1 -->
# STATUS — <lane>, the queue
## 1. Now
IN PROGRESS: CE-xxxx — <where exactly: file, step, what is left>
## 2. The queue (in order) — every row with: state (DONE / IN PROGRESS / SKIPPED — needs decision / SKIPPED — blocked by <lane> <row> / NOT REPRODUCED / TODO), sha, the rail, the scenario verdict
## 3. Taken from new handoffs — <file> at <time>
## 4. Notes sent / received
## 5. Gates (rule 8)
```

## 5. Notes to another lane — `docs/blueprints/batches/NOTE_<From>_To_<To>_<Topic>.md`

A finding in their code, a request, a heads-up that you changed something they use: what, where (`file:line`), why, what you need, by when it
blocks you. ⭐ Cross-lane edits are ALLOWED and SAID (commit + status + a note when it touches what they are working on). ⛔ Never edit another
lane's status or decisions file.

## 6. Every row is done the same way

① **reproduce** (the feature's scenario, the editor over the HTTP debug API, a rail) — ⛔ cannot reproduce ⇒ NOT REPRODUCED with what was tried ·
② **root-cause** (the owning design first — R-129; graph AND grep) · ③ **fix where it lives** with a rail in THAT feature's suite, red without the fix ·
④ **the feature's scenario passes** (`scripts/scenario-regression.sh`; add an expect row the defect slipped past) · ⑤ fold what you learned into the
owning design · ⑥ mark the tracker row DONE with its rail. Gates per CLAUDE.md rule 8; behaviour code with `HROT_PARALLEL_SIM=1` +
`HROT_READ_CHECK=1` + the world guard; display rails under `xvfb-run -a`; every red confirmed pre-existing against your base sha.

## 6a. ⭐⭐ Scenarios per row — PROPORTIONATE, never the whole family *(user, `2026-11-18`, R-4294)*

🔒 User: *"backend seems to go through many scenarios so it does not do basically any fixing just running scenarios"* · *"pls revise if running 40+
scenarios is a good idea and if there are not more efficient ways"*. 📐 Measured: each scenario is a fresh editor process, 7–33 s (a big one ~9× slower
than real time, up to ~30 min); backend ran 46 movement scenarios per row AND the reds again at the base — ~3 rows in 5 hours.

| ⭐ the rule | |
|---|---|
| ⭐⭐⭐ **per row: the ROW'S OWN scenario + the scenarios of the feature it touches** (the feature's set in `docs/CAPABILITIES.md`, usually 1–5) — that is R-4277's DONE | ⛔ not every scenario that happens to move a unit |
| ⭐⭐ **the WIDE set** (a whole family, e.g. the 46 movement scenarios) **once per several rows** — before a `-wip` branch merges into the lane, or at the end of a handoff — in the BACKGROUND while the next row is built | a red there is bisected by the per-row commits (each row is its own commit) |
| ⭐⭐ **a red is "pre-existing" by the LAST WIDE RUN's verdict**, not by re-running the base each time | re-run the base only for a red the last wide run did not have |
| ⭐ **fast where it is free**: `NO_BUILD=1` after the one build; 2–3 invocations in parallel on DISJOINT lists (each child has its own staging root — measured safe, Scenario_Regression §7) | ⛔ never a foreground wait (T3) |
| ⭐ a row that changes no simulation behaviour (a document, a test fixture, a tool) runs no scenario | |

## 7. When the queue runs dry

Take, in order: new handoffs / steers addressed to you (§2 ④) · requests in notes to you · the "always available" rows at the end of your handoff · open tracker rows of your area marked
`RW-M` or higher that no handoff holds (say which in the status) · ⛔ never start a new FEATURE no design approves. ⭐ Nothing left ⇒ `idle — waiting for a handoff` in the status, end the turn, and KEEP the self-wake-up (§1a).
