@echo off
setlocal

set DOMAIN=0
set FOLDER=%~dp0Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0
set RUNNER=Hrot.ClusterRunner.exe
                
start "SimHost" /d %FOLDER% %RUNNER% -d %DOMAIN% -m simhost --no-wait
start "IG"      /d %FOLDER% %RUNNER% -d %DOMAIN% -m ig      --no-wait
start "ExCon"   /d %FOLDER% %RUNNER% -d %DOMAIN% -m excon   --no-wait
start "CGF"     /d %FOLDER% %RUNNER% -d %DOMAIN% -m cgf     --no-wait

#start "SimHost" /d %FOLDER% %RUNNER% -d %DOMAIN% -m simhost --wait-for ig,ios
#start "IG"      /d %FOLDER% %RUNNER% -d %DOMAIN% -m ig      --wait-for simhost,ios
#start "IOS"     /d %FOLDER% %RUNNER% -d %DOMAIN% -m ios     --wait-for simhost,ig