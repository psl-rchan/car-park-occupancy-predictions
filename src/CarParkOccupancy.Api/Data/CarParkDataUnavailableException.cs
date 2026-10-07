namespace CarParkOccupancy.Api.Data;

public sealed class CarParkDataUnavailableException : Exception
{
    public CarParkDataUnavailableException(string message)
        : base(message)
    {
    }

    public CarParkDataUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
