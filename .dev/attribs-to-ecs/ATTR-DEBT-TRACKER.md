# ATTR Debt Tracker

# Update entity attribute response contains dynamic OpaqueData array
This makes the whole message managed, adding unnecessary allocations.
New CycloneDDS.NET library support fixed size arrays.
The OpaqueData should be converted to a fixed size array (32 bytes).
Where FDp is now using this OpaqueData as a component bit mask,
there shlould be sttaic assert checking that the OpaquaData bit size
is at leas as large as the component bitmask size (256 bits as of now).


| ID | Priority | Description | Source | Target Batch | Status |
|---|---|---|---|---|---|
