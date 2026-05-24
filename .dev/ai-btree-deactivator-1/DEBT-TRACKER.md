# DEBT-TRACKER — eqs-2

> P2/P3 deferred issues. P1 issues go directly into the next batch (never here).

| # | Priority | Source | Description | Target Batch |
|---|----------|--------|-------------|--------------|
| D-01 | P3 | DESIGN.md §3.2 | `Action_CreepToAndBeyondSlot` still clears `LocomotionChannel` explicitly on the `Failure` return path after TASK-EQL-006 adds the deactivator. Both paths clear the channel (deactivator fires on Failure too); the in-body clear is redundant. Remove the in-body clear once the deactivator is confirmed stable in integration. | Phase 3 follow-up |
| D-02 | P3 | DESIGN.md §3.4 | When `Action_RequestAreaQuery` is deactivated mid-flight, `CachedEqsRequestId` is reset to -1 but the in-flight area query slot in the solver pool is not cancelled. The slot remains occupied until the solver TTL evicts it. Proper cancellation requires an `AreaQueryBatchHelper.CancelAreaQuery` API that does not yet exist. | Future |
| D-03 | P3 | DESIGN.md §2.5 | `[BTreeDeactivator]` for 3-param bridge actions requires the `TargetAction` string to include the `@0` compound suffix. This is an unintuitive convention. Future work: let the generator accept the plain method name and append the suffix automatically when it detects the target is a bridge method. | Future generator polish |
| D-04 | P2 | DESIGN.md §3.3 | `HillAttackTankNodes.Action_AimAndFireSpecific` calls `ClearWeaponActionIfActive` explicitly before returning `NodeStatus.Success` on the `MaxRounds` path. After TASK-EQL-007, the deactivator also fires on that Success path (because RunningNodeIndex changes). The in-body `ClearWeaponActionIfActive` call on Success is now superseded by the deactivator. Confirm that double-clear (body + deactivator) is idempotent, then remove the in-body call. | Phase 3 follow-up |
