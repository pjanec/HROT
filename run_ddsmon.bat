@echo off
:: DDS monitor (ddsmonitor >= 1.2.20) on a DDS domain.
:: Usage: run_ddsmon.bat [DOMAIN]     (DDS domain, default 0)
::
:: Copies the ClusterRunner binaries from Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0
:: into a temporary directory using robocopy (mirroring the folder),
:: and then runs ddsmonitor from there, to avoid any issues with file locks or permissions in the original directory.
::
:: ExcludeTopics patterns match the topic's CLR FULL TYPE NAME (not the derived topic name such as
:: Hrot_NED_Descriptors_Orchestration_NodeHeartbeat), so they are namespace.TypeName.
setlocal

set DOMAIN=%1
if "%DOMAIN%"=="" set DOMAIN=0

mkdir %~dp0.tmp\ddsmon-dm
robocopy %~dp0Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0 %~dp0.tmp\ddsmon-dm /MIR

start ddsmonitor ^
  --DdsSettings:DomainId=%DOMAIN% ^
  --AppSettings:TopicSources:0="%~dp0.tmp\ddsmon-dm" ^
  --AppSettings:ExcludeTopics:0="Fdp.Toolkit.Time.Messages.TimeSyncRequest" ^
  --AppSettings:ExcludeTopics:1="Fdp.Toolkit.Time.Messages.TimeSyncResponse" ^
  --AppSettings:ExcludeTopics:2="Hrot.NED.Descriptors.Orchestration.AssetInventoryTopic" ^
  --AppSettings:ExcludeTopics:3="Hrot.NED.Descriptors.Orchestration.NodeHeartbeat" ^
  --AppSettings:ExcludeTopics:4="GizmoMap.Network.DebugPrimitivesBatch"
