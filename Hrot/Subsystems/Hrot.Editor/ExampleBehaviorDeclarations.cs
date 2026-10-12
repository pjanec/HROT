using Hrot.Map.Definitions.Behavior;

// ⭐ CE-2134 (docs/DESIGN_Orders_Params_Entity_Classes.md §5b) — the params contracts of the three FDP example behaviours this
//   host registers itself (UrbanCombatNewScenario.RegisterUrbanCombatBehaviors, EditorSubsystem). The examples cannot see
//   Hrot.Core, so the declaration sits beside the host that registers them — and only this host lists them: a CGF node or
//   ExCon, which cannot run them, no longer offers them in its mission pickers.
[assembly: Behavior(BehaviorNames.Ambush, Params = typeof(AmbushParamsJsonDto), Categories = BehaviorCategory.Insurgent)]
[assembly: Behavior(BehaviorNames.InfantryCombat, Params = typeof(InfantryCombatParamsJsonDto), Categories = BehaviorCategory.Infantry)]
[assembly: Behavior(BehaviorNames.ConvoyEscort, Params = typeof(ConvoyEscortParamsJsonDto), Categories = BehaviorCategory.MilitaryApc)]
