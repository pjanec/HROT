@echo off
:: The server side of a networked game: orchestrator, simhost, cgf, traffic. Players (run_Player) join it later.
:: Usage: run_Server.bat [DOMAIN]     (DDS domain, default 0)
setlocal

set DOMAIN=%1
if "%DOMAIN%"=="" set DOMAIN=0

cd /d "%~dp0Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0"
Hrot.ClusterRunner.exe -d %DOMAIN% -m server
