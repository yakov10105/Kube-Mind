using KubeMind.Brain.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace KubeMind.Brain.Tests;

public class RedisIncidentDeduplicationServiceTests
{
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<RedisIncidentDeduplicationService>> _mockLogger;

    public RedisIncidentDeduplicationServiceTests()
    {
        _mockDatabase = new Mock<IDatabase>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<RedisIncidentDeduplicationService>>();
        
        // Setup default configuration
        _mockConfiguration.Setup(c => c["DeduplicationWindow"]).Returns("00:05:00");
    }

    [Fact]
    public async Task IsDuplicateAsync_WhenIncidentIsNew_ReturnsFalse()
    {
        // Arrange
        var incidentId = "new-incident-123";
        _mockDatabase.Setup(db => db.StringSetAsync($"{RedisIncidentDeduplicationService.IncidentKeyPrefix}{incidentId}", 
            "processed", It.IsAny<TimeSpan>(), When.NotExists))
            .ReturnsAsync(true); // Simulate key was set

        var service = new RedisIncidentDeduplicationService(_mockDatabase.Object, _mockConfiguration.Object, _mockLogger.Object);

        // Act
        var result = await service.IsDuplicateAsync(incidentId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsDuplicateAsync_WhenIncidentIsDuplicate_ReturnsTrue()
    {
        // Arrange
        var incidentId = "duplicate-incident-456";
        _mockDatabase.Setup(db => db.StringSetAsync($"{RedisIncidentDeduplicationService.IncidentKeyPrefix}{incidentId}", 
            "processed", It.IsAny<TimeSpan>(), When.NotExists, CommandFlags.None))
            .ReturnsAsync(false); // Simulate key already existed

        var service = new RedisIncidentDeduplicationService(_mockDatabase.Object, _mockConfiguration.Object, _mockLogger.Object);

        // Act
        var result = await service.IsDuplicateAsync(incidentId);

        // Assert
        Assert.True(result);
    }
}
