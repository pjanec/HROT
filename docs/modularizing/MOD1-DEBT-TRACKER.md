# MOD1 Debt Tracker




| ID | Priority | Description | Source | Target Batch | Status |
|---|---|---|---|---|---|
| CT-MOD1-A | P1 | `_frustrationTicks` dictionary memory leak in `NavigationExecutionSystem` for ephemeral entities. Replace with ECS component `FrustrationTicks`. | MOD1-BATCH-01 | MOD1-BATCH-02 | ⏳ Pending |
| DB-MOD1-01 | P2 | `CarKinem.Core.NavigationMode` vs `FDP.Toolkit.Navigation.NavigationMode` naming ambiguity causing subtle C# namespace shadowing bugs. Rename old enum to `KinematicsMode`. | MOD1-BATCH-01 | MOD1-BATCH-02 | ⏳ Pending |
| DB-MOD1-02 | P3 | `GlobalComponentIds` 20-49 toolkit block is full. Needs compile-time automated uniqueness guard / unit tests to prevent silent data corruption. | MOD1-BATCH-01 | TBD | ⏳ Pending |
| DB-MOD1-03 | P2 | `NetworkOwnership.PrimaryOwnerId` residue across other systems. System-wide audit needed to standardize to `WithOwned<T>()`. | MOD1-BATCH-01 | TBD | ⏳ Pending |
