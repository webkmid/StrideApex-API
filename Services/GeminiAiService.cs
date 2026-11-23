using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StrideApex.Api.Models;

namespace StrideApex.Api.Services;

/// <summary>
/// Integrates with Gemini API (gemini-2.5-flash) when GEMINI_API_KEY is configured,
/// with fast deterministic StrideApex fallbacks (text/splits OCR parser & structured physiology coaching).
/// </summary>
public sealed partial class GeminiAiService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    SplitParserService splitParser,
    RacePredictionService raceService,
    GoalTrainingPlanService goalPlanService,
    ILogger<GeminiAiService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"(?:Distance|Jarak)\s*[:=]?\s*([0-9]+(?:[.,][0-9]+)?)\s*km", RegexOptions.IgnoreCase)]
    private static partial Regex DistanceRegex();

    [GeneratedRegex(@"(?:Moving Time|Time|Waktu|Durasi)\s*[:=]?\s*(?:(\d{1,2}):)?(\d{1,2}):(\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"(?:Avg Pace|Average Pace|Pace)\s*[:=]?\s*(\d{1,2}):([0-5]\d)\s*(?:/km)?", RegexOptions.IgnoreCase)]
    private static partial Regex PaceRegex();

    [GeneratedRegex(@"(?:Heart Rate|HR|Detak)\s*[:=]?\s*(\d{2,3})\s*(?:bpm)?", RegexOptions.IgnoreCase)]
    private static partial Regex HrRegex();

    [GeneratedRegex(@"(?:Cadence|Kadens)\s*[:=]?\s*(\d{2,3})\s*(?:spm)?", RegexOptions.IgnoreCase)]
    private static partial Regex CadenceRegex();

    [GeneratedRegex(@"(?:Elev(?:ation)?(?:\s*Gain)?|Elevasi)\s*[:=]?\s*\+?(\d{1,5})\s*m", RegexOptions.IgnoreCase)]
    private static partial Regex ElevRegex();

    private string ResolveApiKey() =>
        Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? configuration["Gemini:ApiKey"]
        ?? string.Empty;

    private string ResolveModel() =>
        configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    public async Task<ExtractedStravaMetricsDto> ExtractStravaScreenshotAsync(OcrExtractRequest req, CancellationToken ct)
    {
        var apiKey = ResolveApiKey();

        // 1. Try Gemini Vision if an image and API key are provided
        if (!string.IsNullOrWhiteSpace(req.ImageBase64) && !string.IsNullOrWhiteSpace(apiKey))
        {
            var cleanBase64 = StripDataUrlPrefix(req.ImageBase64);
            var mime = string.IsNullOrWhiteSpace(req.MimeType) ? "image/jpeg" : req.MimeType;

            var prompt =
                "Extract running workout telemetry from this Strava screenshot strictly as valid JSON (no markdown fences) with keys: " +
                "title (string, e.g. 'Run' or activity title), distanceKm (number), durationSeconds (integer), avgPaceSecondsPerKm (integer), " +
                "avgHeartRate (integer, 0 if missing), cadenceSpm (integer, 0 if missing), elevationGainM (integer, 0 if missing), " +
                "splitsSummary (string formatted as 'KM 1: 7:54/km | KM 2: 6:58/km | ...'), aiCoachingInsight (in-depth Indonesian evaluation). " +
                "CRITICAL RULES: " +
                "1) For durationSeconds, prefer 'Moving Time' over 'Elapsed Time' if both are shown. " +
                "2) If total distance is not explicitly labeled, sum the split distances in the Splits table (e.g. 1 + 1 + 1 + 1 + 0.9 = 4.9) OR calculate distanceKm = durationSeconds / avgPaceSecondsPerKm. Never pick just split 1. " +
                "3) For splitsSummary, read each row's split distance label (e.g. 1, 2, 3, 4, 0.9) and its pace (e.g. 7:54, 6:58, 6:35, 6:45, 6:45), ignoring the rightmost elevation delta column (-3, 2, 1, 0, -1).";

            var payload = new
            {
                contents = new object[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { inline_data = new { mime_type = mime, data = cleanBase64 } },
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new { responseMimeType = "application/json" }
            };

            var rawJson = await CallGeminiRawAsync(apiKey, payload, ct);
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(CleanJsonFence(rawJson));
                    var root = doc.RootElement;
                    var rawDist = GetDouble(root, "distanceKm");
                    var durSec = GetLong(root, "durationSeconds");
                    var paceSec = GetLong(root, "avgPaceSecondsPerKm");

                    var reconciled = ReconcileDistanceAndPace(rawDist, durSec, paceSec);
                    var splits = GetString(root, "splitsSummary");
                    var title = GetString(root, "title");
                    if (string.IsNullOrWhiteSpace(title)) title = "Strava Run";

                    return new ExtractedStravaMetricsDto(
                        Title: title,
                        DistanceKm: reconciled.DistanceKm,
                        DurationSeconds: reconciled.DurationSeconds,
                        AvgPaceSecondsPerKm: reconciled.PaceSeconds,
                        AvgHeartRate: Math.Clamp(GetLong(root, "avgHeartRate"), 0L, 260L),
                        CadenceSpm: Math.Clamp(GetLong(root, "cadenceSpm"), 0L, 300L),
                        ElevationGainM: Math.Clamp(GetLong(root, "elevationGainM"), 0L, 20000L),
                        SplitsSummary: splits,
                        AiCoachingInsight: GetString(root, "aiCoachingInsight",
                            splitParser.BuildLocalRefreshedAiInsight(
                                title, reconciled.DistanceKm, reconciled.DurationSeconds, reconciled.PaceSeconds,
                                GetLong(root, "avgHeartRate"), GetLong(root, "cadenceSpm"), GetLong(root, "elevationGainM"),
                                splits, raceService)),
                        WorkoutDateEpochMs: null,
                        DetectedDateStr: string.Empty,
                        SourceEngine: "GEMINI_VISION"
                    );
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed parsing Gemini Vision JSON output, falling back to local parser.");
                }
            }
        }

        // 2. Parse Raw Text / Splits summary if supplied
        if (!string.IsNullOrWhiteSpace(req.RawText))
        {
            return ParseRawStravaText(req.RawText);
        }

        // 3. Deterministic sample preset fallback matching Android's 3 Strava presets
        return BuildPresetSample(req.PresetIndex ?? 0);
    }

    public ExtractedStravaMetricsDto ParseRawStravaText(string rawText)
    {
        var lines = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var title = lines.FirstOrDefault(l =>
            l.Length is >= 3 and <= 80 &&
            !l.Contains("km", StringComparison.OrdinalIgnoreCase) &&
            !l.Contains(':')) ?? "Strava Run Import";

        var distMatch = DistanceRegex().Match(rawText);
        var rawDist = distMatch.Success && double.TryParse(distMatch.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDist)
            ? parsedDist
            : 0.0;

        var timeMatch = TimeRegex().Match(rawText);
        var durSec = 0L;
        if (timeMatch.Success)
        {
            var hrs = string.IsNullOrEmpty(timeMatch.Groups[1].Value) ? 0L : long.Parse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var mins = long.Parse(timeMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var secs = long.Parse(timeMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            durSec = hrs * 3600 + mins * 60 + secs;
        }

        var paceMatch = PaceRegex().Match(rawText);
        var paceSec = 0L;
        if (paceMatch.Success)
        {
            paceSec = long.Parse(paceMatch.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                    + long.Parse(paceMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        var splitsParsed = splitParser.ParseSplitsSummary(rawText);
        var splitsSummary = splitsParsed.Count > 0
            ? string.Join(" | ", splitsParsed.Select(s => $"KM {s.Km}: {s.PaceStr}/km"))
            : string.Empty;

        if (paceSec <= 0 && splitsParsed.Count > 0)
        {
            paceSec = (long)Math.Round(splitsParsed.Average(s => s.PaceSeconds));
        }

        var reconciled = ReconcileDistanceAndPace(rawDist, durSec, paceSec);
        if (string.IsNullOrWhiteSpace(splitsSummary) && reconciled.DistanceKm >= 1.0)
        {
            splitsSummary = splitParser.RecalibrateSplits(string.Empty, reconciled.DistanceKm, reconciled.PaceSeconds);
        }

        var hr = HrRegex().Match(rawText) is { Success: true } hm ? Math.Clamp(long.Parse(hm.Groups[1].Value, CultureInfo.InvariantCulture), 0, 260) : 0L;
        var cad = CadenceRegex().Match(rawText) is { Success: true } cm ? Math.Clamp(long.Parse(cm.Groups[1].Value, CultureInfo.InvariantCulture), 0, 300) : 0L;
        var elev = ElevRegex().Match(rawText) is { Success: true } em ? Math.Clamp(long.Parse(em.Groups[1].Value, CultureInfo.InvariantCulture), 0, 20000) : 0L;

        var insight = splitParser.BuildLocalRefreshedAiInsight(
            title, reconciled.DistanceKm, reconciled.DurationSeconds, reconciled.PaceSeconds,
            hr, cad, elev, splitsSummary, raceService);

        return new ExtractedStravaMetricsDto(
            Title: title,
            DistanceKm: reconciled.DistanceKm,
            DurationSeconds: reconciled.DurationSeconds,
            AvgPaceSecondsPerKm: reconciled.PaceSeconds,
            AvgHeartRate: hr,
            CadenceSpm: cad,
            ElevationGainM: elev,
            SplitsSummary: splitsSummary,
            AiCoachingInsight: insight,
            WorkoutDateEpochMs: null,
            DetectedDateStr: string.Empty,
            SourceEngine: "LOCAL_PARSER"
        );
    }

    public async Task<RefreshWorkoutResponse> RefreshWorkoutInsightAsync(RefreshWorkoutRequest req, CancellationToken ct)
    {
        var w = req.Workout;
        var safePace = w.AvgPaceSecondsPerKm is >= 60 and <= 3600
            ? w.AvgPaceSecondsPerKm
            : Math.Clamp((long)Math.Round(w.DurationSeconds / Math.Max(0.1, w.DistanceKm)), 60L, 3600L);

        var refreshedSplits = splitParser.NeedsSplitRecalibration(w.SplitsSummary, w.DistanceKm, safePace)
            ? splitParser.RecalibrateSplits(w.SplitsSummary, w.DistanceKm, safePace, generateIfEmpty: true)
            : w.SplitsSummary;

        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var prSummary = req.Prs is { Count: > 0 }
                ? string.Join(", ", req.Prs.Select(p => $"{p.DistanceCategory}: {raceService.FormatDuration(p.BestTimeSeconds)}"))
                : "Belum ada catatan PR khusus";

            var prompt =
                "Anda adalah Pelatih Lari Profesional & Ilmuwan Olahraga. " +
                "Berikan evaluasi terstruktur dan mendalam dalam Bahasa Indonesia (maksimal 3-4 poin ringkas & actionable) untuk sesi lari berikut:\n" +
                string.Create(CultureInfo.InvariantCulture, $"- Judul: {w.Title}\n- Jarak Total: {w.DistanceKm:F2} km\n") +
                $"- Durasi Bergerak: {raceService.FormatDuration(w.DurationSeconds)}\n" +
                $"- Pace Rata-Rata: {raceService.FormatPace(safePace)}\n" +
                (w.AvgHeartRate > 0 ? $"- Heart Rate Rata-Rata: {w.AvgHeartRate} bpm\n" : string.Empty) +
                (w.CadenceSpm > 0 ? $"- Cadence: {w.CadenceSpm} spm\n" : string.Empty) +
                (w.ElevationGainM > 0 ? $"- Elevasi: +{w.ElevationGainM} m\n" : string.Empty) +
                (!string.IsNullOrWhiteSpace(refreshedSplits) ? $"- Rincian Split per KM: {refreshedSplits}\n" : string.Empty) +
                $"- Rekor Pribadi (PR) Saat Ini: {prSummary}\n\n" +
                "Analisis harus mencakup:\n1. Evaluasi Pacing & Konsistensi Split (negative split / positive split).\n2. Observasi Biomekanika / Kardiovaskular & Zona Intensitas VDOT.\n3. Rekomendasi konkret sesi lari dan pemulihan berikutnya.";

            var text = await CallGeminiTextAsync(apiKey, prompt, ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new RefreshWorkoutResponse(refreshedSplits, text.Trim(), "GEMINI_AI");
            }
        }

        var localInsight = splitParser.BuildLocalRefreshedAiInsight(
            w.Title, w.DistanceKm, w.DurationSeconds, safePace,
            w.AvgHeartRate, w.CadenceSpm, w.ElevationGainM, refreshedSplits, raceService);

        return new RefreshWorkoutResponse(refreshedSplits, localInsight, "LOCAL_ENGINE");
    }

    public async Task<PlanAdviceResponse> GeneratePlanAdviceAsync(PlanAdviceRequest req, CancellationToken ct)
    {
        var meta = goalPlanService.ParseGoalMetadata(req.Goal);
        var category = DistanceCategoryHelper.FromKey(req.Goal.TargetDistanceCategory);
        var paceSec = Math.Max(150L, (long)Math.Round(req.Goal.TargetTimeSeconds / category.Kilometers));
        var targetDuration = raceService.FormatDuration(req.Goal.TargetTimeSeconds);
        var targetPace = raceService.FormatPace(paceSec);
        var workouts = req.Workouts ?? [];

        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var sb = new StringBuilder();
            sb.Append($"Anda adalah Pelatih Lari Profesional. Target atlet: '{meta.CleanTitle}' ({category.DisplayName}) ");
            sb.Append($"target waktu {targetDuration} (Pace {targetPace}). Periode: {meta.StartDateDisplay} s/d {meta.TargetDateDisplay} (Sisa {meta.DaysRemaining} hari). ");
            sb.Append($"Jadwal latihan: {meta.TrainingDaysIso.Count}x/minggu ({string.Join(", ", meta.TrainingDaysIso.Select(goalPlanService.DayNameId))}).\n");

            if (workouts.Count > 0)
            {
                sb.Append($"Riwayat terakhir ({workouts.Count} sesi): ");
                foreach (var w in workouts.Take(4))
                {
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $"- {w.Title}: {w.DistanceKm:F2} km, Pace {raceService.FormatPace(w.AvgPaceSecondsPerKm)}, HR {w.AvgHeartRate} bpm. "));
                }
            }

            sb.Append("\nBerikan analisis periodisasi adaptif dalam Bahasa Indonesia (maksimal 3 paragraf singkat): pembagian Recovery/Tempo/Interval, strategi tapering 7 hari sebelum race day, serta pemulihan glikogen & hidrasi.");

            var text = await CallGeminiTextAsync(apiKey, sb.ToString(), ct);
            if (!string.IsNullOrWhiteSpace(text))
                return new PlanAdviceResponse(text.Trim(), "GEMINI_AI");
        }

        var localAdvice =
            $"Rencana Periodisasi '{meta.CleanTitle}' ({category.DisplayName} · Target {targetDuration} @ {targetPace}):\n\n" +
            $"1. Strategi Sesi Adaptif ({meta.TrainingDaysIso.Count}x/minggu): Seimbangkan hari kualitas (Interval VO2Max & Tempo Threshold) dengan jeda minimal 48 jam menggunakan Recovery Run Zona 1-2.\n" +
            $"2. Fase Tapering (H-7 menuju {meta.TargetDateDisplay}): Turunkan volume latihan menjadi ~60–70% dengan intensitas race pace singkat agar glikogen otot terisi penuh saat garis start.\n" +
            $"3. Hidrasi & Pemulihan: Konsumsi karbohidrat kompleks + elektrolit setelah sesi Long Run agar efisiensi mitokondria dan adaptasi tendon tetap optimal.";

        return new PlanAdviceResponse(localAdvice, "ADAPTIVE_AI");
    }

    public async Task<BiomechanicsCoachResponse> AnswerBiomechanicsQuestionAsync(BiomechanicsCoachRequest req, CancellationToken ct)
    {
        var m = req.Module;
        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var prompt =
                $"Anda adalah Ahli Biomekanika Lari. Fokus modul: '{m.Title}' ({m.FocusMetricLabel}: {m.OptimalTargetRange}, Kesalahan Umum: {m.CommonFault}). " +
                $"Atlet bertanya: \"{req.Question}\". Jawab dalam Bahasa Indonesia secara ilmiah namun mudah dipraktikkan (3-4 poin konkret termasuk cue form dan latihan drill).";

            var text = await CallGeminiTextAsync(apiKey, prompt, ct);
            if (!string.IsNullOrWhiteSpace(text))
                return new BiomechanicsCoachResponse(text.Trim(), "GEMINI_AI");
        }

        var fallback =
            $"Evaluasi {m.Title}:\n" +
            $"• Target Parameter: {m.FocusMetricLabel} di rentang optimal {m.OptimalTargetRange} (cadence rujukan {m.TargetCadenceSpm} spm, lean {m.TargetTrunkLeanDeg}°).\n" +
            $"• Koreksi Kesalahan Umum: Hindari '{m.CommonFault}'. Gunakan cue teknis: {m.BiomechanicalCue}\n" +
            $"• Drill Prioritas: {string.Join(" | ", m.Drills)}.";

        return new BiomechanicsCoachResponse(fallback, "BIOMECHANICS_ENGINE");
    }

    private ExtractedStravaMetricsDto BuildPresetSample(int index)
    {
        return (Math.Abs(index) % 3) switch
        {
            0 => new(
                "Morning Carbon Tempo 10K", 10.0, 2360, 236, 172, 184, 64,
                "KM 1: 4:02/km | KM 2: 3:59/km | KM 4: 3:56/km | KM 6: 3:55/km | KM 8: 3:53/km | KM 10: 3:50/km",
                "Pacing negative split sangat matang (+12 detik lebih cepat di KM 10 vs KM 1). Pertahankan cadence 184 spm di sesi threshold berikutnya.",
                null, string.Empty, "SAMPLE_PRESET"),
            1 => new(
                "Half Marathon Progressive Long Run", 21.1, 5380, 255, 161, 180, 142,
                "KM 1: 4:22/km | KM 5: 4:20/km | KM 10: 4:18/km | KM 15: 4:14/km | KM 18: 4:10/km | KM 21: 4:03/km",
                "Daya tahan aerobik solid pada 21.1 km dengan HR rata-rata 161 bpm. Pastikan 48 jam ke depan diisi Easy Recovery Run Zona 1-2.",
                null, string.Empty, "SAMPLE_PRESET"),
            _ => new(
                "Evening Track 5K Speed Trial", 5.0, 1105, 221, 181, 190, 12,
                "KM 1: 3:43/km | KM 2: 3:42/km | KM 3: 3:42/km | KM 4: 3:40/km | KM 5: 3:38/km",
                "Effort VO2Max puncak pada cadence tinggi 190 spm dengan konsistensi split ketat. Sangat baik untuk mendorong kenaikan estimasi VDOT.",
                null, string.Empty, "SAMPLE_PRESET")
        };
    }

    private static (double DistanceKm, long DurationSeconds, long PaceSeconds) ReconcileDistanceAndPace(
        double rawDistKm,
        long durSec,
        long paceSec)
    {
        var safeDur = durSec > 0 ? durSec : 1800L;
        var calcFromDurPace = (durSec > 0 && paceSec > 0)
            ? Math.Round((double)durSec / paceSec, 2)
            : 0.0;

        // Enforce StrideApex Splits rule: if distance missing or falsely picked split-1 (<= 1.05 km) while duration/pace yields >= 1.5 km
        double finalDist;
        if (calcFromDurPace > 0.0 && (rawDistKm <= 0.0 || (rawDistKm <= 1.05 && calcFromDurPace >= 1.5)))
        {
            finalDist = calcFromDurPace;
        }
        else if (rawDistKm > 0.0)
        {
            finalDist = Math.Round(rawDistKm, 2);
        }
        else if (calcFromDurPace > 0.0)
        {
            finalDist = calcFromDurPace;
        }
        else
        {
            finalDist = 5.0;
        }

        finalDist = Math.Clamp(finalDist, 0.1, 500.0);
        var finalPace = paceSec > 0
            ? Math.Clamp(paceSec, 60L, 3600L)
            : Math.Clamp((long)Math.Round(safeDur / finalDist), 60L, 3600L);

        var finalDur = durSec > 0
            ? Math.Clamp(durSec, 1L, 864000L)
            : Math.Clamp((long)Math.Round(finalDist * finalPace), 1L, 864000L);

        return (finalDist, finalDur, finalPace);
    }

    private async Task<string?> CallGeminiTextAsync(string apiKey, string prompt, CancellationToken ct)
    {
        var payload = new
        {
            contents = new object[]
            {
                new { parts = new object[] { new { text = prompt } } }
            }
        };
        return await CallGeminiRawAsync(apiKey, payload, ct);
    }

    private async Task<string?> CallGeminiRawAsync(string apiKey, object payload, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(25);
            var model = ResolveModel();
            var url = apiKey.StartsWith("AQ.", StringComparison.Ordinal)
                ? $"https://aiplatform.googleapis.com/v1/publishers/google/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}"
                : $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";

            using var response = await client.PostAsJsonAsync(url, payload, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini API returned status {StatusCode}", response.StatusCode);
                return null;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var content = candidates[0].GetProperty("content");
                if (content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    return parts[0].GetProperty("text").GetString();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Gemini API invocation failed, using deterministic fallback.");
        }

        return null;
    }

    private static string StripDataUrlPrefix(string input)
    {
        var commaIdx = input.IndexOf(',');
        return input.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIdx >= 0
            ? input[(commaIdx + 1)..]
            : input;
    }

    private static string CleanJsonFence(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```", StringComparison.Ordinal)) trimmed = trimmed[..^3];
        }
        return trimmed.Trim();
    }

    private static double GetDouble(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;

    private static long GetLong(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? (v.TryGetInt64(out var l) ? l : (long)Math.Round(v.GetDouble()))
            : 0L;

    private static string GetString(JsonElement el, string prop, string fallback = "") =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? fallback) : fallback;
}
