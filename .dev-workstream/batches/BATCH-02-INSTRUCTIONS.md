# BATCH-02: Data Model and DER Toolkit Implementation

**Batch Number:** BATCH-02  
**Tasks:** P2.1-P2.4, P3.1-P3.7  
**Phase:** Phase 2: Data Model Assembly & Phase 3: FDP.Toolkit.DER Implementation  
**Estimated Effort:** 3.0 days  
**Priority:** CRITICAL  
**Dependencies:** Batch 01 (Infrastructure Validated)

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch has been expanded to include both the Data Model assembly and the DER (Dynamic Entity Repository) Toolkit implementation. You are responsible for creating the core data types and the repository system that the IOS Mock will use.

**Key Goals:**
1.  **Data Model:** Import authoritative C# types for DDS communication.
2.  **DER Toolkit:** Implement the non-ECS entity repository (DER) for the IOS Mock.
3.  **Validation:** Verify both components with unit and integration tests.

### Required Reading (IN ORDER)
1.  **Task Details:** `docs/design/TASK-DETAILS-SHARED.md` (Sections P2 and P3)
2.  **Source Truth:** `docs/FcdCsharp/` (For Data Model types)

### Source Code Location
-   **Solution:** `IOS-IG-SimHost.sln` (Root)
-   **Data Model Project:** `Bagira.DDS.DataModel/`
-   **DER Project:** `FDP/Toolkits/FDP.Toolkit.DER/`
-   **DER Example:** `FDP.Toolkit.DER.Examples/` (Root)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-02-QUESTIONS.md`

---

## 🎯 Batch Objectives
-   Create `Bagira.DDS.DataModel` and import all types.
-   Create `FDP.Toolkit.DER` and implement the `IDerRepo` and `IDerEntity` system.
-   Verify DDS communication with an integration test.
-   Verify thread-safe entity management with unit tests.

---

## ✅ Tasks: Data Model Assembly

### Task 1: Create Bagira.DDS.DataModel Project (P2.1)
**File:** `Bagira.DDS.DataModel/Bagira.DDS.DataModel.csproj` (CREATE)  
**Task Definition:** [Details](../docs/design/TASK-DETAILS-SHARED.md#task-p21-create-bagiraddsdatamodel-project)

**Instructions:**
1.  Run: `dotnet new classlib -n Bagira.DDS.DataModel -f net8.0` in the root `Bagira.DDS.DataModel/` folder.
2.  Add project to `IOS-IG-SimHost.sln`.
3.  Add `CycloneDDS.NET` NuGet package.
4.  Create folder structure: `Common/`, `Descriptors/`, `Messages/`, `Map/`, `Mission/`.

---

### Task 2: Import FcdCsharp Types (P2.2)
**Source:** `docs/FcdCsharp/*`  
**Target:** `Bagira.DDS.DataModel/**/*`  
**Task Definition:** [Details](../docs/design/TASK-DETAILS-SHARED.md#task-p22-import-fcdcsharp-types)

**Instructions:**
1.  Copy files from `docs/FcdCsharp/` to their respective folders in the new project.
    -   **Common/**: `Common.cs`
    -   **Descriptors/**: `GenericDescriptors.cs`, `SimDescriptors.cs`
    -   **Map/**: `MapDescriptors.cs`, `MapMessages.cs`
    -   **Mission/**: `MissionDescriptors.cs`, `MissionMessages.cs`
    -   **Messages/**: `GenericMessages.cs`
2.  Update namespaces to `Bagira.DDS.DataModel.*`.

---

### Task 3: Add DDS Attributes (P2.3)
**File:** `Bagira.DDS.DataModel/**/*.cs`  
**Task Definition:** [Details](../docs/design/TASK-DETAILS-SHARED.md#task-p23-add-dds-attributes)

**Instructions:**
1.  Ensure every topic struct/class has `[DdsTopic("Name")]`.
2.  Ensure every key field has `[DdsKey]`.
3.  **Verify:** `EntityId` type (int/long) must match the source definition.

---

### Task 4: Create DDS Publisher/Subscriber Test (P2.4)
**File:** `Bagira.DDS.DataModel.Tests/DdsIntegrationTests.cs` (CREATE)  
**Task Definition:** [Details](../docs/design/TASK-DETAILS-SHARED.md#task-p24-create-dds-publishersubscriber-test)

**Instructions:**
1.  Create a **new MSTest project** `Bagira.DDS.DataModel.Tests` (net8.0).
2.  Add to `IOS-IG-SimHost.sln`.
3.  Add reference to `Bagira.DDS.DataModel`.
4.  Implement a test that verifies Pub/Sub for `EntityMaster`.

---

## ✅ Tasks: FDP.Toolkit.DER Implementation

### Task 5: Create FDP.Toolkit.DER Project (P3.1)
**File:** `FDP/Toolkits/FDP.Toolkit.DER/FDP.Toolkit.DER.csproj` (CREATE)  
**Task Definition:** [Details](../docs/design/TASK-DETAILS-SHARED.md#task-p31-create-fdptoolkitder-project)

**Instructions:**
1.  Run: `dotnet new classlib -n FDP.Toolkit.DER -f net8.0` in `FDP/Toolkits/FDP.Toolkit.DER/`.
2.  Add to `IOS-IG-SimHost.sln`.
3.  **Crucial:** This library must NOT reference `Bagira.DDS.DataModel`. It is a generic repository.

---

### Task 6: Implement Interfaces (P3.2, P3.3)
**Files:** `IDerRepo.cs`, `IDerEntity.cs`, `IDerDescriptor.cs`  
**Task Definition:** [P3.2 Details](../docs/design/TASK-DETAILS-SHARED.md#task-p32-implement-iderrepo-interface), [P3.3 Details](../docs/design/TASK-DETAILS-SHARED.md#task-p33-implement-iderentity-interface)

**Instructions:**
1.  Define `IDerRepo`: Get/Create/Delete entity, Events (EntityCreated/Deleted).
2.  Define `IDerEntity`: Get/Set descriptors.
3.  Define `IDerDescriptor`: Marker interface with `EntityId` and `Version`.

---

### Task 7: Implement Classes (P3.4, P3.5)
**Files:** `DerRepo.cs`, `DerEntity.cs`  
**Task Definition:** [P3.4 Details](../docs/design/TASK-DETAILS-SHARED.md#task-p34-implement-derrepo-class), [P3.5 Details](../docs/design/TASK-DETAILS-SHARED.md#task-p35-implement-derentity-class)

**Instructions:**
1.  Implement `DerRepo` using `ConcurrentDictionary`.
2.  Implement `DerEntity` using `ConcurrentDictionary` for descriptors.
3.  Ensure thread-safety for all operations.

---

### Task 8: Write DER Unit Tests (P3.6)
**File:** `FDP.Toolkit.DER.Tests` (CREATE)  
**Task Definition:** [Details](../docs/design/TASK-DETAILS-SHARED.md#task-p36-write-der-unit-tests)

**Instructions:**
1.  Create `FDP.Toolkit.DER.Tests`.
2.  Add to `IOS-IG-SimHost.sln`.
3.  Implement tests for:
    -   Entity CRUD operations.
    -   Descriptor Get/Set.
    -   Concurrency (multiple threads creating/reading entities).

---

### Task 9: Create DDS Translator Example (P3.7)
**File:** `FDP.Toolkit.DER.Examples/EntityMasterIngressExample.cs` (CREATE)  
**Task Definition:** [Details](../docs/design/TASK-DETAILS-SHARED.md#task-p37-create-dds-translator-example)

**Instructions:**
1.  Create `FDP.Toolkit.DER.Examples` console project in the **root directory** (since it depends on top-level projects).
2.  **Note:** While the core `FDP.Toolkit.DER` library is generic and **does not** depend on `Bagira.DDS.DataModel`, this *example* demonstrates how to bridge the two. Therefore, this example project **will** reference `Bagira.DDS.DataModel`.
3.  Implement the listener as shown in the task details.

---

## 🧪 Testing Requirements
-   **Pass Rate:** 100% for `Bagira.DDS.DataModel.Tests` and `FDP.Toolkit.DER.Tests`.
-   **Coverage:** Ensure the concurrency tests in DER actually stress the system (use `Task.Run` and `Task.WaitAll`).

---

## 📊 Report Requirements
Submit a report to `.dev-workstream/reports/BATCH-02-REPORT.md`.
1.  Did imports from `FcdCsharp` require any manual fixing?
2.  Confirm that `EntityId` types match across all layers.
3.  Did the concurrent DER tests reveal any locking issues?
