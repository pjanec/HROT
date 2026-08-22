<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-22
current-answer: a RELAY note for the TIME lane (on hold) — three confirmatory follow-ups after the
  T1-T7 + W1/W2 refactor merged. Not a new batch of features; verification + as-built bookkeeping.
known-conflict: none.
-->
# STEER — TIME lane · **verify the cluster-time invariant, and make the design as-built**

> ⭐ **Relayed via the user** *(the coordinator does not reach into a session)*. Lane branch
> `claude/time-system-refactor-batch-104-gp617x`. ⭐ ids **`TM-`**, tracker **Area H only**.
> ⚠ **This is CONFIRMATORY, not a fire.** An independent coordinator run *(HEAD `3192d9ec` vs pre-refactor
> `34deca154^`)* already found **zero new failures** in the four cluster/time suites, and
> `SimTimeSyncIntegrationTests` passes **6/6 in isolation** on HEAD. These three items put that on the
> record where it belongs — in YOUR gate table and the design — instead of only in the coordinator's.

## Why this note exists

📌 **The gap the user caught:** the refactor changed **cluster time control** — a system-level invariant —
but the TM105–TM111 gate tables named `Fdp.ModuleHost.Tests` and filtered rails, and **never named the
integration suites that assert nodes stay time-synced**. Unit rails prove a class; only an integration
suite proves the SYSTEM still holds together. ⇒ this is now **gate-contract row 8** in `CLAUDE.md`.

## The three items *(all Area H; none blocked)*

| # | do | detail |
|---|---|---|
| **①** | ⭐⭐ **Run `SimTimeSyncIntegrationTests` in ISOLATION and report it** *(row 8)* | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --filter "FullyQualifiedName~SimTimeSyncIntegrationTests"` under `xvfb-run -a`. ⭐ These assert the cross-node invariant your changes could break — continuous-run sync, pause-all-same-time, step-all-same-time. State the base sha. *(Coordinator saw 6/6 in isolation; you own recording it.)* |
| **②** | ⛔⛔⛔ **Make `DESIGN_Time_Architecture.md` AS-BUILT** *(obligation ⑤)* | it is still `build-state: DESIGN` while the thing is BUILT + merged. ⭐ Advance the STATUS `build-state`; confirm **§10** reflects the shipped seam *(`HasPending · IsRewound · DrainInto · TryGetPending`, the **trimmed `RestorePostTick()`**, the drain-as-PULL)*; move any now-superseded TARGET-state to `## ⛔ HISTORY` or mark it `stale-below`. ⚠ Same for `PLAN_Time_System_Refactor.md` if its stage table now lags the code. |
| **③** | ⭐ **File the `ClusterRunner.Integration.Tests` un-gateability as a DEBT** *(Area H)* | 📐 the suite host **crashes mid-run**: `CycloneDDS.Runtime.DdsException: dds_take failed: -3 (BadParameter)` in `DdsIdAllocatorServer.ProcessRequests` → the run aborts and fail-counts swing wildly *(HEAD 26–74, baseline up to 86)*. ⛔ **Pre-existing** *(identical on `34deca154^`)*, **not time-related**. ⭐ Record it as a known un-gateable suite so it is not rediscovered each batch, and so its noise never stands in for *"verified."* Gate the invariant via ① *(isolation)* until the DDS crash is fixed. |

## Not this note

⛔ No feature work. ⛔ No UI/variable file *(the freeze still binds)*. ⛔ Nothing outside Area H.
⭐ If ① surfaces a REAL sync failure that reproduces on HEAD but not on `34deca154^`, **STOP and report** —
that would be a genuine regression the independent run did not see, and it changes everything.
