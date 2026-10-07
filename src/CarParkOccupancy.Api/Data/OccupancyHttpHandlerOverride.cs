namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Lets tests replace the occupancy HTTP handler. Production leaves <see cref="HandlerFactory"/> null.
/// </summary>
public sealed class OccupancyHttpHandlerOverride
{
    public Func<HttpMessageHandler>? HandlerFactory { get; set; }

    public HttpMessageHandler CreateHandler() =>
        HandlerFactory?.Invoke() ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
}
