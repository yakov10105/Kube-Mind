using Microsoft.Extensions.VectorData;

namespace KubeMind.Brain.Infrastructure.Data;

/// <summary>
/// Represents a memory record for a Kubernetes incident, stored in the vector database.
/// </summary>
public class IncidentMemory
{
    /// <summary>
    /// Gets or sets the unique identifier for the memory record.
    /// </summary>
    [VectorStoreKey]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the cluster identifier where the incident occurred.
    /// This field is filterable to allow targeted searches within specific clusters.
    /// </summary>
    [VectorStoreData]
    public required string ClusterId { get; set; }

    /// <summary>
    /// Gets or sets the namespace of the affected pod.
    /// This field is filterable to allow targeted searches within specific namespaces.
    /// </summary>
    [VectorStoreData]
    public required string Namespace { get; set; }

    /// <summary>
    /// Gets or sets the raw log message from the incident.
    /// </summary>
    [VectorStoreData]
    public required string RawLog { get; set; }

    /// <summary>
    /// Gets or sets the successful resolution action taken for a similar past incident.
    /// </summary>
    [VectorStoreData]
    public required string ResolutionAction { get; set; }

    /// <summary>
    /// Gets or sets the 768-dimension vector representation of the raw log.
    /// Used for semantic similarity searches using Cosine Similarity.
    /// </summary>
    [VectorStoreVector(768)]
    public required ReadOnlyMemory<float> Embedding { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the incident was recorded.
    /// </summary>
    [VectorStoreData]
    public required DateTimeOffset CreatedAt { get; set; }
}
