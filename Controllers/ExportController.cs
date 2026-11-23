using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StrideApex.Api.Models;
using StrideApex.Api.Services;

namespace StrideApex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ExportController(RacePredictionService raceService) : ControllerBase
{
    private static readonly JsonSerializerOptions PrettyJsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [HttpPost("csv")]
    public IActionResult ExportCsv([FromBody] ComprehensiveAnalyticsRequest request)
    {
        var workouts = (request.Workouts ?? []).OrderByDescending(w => w.CreatedAtEpochMs);
        var sb = new StringBuilder();
        sb.AppendLine("Id,Date,Title,DistanceKm,Duration,AvgPace,AvgHeartRate,CadenceSpm,ElevationGainM,SourceType,SplitsSummary");

        foreach (var w in workouts)
        {
            var dateStr = w.CreatedAtEpochMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(w.CreatedAtEpochMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "";
            var durStr = raceService.FormatDuration(w.DurationSeconds);
            var paceStr = raceService.FormatPace(w.AvgPaceSecondsPerKm);

            sb.AppendLine(string.Join(",",
                CsvEscape(w.Id),
                CsvEscape(dateStr),
                CsvEscape(w.Title),
                w.DistanceKm.ToString("F2", CultureInfo.InvariantCulture),
                CsvEscape(durStr),
                CsvEscape(paceStr),
                w.AvgHeartRate,
                w.CadenceSpm,
                w.ElevationGainM,
                CsvEscape(w.SourceType),
                CsvEscape(w.SplitsSummary)
            ));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", "strideapex-workouts.csv");
    }

    [HttpPost("json")]
    public IActionResult ExportJson([FromBody] ComprehensiveAnalyticsRequest request)
    {
        var payload = new
        {
            exportedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            schema = "strideapex-backup-v1",
            workouts = request.Workouts ?? [],
            personalRecords = request.Prs ?? [],
            goals = request.Goals ?? []
        };

        var json = JsonSerializer.Serialize(payload, PrettyJsonOpts);
        return File(Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", "strideapex-backup.json");
    }

    private static string CsvEscape(string? val)
    {
        var safe = val ?? string.Empty;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }
}
