@echo off
setlocal

set DOMAIN=0
cd /d "%~dp0Hrot.ClusterRunner\bin\Debug\net8.0"
set RUNNER=Hrot.ClusterRunner.exe

#start "SimHost" %RUNNER% -d %DOMAIN% -m simhost --no-wait
#start "IG"      %RUNNER% -d %DOMAIN% -m ig      --no-wait
start "IOS"     %RUNNER% -d %DOMAIN% -m ios     --no-wait

#start "SimHost" %RUNNER% -d %DOMAIN% -m simhost --wait-for ig,ios
#start "IG"      %RUNNER% -d %DOMAIN% -m ig      --wait-for simhost,ios
#start "IOS"     %RUNNER% -d %DOMAIN% -m ios     --wait-for simhost,ig