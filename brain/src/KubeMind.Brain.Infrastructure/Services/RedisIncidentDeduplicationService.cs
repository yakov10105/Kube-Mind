using KubeMind.Brain.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace KubeMind.Brain.Infrastructure.Services;

/// <summary>
/// Implements incident de-duplication using a distributed Redis cache.
/// </summary>
public class RedisIncidentDeduplicationService : IIncidentDeduplicationService
{
    private readonly IDatabase _database;
    private readonly TimeSpan _deduplicationWindow;
    private readonly ILogger<RedisIncidentDeduplicationService> _logger;
    internal const string IncidentKeyPrefix = "incident:";

    public RedisIncidentDeduplicationService(IDatabase database, IConfiguration configuration, ILogger<RedisIncidentDeduplicationService> logger)
    {
        _database = database;
        _logger = logger;

        if (!TimeSpan.TryParse(configuration["DeduplicationWindow"], out _deduplicationWindow))
        {
            _deduplicationWindow = TimeSpan.FromMinutes(5);
            _logger.LogWarning("DeduplicationWindow not configured or invalid. Defaulting to {DefaultWindow} minutes.", _deduplicationWindow.TotalMinutes);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsDuplicateAsync(string incidentId, CancellationToken cancellationToken = default)
    {
        var key = $"{IncidentKeyPrefix}{incidentId}";

        var wasSet = await _database.StringSetAsync(key, "processed", _deduplicationWindow, When.NotExists);

        if (wasSet)
        {
            _logger.LogInformation("Incident {IncidentId} is new. Processing.", incidentId);
            return false; 
        }
        
        _logger.LogInformation("Incident {IncidentId} is a duplicate.", incidentId);
        return true; 
    }
}
