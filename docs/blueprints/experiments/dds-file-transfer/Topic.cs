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
