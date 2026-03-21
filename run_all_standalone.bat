@echo off
setlocal

set DOMAIN=0
set FOLDER=%~dp0Bagira.Runner\bin\Debug\net8.0
set RUNNER=Bagira.Runner.exe
                
start "SimHost" /d %FOLDER% %RUNNER% -d %DOMAIN% -m simhost --no-wait
start "IG"      /d %FOLDER% %RUNNER% -d %DOMAIN% -m ig      --no-wait
start "IOS"     /d %FOLDER% %RUNNER% -d %DOMAIN% -m ios     --no-wait

#start "SimHost" /d %FOLDER% %RUNNER% -d %DOMAIN% -m simhost --wait-for ig,ios
#start "IG"      /d %FOLDER% %RUNNER% -d %DOMAIN% -m ig      --wait-for simhost,ios
#start "IOS"     /d %FOLDER% %RUNNER% -d %DOMAIN% -m ios     --wait-for simhost,ig