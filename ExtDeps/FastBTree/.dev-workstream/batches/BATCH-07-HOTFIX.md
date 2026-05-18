# BATCH-07-HOTFIX: Combat Agent Stuck in Chase

**Issue:** Combat agent never enters Attack state  
**Root Cause:** Movement stops at 1px, but ChaseEnemy requires < 20px  
**Priority:** CRITICAL  
**Time:** 5 minutes

---

## 🔥 The Problem

**Agent.UpdateMovement()** stops at:
```csharp
if (diff.LengthSquared() > 1.0f) // Stops at ~1 pixel
```

**ChaseEnemy** requires:
```csharp
if (distance < 20f) // Needs to be within 20 pixels
    return NodeStatus.Success;
```

**Result:** Agent approaches target, stops ~50px away (due to speed/dt), never gets closer than ~10-50px, ChaseEnemy keeps returning `Running` forever!

---

## ✅ Fix 1: Increase Movement Threshold

**File:** `demos/Fbt.Demo.Visual/Entities/Agent.cs`

**Line 52, change:**

```csharp
// BEFORE
if (diff.LengthSquared() > 1.0f) // Threshold

// AFTER  
if (diff.LengthSquared() > 100.0f) // 10px threshold (10*10=100)
```

This allows the agent to get within 10 pixels before stopping.

---

## ✅ Fix 2: Reduce Attack Distance

**File:** `demos/Fbt.Demo.Visual/Systems/BehaviorSystem.cs`

**In `ChaseEnemy`, change:**

```csharp
// BEFORE
if (distance < 20f) // Attack range
    return NodeStatus.Success;

// AFTER
if (distance < 30f) // Attack range - wider to ensure it triggers
    return NodeStatus.Success;
```

This gives a comfortable margin: agent stops at ~10px, ChaseEnemy succeeds at < 30px.

---

## ✅ Fix 3: Add Missing TargetAgentId Field

**File:** `demos/Fbt.Demo.Visual/Entities/Agent.cs`

**Line 77, update AgentBlackboard:**

```csharp
public struct AgentBlackboard
{
    public int PatrolPointIndex;
    public float LastPatrolTime;
    public int ResourceCount;
    public bool HasTarget;
    public int TargetAgentId;  // ADD THIS LINE
}
```

---

## ✅ Fix 4: Add Visual Debug Info (Optional)

**File:** `demos/Fbt.Demo.Visual/UI/TreeVisualizer.cs`

**After the "Distance to Target" line, add:**

```csharp
// Distance to target
float distToTarget = Vector2.Distance(agent.Position, agent.TargetPosition);
ImGui.Text($"Distance to Target: {distToTarget:F1}");

// ADD THIS - Show movement threshold warning
if (distToTarget > 0 && distToTarget < 30f)
{
    ImGui.TextColored(
        new Vector4(1f, 0.8f, 0f, 1f), 
        $"  → In attack range!");
}
```

---

## 🎯 Why This Fixes It

**Before:**
- Agent approaches → stops at random distance (10-50px due to dt jumps)
- ChaseEnemy checks: 15px < 20px? → **NO** (if stopped at 25px)
- Returns Running forever
- Never progresses to Attack

**After:**
- Agent approaches → stops at ~10px (controlled threshold)
- ChaseEnemy checks: 10px < 30px? → **YES**
- Returns Success
- Sequence progresses to Attack node
- **WHITE FLASH, yellow ring!**
- Attack completes, resets

---

## 🧪 Test After Fix

1. Spawn combat agent + patrol agent
2. Combat agent detects, chases (red line)
3. **Gets close** (~10-15 pixels)
4. Inspector shows: Node changes from "ChaseEnemy" to "Attack"
5. **WHITE FLASH** + yellow expanding circle
6. Target lost, back to wandering

---

## ⏱️ Time: 5 minutes

Three simple number changes fix the entire issue!

---

**The Math:**
- Movement threshold: 100 (= 10px)
- Attack range: 30px
- Agent stops at: ~10px
- 10px < 30px ✅ **ATTACK TRIGGERS!**
