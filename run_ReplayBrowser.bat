@echo off
:: The replay browser: an offline host that browses recorded exercises (no cluster, no DDS peers).
setlocal

cd /d "%~dp0Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0"
set RUNNER=Hrot.ClusterRunner.exe

start "ReplayBrowser" %RUNNER% -m replaybrowser
