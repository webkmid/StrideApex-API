using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using StrideApex.Api.Models;

namespace StrideApex.Api.Services;

public sealed record SplitAnalysisSummary(
    IReadOnlyList<SplitItemDto> Splits,
    double SplitCvPct,
    long FirstHalfAvgPaceSec,
    long SecondHalfAvgPaceSec,
    long SplitDeltaSec,
    string SplitStrategy,
    SplitItemDto? FastestSplit,
    SplitItemDto? SlowestSplit,
    long PaceSpreadSec
);

/// <summary>
/// Parses per-kilometer Strava splits, recalibrates splits on workout edits,
/// and computes deep split consistency metrics (CV%, negative/positive split delta).
/// </summary>
public sealed partial class SplitParserService
{
    [GeneratedRegex(@"(?:KM\s*)?(0[.,]\d+|[1-9][0-9]*(?:[.,]\d+)?)\s*[:.]?\s*([0-9]{1,2}:[0-5][0-9])(?:/km)?", RegexOptions.IgnoreCase)]
    private static partial Regex SplitRegex();

    public IReadOnlyList<SplitItemDto> ParseSplitsSummary(string? splitsSummary, long avgPaceSecondsPerKm = 0)
    {
        if (string.IsNullOrWhiteSpace(splitsSummary))
            return [];

        var rawItems = new List<(double Km, string PaceStr, long PaceSeconds)>();
        foreach (Match match in SplitRegex().Matches(splitsSummary))
        {
            var rawKm = match.Groups[1].Value.Replace(',', '.');
            if (!double.TryParse(rawKm, NumberStyles.Float, CultureInfo.InvariantCulture, out var kmNum))
                continue;

            var paceStr = match.Groups[2].Value;
            var parts = paceStr.Split(':');
            if (parts.Length != 2)
                continue;

            if (!long.TryParse(parts[0], out var mins) || !long.TryParse(parts[1], out var secs))
                continue;

            var totalSec = mins * 60 + secs;
            if (totalSec is >= 120 and <= 900)
            {
                rawItems.Add((kmNum, paceStr, totalSec));
            }
        }

        if (rawItems.Count == 0)
            return [];

        var computedAvg = avgPaceSecondsPerKm > 0
            ? avgPaceSecondsPerKm
            : (long)Math.Round(rawItems.Average(x => x.PaceSeconds));

        return rawItems
            .Select(x => new SplitItemDto(x.Km, x.PaceStr, x.PaceSeconds, x.PaceSeconds - computedAvg))
            .ToList();
    }

    public SplitAnalysisSummary AnalyzeSplits(string? splitsSummary, long avgPaceSecondsPerKm = 0)
    {
        var splits = ParseSplitsSummary(splitsSummary, avgPaceSecondsPerKm);
        if (splits.Count == 0)
        {
            return new SplitAnalysisSummary(
                Splits: [],
                SplitCvPct: 0.0,
                FirstHalfAvgPaceSec: avgPaceSecondsPerKm,
                SecondHalfAvgPaceSec: avgPaceSecondsPerKm,
                SplitDeltaSec: 0,
                SplitStrategy: "Belum Ada Data Split",
                FastestSplit: null,
                SlowestSplit: null,
                PaceSpreadSec: 0
            );
        }

        var mean = splits.Average(s => s.PaceSeconds);
        var variance = splits.Average(s => Math.Pow(s.PaceSeconds - mean, 2));
        var stdDev = Math.Sqrt(variance);
        var cvPct = mean > 0 ? Math.Round((stdDev / mean) * 100.0, 2) : 0.0;

        var fastest = splits.MinBy(s => s.PaceSeconds);
        var slowest = splits.MaxBy(s => s.PaceSeconds);
        var spread = (slowest?.PaceSeconds ?? 0) - (fastest?.PaceSeconds ?? 0);

        long firstHalfAvg;
        long secondHalfAvg;
        if (splits.Count >= 2)
        {
            var half = Math.Max(1, splits.Count / 2);
            firstHalfAvg = (long)Math.Round(splits.Take(half).Average(s => s.PaceSeconds));
            secondHalfAvg = (long)Math.Round(splits.Skip(half).Average(s => s.PaceSeconds));
        }
        else
        {
            firstHalfAvg = splits[0].PaceSeconds;
            secondHalfAvg = splits[0].PaceSeconds;
        }

        var delta = secondHalfAvg - firstHalfAvg;
        var strategy = splits.Count < 2
            ? "Single Split"
            : delta <= -5
                ? "Negative Split"
                : delta >= 10
                    ? "Positive Split"
                    : "Even Split";

        return new SplitAnalysisSummary(
            Splits: splits,
            SplitCvPct: cvPct,
            FirstHalfAvgPaceSec: firstHalfAvg,
            SecondHalfAvgPaceSec: secondHalfAvg,
            SplitDeltaSec: delta,
            SplitStrategy: strategy,
            FastestSplit: fastest,
            SlowestSplit: slowest,
            PaceSpreadSec: spread
        );
    }

    public string RecalibrateSplits(
        string? oldSplitsSummary,
        double newDistanceKm,
        long newAvgPaceSec,
        bool generateIfEmpty = true)
    {
        var safePace = Math.Clamp(newAvgPaceSec, 120L, 1200L);
        var safeDist = Math.Clamp(newDistanceKm, 0.1, 500.0);
        var targetCount = (int)Math.Floor(safeDist + 0.05);
        if (safeDist >= 0.5 && targetCount < 1) targetCount = 1;
        targetCount = Math.Min(targetCount, 100);

        if (targetCount <= 0)
            return string.Empty;

        var oldParsed = ParseSplitsSummary(oldSplitsSummary, safePace);
        if (oldParsed.Count == 0 && !generateIfEmpty)
            return string.Empty;

        var generatedPaces = new List<long>(targetCount);
        if (oldParsed.Count > 0)
        {
            var oldMean = Math.Max(1.0, oldParsed.Average(p => p.PaceSeconds));
            for (var idx = 0; idx < targetCount; idx++)
            {
                long basePace;
                if (idx < oldParsed.Count)
                {
                    basePace = Math.Clamp(
                        (long)Math.Round(oldParsed[idx].PaceSeconds * (double)safePace / oldMean),
                        120L,
                        1200L
                    );
                }
                else
                {
                    var slightOffset = idx == targetCount - 1 && targetCount > 2
                        ? -3L
                        : idx % 2 == 0 ? 2L : -1L;
                    basePace = Math.Clamp(safePace + slightOffset, 120L, 1200L);
                }
                generatedPaces.Add(basePace);
            }
        }
        else
        {
            for (var idx = 0; idx < targetCount; idx++)
            {
                var delta = targetCount == 1
                    ? 0L
                    : idx == 0 ? 4L
                    : idx == targetCount - 1 ? -4L
                    : idx % 2 == 1 ? 1L : -1L;
                generatedPaces.Add(Math.Clamp(safePace + delta, 120L, 1200L));
            }
        }

        var currentMeanDiff = safePace - (long)Math.Round(generatedPaces.Average());
        if (currentMeanDiff != 0)
        {
            for (var i = 0; i < generatedPaces.Count; i++)
            {
                generatedPaces[i] = Math.Clamp(generatedPaces[i] + currentMeanDiff, 120L, 1200L);
            }
        }

        return string.Join(
            " | ",
            generatedPaces.Select((paceSec, idx) =>
                string.Create(CultureInfo.InvariantCulture, $"KM {idx + 1}: {paceSec / 60}:{paceSec % 60:00}/km"))
        );
    }

    public bool NeedsSplitRecalibration(string? splitsSummary, double distanceKm, long avgPaceSec)
    {
        var parsed = ParseSplitsSummary(splitsSummary, avgPaceSec);
        if (parsed.Count == 0)
            return distanceKm >= 1.0;

        var expectedCount = Math.Max(1, (int)Math.Floor(distanceKm + 0.05));
        var countMismatch = Math.Abs(parsed.Count - expectedCount) >= 1 && distanceKm >= 1.0;
        var splitsAvg = parsed.Average(p => p.PaceSeconds);
        var paceMismatch = Math.Abs(splitsAvg - avgPaceSec) >= 12.0;
        return countMismatch || paceMismatch;
    }

    public string BuildLocalRefreshedAiInsight(
        string title,
        double distanceKm,
        long durationSeconds,
        long avgPaceSec,
        long avgHeartRate,
        long cadenceSpm,
        long elevationGainM,
        string splitsSummary,
        RacePredictionService raceService)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Sesi Lari" : title;
        var analysis = AnalyzeSplits(splitsSummary, avgPaceSec);
        var durStr = raceService.FormatDuration(durationSeconds);
        var paceStr = raceService.FormatPace(avgPaceSec);

        var sb = new StringBuilder();
        sb.AppendLine($"Analisis Diperbarui ({safeTitle}):");
        sb.AppendLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"1. Ringkasan Data Baru: Jarak {distanceKm:F2} km selesai dalam waktu {durStr} (Pace rata-rata {paceStr})."
            )
        );

        if (analysis.Splits.Count >= 2)
        {
            var first = analysis.Splits[0];
            var last = analysis.Splits[^1];
            var trend = analysis.SplitDeltaSec <= -5
                ? "negative split (paruh akhir lebih cepat)"
                : analysis.SplitDeltaSec >= 10
                    ? "positive split (melambat di paruh akhir)"
                    : "pacing merata dan konsisten";
            sb.AppendLine(
                $"2. Profil Split ({analysis.Splits.Count} KM · CV {analysis.SplitCvPct:F1}%): Pola lari menunjukkan {trend}, dari KM 1 ({first.PaceStr}/km) hingga KM {last.Km} ({last.PaceStr}/km)."
            );
        }
        else
        {
            var elevNote = elevationGainM > 0 ? $" dengan elevasi +{elevationGainM} m" : string.Empty;
            sb.AppendLine($"2. Konsistensi Ritme: Pace terkunci rata-rata di {paceStr} sepanjang rute{elevNote}.");
        }

        var bioParts = new List<string>();
        if (avgHeartRate > 0) bioParts.Add($"HR rata-rata {avgHeartRate} bpm");
        if (cadenceSpm > 0) bioParts.Add($"cadence {cadenceSpm} spm");

        if (bioParts.Count > 0)
        {
            sb.Append($"3. Beban Biomekanik: {string.Join(" & ", bioParts)}. Jaga hidrasi serta sesi easy/recovery di hari berikutnya.");
        }
        else
        {
            sb.Append("3. Rekomendasi: Pertahankan ritme aerobik konsisten dan jadwalkan recovery run ringan setelah sesi intens.");
        }

        return sb.ToString().Trim();
    }
}
