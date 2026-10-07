using Microsoft.Data.SqlClient;

namespace CarParkOccupancy.Api.Data;

public interface IDbConnectionFactory
{
    SqlConnection Create();
}
