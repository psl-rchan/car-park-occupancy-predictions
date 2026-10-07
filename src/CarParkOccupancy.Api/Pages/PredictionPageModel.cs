using CarParkOccupancy.Api.Services.Prediction;
using CarParkOccupancy.Api.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarParkOccupancy.Api.Pages;

public abstract class PredictionPageModel : PageModel
{
    private readonly CarParkApiClient _api;

    protected PredictionPageModel(CarParkApiClient api)
    {
        _api = api;
    }

    protected CarParkApiClient Api => _api;

    public IReadOnlyList<int> Horizons { get; } = HorizonParser.DefaultHorizons;

    public bool DataUnavailable { get; private set; }

    public bool ApiUnreachable { get; private set; }

    public string? ErrorMessage { get; protected set; }

    protected IActionResult Fail(CarParkApiException exception)
    {
        ErrorMessage = exception.Message;
        Response.StatusCode = exception.StatusCode;
        if (exception.StatusCode is StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable)
        {
            DataUnavailable = true;
        }
        else if (exception.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            ApiUnreachable = true;
        }

        return Page();
    }

    protected IActionResult FailTransport()
    {
        ApiUnreachable = true;
        ErrorMessage = "The page could not reach the prediction API on this server.";
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return Page();
    }

    protected IActionResult FailTimeout()
    {
        ApiUnreachable = true;
        ErrorMessage = "The prediction API timed out.";
        Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        return Page();
    }
}
