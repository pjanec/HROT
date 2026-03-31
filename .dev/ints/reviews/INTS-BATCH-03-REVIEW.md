# INTS-BATCH-03 Review

**Batch:** INTS-BATCH-03  
**Reviewer:** Development Lead  
**Date:** 2026-02-27  
**Status:** ❌ REJECTED

---

## Summary

Trace logging integration and the architectural fix for `HrotEnvironment` (using a composition root delegate) are solid and acceptable. However, the E2E integration test entirely violates the task specifications and uses a dirty testing shortcut instead of verifying the real network boundary.

---

## Issues Found

### Issue 1: Fake End-to-End Integration Test (INTS-P3-014)

**File:** `Hrot.SimHost.Integration.Tests/EntityLifecycleIntegrationTests.cs`  
**Problem:** The test uses an in-memory `SimHostInstance` stub that bypasses DDS entirely, and manually copies ECS components to a mock `igWorld` to execute the `StyleResolutionSystem`. This blatantly violates the INTS-P3-014 requirement to "use real DDS on domain 10 and real in-process ECS worlds for SimHostApp and IgApplication," which was intended to prove the network code works. You wrote a unit test for ECS component copying, not an E2E test.  
**Fix:** Delete the mocked approach. The test must instantiate real `SimHostApp` and `IgApplication` headless instances connected via Domain 10, dispatch a spawn command, and assert the state on the receiving application's world after a brief simulation polling period. 

---

## Verdict

**Status:** ❌ REJECTED

**Required Actions:**
1. Implement the real E2E integration test in the next batch without mocking the DDS boundary. 

---

**Next Batch:** INTS-BATCH-04
