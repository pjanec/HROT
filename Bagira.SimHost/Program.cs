// SimHost Mock — Authority Node
// Receives CreateEntityRequest over DDS, spawns entities via NetworkSpawningSystem,
// replicates state to IG and IOS peers.
//
// Full initialization wired in a later batch (S3+).
// This stub keeps the project compilable during incremental development.

Console.WriteLine("[SimHost] Starting…");
Console.WriteLine("[SimHost] Use SimHostModule to register CreateEntityRequestSystem and NetworkSpawningSystem.");
