namespace CarParkOccupancy.Api.Ui;

public sealed class CarParkApiException : Exception
{
    public CarParkApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
