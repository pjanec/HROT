using CycloneDDS.Schema;
namespace DdsBlob
{
    [DdsTopic("FileBlob")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct FileBlob
    {
        [DdsKey] public int TransferId;
        public int Seq;
        public int SeqCount;
        [DdsManaged] public string RelativePath;
        [DdsManaged] public byte[] Data;
    }
}
namespace DdsBlob
{
    [CycloneDDS.Schema.DdsTopic("BlobPing")]
    [CycloneDDS.Schema.DdsQos(Reliability = CycloneDDS.Schema.DdsReliability.Reliable, Durability = CycloneDDS.Schema.DdsDurability.Volatile, HistoryKind = CycloneDDS.Schema.DdsHistoryKind.KeepLast, HistoryDepth = 16)]
    public partial struct BlobPing
    {
        [CycloneDDS.Schema.DdsKey] public int Id;
        public int Seq;
        public long SentTicks;
    }
}
namespace DdsBlob
{
    [CycloneDDS.Schema.DdsTopic("XferChunk")]
    [CycloneDDS.Schema.DdsQos(Reliability = CycloneDDS.Schema.DdsReliability.Reliable, Durability = CycloneDDS.Schema.DdsDurability.Volatile, HistoryKind = CycloneDDS.Schema.DdsHistoryKind.KeepAll)]
    public partial struct XferChunk
    {
        [CycloneDDS.Schema.DdsKey] public int TransferId;
        public int Seq;
        public int SeqCount;
        public long TotalBytes;
        public int ChunkSize;
        [CycloneDDS.Schema.DdsManaged] public byte[] Data;
    }
    [CycloneDDS.Schema.DdsTopic("XferAck")]
    [CycloneDDS.Schema.DdsQos(Reliability = CycloneDDS.Schema.DdsReliability.Reliable, Durability = CycloneDDS.Schema.DdsDurability.Volatile, HistoryKind = CycloneDDS.Schema.DdsHistoryKind.KeepAll)]
    public partial struct XferAck
    {
        [CycloneDDS.Schema.DdsKey] public int TransferId;
        [CycloneDDS.Schema.DdsKey] public int NodeId;
        public int ContiguousSeq;   // highest seq received with no gap, -1 = none
        public int Done;            // 1 = all received and verified
        [CycloneDDS.Schema.DdsManaged] public string Sha;
    }
}
