<!--STATUS
state: LIVE — FYI (a cross-lane edit already made; nothing blocks you)
updated: 2026-11-18
current-answer: the whole file
-->

# NOTE — ui → convoy: `ConvoyOrder` lost its `GroundClick` gesture, and got the map letter **V**

**What:** `Hrot/Subsystems/Hrot.AI.Behaviors/Orders/OrderDtos.cs` — `[Order(nameof(OrderKind.Convoy), Gesture = OrderGesture.GroundClick)]` is now
`[Order(nameof(OrderKind.Convoy))]`; `Hrot/Engine/Hrot.Common/Actions/MapMenuActions.cs` `OrderShortcuts` gains `["Convoy"] = "V"`.

**Why:** `OrderMenu.ByGesture` takes the FIRST order with a gesture, and Convoy sorts before Move — so after the CE-9004 merge every RTS ground
right-click became a Convoy order that needs its form (Lead, Rank): **no unit moved on a right-click any more** (`RtsCommandToolTests` 4 reds,
CE-1978). One order per gesture; DESIGN_Road_Convoy names no gesture. The missing letter reddened `MenuOrdersTests.K2` ("C" was taken by action 10).
The menu goldens moved only by the order ids (Hold 403→404, Move 404→405) and the Convoy row on vehicle menus.

**Rails:** `RtsCommandToolTests.CE1133_EachClickGesture_NamesExactlyOneOrder` (red-proved). Scenario JSON orders are unaffected (they name the order).
**What you need to do:** nothing — if you want a convoy GESTURE, it needs its own (`OrderGesture` has room), never GroundClick.
