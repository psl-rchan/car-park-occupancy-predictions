namespace CarParkOccupancy.Api.Ui;

/// <summary>
/// Supplies the HTTP handler the Razor Pages use to call this host's prediction API.
/// Production uses a normal socket handler. Tests can set <see cref="HandlerFactory"/>
/// to the in-memory test server so the pages still go through the HTTP endpoints.
/// </summary>
public sealed class CarParkApiHandlerOverride
{
    public Func<HttpMessageHandler>? HandlerFactory { get; set; }

    public HttpMessageHandler CreateHandler() =>
        HandlerFactory?.Invoke() ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };
}
