@echo off
setlocal

set DOMAIN=0
cd /d "%~dp0Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0"
set RUNNER=Hrot.ClusterRunner.exe

start "Editor" %RUNNER% -m editor --no-wait
