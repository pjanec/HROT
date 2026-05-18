# BATCH-04 Review

**Status:** ⚠️ CHANGES REQUIRED  
**Grade:** C+ (6/10)

---

## Critical Issues

### 1. **HsmDefinitionBlob Design Violations**
**File:** `HsmDefinitionBlob.cs`

❌ **NOT sealed** - Spec required: `public sealed class`  
❌ **Public array properties** - Spec: arrays should be private, only spans public  
❌ **Missing ActionIds/GuardIds** - Spec required these dispatch tables

**Current:**
```csharp
public StateDef[] States { get; set; }  // PUBLIC, MUTABLE
```

**Required:**
```csharp
private readonly StateDef[] _states;
public ReadOnlySpan<StateDef> States => _states;
```

### 2. **Event Queue Priority Not Enforced in Ring**
**File:** `HsmEventQueue.cs`

Tier 2/3 ring doesn't distinguish Normal vs Low priority. Both go to same ring without ordering. Dequeue is FIFO, ignoring priority.

**Impact:** Low priority events can block Normal priority in ring.

### 3. **No Report Submitted**
Missing `.dev-workstream/reports/BATCH-04-REPORT.md`

---

## Code Analysis

### HsmDefinitionHeader ✅
- Size: 32 bytes ✅
- Magic number: 0x4D534846 ✅
- IsValid() method ✅

### HsmDefinitionBlob ⚠️
- Spans work but arrays exposed ❌
- Indexed accessors correct ✅
- Bounds checking good ✅
- `ref readonly` return correct ✅

### HsmInstanceManager ⚠️
- Initialize/Reset logic correct ✅
- Tier selection thresholds correct ✅
- **Bug:** Generation increment might overflow ushort ⚠️

### HsmEventQueue ⚠️
- Tier 1 overwrite logic **CORRECT** ✅
- Tier 2/3 reserved slot **CORRECT** ✅
- Offsets match instance structures ✅
- Ring wraparound works ✅
- **Priority ordering in ring missing** ❌
- Capacity calculations correct ✅

### HsmValidator ✅
- Magic check ✅
- Root validation ✅
- Parent index bounds ✅
- Transition targets validated ✅
- Missing: circular parent chain detection ⚠️

### Tests ✅
- Count: 30 tests (meets 30+ requirement) ✅
- Tier 1 overwrite verified ✅
- Tier 2/3 reserved slot verified ✅
- Wraparound tested ✅
- Validation tested ✅
- **Missing:** Circular dependency test ⚠️

---

## What Worked

1. ✅ Tier-specific event queue strategies implemented correctly
2. ✅ Tier 1 overwrite logic matches Architect's fix
3. ✅ Tier 2/3 reserved slot works as designed
4. ✅ Ring buffer wraparound correct
5. ✅ Tier selection heuristics match spec
6. ✅ 30 tests, all passing

---

## Fix Required

**Create:** `.dev-workstream/reports/BATCH-04-REPORT.md` with:
- Tasks completed
- Design decisions made
- Edge cases found
- Time spent

---

## Commit Message

```
feat: definition blob, instance manager, event queue (BATCH-04)

HsmDefinitionBlob: ROM container with span accessors
- Header (32B) with magic/hashes for hot reload
- Span-based zero-alloc accessors
- Indexed access with bounds checking
- NOTE: Arrays currently public, needs refactor to private

HsmInstanceManager: lifecycle management
- Initialize/Reset for all tiers
- Tier selection (64/128/256) based on complexity
- Zeroes memory, sets defaults

HsmEventQueue: tier-specific strategies (Architect's fix)
- Tier 1 (64B): Single queue, interrupt overwrites oldest normal
- Tier 2/3: Reserved interrupt slot + shared ring
- Wraparound for ring buffer
- Clear/GetCount/Peek operations
- NOTE: Ring doesn't enforce priority ordering yet

HsmValidator: definition/instance validation
- Magic number, state counts, root validation
- Parent index bounds, transition target validation
- Instance phase/ID validation

Tests: 30 integration tests
- Blob accessors, tier selection, event queue operations
- Tier 1 overwrite, Tier 2/3 reserved slots
- Wraparound, validation edge cases

Related: docs/design/ARCHITECT-REVIEW-SUMMARY.md Q1
```
