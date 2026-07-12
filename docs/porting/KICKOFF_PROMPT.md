# Kickoff Prompt - Linux/Windows Cross-Platform Port

Paste the block below into a Claude Code session **on the Windows box** and
another session **on the Linux VM**. The prompt is OS-parametric: the session
detects its own OS and follows the matching lane in the spec. Both sessions work
from the same branch, `claude/linux-windows-port`.

Before starting, make sure the branch is checked out and up to date:
`git checkout claude/linux-windows-port && git pull --rebase origin claude/linux-windows-port`

---

## Prompt (paste this)

You are working on the cross-platform (Linux + Windows) port of this engine.
The full plan is in `docs/porting/LINUX_WINDOWS_PORT_SPEC.md` - read it first and
treat it as the source of truth. Work on the branch `claude/linux-windows-port`.

**Step 0 - declare your lane.** Determine which OS you are running on
(`dotnet --info`) and state it. You are the **Windows box** (regression
validator + owner of WI-9 Stride decision and the Windows dialog path) or the
**Linux VM** (primary implementation driver + owner of WI-1 POSIX backend, WI-2
DDS native, WI-8 scripts). Follow the ownership split in spec section 3.

**Roles and models.** Act as the orchestrator and reviewer yourself. Delegate
the mechanical implementation work to **Sonnet subagents** (the backend split,
the `EnumerationOptions` sweep, the staging-path centralization, the dialog
factory, script authoring). Reserve your own (Opus) attention for orchestration,
the two architecture decisions (WI-2 DDS strategy, WI-9 Stride), and reviewing
every subagent diff before it is committed. Launch independent subagents in
parallel in a single message.

**Working rules (from `AGENTS.md`, non-negotiable):**
- Single build artifact, runtime OS detection via `OperatingSystem.IsWindows()` /
  `OperatingSystem.IsLinux()`. Do NOT introduce `#if WINDOWS` compile-time forks.
- Preserve public API surface; preserve existing comments and Unicode exactly;
  minimize diffs; ASCII-only in new comments/strings where ASCII suffices.
- The solution MUST build before every commit. Establish a green baseline BEFORE
  changing anything so regressions are attributable.

**Execution order.**
1. Confirm a clean baseline build/test in your lane; note it in
   `docs/porting/PORT_STATUS.md` (create it if missing; use a simple per-work-item
   table: id | owner-os | state | build-win | build-linux | test-win | test-linux | notes).
2. Linux VM: start WI-2 (DDS native) first in parallel - it is the schedule risk -
   then proceed WI-1, WI-4, WI-5, WI-3, WI-6, WI-8. Windows box: confirm baseline,
   then take WI-9 and WI-7, and re-validate each portable change the Linux box
   pushes.
3. One work item in flight at a time per author. `git pull --rebase` before
   starting an item and before every push. Commit per item with the id prefix,
   e.g. `WI-1: cross-platform NativeMemoryAllocator`. Push to
   `claude/linux-windows-port` with `git push -u origin claude/linux-windows-port`
   (retry on network errors with backoff 2s/4s/8s/16s).
4. After each portable change lands, the OTHER box pulls, rebuilds, runs the
   affected test projects, and updates `PORT_STATUS.md` with pass/fail. Mark
   anything you cannot validate in your lane `NEEDS-<other-os>-VALIDATION`.

**Acceptance / definition of done** is in spec sections 6, 7, and 8. Do not
consider a work item done until it meets its acceptance criteria and does not
regress the other OS. Do NOT open a pull request unless explicitly asked.

Start now with Step 0, then Step 1 (baseline), and report before making code
changes.

---

## Notes for the human coordinator

- Run the two sessions roughly in parallel. The Linux VM does most of the
  authoring; the Windows box mostly pulls, rebuilds, and validates, plus owns the
  two Windows-specific items (WI-7 is trivial and can go on either box).
- WI-2 (DDS Linux native) may need out-of-repo work (building Eclipse Cyclone DDS
  for linux-x64, staging `libddsc.so`). If it stalls, the rest of the port
  (WI-1/4/5/3/6) can still be completed and validated independently; only the
  live DDS smoke test in the Definition of Done is blocked on it.
- `PORT_STATUS.md` is the shared source of truth for progress between the two
  boxes; keep it current on every push.
