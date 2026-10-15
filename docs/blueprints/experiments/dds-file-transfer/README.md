# DDS file-transfer experiment (Q91 §4-E2)

The probe behind the measurements in `docs/blueprints/Architect_Question_91_Player_Nodes.md` §4-E2.
Not part of any solution; kept so the numbers can be re-measured on a real network (M1).

```bash
dotnet build -c Release            # needs the repo's nuget.config (CycloneDDS.NET 0.3.2)
EXE="dotnet bin/Release/net8.0/DdsBlob.dll"
# receiver first, then sender, same <domain>, in two processes (or two hosts)
$EXE recv <domain> <totalBytes> <timeoutSec> <chunkKB>
$EXE send <domain> <totalMB> <chunkKB>
# latency of a small reliable topic during a transfer
$EXE pingrecv <domain> <seconds>   &   $EXE pingsend <domain> <seconds>
```
