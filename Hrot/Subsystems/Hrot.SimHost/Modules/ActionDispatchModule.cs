// ActionDispatchModule has been relocated to FDP.Toolkit.Behavior.Modules (CT-MOD1-E).
// The canonical implementation lives at:
//   FDP/Toolkits/FDP.Toolkit.Behavior/Modules/ActionDispatchModule.cs
//
// All composition-root call sites (SimulationLogicModule, NodeBootstrapper) now wire
// Hrot-domain executors (JoinFormationExecutor, AimAndFireExecutor) via constructor
// injection so that the generic toolkit assembly carries no Hrot-domain references.

