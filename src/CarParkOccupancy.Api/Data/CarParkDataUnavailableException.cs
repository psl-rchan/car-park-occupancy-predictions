namespace CarParkOccupancy.Api.Data;

public sealed class CarParkDataUnavailableException : Exception
{
    public const int BadGatewayStatus = 502;

    public const int ServiceUnavailableStatus = 503;

    public CarParkDataUnavailableException(string message)
        : this(message, ServiceUnavailableStatus, null)
    {
    }

    public CarParkDataUnavailableException(string message, Exception innerException)
        : this(message, ServiceUnavailableStatus, innerException)
    {
    }

    public CarParkDataUnavailableException(string message, int statusCode)
        : this(message, statusCode, null)
    {
    }

    public CarParkDataUnavailableException(string message, int statusCode, Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode is BadGatewayStatus or ServiceUnavailableStatus
            ? statusCode
            : ServiceUnavailableStatus;
    }

    public int StatusCode { get; }
}
