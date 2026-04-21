:: copies the ClusterRunner binaries from Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0
:: into a temporary directory using robocopy (mirroring the folder),
:: and then runs the ddsmon.exe from there, to avoid any issues with file locks or permissions in the original directory.
mkdir %~dp0.tmp\ddsmon-dm
robocopy %~dp0Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0 %~dp0.tmp\ddsmon-dm /MIR

start DdsMonitor ^
  --AppSettings:TopicSources:0="%~dp0.tmp\ddsmon-dm" ^
  --AppSettings:ExcludeTopics:0="Fdp.Toolkit.Time.Messages.TimeSyncRequest" ^
  --AppSettings:ExcludeTopics:1="Fdp.Toolkit.Time.Messages.TimeSyncResponse" ^
  --AppSettings:ExcludeTopics:2="Hrot.NED.Descriptors.Orchestration.AssetInventoryTopic" ^
  --AppSettings:ExcludeTopics:3="Hrot.NED.Descriptors.Orchestration.NodeHeartbeat"

