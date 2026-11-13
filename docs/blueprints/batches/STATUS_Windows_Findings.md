<!--STATUS
state: LIVE — in progress
updated: 2026-11-13
current-answer: the whole file (NEXT is the resume point)
related-designs:
  - HANDOFF_Coordinator_To_Backend_Windows_Findings.md — the work F-1 … F-6 (CE-4501 … CE-4506), dispatched at abc319675
  - STATUS_Mode_And_Serial_Systems.md — M-D9 / M-D10 / M-D12: the stepped gate's differences measured to their cause (padding, one race)
  - ../../DESIGN_Parallel_Simulation.md — §18.7 the as-built gate; §19 Windows scheduling
  - ../DESIGN_Time_Architecture.md — §17 stepped mode waits for background jobs
-->

# STATUS — what the Windows run found: stepped repeatability, the gate, small fixes

Branch `exp/threaded-build`. Started after the mode + serial-systems batch (its M-7 gate already took F-3's root cause — the order the contract allows
when an item is blocked).

## 1. DONE

| item | what | commit |
|---|---|---|
| F-1 `CE-4501` | ⭐ the stepped gate committed: `scripts/stepped-hash.py` — the one session driver (hidden window on Windows, Xvfb on Linux), the field-bytes hash, `--rows <T> --bytes` names the entities and the bytes that differ, `--kernel 0|3`, exit code 0 / 1 / 2 (equal / differs / a run failed) | (this commit) |
| F-5 `CE-4505` | ⭐ `[FrameStats]` read in any locale (a decimal comma): ONE parser in the session driver, used by both measuring scripts · `WindowsScheduling.Describe` — the runner logs what the opt-out did, once (`[Scheduling] power throttling: …`) | `dbd…` (F-1 + F-5 commit) |
| F-3 `CE-4503` (root cause, taken in M-7) | ⭐ the stepped run IS repeatable — the differences were the GATE: `/world/hash` hashed raw rows, padding included, and a padding byte keeps whatever the slot held. 📐 Measured with the row bytes (`?rows=<T>&bytes=1`): `ScenarioClock` bytes 10–12, `Utterance` 1–3 / 10–12 — padding only. With the hash over FIELD bytes, two identical switch-off 1 000-man runs: STATE EQUAL. With every phase on, one real race found by the read-check survey (the weapon phase read the target's `WeaponPosture` live) and fixed; the residual `WeaponChannel` difference is `Entity` padding inside an opaque buffer ⇒ CE-4507 | `STATUS_Mode_And_Serial_Systems` M-D9 / M-D10 / M-D12 |

## 2. GATES

| gate | command | result |
|---|---|---|
| F-1 the committed gate | `HROT_PARALLEL_SIM=1 python3 scripts/stepped-hash.py --threads 1,4` (Release, 1 000 men) | STATE EQUAL, exit 0 |
| F-5 rails | `python3 scripts/test_frame_stats.py` · Core `WindowsScheduling` | ok (red-proved: the old `[0-9.]+` ⇒ the Czech line read as 8) · 4 / 0; the live log shows the line |

## 3. DECISIONS

| # | question | decision | where |
|---|---|---|---|

## 4. OPEN

| item | why | what it needs |
|---|---|---|

## NEXT (the resume point)
F-1 (the committed gate) · F-5 (the decimal comma, the opt-out line) · F-2 (the table: switch off / mode 3 × repeatable) · F-4 (1 thread slower than
off) · F-6 (fold).
