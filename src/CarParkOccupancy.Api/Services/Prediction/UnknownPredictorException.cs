namespace CarParkOccupancy.Api.Services.Prediction;

public sealed class UnknownPredictorException : Exception
{
    public UnknownPredictorException(string message)
        : base(message)
    {
    }
}
