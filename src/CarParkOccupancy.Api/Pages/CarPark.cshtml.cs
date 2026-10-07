using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Ui;
using Microsoft.AspNetCore.Mvc;

namespace CarParkOccupancy.Api.Pages;

public sealed class CarParkModel : PredictionPageModel
{
    public CarParkModel(CarParkApiClient api)
        : base(api)
    {
    }

    public string Code { get; private set; } = "";

    public CarParkPredictionResult? Prediction { get; private set; }

    public bool NotFoundPark { get; private set; }

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken cancellationToken)
    {
        Code = code?.Trim() ?? "";
        if (Code.Length == 0)
        {
            NotFoundPark = true;
            ErrorMessage = "Car park code is required.";
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        try
        {
            Prediction = await Api.GetPredictionAsync(Code, cancellationToken);
            return Page();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailTimeout();
        }
        catch (CarParkApiException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            NotFoundPark = true;
            ErrorMessage = ex.Message;
            Response.StatusCode = ex.StatusCode;
            return Page();
        }
        catch (CarParkApiException ex)
        {
            return Fail(ex);
        }
        catch (HttpRequestException)
        {
            return FailTransport();
        }
    }
}
