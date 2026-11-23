using System.Text.Json;
using System.Text.Json.Serialization;
using StrideApex.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portEnv) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddHttpClient();

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? ["https://strideapex.vercel.app", "http://localhost:3000", "http://127.0.0.1:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("StrideApexWeb", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Domain & Analytics Services
builder.Services.AddSingleton<SplitParserService>();
builder.Services.AddSingleton<RacePredictionService>();
builder.Services.AddSingleton<ShoeUnlockService>();
builder.Services.AddSingleton<WeeklyProgressService>();
builder.Services.AddSingleton<GoalTrainingPlanService>();
builder.Services.AddSingleton<BiomechanicsCatalogService>();
builder.Services.AddSingleton<DeepAnalyticsService>();
builder.Services.AddSingleton<GeminiAiService>();

var app = builder.Build();

app.UseCors("StrideApexWeb");
app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    status = "ok",
    service = "StrideApex.Api"
}));

app.MapGet("/api/health", (IConfiguration config) =>
{
    var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? config["Gemini:ApiKey"]
        ?? string.Empty;

    return Results.Ok(new
    {
        status = "ok",
        service = "StrideApex.Api",
        geminiConfigured = !string.IsNullOrWhiteSpace(geminiKey),
        timestampUtc = DateTimeOffset.UtcNow.ToString("O")
    });
});

app.Run();
