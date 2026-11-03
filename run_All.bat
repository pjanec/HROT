@echo off
:: Every fine-grained node in ONE process: orchestrator, simhost, ig (player), excon, cgf, traffic.
:: Usage: run_All.bat [DOMAIN] [runner options...]     (DDS domain, default 0)
::   e.g. run_All.bat --scenario showcase-crossfire --play     (load Crossfire LIVE and run it; docs\MANUAL_TEST_Scenario_Evaluation.md section 1)
setlocal EnableDelayedExpansion

set DOMAIN=0
set FIRST=%~1
if not "%FIRST%"=="" if not "%FIRST:~0,1%"=="-" (set DOMAIN=%FIRST%& shift)
set REST=
:collect
if "%~1"=="" goto run
set REST=!REST! %1
shift
goto collect
:run

cd /d "%~dp0Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0"
Hrot.ClusterRunner.exe -d %DOMAIN% -m all%REST%
