using KubeMind.Brain.Application.Models;

namespace KubeMind.Brain.Application.Services;

/// <summary>
/// Defines a contract for buffering resolved incidents for asynchronous processing.
/// </summary>
public interface IMemoryBuffer
{
    /// <summary>
    /// Writes a resolved incident to the memory buffer.
    /// </summary>
    /// <param name="resolution">The incident resolution to buffer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ValueTask representing the asynchronous operation.</returns>
    ValueTask WriteAsync(IncidentResolution resolution, CancellationToken cancellationToken = default);
}
