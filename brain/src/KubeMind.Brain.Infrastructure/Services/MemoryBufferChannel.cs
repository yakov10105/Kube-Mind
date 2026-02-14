using System.Threading.Channels;
using KubeMind.Brain.Application.Models;
using KubeMind.Brain.Application.Services;

namespace KubeMind.Brain.Infrastructure.Services;

/// <summary>
/// A persistent, thread-safe buffer for incident resolutions using System.Threading.Channels.
/// Acts as a bridge between the synchronous IncidentService and the asynchronous MemoryConsolidationService.
/// </summary>
public class MemoryBufferChannel : IMemoryBuffer
{
    private readonly Channel<IncidentResolution> _channel;

    public MemoryBufferChannel()
    {
        // Bounded channel to provide backpressure prevention.
        // If the consumer falls behind, we prefer to wait (non-destructively) rather than drop 
        // useful cognitive data, assuming the volume isn't catastrophic.
        var options = new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        _channel = Channel.CreateBounded<IncidentResolution>(options);
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(IncidentResolution resolution, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(resolution, cancellationToken);
    }

    /// <summary>
    /// Gets the reader for the channel. Intended for internal use by the consolidation service.
    /// </summary>
    public ChannelReader<IncidentResolution> Reader => _channel.Reader;
}
