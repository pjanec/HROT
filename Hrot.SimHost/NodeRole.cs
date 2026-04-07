// Backward-compat shim: NodeRole moved to Hrot.Common (BATCH-03 PM-2).
// All code in Hrot.SimHost that uses NodeRole continues to compile without modification.
global using NodeRole = Hrot.Common.NodeRole;
