@echo off
:: Every fine-grained node in ONE process: orchestrator, simhost, ig (player), excon, cgf, traffic.
:: Usage: run_All.bat [DOMAIN]     (DDS domain, default 0)
setlocal

set DOMAIN=%1
if "%DOMAIN%"=="" set DOMAIN=0

cd /d "%~dp0Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0"
Hrot.ClusterRunner.exe -d %DOMAIN% -m all
