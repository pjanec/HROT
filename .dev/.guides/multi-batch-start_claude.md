You are the **Dev Lead**. Follow `.dev/.guides/DEV-LEAD-GUIDE_claude.md`.

    TOPIC_DIR = .dev/<TOPIC>      ← set this ONCE (the only path you edit)

Everything below refers to `$TOPIC_DIR`. Your goal is to manage the implementation of
all tasks in `$TOPIC_DIR/TASK-TRACKER.md`.

Run the **Plan → Delegate → Review → Commit → Repeat** loop autonomously until every
task is done. Do not stop between batches to ask permission.

**Per batch:**
1. Plan the batch — tech debt (from `$TOPIC_DIR/DEBT-TRACKER.md`) first, then new tasks;
   any P1 from the last review becomes Corrective Task 0. ~10–20h of work per batch.
   Prefer referencing the design/task details over duplicating them into the batch.
   Batch/report/review files live under `$TOPIC_DIR/{batches,reports,reviews}/`.
2. Delegate to a coder sub-agent via the **Agent tool** (`subagent_type: general-purpose`,
   `model: sonnet`) — never the Explore agent. Tell it to follow
   `.dev/.guides/DEV-GUIDE_claude.md`, implement the batch, prove each design
   success-condition with real tests, and write its report.
3. When it returns, **review hard**: don't trust the report — open the source files.
   Focus especially on **test quality** — are the tests aligned with the DESIGN
   (not fake/over-simplified, exercising the real code paths and all required
   behavior)? Project any issues found into the next batch.
4. Write the review, update `$TOPIC_DIR/TASK-TRACKER.md` and `$TOPIC_DIR/DEBT-TRACKER.md`,
   then **commit** this batch (each changed git submodule with its own message, then the
   superproject; stop if any submodule is in detached HEAD).
5. **Immediately create and delegate the next batch.** Do not stop until all tasks
   are done.

Begin by reading `$TOPIC_DIR/TASK-TRACKER.md` and `$TOPIC_DIR/DEBT-TRACKER.md`.
