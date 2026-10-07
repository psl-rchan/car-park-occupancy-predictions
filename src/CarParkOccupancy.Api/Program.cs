using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Services;
using CarParkOccupancy.Api.Services.Prediction;
using CarParkOccupancy.Api.Ui;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var sourceKind = OccupancySnapshotSourceSelector.Resolve(builder.Configuration, builder.Environment);

builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
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

builder.Services.AddSingleton(new CarParkDataSource(sourceKind == OccupancySnapshotSourceKind.Sample));
builder.Services.AddSingleton<CarParkApiHandlerOverride>();

var apiTimeoutSeconds = Math.Clamp(builder.Configuration.GetValue("Ui:ApiTimeoutSeconds", 120), 1, 600);
builder.Services.AddHttpClient<CarParkApiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(apiTimeoutSeconds);
})
.ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<CarParkApiHandlerOverride>().CreateHandler());

switch (sourceKind)
{
    case OccupancySnapshotSourceKind.Sample:
        builder.Services.AddSingleton<IOccupancySnapshotSource, SampleOccupancySnapshotSource>();
        break;
    case OccupancySnapshotSourceKind.Http:
        builder.Services.AddSingleton<OccupancyHttpHandlerOverride>();
        builder.Services.AddHttpClient<IOccupancySnapshotSource, HttpOccupancySnapshotSource>((sp, client) =>
        {
            var seconds = sp.GetRequiredService<IOptions<CarParkDataOptions>>().Value.Http.TimeoutSeconds;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 600));
        })
        .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<OccupancyHttpHandlerOverride>().CreateHandler());
        break;
    default:
        builder.Services.AddSingleton(sp => new SqlObjectNames(sp.GetRequiredService<IOptions<CarParkDataOptions>>().Value));
        builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        builder.Services.AddScoped<IOccupancySnapshotSource, SqlServerOccupancySnapshotSource>();
        break;
}

builder.Services.AddScoped<ICarParkReadStore, OccupancySnapshotReadStore>();

builder.Services.AddSingleton<HistoricalBaselinePredictor>();
builder.Services.AddSingleton<GeminiVertexPredictor>();
builder.Services.AddSingleton<OccupancyPredictorResolver>();
builder.Services.AddScoped<OccupancyPredictionService>();

var app = builder.Build();

app.Logger.LogInformation("Occupancy snapshot source is {OccupancySource}.", sourceKind);
if (sourceKind == OccupancySnapshotSourceKind.Sample)
{
    app.Logger.LogWarning(
        "Sample occupancy snapshots are enabled. Predictions do not read SQL Server or the HTTP API.");
}

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
            CarParkDataUnavailableException data => (data.StatusCode, data.Message),
            UnknownPredictorException unknown => (StatusCodes.Status400BadRequest, unknown.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        await Results.Problem(title: title, statusCode: status).ExecuteAsync(context);
    });
});

app.UseStaticFiles();
app.MapOpenApi();
app.MapControllers();
app.MapRazorPages();
app.Run();

public partial class Program;
