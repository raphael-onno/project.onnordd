namespace MediaspotIngest.Core.Domain;

/// <summary>A delivery lot: the set of items exported together in one Legacy Importer file.</summary>
public sealed class IngestBatch
{
    public string ProfileName { get; set; } = "";

    public int Number { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string Name => $"Batch_{Number:000}";

    public override string ToString() => string.IsNullOrWhiteSpace(Description) ? Name : $"{Name} — {Description}";
}
