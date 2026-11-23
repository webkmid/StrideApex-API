using System.Globalization;
using StrideApex.Api.Models;

namespace StrideApex.Api.Services;

/// <summary>
/// Web-exclusive deep sports-science engine combining StrideApex domain engines
/// with CTL/ATL/TSB Fitness-Fatigue modeling, ACWR injury risk, 80/20 polarization,
/// Aerobic Efficiency Factor (EF), Stride Length, Streaks, Heatmaps, and Race Readiness.
/// </summary>
public sealed class DeepAnalyticsService(
    SplitParserService splitParser,
    RacePredictionService raceService,
    ShoeUnlockService shoeService,
    WeeklyProgressService weeklyService,
    GoalTrainingPlanService goalPlanService)
{
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");

    public ComprehensiveAnalyticsResponse ComputeComprehensive(ComprehensiveAnalyticsRequest request)
    {
        var nowMs = request.NowEpochMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var workouts = (request.Workouts ?? [])
            .OrderByDescending(w => w.CreatedAtEpochMs)
            .ToList();
        var prs = (request.Prs ?? []).ToList();
        var goals = (request.Goals ?? []).ToList();

        // 1. Base Android-aligned predictions, zones, shoe unlock, weekly progress, goals
        var predictions = raceService.ComputeRacePredictions(workouts, prs, request.FatigueExponent);
        var tenKPred = predictions.FirstOrDefault(p => p.CategoryKey == "10K");
        var tenKPace = tenKPred?.TargetPaceSecondsPerKm ?? 0L;
        var currentVdot = tenKPred?.VdotScore ?? 0.0;
        var tPace = tenKPace > 0 ? tenKPace * 1.03 : 330.0;

        var zones = tenKPace > 0
            ? raceService.ComputeTrainingPaceZones(tenKPace)
            : [];
        var shoeProgress = shoeService.EvaluateProgress(workouts, nowMs);
        var activeGoal = goals.FirstOrDefault(g => !g.Completed) ?? goals.FirstOrDefault();
        var weeklyProgress = weeklyService.ComputeWeeklyProgress(workouts, activeGoal, nowMs);
        var goalSchedules = goals
            .Select(g => goalPlanService.BuildGoalScheduleWithSummary(g, workouts, nowMs))
            .ToList();

        // 2. Per-workout deep metrics & rTSS
        var deepMap = new Dictionary<string, WorkoutDeepMetricsDto>(StringComparer.Ordinal);
        foreach (var w in workouts)
        {
            deepMap[w.Id] = AnalyzeSingleWorkout(w, tPace, request.MaxHr, request.RestingHr);
        }

        // 3. Overview KPIs + 30-day VDOT delta + Streak
        var overview = BuildOverview(workouts, prs, currentVdot, nowMs);

        // 4. 90-day CTL/ATL/TSB + ACWR
        var fitnessSeries = BuildFitnessLoadSeries(workouts, deepMap, nowMs);
        var workloadStatus = ComputeWorkloadStatus(fitnessSeries);

        // 5. 80/20 Polarized Training Distribution
        var polarized = ComputePolarizedDistribution(workouts, deepMap);

        // 6. Aerobic Efficiency Factor Trend (chronological)
        var efficiencyTrend = BuildEfficiencyTrend(workouts, deepMap, nowMs);

        // 7. Pacing Consistency summary
        var pacingConsistency = ComputePacingConsistency(workouts, deepMap);

        // 8. 6-Month Monthly Trends & 112-Day Heatmap
        var monthlyTrends = BuildMonthlyTrends(workouts, deepMap, nowMs);
        var heatmap = BuildActivityHeatmap(workouts, nowMs);

        // 9. Cadence & Stride Diagnostics
        var cadenceDiag = BuildCadenceDiagnostics(workouts, overview.AvgPaceSec);

        // 10. Race Readiness Score
        var readiness = ComputeRaceReadiness(
            workouts,
            activeGoal,
            weeklyProgress,
            workloadStatus,
            predictions,
            nowMs
        );

        return new ComprehensiveAnalyticsResponse(
            Overview: overview,
            RacePredictions: predictions,
            PaceZones: zones,
            ShoeUnlock: shoeProgress,
            WeeklyProgress: weeklyProgress,
            GoalSchedules: goalSchedules,
            FitnessLoadSeries: fitnessSeries,
            WorkloadStatus: workloadStatus,
            PolarizedDistribution: polarized,
            EfficiencyTrend: efficiencyTrend,
            PacingConsistency: pacingConsistency,
            RaceReadiness: readiness,
            MonthlyTrends: monthlyTrends,
            ActivityHeatmap: heatmap,
            CadenceDiagnostics: cadenceDiag,
            WorkoutDeepMetrics: deepMap
        );
    }

    public WorkoutDeepMetricsDto AnalyzeSingleWorkout(
        WorkoutLogDto workout,
        double thresholdPaceSec = 300.0,
        int maxHr = 190,
        int restingHr = 55)
    {
        var safePace = workout.AvgPaceSecondsPerKm > 0
            ? workout.AvgPaceSecondsPerKm
            : workout.DistanceKm > 0.0
                ? Math.Clamp((long)Math.Round(workout.DurationSeconds / workout.DistanceKm), 60L, 3600L)
                : 360L;

        var splitAnalysis = splitParser.AnalyzeSplits(workout.SplitsSummary, safePace);
        var (zoneCode, zoneName) = ClassifyPaceZone(safePace, thresholdPaceSec);
        var rtss = ComputeWorkoutRtss(workout, safePace, thresholdPaceSec, maxHr, restingHr);

        var speedMetersPerMin = workout.DurationSeconds > 0 && workout.DistanceKm > 0
            ? (workout.DistanceKm * 1000.0) / (workout.DurationSeconds / 60.0)
            : 0.0;

        var ef = workout.AvgHeartRate > 0 && speedMetersPerMin > 0
            ? Math.Round(speedMetersPerMin / workout.AvgHeartRate, 2)
            : 0.0;

        var strideLengthM = workout.CadenceSpm > 0 && speedMetersPerMin > 0
            ? Math.Round(speedMetersPerMin / workout.CadenceSpm, 2)
            : 0.0;

        var vdot = workout.DistanceKm >= 0.8 && workout.DurationSeconds >= 60
            ? Math.Round(raceService.CalculateVdot(workout.DistanceKm * 1000.0, workout.DurationSeconds), 1)
            : 0.0;

        return new WorkoutDeepMetricsDto(
            WorkoutId: workout.Id,
            Splits: splitAnalysis.Splits,
            SplitCvPct: splitAnalysis.SplitCvPct,
            FirstHalfAvgPaceSec: splitAnalysis.FirstHalfAvgPaceSec,
            SecondHalfAvgPaceSec: splitAnalysis.SecondHalfAvgPaceSec,
            SplitDeltaSec: splitAnalysis.SplitDeltaSec,
            SplitStrategy: splitAnalysis.SplitStrategy,
            FastestSplit: splitAnalysis.FastestSplit,
            SlowestSplit: splitAnalysis.SlowestSplit,
            EstimatedRtss: rtss,
            AerobicEfficiency: ef,
            StrideLengthM: strideLengthM,
            Vdot: vdot,
            ZoneCode: zoneCode,
            ZoneName: zoneName,
            PaceSpreadSec: splitAnalysis.PaceSpreadSec
        );
    }

    private OverviewSummaryDto BuildOverview(
        IReadOnlyList<WorkoutLogDto> workouts,
        IReadOnlyList<PersonalRecordDto> prs,
        double currentVdot,
        long nowMs)
    {
        var totalDist = workouts.Sum(w => w.DistanceKm);
        var totalDur = workouts.Sum(w => w.DurationSeconds);
        var totalElev = workouts.Sum(w => w.ElevationGainM);
        var avgPace = totalDist > 0.0 ? (long)Math.Round(totalDur / totalDist) : 0L;

        var hrRuns = workouts.Where(w => w.AvgHeartRate > 0).ToList();
        var avgHr = hrRuns.Count > 0 ? (long)Math.Round(hrRuns.Average(w => w.AvgHeartRate)) : 0L;

        var cadRuns = workouts.Where(w => w.CadenceSpm > 0).ToList();
        var avgCad = cadRuns.Count > 0 ? (long)Math.Round(cadRuns.Average(w => w.CadenceSpm)) : 0L;

        // 30-day VDOT delta
        var cutoff30d = nowMs - 30L * 86_400_000L;
        var olderWorkouts = workouts.Where(w => w.CreatedAtEpochMs < cutoff30d && w.DistanceKm >= 3.0).ToList();
        var olderPreds = olderWorkouts.Count > 0
            ? raceService.ComputeRacePredictions(olderWorkouts, prs)
            : null;
        var olderVdot = olderPreds?.FirstOrDefault()?.VdotScore ?? currentVdot;
        var vdotDelta = workouts.Count > 0 ? Math.Round(currentVdot - olderVdot, 1) : 0.0;

        var (currentStreak, longestStreak) = ComputeStreaks(workouts, nowMs);
        var calories = (long)Math.Round(totalDist * 65.0 + totalElev * 0.25);
        var longestRun = workouts.Count > 0 ? Math.Round(workouts.Max(w => w.DistanceKm), 2) : 0.0;
        var validPaceRuns = workouts.Where(w => w.DistanceKm >= 0.5 && w.AvgPaceSecondsPerKm > 0).ToList();
        var fastestPace = validPaceRuns.Count > 0 ? validPaceRuns.Min(w => w.AvgPaceSecondsPerKm) : 0L;

        return new OverviewSummaryDto(
            TotalWorkouts: workouts.Count,
            TotalDistanceKm: Math.Round(totalDist, 2),
            TotalDurationSeconds: totalDur,
            TotalElevationM: totalElev,
            AvgPaceSec: avgPace,
            AvgHeartRate: avgHr,
            AvgCadenceSpm: avgCad,
            CurrentVdot: Math.Round(currentVdot, 1),
            VdotDelta30d: vdotDelta,
            CurrentStreakDays: currentStreak,
            LongestStreakDays: longestStreak,
            EstimatedCaloriesKcal: calories,
            LongestRunKm: longestRun,
            FastestPaceSec: fastestPace
        );
    }

    private static (int CurrentStreak, int LongestStreak) ComputeStreaks(IReadOnlyList<WorkoutLogDto> workouts, long nowMs)
    {
        if (workouts.Count == 0) return (0, 0);

        var runDates = workouts
            .Where(w => w.CreatedAtEpochMs > 0)
            .Select(w => DateTimeOffset.FromUnixTimeMilliseconds(w.CreatedAtEpochMs).ToLocalTime().Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (runDates.Count == 0) return (0, 0);

        var longest = 1;
        var currentRun = 1;
        for (var i = 1; i < runDates.Count; i++)
        {
            if ((runDates[i] - runDates[i - 1]).Days == 1)
            {
                currentRun++;
                if (currentRun > longest) longest = currentRun;
            }
            else
            {
                currentRun = 1;
            }
        }

        var today = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToLocalTime().Date;
        var set = runDates.ToHashSet();
        var active = 0;
        var cursor = set.Contains(today) ? today : today.AddDays(-1);
        while (set.Contains(cursor))
        {
            active++;
            cursor = cursor.AddDays(-1);
        }

        return (active, longest);
    }

    private static double ComputeWorkoutRtss(
        WorkoutLogDto workout,
        long safePaceSec,
        double thresholdPaceSec,
        int maxHr,
        int restingHr)
    {
        var durationHours = Math.Max(0.01, workout.DurationSeconds / 3600.0);
        var paceRatio = Math.Clamp(thresholdPaceSec / Math.Max(120.0, safePaceSec), 0.55, 1.35);
        var paceRtss = durationHours * Math.Pow(paceRatio, 2.0) * 100.0 + workout.ElevationGainM * 0.04;

        if (workout.AvgHeartRate > restingHr && maxHr > restingHr + 20)
        {
            var hrReserve = Math.Clamp((double)(workout.AvgHeartRate - restingHr) / (maxHr - restingHr), 0.25, 1.05);
            var hrTrimp = (workout.DurationSeconds / 60.0) * hrReserve * 0.64 * Math.Exp(1.92 * hrReserve);
            return Math.Round((paceRtss * 0.6 + hrTrimp * 0.4), 1);
        }

        return Math.Round(paceRtss, 1);
    }

    private static (string Code, string Name) ClassifyPaceZone(long paceSec, double tPace)
    {
        if (paceSec >= tPace * 1.135) return ("E", "Zone 1-2 · Easy / Aerobic");
        if (paceSec >= tPace * 1.035) return ("M", "Zone 3 · Marathon / Steady");
        if (paceSec >= tPace * 0.96) return ("T", "Zone 4 · Threshold");
        if (paceSec >= tPace * 0.895) return ("I", "Zone 5A · VO2 Max");
        return ("R", "Zone 5B · Repetition");
    }

    private static IReadOnlyList<DailyFitnessPointDto> BuildFitnessLoadSeries(
        IReadOnlyList<WorkoutLogDto> workouts,
        IReadOnlyDictionary<string, WorkoutDeepMetricsDto> deepMap,
        long nowMs)
    {
        var today = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToLocalTime().Date;
        var dailyAgg = new Dictionary<DateTime, (double Load, double DistKm)>();

        foreach (var w in workouts)
        {
            var dt = w.CreatedAtEpochMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(w.CreatedAtEpochMs).ToLocalTime().Date
                : today;
            var load = deepMap.TryGetValue(w.Id, out var m) ? m.EstimatedRtss : 35.0;
            if (dailyAgg.TryGetValue(dt, out var existing))
                dailyAgg[dt] = (existing.Load + load, existing.DistKm + w.DistanceKm);
            else
                dailyAgg[dt] = (load, w.DistanceKm);
        }

        // Warm up EWMA for 120 days prior to today so CTL starts realistically, then emit last 90 days
        const int warmupDays = 120;
        const int outputDays = 90;
        var startDay = today.AddDays(-(warmupDays - 1));

        var ctl = 0.0;
        var atl = 0.0;
        var alphaCtl = 2.0 / (42.0 + 1.0);
        var alphaAtl = 2.0 / (7.0 + 1.0);

        var output = new List<DailyFitnessPointDto>(outputDays);
        var outputStart = today.AddDays(-(outputDays - 1));

        for (var i = 0; i < warmupDays; i++)
        {
            var d = startDay.AddDays(i);
            dailyAgg.TryGetValue(d, out var dayData);
            var tsb = ctl - atl; // Banister form prior to today's stress
            ctl = ctl + (dayData.Load - ctl) * alphaCtl;
            atl = atl + (dayData.Load - atl) * alphaAtl;

            if (d >= outputStart)
            {
                output.Add(new DailyFitnessPointDto(
                    DateKey: d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DailyLoad: Math.Round(dayData.Load, 1),
                    CtlFitness: Math.Round(ctl, 1),
                    AtlFatigue: Math.Round(atl, 1),
                    TsbForm: Math.Round(tsb, 1),
                    DistanceKm: Math.Round(dayData.DistKm, 2)
                ));
            }
        }

        return output;
    }

    private static WorkloadStatusDto ComputeWorkloadStatus(IReadOnlyList<DailyFitnessPointDto> series)
    {
        if (series.Count == 0)
        {
            return new WorkloadStatusDto(1.0, 0, 0, "UNDERTRAINED", "Belum Ada Data",
                "Mulai unggah sesi lari Anda untuk menghitung Acute:Chronic Workload Ratio (ACWR).");
        }

        var last7 = series.TakeLast(7).Sum(p => p.DailyLoad);
        var last28 = series.TakeLast(28).Sum(p => p.DailyLoad);
        var chronicWeeklyAvg = last28 / 4.0;
        var acwr = chronicWeeklyAvg >= 5.0
            ? Math.Round(last7 / chronicWeeklyAvg, 2)
            : (last7 > 0 ? 1.15 : 0.0);

        var (zone, label, rec) = acwr switch
        {
            < 0.8 => ("UNDERTRAINED", "Detraining / Pemulihan Ringan",
                "Beban 7 hari terakhir lebih rendah dari rata-rata 4 minggu. Aman menambah porsi volume aerobik secara bertahap (+10%/minggu)."),
            <= 1.3 => ("SWEET_SPOT", "Zona Optimal (Sweet Spot)",
                "Rasio beban akut terhadap kronis ideal (0.8–1.3). Adaptasi kebugaran maksimal dengan risiko cedera minimal."),
            <= 1.5 => ("CAUTION", "Zona Waspada (Overreaching)",
                "Lonjakan beban mingguan mulai tinggi (1.3–1.5). Prioritaskan tidur, hidrasi, dan ubah 1 sesi kualitas menjadi Easy Run."),
            _ => ("HIGH_RISK", "Risiko Cedera Tinggi (> 1.5)",
                "Lonjakan beban ekstrem dibanding baseline 4 minggu. Disarankan mengambil 1–2 hari recovery aktif/istirahat untuk mencegah overuse injury.")
        };

        return new WorkloadStatusDto(
            Acwr: acwr,
            Acute7dLoad: Math.Round(last7, 1),
            Chronic28dWeeklyAvgLoad: Math.Round(chronicWeeklyAvg, 1),
            Zone: zone,
            Label: label,
            Recommendation: rec
        );
    }

    private static PolarizedDistributionDto ComputePolarizedDistribution(
        IReadOnlyList<WorkoutLogDto> workouts,
        IReadOnlyDictionary<string, WorkoutDeepMetricsDto> deepMap)
    {
        var easyKm = 0.0;
        var moderateKm = 0.0;
        var hardKm = 0.0;

        foreach (var w in workouts)
        {
            var code = deepMap.TryGetValue(w.Id, out var m) ? m.ZoneCode : "E";
            switch (code)
            {
                case "E": easyKm += w.DistanceKm; break;
                case "M": moderateKm += w.DistanceKm; break;
                default: hardKm += w.DistanceKm; break;
            }
        }

        var total = easyKm + moderateKm + hardKm;
        if (total <= 0.01)
        {
            return new PolarizedDistributionDto(0, 0, 0, 0, 0, 0, 0,
                "Belum ada data volume untuk analisis distribusi intensitas 80/20.");
        }

        var easyPct = Math.Round(easyKm * 100.0 / total, 1);
        var modPct = Math.Round(moderateKm * 100.0 / total, 1);
        var hardPct = Math.Round(Math.Max(0.0, 100.0 - easyPct - modPct), 1);

        // Ideal 80/20 benchmark: ~80% Easy+LowMod, <=10% Grey-Zone, ~15-20% Threshold/VO2
        var penalty = Math.Abs(80.0 - easyPct) * 0.7 + modPct * 0.4 + Math.Abs(20.0 - hardPct) * 0.5;
        var score = (int)Math.Clamp(Math.Round(100.0 - penalty), 20, 100);

        var note = easyPct < 60.0
            ? "Terlalu banyak volume intensitas menengah/tinggi (" + (modPct + hardPct).ToString("F0", CultureInfo.InvariantCulture) + "%). Tambahkan lebih banyak Easy Run Zona 1-2 agar pemulihan aerobik optimal."
            : modPct > 30.0
                ? "Banyak sesi terjebak di 'grey zone' Marathon Pace. Pertajam polarisasi: perlambat Easy Run dan fokuskan kualitas di sesi Tempo/Interval."
                : "Distribusi intensitas 80/20 sangat sehat! Fondasi aerobik kuat dengan stimulus kecepatan yang terkontrol.";

        return new PolarizedDistributionDto(
            EasyPct: easyPct,
            ModeratePct: modPct,
            HardPct: hardPct,
            EasyKm: Math.Round(easyKm, 2),
            ModerateKm: Math.Round(moderateKm, 2),
            HardKm: Math.Round(hardKm, 2),
            AlignmentScore: score,
            CoachingNote: note
        );
    }

    private static IReadOnlyList<EfficiencyPointDto> BuildEfficiencyTrend(
        IReadOnlyList<WorkoutLogDto> workouts,
        IReadOnlyDictionary<string, WorkoutDeepMetricsDto> deepMap,
        long nowMs)
    {
        return workouts
            .OrderBy(w => w.CreatedAtEpochMs)
            .Select(w =>
            {
                deepMap.TryGetValue(w.Id, out var m);
                return new EfficiencyPointDto(
                    WorkoutId: w.Id,
                    DateKey: GoalTrainingPlanService.WorkoutDateKey(w, nowMs),
                    Title: w.Title,
                    DistanceKm: Math.Round(w.DistanceKm, 2),
                    AvgPaceSec: w.AvgPaceSecondsPerKm,
                    AvgHeartRate: w.AvgHeartRate,
                    EfficiencyFactor: m?.AerobicEfficiency ?? 0.0,
                    Vdot: m?.Vdot ?? 0.0,
                    CadenceSpm: w.CadenceSpm,
                    StrideLengthM: m?.StrideLengthM ?? 0.0
                );
            })
            .ToList();
    }

    private static PacingConsistencyDto ComputePacingConsistency(
        IReadOnlyList<WorkoutLogDto> workouts,
        IReadOnlyDictionary<string, WorkoutDeepMetricsDto> deepMap)
    {
        var splitRuns = workouts
            .Select(w => (Workout: w, Deep: deepMap[w.Id]))
            .Where(x => x.Deep.Splits.Count >= 2)
            .ToList();

        if (splitRuns.Count == 0)
        {
            return new PacingConsistencyDto(
                0.0, 0, 0, 0, "N/A", "-",
                "Unggah screenshot splits atau simpan detail per-KM untuk membuka analisis konsistensi pacing.");
        }

        var avgCv = Math.Round(splitRuns.Average(x => x.Deep.SplitCvPct), 2);
        var negCount = splitRuns.Count(x => x.Deep.SplitStrategy == "Negative Split");
        var evenCount = splitRuns.Count(x => x.Deep.SplitStrategy == "Even Split");
        var posCount = splitRuns.Count(x => x.Deep.SplitStrategy == "Positive Split");
        var best = splitRuns.MinBy(x => x.Deep.SplitCvPct).Workout.Title;

        var grade = avgCv switch
        {
            <= 2.2 => "A+",
            <= 3.8 => "A",
            <= 5.5 => "B",
            <= 8.0 => "C",
            _ => "D"
        };

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Rata-rata variabilitas split (CV) {avgCv:F1}% di {splitRuns.Count} sesi ({negCount} negative split, {evenCount} even split)."
        );

        return new PacingConsistencyDto(avgCv, negCount, evenCount, posCount, grade, best, summary);
    }

    private IReadOnlyList<MonthlyTrendDto> BuildMonthlyTrends(
        IReadOnlyList<WorkoutLogDto> workouts,
        IReadOnlyDictionary<string, WorkoutDeepMetricsDto> deepMap,
        long nowMs)
    {
        var nowDate = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToLocalTime().Date;
        var startMonth = new DateTime(nowDate.Year, nowDate.Month, 1).AddMonths(-5);
        var list = new List<MonthlyTrendDto>(6);

        for (var i = 0; i < 6; i++)
        {
            var mStart = startMonth.AddMonths(i);
            var mEnd = mStart.AddMonths(1);
            var mRuns = workouts.Where(w =>
            {
                var d = w.CreatedAtEpochMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(w.CreatedAtEpochMs).ToLocalTime().Date
                    : nowDate;
                return d >= mStart && d < mEnd;
            }).ToList();

            var dist = mRuns.Sum(w => w.DistanceKm);
            var dur = mRuns.Sum(w => w.DurationSeconds);
            var elev = mRuns.Sum(w => w.ElevationGainM);
            var pace = dist > 0 ? (long)Math.Round(dur / dist) : 0L;
            var totalLoad = mRuns.Sum(w => deepMap.TryGetValue(w.Id, out var m) ? m.EstimatedRtss : 0.0);

            list.Add(new MonthlyTrendDto(
                MonthKey: mStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                MonthLabel: mStart.ToString("MMM yy", Indonesian),
                RunsCount: mRuns.Count,
                TotalDistanceKm: Math.Round(dist, 1),
                AvgPaceSec: pace,
                ElevationGainM: elev,
                AvgLoad: Math.Round(totalLoad, 1)
            ));
        }

        return list;
    }

    private static IReadOnlyList<HeatmapDayDto> BuildActivityHeatmap(IReadOnlyList<WorkoutLogDto> workouts, long nowMs)
    {
        var today = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToLocalTime().Date;
        const int totalDays = 112; // 16 weeks
        var start = today.AddDays(-(totalDays - 1));

        var byDay = workouts
            .Where(w => w.CreatedAtEpochMs > 0)
            .GroupBy(w => DateTimeOffset.FromUnixTimeMilliseconds(w.CreatedAtEpochMs).ToLocalTime().Date)
            .ToDictionary(g => g.Key, g => (Dist: g.Sum(w => w.DistanceKm), Count: g.Count()));

        var result = new List<HeatmapDayDto>(totalDays);
        for (var i = 0; i < totalDays; i++)
        {
            var d = start.AddDays(i);
            byDay.TryGetValue(d, out var data);
            var level = data.Dist switch
            {
                <= 0.0 => 0,
                < 5.0 => 1,
                < 10.0 => 2,
                < 16.0 => 3,
                _ => 4
            };
            result.Add(new HeatmapDayDto(
                DateKey: d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DistanceKm: Math.Round(data.Dist, 2),
                RunsCount: data.Count,
                IntensityLevel: level
            ));
        }

        return result;
    }

    private static CadenceDiagnosticsDto BuildCadenceDiagnostics(
        IReadOnlyList<WorkoutLogDto> workouts,
        long overallAvgPaceSec)
    {
        var cadRuns = workouts.Where(w => w.CadenceSpm > 0).ToList();
        if (cadRuns.Count == 0)
        {
            return new CadenceDiagnosticsDto(
                AvgCadenceSpm: 0,
                EstimatedAvgStrideLengthM: 0.0,
                OptimalRange: "176 - 186 spm",
                Status: "BELUM ADA DATA",
                RecommendedModuleId: "cadence_recoil",
                CoachingNote: "Catat cadence (spm) pada sesi lari Anda untuk mengevaluasi ground contact time dan risiko overstriding."
            );
        }

        var avgCad = (long)Math.Round(cadRuns.Average(w => w.CadenceSpm));
        var speedMPerMin = overallAvgPaceSec > 0 ? 1000.0 / (overallAvgPaceSec / 60.0) : 180.0;
        var avgStride = Math.Round(speedMPerMin / avgCad, 2);

        if (avgCad < 170)
        {
            return new CadenceDiagnosticsDto(
                avgCad, avgStride, "176 - 186 spm", "PERLU NAIK (+5% SPM)", "midfoot_strike",
                $"Cadence rata-rata Anda {avgCad} spm relatif rendah dan rentan overstriding. Latih drill midfoot strike & tingkatkan langkah bertahap +4–6 spm.");
        }

        if (avgCad < 176)
        {
            return new CadenceDiagnosticsDto(
                avgCad, avgStride, "176 - 186 spm", "CUKUP BAIK", "cadence_recoil",
                $"Cadence {avgCad} spm sudah mendekati zona efisien. Gunakan drill ankling & metronome 180 spm saat pemanasan untuk recoil tendon Achilles lebih cepat.");
        }

        return new CadenceDiagnosticsDto(
            avgCad, avgStride, "176 - 186 spm", "OPTIMAL", "pelvic_neutrality",
            $"Cadence {avgCad} spm dengan estimasi panjang langkah {avgStride:F2} m berada di zona elit! Fokus pada stabilitas panggul & triple extension.");
    }

    private RaceReadinessDto ComputeRaceReadiness(
        IReadOnlyList<WorkoutLogDto> workouts,
        TrainingGoalDto? activeGoal,
        WeeklyAnalyticsDto weeklyProgress,
        WorkloadStatusDto workload,
        IReadOnlyList<RacePredictionDto> predictions,
        long nowMs)
    {
        if (workouts.Count == 0)
        {
            return new RaceReadinessDto(
                Score: 0,
                Grade: "-",
                GoalTitle: activeGoal != null ? goalPlanService.StripScheduleSuffix(activeGoal.Title) : "Belum Ada Target",
                TargetDistanceLabel: activeGoal?.TargetDistanceCategory ?? "-",
                VolumeReadinessPct: 0,
                VdotGapSecPerKm: 0,
                AcwrSafe: true,
                LongRunCoveragePct: 0,
                Bottleneck: "Belum ada sesi lari tercatat",
                ActionItems: ["Scan screenshot Strava atau input lari pertama Anda untuk membuka skor kesiapan lomba."]
            );
        }

        var cat = DistanceCategoryHelper.FromKey(activeGoal?.TargetDistanceCategory ?? "10K");
        var goalTitle = activeGoal != null ? goalPlanService.StripScheduleSuffix(activeGoal.Title) : $"Target {cat.ShortLabel}";
        var pred = predictions.FirstOrDefault(p => p.CategoryKey == cat.Key) ?? predictions[1];

        var goalPace = activeGoal != null && activeGoal.TargetTimeSeconds > 0
            ? (long)Math.Round(activeGoal.TargetTimeSeconds / cat.Kilometers)
            : pred.TargetPaceSecondsPerKm;

        var volPct = Math.Clamp(Math.Round((weeklyProgress.TotalDistanceKm / Math.Max(1.0, weeklyProgress.TargetDistanceKm)) * 100.0, 1), 0.0, 120.0);
        var volComponent = Math.Min(30.0, (volPct / 100.0) * 30.0);

        var paceGap = pred.TargetPaceSecondsPerKm - goalPace; // <= 0 means already faster than goal!
        var vdotScorePart = paceGap <= 0
            ? 35.0
            : Math.Max(5.0, 35.0 - paceGap * 0.45);

        var acwrSafe = workload.Acwr is >= 0.75 and <= 1.35;
        var acwrScorePart = workload.Acwr switch
        {
            >= 0.8 and <= 1.3 => 20.0,
            >= 0.6 and <= 1.45 => 14.0,
            _ => 8.0
        };

        var cutoff28d = nowMs - 28L * 86_400_000L;
        var recentRuns = workouts.Where(w => w.CreatedAtEpochMs >= cutoff28d).ToList();
        var longestRecent = recentRuns.Count > 0 ? recentRuns.Max(w => w.DistanceKm) : workouts.Max(w => w.DistanceKm);
        var recommendedLongKm = cat.Key switch
        {
            "5K" => 8.0,
            "10K" => 12.0,
            "HALF_MARATHON" => 18.0,
            _ => 30.0
        };
        var longPct = Math.Clamp(Math.Round((longestRecent / recommendedLongKm) * 100.0, 1), 0.0, 120.0);
        var longScorePart = Math.Min(15.0, (longPct / 100.0) * 15.0);

        var totalScore = (int)Math.Clamp(Math.Round(volComponent + vdotScorePart + acwrScorePart + longScorePart), 0, 100);
        var grade = totalScore switch
        {
            >= 85 => "Siap Balap (Peak)",
            >= 70 => "Siap Kompetitif",
            >= 55 => "Dalam Pembangunan",
            _ => "Tahap Fondasi"
        };

        var actions = new List<string>();
        string bottleneck;
        if (paceGap > 10)
        {
            bottleneck = $"Pace prediksi VDOT masih terpaut +{paceGap}s/km dari target pace {raceService.FormatPace(goalPace)}";
            actions.Add($"Sisipkan 1 sesi Threshold/Interval per minggu untuk memangkas gap {paceGap}s/km.");
        }
        else if (longPct < 75.0)
        {
            bottleneck = $"Durabilitas Long Run baru {longPct:F0}% (terjauh 28 hari: {longestRecent:F1} km vs patokan {recommendedLongKm:F0} km)";
            actions.Add($"Naikkan jarak Long Run akhir pekan secara bertahap menuju {recommendedLongKm:F0} km.");
        }
        else if (!acwrSafe)
        {
            bottleneck = $"ACWR ({workload.Acwr:F2}) di luar Sweet Spot 0.8–1.3";
            actions.Add(workload.Recommendation);
        }
        else
        {
            bottleneck = "Tidak ada hambatan kritis — metrik volume, VDOT & beban seimbang.";
            actions.Add("Pertahankan konsistensi 80/20 dan mulai tapering 7 hari sebelum hari lomba.");
        }

        if (volPct < 75.0 && actions.Count < 2)
        {
            actions.Add($"Volume minggu ini {weeklyProgress.TotalDistanceKm:F1} / {weeklyProgress.TargetDistanceKm} km — tambah sesi easy santai.");
        }

        return new RaceReadinessDto(
            Score: totalScore,
            Grade: grade,
            GoalTitle: goalTitle,
            TargetDistanceLabel: cat.DisplayName,
            VolumeReadinessPct: volPct,
            VdotGapSecPerKm: paceGap,
            AcwrSafe: acwrSafe,
            LongRunCoveragePct: longPct,
            Bottleneck: bottleneck,
            ActionItems: actions
        );
    }
}
