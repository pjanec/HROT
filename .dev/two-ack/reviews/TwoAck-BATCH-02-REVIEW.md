# TwoAck-BATCH-02 Review

**Batch:** TwoAck-BATCH-02
**Reviewer:** Development Lead
**Date:** 2026-03-22
**Status:** ⚠️ NEEDS FIXES 

---

## Summary

The developer accurately corrected the issues pointed out in Batch 01: The missing `ImGui.BeginDisabled()` assertion was rewritten properly, an integration test for `IosMock.DrawUI` was added, and the enum mismatch crashing `Hrot.SimHost.Tests` was corrected. The UX copy was successfully replaced. Furthermore, Developer Insights were provided showing good analysis of edge-cases and existing architecture.

**HOWEVER, test integration regressions were completely ignored.** While `Hrot.SimHost.Tests` and `Hrot.ExCon.Tests` now pass nicely, exactly **17 Integration Tests** are failing in `Hrot.SimHost.Integration.Tests` alongside another failure in `Hrot.ClusterRunner.Integration.Tests`. 

---

## Issues Found

### Issue 1: MockIOSClient Breaks Under Two-ACK Architecture
**File:** `Hrot.SimHost.Integration.Tests/Infrastructure/MockIOSClient.cs`
**Problem:** `TryGetAck(requestId)` just returns the *first* matching ACK blindly. Because we changed creation sequences in Batch 01 to emit `InProgress=1` followed by `Success=0`, `WaitForAckAsync()` now yields `StatusCode=1`. The downstream test assertions are written expecting `0` precisely, leading to sweeping failures across `EntityCreationFlowTests.cs` and `NavComponentsPresenceTests.cs`.
**Fix:** Refactor `WaitForAckAsync` or the `TryGetAck` query loop to skip non-terminal ACKs (like `InProgress=1`) and return only the terminal `Success` or `Error` states, or modify the test suite to await the specific phase code explicitly.

### Issue 2: Runner Integration Validation
**File:** `Hrot.ClusterRunner.Integration.Tests/MiniIosIntegrationTests.cs`
**Problem:** `FirstSpawn_DoesNotExhaustIdPool` fails, asserting `StatusCode` equality expecting `0` but obtaining `1` (InProgress).
**Fix:** Apply the same phase-awareness patching for Mock consumers receiving Two-ACK sequences.

---

## Test Quality Assessment
The new ImGui-oriented tests using `ImGui.CreateContext` function brilliantly. Test isolations via `[Collection("ImGui Sequential")]` are structurally sound. Excellent improvement on UI rendering and Side-Effect validation. However, developers must not treat unit tests as the only source of truth. Running `dotnet test` against the whole sln is mandatory.

---

## Verdict
**Status:** ⚠️ NEEDS FIXES
Because the CI pipeline falls apart completely on the integration steps due to the API shift in Batch 01, we cannot merge. I am pipelining the fixes as P1 blocking Corrective Tasks into Batch 03. 

---

## 📝 Commit Message

```
fix/test: implement rigorous headless imgui verification (TwoAck-BATCH-02)

Addresses P1 and P2 test debt from TwoAck-BATCH-01.
- Fixes MissionControlRequestSystemTests entity lookup shift failure.
- Rewrites MissionPanel.Draw() pending state validations utilizing headless ImGui contexts.
- Validates IosMock modal execution via ImGui Stack inspection.
- Updates UX InProgress label output format.
```

---

**Next Batch:** TwoAck-BATCH-03
