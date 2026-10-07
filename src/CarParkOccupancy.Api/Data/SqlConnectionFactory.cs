using Microsoft.Data.SqlClient;

namespace CarParkOccupancy.Api.Data;

public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    public const string ConnectionStringName = "CarParkDb";

    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;
    }

    public SqlConnection Create()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new CarParkDataUnavailableException(
                "ConnectionStrings:CarParkDb is not configured. Set it with user secrets or the ConnectionStrings__CarParkDb environment variable.");
        }

        return new SqlConnection(_connectionString);
    }
}
