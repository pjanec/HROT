# ENUM-SAMPLE Report

**Branch:** `blueprint-integ-1`
**Date:** 2026-06-06
**Goal:** Live enum-param action for AN6 EnumPinEditor testing.

---

## What was implemented

### 1. Demo enum + DTO (Step 1)

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Demo/DemoEnumAction.cs`  
**Namespace:** `Fdp.Toolkit.Behavior.Demo`

```csharp
public enum DemoStance : int { Standing = 0, Crouching = 1, Prone = 2 }

[StructLayout(LayoutKind.Sequential)]
public struct DemoEnumActionParams
{
    public Vector3 TargetPos;   // 12 bytes
    public DemoStance Stance;   // 4 bytes (enum int-backed = AN2 assumption)
    public int Repeat;          // 4 bytes
}                               // total = 20 bytes ≤ 32-byte channel limit
```

Both types are in `Fdp.Toolkits` (net8.0), which is already referenced by:
- `Hrot.Blueprints.Editor` (so NodePinSchema can reflect them)
- `Hrot.AI.Behaviors` (loaded by the editor at runtime)
- `Hrot.Blueprints.Tests` (test assertions work headlessly)

### 2. Catalog entry (Step 2)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/BuiltInChannelCommandCatalog.cs`

```csharp
new("DemoEnumAction", "Fdp.Toolkit.Behavior.Components.LocomotionChannel", 99,
    "Fdp.Toolkit.Behavior.Demo.DemoEnumActionParams"),
```

- **ActionId 99** — confirmed unused on LocomotionChannel (existing: 1=MoveTo, 3=FollowRoute).
- Marked with a `// DEMO … REMOVABLE` block comment.
- Runtime no-op: no executor registered for ActionId 99.

### 3. Recipe (Step 3)

**File:** `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/EnumDemo.bp.json`

```
AssetId: 00000000-aaaa-0001-0000-00000000ee01
DisplayName: "Enum Demo (AN6)"
Category: "AI Primitives"
Layout: EventEntry → DemoEnumAction (LocomotionChannel, ActionId=DemoEnumAction) → Return
```

Mirrors `LocomotionMoveToDemo.bp.json` structure exactly. The recipe is bundled as `Content`
(CopyToOutputDirectory: PreserveNewest) by the existing Hrot.AI.Behaviors csproj glob.

---

## Test results

**New test file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/EnumSampleTests.cs`

7 focused tests — all green:

| # | Test | Verifies |
|---|------|----------|
| 1 | `Catalog_ContainsDemoEnumAction_OnLocomotionChannel` | Entry in BuiltInChannelCommandCatalog, ActionId=99, correct FQN |
| 2 | `Palette_ContainsDemoEnumAction_Entry` | AN4: per-action palette has `ChannelCommand:LocomotionChannel:DemoEnumAction` |
| 3 | `NodePinSchema_DemoEnumAction_ProjectsStancePinWithGlobalPrefix` | AN6: Stance pin TypeId = `"global::Fdp.Toolkit.Behavior.Demo.DemoStance"`, 3 data-IN pins total |
| 4 | `EnumValueProvider_ResolvesDemoStance_Members` | AN6: BlueprintEnumValueProvider resolves Standing/Crouching/Prone with values 0/1/2 |
| 5 | `StaticTypeRegistry_AcceptsGlobalPrefixedEnumTypeId` | AN2: global::-prefix accepted, IrTypeRef.FullName is unprefixed, size=4 |
| 6 | `DemoEnumAction_BlueprintCompiles_WithNoErrors` | End-to-end BlueprintCompiler.Compile passes (0 errors) |
| 7 | `DemoEnumAction_GeneratedSource_ContainsEnumCast` | AN1: emitted C# contains `global::Fdp.Toolkit.Behavior.Demo.DemoStance`, no `global::global::` |

**Full suite (Hrot.Blueprints.Tests):** 1546 passed / 4 failed / 8 skipped  
The 4 failures are all pre-existing: `ScoreCrossed`, `AllocatesZeroBytes`, `Library_EmitMatchesGoldenSource`, `LibraryMath_GeneratedSource_Snapshot`. Zero new failures.

**Hrot.Editor.AiShared.Tests:** 832/832 passed, 0 failures.

**Full solution build:** `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## How to test live

### Palette path
1. Open the Blueprint editor (running editor / `EditorSubsystem`).
2. Open any AiPrimitive blueprint (or New → "Enum Demo (AN6)" recipe).
3. In the node picker (TAB or palette panel), look under **Channel / Locomotion**.
4. You should see **"Locomotion / DemoEnumAction"** as a palette entry.
5. Drop it onto the canvas → node appears with `ChannelType=LocomotionChannel`, `ActionId=DemoEnumAction`.
6. Select the node → Details panel shows 3 data-IN pins: **TargetPos**, **Stance**, **Repeat**.
7. The **Stance** pin renders an ImGui Combo with **Standing / Crouching / Prone**.
8. Pick a value → it persists to `PinDefaults` as an integer.

### Recipe path
1. In the editor, click **New from Recipe**.
2. Select **"Enum Demo (AN6)"** from the AI Primitives category.
3. Open the created blueprint → canvas shows EventEntry → DemoEnumAction → Return (exec-wired).
4. Click DemoEnumAction node → Stance enum combo renders in Details.

### Compile verification
- Connect the node's exec wires (EventEntry Out → DemoEnumAction In → Return In).
- Optionally set Stance to `Crouching`.
- Click **Compile** (toolbar).
- Inspect the generated C# (Debug map or log): should contain `(global::Fdp.Toolkit.Behavior.Demo.DemoStance)1`.

---

## Files changed

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Demo/DemoEnumAction.cs` | **NEW** — DemoStance enum + DemoEnumActionParams struct |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/BuiltInChannelCommandCatalog.cs` | **EDITED** — added DemoEnumAction entry (ActionId 99) |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/EnumDemo.bp.json` | **NEW** — recipe for live testing |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/EnumSampleTests.cs` | **NEW** — 7 focused headless tests |
