#!/usr/bin/env bash
# DDS monitor (ddsmonitor >= 1.2.20) on a DDS domain, with the browser UI.
# Usage: run_ddsmon.sh [DOMAIN]     (DDS domain, default 0)
#
# ExcludeTopics patterns match the topic's CLR FULL TYPE NAME (not the derived topic name such as
# Hrot_NED_Descriptors_Orchestration_NodeHeartbeat), so they are namespace.TypeName.
# The runner's build folder is read in place (no file locks on Linux, unlike run_ddsmon.bat).
set -euo pipefail

# Resolve the directory this script lives in, so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOMAIN="${1:-0}"
RUNNER_DIR="$SCRIPT_DIR/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0"

exec ddsmonitor \
  --DdsSettings:DomainId="$DOMAIN" \
  --AppSettings:TopicSources:0="$RUNNER_DIR" \
  --AppSettings:ExcludeTopics:0="Fdp.Toolkit.Time.Messages.TimeSyncRequest" \
  --AppSettings:ExcludeTopics:1="Fdp.Toolkit.Time.Messages.TimeSyncResponse" \
  --AppSettings:ExcludeTopics:2="Hrot.NED.Descriptors.Orchestration.AssetInventoryTopic" \
  --AppSettings:ExcludeTopics:3="Hrot.NED.Descriptors.Orchestration.NodeHeartbeat" \
  --AppSettings:ExcludeTopics:4="GizmoMap.Network.DebugPrimitivesBatch"
