# BATCH-15 Status Report

**Status:** IN PROGRESS (Build Errors)  
**Completed:** 70%  
**Blockers:** API Mismatches

---

## ✅ Completed Tasks

1. **Task 1:** Agent.cs updated
   - ✅ Replaced BTree types with HSM types
   - ✅ Added HsmInstance64, AgentContext
   - ✅ Added ActiveStates, RecentTransitions
   - ⚠️ AgentContext fixed (removed managed type `Agent` field)

2. **Task 2:** MachineDefinitions.cs created
   - ✅ Event IDs defined
   - ✅ Three machine builders written
   - ⚠️ API mismatch: `Root()` and `AddChild()` don't exist

3. **Task 3:** Actions.cs created
   - ✅ All 15 actions implemented
   - ✅ All 3 guards implemented
   - ⚠️ API mismatch: `HsmEventQueue` usage needs fix
   - ⚠️ Actions reference `ctx->Agent` which no longer exists

4. **Task 4:** BehaviorSystem.cs rewritten
   - ✅ Complete rewrite with HSM
   - ✅ Combat scanning logic
   - ✅ UpdateActiveStates logic
   - ⚠️ API mismatch: `AgentContext` contains `Agent` field (now fixed)

5. **Task 5:** StateMachineVisualizer.cs created
   - ✅ Complete UI implementation
   - ✅ State hierarchy rendering
   - ✅ Manual event injection
   - ✅ Transition history

6. **Task 6:** DemoApp.cs updated
   - ✅ All references changed to HSM
   - ✅ Spawn methods rewritten
   - ✅ UI updated

7. **Task 7:** Project references updated
   - ✅ Added Fhsm.Compiler reference
   - ✅ Added Fhsm.SourceGen reference

8. **Task 8:** README.md updated
   - ✅ Complete rewrite for HSM

9. **Cleanup:**
   - ✅ Deleted old BTree UI files
   - ✅ Updated namespaces
   - ✅ RenderSystem.cs updated

---

## ❌ Remaining Issues

### Issue 1: Builder API Mismatch

**Problem:**
```csharp
// Code uses:
var root = builder.Root();
patrolling.AddChild(selecting);

// But actual API is:
// States are automatically children of implicit root
var red = builder.State("Red");
```

**Fix Required:**
- Remove `Root()` calls
- Remove `AddChild()` calls
- Use flat state creation (builder automatically handles hierarchy)

**Files Affected:**
- `MachineDefinitions.cs` (all three machine builders)

---

### Issue 2: Actions Reference Non-Existent Agent Field

**Problem:**
```csharp
var agent = ctx->Agent;  // Agent field removed from AgentContext
ctx->Agent.TargetPosition = target;  // No longer valid
```

**Fix Required:**
- Actions need access to agent to update `TargetPosition`, `AttackFlashTimer`
- Options:
  1. Pass agent list to actions (complex)
  2. Store agent data in context (duplicate data)
  3. Use unsafe pointer to agent in context (breaks unmanaged constraint)
  4. Store only position/target in context, update agent from BehaviorSystem

**Recommended:** Option 4 - Context stores data, BehaviorSystem syncs back to Agent

**Files Affected:**
- `Actions.cs` (all actions that reference ctx->Agent)
- `BehaviorSystem.cs` (needs sync logic)

---

### Issue 3: HsmEventQueue API

**Problem:**
```csharp
var inst = (HsmInstance64*)instance;
HsmEventQueue.TryEnqueue(inst, 64, evt);  // Correct usage
```

**Fix Required:**
- Already correct in code
- Might need namespace import: `using Fhsm.Kernel;`

**Files Affected:**
- `Actions.cs` (add using if missing)

---

### Issue 4: HsmGraphValidator.Validate API

**Problem:**
```csharp
// Code uses:
if (!HsmGraphValidator.Validate(graph, out var errors))

// But actual API returns List<ValidationError>:
var errors = HsmGraphValidator.Validate(graph);
if (errors.Count > 0)
```

**Fix Required:**
- Change validation logic in `MachineDefinitions.cs`

**Files Affected:**
- `MachineDefinitions.cs` (CompileAndEmit method)

---

### Issue 5: Builder Hierarchy

**Problem:**
The flat builder API doesn't support explicit parent-child relationships with `AddChild()`.

**Solution:**
Hierarchical states are created implicitly by the builder based on the graph structure.
For the demo, we can simplify to use flat state machines without deep hierarchies.

**Simplified Approach:**
```csharp
// Patrol Machine
var selecting = builder.State("SelectingPoint").OnEntry("FindPatrolPoint");
var moving = builder.State("Moving").Activity("MoveToTarget");
var waiting = builder.State("Waiting");

selecting.On(PointSelected).GoTo(moving);
moving.On(Arrived).GoTo(waiting);
waiting.On(TimerExpired).GoTo(selecting);

selecting.Initial();
```

---

## 🔧 Recommended Actions

1. **Fix MachineDefinitions.cs** (30 min)
   - Remove Root() and AddChild() calls
   - Simplify to flat state machines
   - Fix validator API call

2. **Fix Actions.cs** (45 min)
   - Remove ctx->Agent references
   - Update actions to work with context data only
   - Add logic to BehaviorSystem to sync context → agent

3. **Test Build** (15 min)
   - Verify compilation
   - Fix any remaining errors

4. **Manual Testing** (1 hour)
   - Run demo
   - Verify agents move
   - Test state visualization
   - Test manual events

---

## 📊 Estimated Time to Complete

- **Fixes:** 1.5 hours
- **Testing:** 1 hour
- **Total:** 2.5 hours

---

## 💡 Alternative Approach

If the current approach is too complex, consider:

**Option A: Simpler State Machines**
- Remove hierarchical states entirely
- Use flat 3-state machines (easier to implement)
- Focus on visualization rather than complex state logic

**Option B: Simplified Actions**
- Store all agent state in `AgentContext`
- BehaviorSystem syncs: Agent → Context (before update), Context → Agent (after update)
- Actions only modify context, never agent directly

**Option C: Pause and Review**
- Review actual HSM Builder API from working examples
- Redesign machine definitions to match actual API
- Continue with corrected approach

---

## 🎯 Current Decision Point

**Question for User:** How should we proceed?

1. **Continue fixing** (2.5 hours estimated)
2. **Simplify approach** (Option A or B)
3. **Pause for review** (Option C)

---

**Files Modified:** 12  
**Files Created:** 4  
**Build Status:** ❌ FAILED (41 errors)  
**Tests Status:** Not run yet
