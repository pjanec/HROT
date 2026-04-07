// NodeRole moved from Hrot.SimHost to Hrot.Common (BATCH-03 PM-2).
// This alias restores backward-compat for test files that rely on type access via
// the Hrot.SimHost.Tests ancestor namespace scope.
global using NodeRole = Hrot.Common.NodeRole;
