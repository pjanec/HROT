// Backward-compat shim. NodeRole moved Hrot.SimHost -> Hrot.Common (BATCH-03 PM-2), then
// Hrot.Common -> Fdp.Core (2026-09-12, user ruling: "roles can be defined in fdp if needed as they
// are pretty generic"; the engine needs a role-keyed seam for DESIGN_Role_Affinity_Ownership §3.8).
// All code in Hrot.SimHost that uses NodeRole continues to compile without modification.
global using NodeRole = Fdp.Core.NodeRole;
