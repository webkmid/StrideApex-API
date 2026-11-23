using System.Globalization;
using System.Text.RegularExpressions;
using StrideApex.Api.Models;

namespace StrideApex.Api.Services;

/// <summary>
/// Port of Android GoalTrainingPlanEngine & AdaptiveTrainingAi:
/// Compatible with Firestore "[start~target|days]" goal titles, tapering, and load adaptation.
/// </summary>
public sealed partial class GoalTrainingPlanService(RacePredictionService raceService)
{
    public const int MaxPlanDays = 370;

    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");

    [GeneratedRegex(@"\s*\[(?:(\d{4}-\d{2}-\d{2})~)?(\d{4}-\d{2}-\d{2})\|([0-9,]+)\]$")]
    private static partial Regex ScheduleSuffixRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DateKeyRegex();

    public string DayNameId(int isoDay) => isoDay switch
    {
        1 => "Senin",
        2 => "Selasa",
        3 => "Rabu",
        4 => "Kamis",
        5 => "Jumat",
        6 => "Sabtu",
        _ => "Minggu"
    };

    public string StripScheduleSuffix(string? title) =>
        ScheduleSuffixRegex().Replace(title ?? string.Empty, string.Empty).Trim();

    public string EncodeGoalTitleWithSchedule(
        string cleanTitle,
        string targetDateKey,
        IReadOnlyList<int> trainingDaysIso,
        string? startDateKey = null)
    {
        var sortedDays = trainingDaysIso.Where(d => d is >= 1 and <= 7).Distinct().OrderBy(d => d).ToList();
        if (sortedDays.Count == 0) sortedDays = [2, 4, 7];

        var safeTarget = IsValidDateKey(targetDateKey) ? targetDateKey : DefaultTargetDateKey(21);
        var safeStart = (!string.IsNullOrWhiteSpace(startDateKey) && IsValidDateKey(startDateKey) && string.CompareOrdinal(startDateKey, safeTarget) <= 0)
            ? startDateKey
            : null;

        var datePart = safeStart != null ? $"{safeStart}~{safeTarget}" : safeTarget;
        var suffix = $" [{datePart}|{string.Join(",", sortedDays)}]";
        var maxTitleLen = Math.Max(10, 120 - suffix.Length);
        var trimmed = StripScheduleSuffix(cleanTitle);
        if (trimmed.Length > maxTitleLen) trimmed = trimmed[..maxTitleLen];
        trimmed = trimmed.Trim();
        if (string.IsNullOrEmpty(trimmed)) trimmed = "Target Lari";
        return $"{trimmed}{suffix}";
    }

    public GoalScheduleMetadataDto ParseGoalMetadata(TrainingGoalDto goal, long? nowEpochMs = null)
    {
        var raw = (goal.Title ?? string.Empty).Trim();
        var match = ScheduleSuffixRegex().Match(raw);
        var todayKey = TodayKey(nowEpochMs);
        var createdKey = goal.CreatedAtEpochMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(goal.CreatedAtEpochMs).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : todayKey;

        string cleanTitle;
        string startKey;
        string targetKey;
        List<int> daysIso;

        if (match.Success)
        {
            cleanTitle = raw[..match.Index].Trim();
            if (string.IsNullOrEmpty(cleanTitle)) cleanTitle = "Target Lari";
            targetKey = match.Groups[2].Value;
            var groupStart = match.Groups[1].Value;
            startKey = !string.IsNullOrWhiteSpace(groupStart)
                ? groupStart
                : (string.CompareOrdinal(createdKey, targetKey) <= 0 ? createdKey : targetKey);
            daysIso = match.Groups[3].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var v) ? v : -1)
                .Where(v => v is >= 1 and <= 7)
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            if (daysIso.Count == 0) daysIso = [2, 4, 7];
        }
        else
        {
            cleanTitle = string.IsNullOrEmpty(raw) ? "Target Lari" : raw;
            startKey = createdKey;
            targetKey = AddDaysToKey(createdKey, 21);
            daysIso = [2, 4, 7];
        }

        var diffDays = (int)DaysBetween(todayKey, targetKey);
        return new GoalScheduleMetadataDto(
            CleanTitle: cleanTitle,
            StartDateKey: startKey,
            StartDateDisplay: FormatShortDate(startKey),
            TargetDateKey: targetKey,
            TargetDateDisplay: FormatShortDate(targetKey),
            DaysRemaining: diffDays,
            TrainingDaysIso: daysIso
        );
    }

    public GoalScheduleWithSummaryDto BuildGoalScheduleWithSummary(
        TrainingGoalDto goal,
        IReadOnlyList<WorkoutLogDto> workouts,
        long? nowEpochMs = null)
    {
        var meta = ParseGoalMetadata(goal, nowEpochMs);
        var sessions = GenerateScheduleForGoal(goal, workouts, nowEpochMs);
        var completed = sessions.Count(s => s.Status == "COMPLETED");
        var pending = sessions.Count(s => s.Status == "TODAY_PENDING");
        var skipped = sessions.Count(s => s.Status == "SKIPPED");
        var upcoming = sessions.Count(s => s.Status == "UPCOMING");
        var pastOrToday = completed + skipped + pending;
        var compliancePct = pastOrToday > 0 ? Math.Round(completed * 100.0 / pastOrToday, 1) : 100.0;

        return new GoalScheduleWithSummaryDto(
            GoalId: goal.Id,
            Goal: goal,
            Metadata: meta,
            CompletedSessions: completed,
            TodayPendingSessions: pending,
            SkippedSessions: skipped,
            UpcomingSessions: upcoming,
            CompliancePct: compliancePct,
            Sessions: sessions
        );
    }

    public IReadOnlyList<ScheduledSessionDto> GenerateScheduleForGoal(
        TrainingGoalDto goal,
        IReadOnlyList<WorkoutLogDto> workouts,
        long? nowEpochMs = null)
    {
        var meta = ParseGoalMetadata(goal, nowEpochMs);
        var category = DistanceCategoryHelper.FromKey(goal.TargetDistanceCategory);
        var goalRacePaceSec = Math.Clamp((long)Math.Round(goal.TargetTimeSeconds / category.Kilometers), 150L, 1200L);
        var todayKey = TodayKey(nowEpochMs);
        var startKey = meta.StartDateKey;
        var endKey = string.CompareOrdinal(meta.TargetDateKey, startKey) < 0 ? startKey : meta.TargetDateKey;

        if (DaysBetween(startKey, endKey) > MaxPlanDays)
            endKey = AddDaysToKey(startKey, MaxPlanDays);

        var now = nowEpochMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var workoutsByDate = workouts
            .GroupBy(w => WorkoutDateKey(w, now))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(w => w.DistanceKm).First());

        var slots = new List<(string DateKey, long EpochDay, int IsoDay, bool IsRaceDay)>();
        var cursorDate = ParseDateOnly(startKey) ?? DateTime.Today;
        var endDate = ParseDateOnly(endKey) ?? cursorDate.AddDays(21);
        for (var i = 0; i <= MaxPlanDays + 1 && cursorDate <= endDate; i++, cursorDate = cursorDate.AddDays(1))
        {
            var key = cursorDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var isoDay = cursorDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)cursorDate.DayOfWeek;
            var isRaceDay = key == meta.TargetDateKey;
            if (isRaceDay || meta.TrainingDaysIso.Contains(isoDay))
            {
                slots.Add((key, EpochDayOf(key), isoDay, isRaceDay));
            }
        }

        if (slots.Count == 0)
            return [];

        var avgSessionKm = Math.Clamp((double)goal.WeeklyMileageTargetKm / Math.Max(1, meta.TrainingDaysIso.Count), 3.0, 25.0);
        var currentRacePace = EstimateCurrentRacePace(workouts, category, goalRacePaceSec, now);
        var decisions = PlanSessionTypes(slots, workouts, category, meta.TrainingDaysIso, avgSessionKm);

        var startEpoch = EpochDayOf(startKey);
        var raceEpoch = EpochDayOf(meta.TargetDateKey);

        // Pass 1: Exact-date matches take priority.
        var matchedBySlotDate = new Dictionary<string, (WorkoutLogDto Workout, string? EarlyDateKey)>();
        var usedWorkoutDates = new HashSet<string>();
        foreach (var slot in slots)
        {
            if (workoutsByDate.TryGetValue(slot.DateKey, out var exact))
            {
                matchedBySlotDate[slot.DateKey] = (exact, null);
                usedWorkoutDates.Add(slot.DateKey);
            }
        }

        // Pass 2: Accelerated ("dipercepat") runs logged 1-2 days before an upcoming unmatched slot.
        foreach (var (wDate, workout) in workoutsByDate.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (usedWorkoutDates.Contains(wDate) ||
                string.CompareOrdinal(wDate, startKey) < 0 ||
                string.CompareOrdinal(wDate, endKey) > 0)
            {
                continue;
            }

            var nextSlot = slots.FirstOrDefault(slot =>
                !matchedBySlotDate.ContainsKey(slot.DateKey) &&
                string.CompareOrdinal(slot.DateKey, wDate) > 0 &&
                DaysBetween(wDate, slot.DateKey) is >= 1 and <= 2 &&
                (!slot.IsRaceDay || workout.DistanceKm >= category.Kilometers * 0.85));

            if (!string.IsNullOrEmpty(nextSlot.DateKey))
            {
                matchedBySlotDate[nextSlot.DateKey] = (workout, wDate);
                usedWorkoutDates.Add(wDate);
            }
        }

        return slots.Select((slot, idx) =>
        {
            var (wType, baseReason) = decisions[idx];
            var progress = raceEpoch > startEpoch
                ? Math.Clamp((double)(slot.EpochDay - startEpoch) / (raceEpoch - startEpoch), 0.0, 1.0)
                : 1.0;

            var basePace = currentRacePace <= goalRacePaceSec
                ? currentRacePace
                : (long)Math.Round(currentRacePace + (goalRacePaceSec - currentRacePace) * progress);

            var daysToRace = (int)(raceEpoch - slot.EpochDay);
            var (distKm, paceSec, title, desc) = BuildSessionPrescription(
                wType, category, basePace, goalRacePaceSec, avgSessionKm, daysToRace, meta.CleanTitle);

            matchedBySlotDate.TryGetValue(slot.DateKey, out var matchEntry);
            var matched = matchEntry.Workout;
            var reason = matchEntry.EarlyDateKey != null
                ? $"{baseReason} • Dipercepat: selesai pada {FormatShortDate(matchEntry.EarlyDateKey)}."
                : baseReason;
            var status = matched != null
                ? "COMPLETED"
                : slot.DateKey == todayKey
                    ? "TODAY_PENDING"
                    : string.CompareOrdinal(slot.DateKey, todayKey) < 0
                        ? "SKIPPED"
                        : "UPCOMING";

            var dayName = DayNameId(slot.IsoDay);
            var (displayName, shortBadge, zoneLabel) = WorkoutTypeLabels(wType);

            return new ScheduledSessionDto(
                DateKey: slot.DateKey,
                DisplayDate: $"{dayName}, {FormatShortDate(slot.DateKey)}",
                DayOfWeekIso: slot.IsoDay,
                DayName: dayName,
                WorkoutTypeCode: wType,
                WorkoutTypeDisplay: displayName,
                ShortBadge: shortBadge,
                ZoneLabel: zoneLabel,
                SessionTitle: title,
                SessionDescription: desc,
                RecommendedDistanceKm: Math.Round(distKm, 1),
                TargetPaceSecondsPerKm: paceSec,
                Status: status,
                MatchedWorkoutId: matched?.Id,
                MatchedWorkoutTitle: matched?.Title,
                AiReason: reason,
                DecisionSource: "ADAPTIVE_AI"
            );
        }).ToList();
    }

    private long EstimateCurrentRacePace(
        IReadOnlyList<WorkoutLogDto> workouts,
        DistanceCategoryInfo category,
        long goalRacePaceSec,
        long nowMs)
    {
        if (workouts.Count == 0)
            return Math.Clamp((long)Math.Round(goalRacePaceSec * 1.15), 150L, 1200L);

        var cutoff = nowMs - 60L * 86_400_000L;
        var recent = workouts.Where(w => w.CreatedAtEpochMs >= cutoff).ToList();
        if (recent.Count == 0) recent = workouts.ToList();

        var comparable = recent
            .Where(w => w.DistanceKm >= category.Kilometers * 0.4 && w.AvgPaceSecondsPerKm > 60)
            .ToList();
        if (comparable.Count > 0)
            return Math.Clamp(comparable.Min(w => w.AvgPaceSecondsPerKm), 150L, 1200L);

        var validPaces = recent.Select(w => w.AvgPaceSecondsPerKm).Where(p => p is >= 120 and <= 1200).ToList();
        return validPaces.Count > 0
            ? Math.Clamp((long)Math.Round(validPaces.Average()), 150L, 1200L)
            : Math.Clamp((long)Math.Round(goalRacePaceSec * 1.15), 150L, 1200L);
    }

    private List<(string Type, string Reason)> PlanSessionTypes(
        IReadOnlyList<(string DateKey, long EpochDay, int IsoDay, bool IsRaceDay)> slots,
        IReadOnlyList<WorkoutLogDto> workouts,
        DistanceCategoryInfo category,
        IReadOnlyList<int> trainingDaysIso,
        double avgSessionKm)
    {
        var raceEpochDay = slots.LastOrDefault(s => s.IsRaceDay).EpochDay;
        if (raceEpochDay == 0 && slots.Count > 0) raceEpochDay = slots[^1].EpochDay;

        var workoutDayMap = new Dictionary<long, WorkoutLogDto>();
        foreach (var w in workouts)
        {
            if (w.CreatedAtEpochMs <= 0) continue;
            var eDay = EpochDayOf(WorkoutDateKey(w, w.CreatedAtEpochMs));
            if (!workoutDayMap.TryGetValue(eDay, out var existing) || w.DistanceKm > existing.DistanceKm)
                workoutDayMap[eDay] = w;
        }

        var decisions = new List<(string, string)>(slots.Count);
        var lastQualityEpochDay = -999L;
        var lastLongEpochDay = -999L;
        var lastTrainingDay = trainingDaysIso.Count > 0 ? trainingDaysIso[^1] : 7;

        foreach (var slot in slots)
        {
            if (slot.IsRaceDay)
            {
                decisions.Add(("RACE_DAY", "Hari Perlombaan! Eksekusi strategi race pace target dengan disiplin energi."));
                continue;
            }

            var daysUntilRace = (int)(raceEpochDay - slot.EpochDay);
            if (daysUntilRace is >= 1 and <= 6)
            {
                if (daysUntilRace <= 3)
                {
                    decisions.Add(("RECOVERY", $"Fase Tapering Akhir (H-{daysUntilRace}): Lari pemulihan sangat santai untuk menyimpan simpanan glikogen dan menyegarkan otot sebelum race."));
                }
                else
                {
                    decisions.Add(("TEMPO", $"Fase Tapering (H-{daysUntilRace}): Sesi tempo singkat penyesuaian ritme race pace tanpa menimbulkan kelelahan otot mendalam."));
                }
                continue;
            }

            workoutDayMap.TryGetValue(slot.EpochDay - 1, out var yesterday);
            workoutDayMap.TryGetValue(slot.EpochDay - 2, out var twoDaysAgo);

            var recentHard = yesterday != null && (yesterday.DistanceKm >= avgSessionKm * 1.1 || yesterday.AvgHeartRate >= 165 || yesterday.ElevationGainM >= 60)
                ? yesterday
                : twoDaysAgo != null && (twoDaysAgo.DistanceKm >= avgSessionKm * 1.3 || twoDaysAgo.AvgHeartRate >= 170)
                    ? twoDaysAgo
                    : null;

            if (recentHard != null)
            {
                decisions.Add(("RECOVERY", $"Adaptasi Beban: Riwayat lari {recentHard.DistanceKm:F1} km ({recentHard.Title}) baru saja diselesaikan. Sesi dialihkan ke Recovery Run Zona 1-2 untuk mencegah cedera & overtraining."));
                continue;
            }

            var daysSinceQuality = (int)(slot.EpochDay - lastQualityEpochDay);
            var daysSinceLong = (int)(slot.EpochDay - lastLongEpochDay);
            var isWeekend = slot.IsoDay is 6 or 7;
            var isLastTrainingDay = slot.IsoDay == lastTrainingDay;

            if ((isWeekend || isLastTrainingDay) && daysSinceLong >= 5)
            {
                lastLongEpochDay = slot.EpochDay;
                decisions.Add(("EASY_LONG", $"Sesi Aerobik Jarak Jauh (Long Run): Membangun efisiensi mitokondria dan kapasitas endurance menyongsong target {category.ShortLabel}."));
            }
            else if (daysSinceQuality >= 3 && !isWeekend)
            {
                lastQualityEpochDay = slot.EpochDay;
                decisions.Add(("INTERVAL", "Kondisi fisik optimal setelah masa pemulihan. Jadwal diarahkan ke Interval Speed VO2Max guna menaikkan ambang batas kecepatan puncak."));
            }
            else if (daysSinceQuality >= 2 && !isWeekend)
            {
                lastQualityEpochDay = slot.EpochDay;
                decisions.Add(("TEMPO", "Sesi Laktat Threshold (Tempo): Melatih tubuh membuang asam laktat secara efisien pada kecepatan mendekati target race pace."));
            }
            else
            {
                decisions.Add(("RECOVERY", "Sesi Regenerasi Aktif: Menjaga turnover langkah kaki tanpa menambah beban kardiovaskular berat."));
            }
        }

        return decisions;
    }

    private (double DistKm, long PaceSec, string Title, string Desc) BuildSessionPrescription(
        string wType,
        DistanceCategoryInfo category,
        long basePaceSec,
        long goalRacePaceSec,
        double avgSessionKm,
        int daysToRace,
        string cleanGoalTitle)
    {
        var taperFactor = daysToRace is >= 1 and <= 7 ? 0.7 : 1.0;
        var (minLong, maxLong) = category.Key switch
        {
            "5K" => (6.0, 10.0),
            "TEN_K" or "10K" => (10.0, 16.0),
            "HALF_MARATHON" => (14.0, 22.0),
            _ => (22.0, 32.0)
        };
        var longRunBase = Math.Clamp(avgSessionKm * 1.35 * taperFactor, minLong, maxLong);

        switch (wType)
        {
            case "INTERVAL":
            {
                var p = Math.Max(150L, (long)Math.Round(basePaceSec * 0.93));
                var maxInterval = Math.Max(3.0, longRunBase * 0.65);
                var d = Math.Clamp(avgSessionKm * 0.75 * taperFactor, 3.0, maxInterval);
                var reps = category.Key switch
                {
                    "5K" => "6x400m",
                    "10K" => "5x800m",
                    "HALF_MARATHON" => "4x1600m",
                    _ => "3x2km"
                };
                return (d, p, string.Create(CultureInfo.InvariantCulture, $"Interval Speed ({d:F1} km)"),
                    $"Pemanasan 1 km + {reps} pada pace {raceService.FormatPace(p)} (jeda jalan/jog 90 detik) untuk mendongkrak VO2Max.");
            }
            case "TEMPO":
            {
                var p = Math.Max(160L, basePaceSec);
                var catMax = category.Key switch
                {
                    "5K" => 5.0,
                    "10K" => 8.0,
                    "HALF_MARATHON" => 12.0,
                    _ => 16.0
                };
                var maxTempo = Math.Clamp(longRunBase * 0.65, 3.0, catMax);
                var d = Math.Clamp(avgSessionKm * 0.80 * taperFactor, 3.0, maxTempo);
                return (d, p, string.Create(CultureInfo.InvariantCulture, $"Tempo Run ({d:F1} km)"),
                    $"Lari ritme stabil di sekitar {raceService.FormatPace(p)} untuk melatih ambang batas asam laktat.");
            }
            case "RECOVERY":
            {
                var p = (long)Math.Round(basePaceSec * 1.18);
                var maxRec = Math.Max(3.0, longRunBase * 0.55);
                var d = Math.Clamp(avgSessionKm * 0.55 * taperFactor, 3.0, maxRec);
                return (d, p, string.Create(CultureInfo.InvariantCulture, $"Recovery / Easy Run ({d:F1} km)"),
                    $"Lari ringan Zona 1-2 pada pace santai {raceService.FormatPace(p)}, harus bisa sambil mengobrol.");
            }
            case "EASY_LONG":
            {
                var p = (long)Math.Round(basePaceSec * 1.12);
                return (longRunBase, p, string.Create(CultureInfo.InvariantCulture, $"Long Run ({longRunBase:F1} km)"),
                    $"Lari daya tahan aerobik Zona 2 pada pace {raceService.FormatPace(p)} guna memperkuat fondasi jarak {category.ShortLabel}.");
            }
            default:
                return (category.Kilometers, goalRacePaceSec,
                    $"RACE DAY: {cleanGoalTitle} ({category.ShortLabel})",
                    $"Hari Lomba! Jaga target pace {raceService.FormatPace(goalRacePaceSec)} untuk mencapai target waktu {raceService.FormatDuration((long)Math.Round(goalRacePaceSec * category.Kilometers))}.");
        }
    }

    private static (string DisplayName, string ShortBadge, string ZoneLabel) WorkoutTypeLabels(string code) => code switch
    {
        "INTERVAL" => ("Interval / Speed Run", "INTERVAL", "Zona 5 (VO2Max)"),
        "TEMPO" => ("Tempo / Threshold Run", "TEMPO", "Zona 4 (Threshold)"),
        "RECOVERY" => ("Recovery / Easy Run", "RECOVERY", "Zona 1-2 (Pemulihan)"),
        "EASY_LONG" => ("Long Easy / Endurance Run", "LONG RUN", "Zona 2 (Endurance)"),
        _ => ("Race Day Target!", "RACE DAY", "Target Pace Lomba")
    };

    public static bool IsValidDateKey(string key) =>
        DateKeyRegex().IsMatch(key) && ParseDateOnly(key).HasValue;

    public static string TodayKey(long? nowEpochMs = null)
    {
        var dto = nowEpochMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(nowEpochMs.Value).ToLocalTime()
            : DateTimeOffset.Now;
        return dto.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string DefaultTargetDateKey(int daysFromNow = 21) =>
        DateTime.Today.AddDays(daysFromNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string AddDaysToKey(string dateKey, int days)
    {
        var d = ParseDateOnly(dateKey) ?? DateTime.Today;
        return d.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static long DaysBetween(string fromKey, string toKey) =>
        EpochDayOf(toKey) - EpochDayOf(fromKey);

    public static long EpochDayOf(string dateKey)
    {
        var parsed = ParseDateOnly(dateKey);
        if (!parsed.HasValue) return 0L;
        return (long)(parsed.Value.Date - DateTime.UnixEpoch.Date).TotalDays;
    }

    private static DateTime? ParseDateOnly(string key) =>
        DateTime.TryParseExact(key.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.Date
            : null;

    public static string FormatShortDate(string dateKey)
    {
        var parsed = ParseDateOnly(dateKey);
        return parsed.HasValue ? parsed.Value.ToString("dd MMM yyyy", Indonesian) : dateKey;
    }

    public static string WorkoutDateKey(WorkoutLogDto w, long fallbackMs)
    {
        var ms = w.CreatedAtEpochMs > 0 ? w.CreatedAtEpochMs : fallbackMs;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
