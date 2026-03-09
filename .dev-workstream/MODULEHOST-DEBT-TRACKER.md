# Technical Debt & Deferred Issues Tracker - ModuleHost

Tracks P2/P3 issues, known risks, and design decisions deferred from batch reviews.  
**P1 issues are never deferred** — they become Corrective Task 0 in the next batch.

Update this file when an item is resolved. Do not delete resolved rows — mark them ✅.

---

## How to Use

- **Dev lead:** during each review, add any new P2/P3 items here before writing the next batch.  
- **Developer:** check this file during onboarding. If your batch touches a file mentioned here, fix the relevant item even if it wasn't explicitly assigned.
- **Priority:** P2 = fix within the next 1–2 batches; P3 = fix before Phase complete or whenever the area is touched.

---

## Open Items

| ID | Sev | Source | Description | Target | Status |
|---|---|---|---|---|---|
| MH-DEBT-01 | P3 | `ModuleHostKernel` | `EnsureComponentsRegistered` uses reflection (`GetMethod().MakeGenericMethod()`), causing minor overhead during hot-plugging. Needs native non-generic `RegisterComponent(Type)` in `EntityRepository`. | Core ECS | Open |
| MH-DEBT-02 | P3 | `ModuleHostKernel` | `AssignProviderForDynamicInstall` mutates live `ModuleEntry.Provider` fields on background thread during convoy upgrades. Safe due to `LeasedProvider`, but breaks pure RCU immuatbility rules. Refactor to copy `ModuleEntry` instead. | TBD | Open |

---

## Resolved Items (archive)

| ID | Sev | Description | Resolved In |
|---|---|---|---|
|  |  |  |  |
