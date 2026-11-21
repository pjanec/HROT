@echo off
:: A player: a client joining a networked game (run_Server, or run_All on the same domain). No -n: the cluster master assigns the node id.
:: Usage: run_Player.bat [DOMAIN]     (DDS domain, default 0)
setlocal

set DOMAIN=%1
if "%DOMAIN%"=="" set DOMAIN=0

cd /d "%~dp0Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0"
set RUNNER=Hrot.ClusterRunner.exe

start "Player" %RUNNER% -d %DOMAIN% -m player --no-wait
