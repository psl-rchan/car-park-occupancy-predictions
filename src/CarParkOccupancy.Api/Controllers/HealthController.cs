using CarParkOccupancy.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace CarParkOccupancy.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get() => Ok(new HealthResponse());
}
