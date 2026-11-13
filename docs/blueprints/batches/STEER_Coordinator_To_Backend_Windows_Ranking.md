<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Backend_Mode_And_Serial_Systems.md — the RUNNING batch this steers (M-4 / M-5 / M-6 and its "keep going" line); NOT amended
  - STATUS_Windows_Threaded_Scaling.md (on exp/windows-threaded-measure) — the real-core numbers (W-3 the ranking)
-->

# STEER — coordinator → backend (the running mode + serial-systems batch): convert by the WINDOWS ranking

🔒 User, `2026-11-13`: *"Yes"* — approving the coordinator's proposal after the Windows run.

**Not an amendment (rule 1c):** the handoff's items are unchanged. Its own text says the Windows ranking feeds M-4 / M-5; it is now in.

| | |
|---|---|
| ⭐ **convert in THIS order** (Release, real cores, Ryzen 8C/16T, 1 000 men, ms at 8 threads) | `StressSystem` 16.4 · `CalloutSystem` 10.3 · `CgfThreatEvaluationSystem` 5.9 · `WeaponDispatcherSystem` 5.7 · `StaffComposerSystem` 2.8 · on the CGF node the two `CycloneEgressSystem` passes ≈ 14.6 ms. ⚠ Stress, Callout and Threat ARE parallel phases already and barely gain (1.3–1.6×) ⇒ find their SERIAL part (the pre-pass, the follow-ups, the merge) |
| ⭐ **stop M-4b** (profiling the ~100 ms "outside the systems" on the VM) | on Windows the group's total equals the sum of its members at 2–12 threads, and the tick does not get worse with threads (144 → ~110 ms, flat from 4) ⇒ a VM artefact (4 cores shared with other node processes). Record that in the status and move on |
| unchanged | the never-stop contract, M-7's gates, M-8's numbers, M-9's fold — the M-8 numbers may cite the Windows file for real cores |

New work found by the same run is NOT here — it is a separate handoff, queued after this batch
([`HANDOFF_Coordinator_To_Backend_Windows_Findings.md`](HANDOFF_Coordinator_To_Backend_Windows_Findings.md)).

---
_coordinator lane, 2026-11-13_
