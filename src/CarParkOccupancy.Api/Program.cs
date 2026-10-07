using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Services;
using CarParkOccupancy.Api.Services.Prediction;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Car park occupancy predictions";
        document.Info.Version = "v1";
        document.Info.Description =
            "PredictedOccupancyPercent is occupancy as a percent of capacity on a 0-100 scale (0 empty, 100 full). It is not a 0-1 fraction. Target framework: net10.0.";
        return Task.CompletedTask;
    });
});

builder.Services.AddSingleton<IValidateOptions<CarParkDataOptions>, CarParkDataOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<PredictionOptions>, PredictionOptionsValidator>();
builder.Services.AddOptions<CarParkDataOptions>()
    .Bind(builder.Configuration.GetSection(CarParkDataOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<PredictionOptions>()
    .Bind(builder.Configuration.GetSection(PredictionOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton(sp => new SqlObjectNames(sp.GetRequiredService<IOptions<CarParkDataOptions>>().Value));
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<ICarParkReadStore, DapperCarParkReadStore>();
builder.Services.AddSingleton<HistoricalBaselinePredictor>();
builder.Services.AddSingleton<GeminiVertexPredictor>();
builder.Services.AddSingleton<OccupancyPredictorResolver>();
builder.Services.AddScoped<OccupancyPredictionService>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CarParkOccupancy.Api.ExceptionHandler");
        logger.LogError(exception, "Unhandled exception.");

        var (status, title) = exception switch
        {
            CarParkDataUnavailableException data => (StatusCodes.Status503ServiceUnavailable, data.Message),
            UnknownPredictorException unknown => (StatusCodes.Status400BadRequest, unknown.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        await Results.Problem(title: title, statusCode: status).ExecuteAsync(context);
    });
});

app.MapOpenApi();
app.MapControllers();
app.Run();
