set DOMAIN=0

SET RUNNER=Bagira.Runner\bin\Debug\net8.0\Bagira.Runner.exe -d %DOMAIN%

start "SimHost" %RUNNER% -m simhost --no-wait
#start "IG"      %RUNNER% -m ig --no-wait
#start "IOS"     %RUNNER% -m ios --no-wait

#start "SimHost" %RUNNER% -m simhost --wait-for ig,ios
#start "IG"      %RUNNER% -m ig      --wait-for simhost,ios
#start "IOS"     %RUNNER% -m ios     --wait-for simhost,ig