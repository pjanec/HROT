// LinearKinematicsSystem has been moved to FDP.Toolkit.CarKinem.Systems (CT-MOD1-F).
//
// Rationale: FDP.Toolkit.Physics referenced FDP.Toolkit.CarKinem to access VehicleState,
// which prevented GroundKinematicsModule (in CarKinem) from ever including this system
// without creating a circular assembly reference.
//
// The system now lives at:
//   FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/LinearKinematicsSystem.cs
// in namespace FDP.Toolkit.CarKinem.Systems and is registered by GroundKinematicsModule.
//
// This file is intentionally empty so that old build artefacts referencing
// FDP.Toolkit.Physics.Systems.LinearKinematicsSystem produce a clear compile error
// rather than silently linking the wrong type.
