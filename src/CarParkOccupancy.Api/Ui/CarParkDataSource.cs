namespace CarParkOccupancy.Api.Ui;

public sealed class CarParkDataSource
{
    public CarParkDataSource(bool usingSampleSnapshots)
    {
        UsingSampleSnapshots = usingSampleSnapshots;
    }

    public bool UsingSampleSnapshots { get; }
}
