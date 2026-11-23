using System.Globalization;
using StrideApex.Api.Models;

namespace StrideApex.Api.Services;

/// <summary>
/// Port of Android WeeklyProgressEngine: Monday-to-Sunday weekly volume, pace comparison,
/// and 7-day breakdown.
/// </summary>
public sealed class WeeklyProgressService
{
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");
    private static readonly string[] DayNames = ["Sen", "Sel", "Rab", "Kam", "Jum", "Sab", "Min"];

    public WeeklyAnalyticsDto ComputeWeeklyProgress(
        IReadOnlyList<WorkoutLogDto> workouts,
        TrainingGoalDto? activeGoal = null,
        long? nowEpochMs = null)
    {
        var nowUtc = nowEpochMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(nowEpochMs.Value).ToLocalTime()
            : DateTimeOffset.Now;

        var todayDate = nowUtc.Date;
        var todayKey = todayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Monday = 1 .. Sunday = 7
        var isoDow = todayDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)todayDate.DayOfWeek;
        var weekStartDate = todayDate.AddDays(-(isoDow - 1));
        var weekEndDate = weekStartDate.AddDays(6);

        var weekStartMs = new DateTimeOffset(weekStartDate, nowUtc.Offset).ToUnixTimeMilliseconds();
        var weekEndMs = new DateTimeOffset(weekEndDate.AddDays(1), nowUtc.Offset).ToUnixTimeMilliseconds() - 1;

        var prevWeekStartDate = weekStartDate.AddDays(-7);
        var prevWeekStartMs = new DateTimeOffset(prevWeekStartDate, nowUtc.Offset).ToUnixTimeMilliseconds();
        var prevWeekEndMs = weekStartMs - 1;

        var weekRangeDisplay = $"{weekStartDate.ToString("d MMM", Indonesian)} - {weekEndDate.ToString("d MMM", Indonesian)}";

        var currentWeekWorkouts = workouts
            .Where(w => w.CreatedAtEpochMs >= weekStartMs && w.CreatedAtEpochMs <= weekEndMs)
            .ToList();
        var prevWeekWorkouts = workouts
            .Where(w => w.CreatedAtEpochMs >= prevWeekStartMs && w.CreatedAtEpochMs <= prevWeekEndMs)
            .ToList();

        var totalDistanceKm = currentWeekWorkouts.Sum(w => w.DistanceKm);
        var totalDurationSec = currentWeekWorkouts.Sum(w => w.DurationSeconds);
        var runsCount = currentWeekWorkouts.Count;
        var avgPaceSec = totalDistanceKm > 0.0
            ? (long)Math.Round(totalDurationSec / totalDistanceKm)
            : 0L;

        var prevDist = prevWeekWorkouts.Sum(w => w.DistanceKm);
        var prevDur = prevWeekWorkouts.Sum(w => w.DurationSeconds);
        var prevAvgPaceSec = prevDist > 0.0 ? (long)Math.Round(prevDur / prevDist) : 0L;

        var distanceDiffKm = totalDistanceKm - prevDist;
        var paceDiffSec = (avgPaceSec > 0 && prevAvgPaceSec > 0) ? avgPaceSec - prevAvgPaceSec : 0L;
        var targetDistanceKm = activeGoal != null ? Math.Max(10L, activeGoal.WeeklyMileageTargetKm) : 25L;

        var dailyStats = Enumerable.Range(0, 7).Select(dayIdx =>
        {
            var dayDate = weekStartDate.AddDays(dayIdx);
            var dateKey = dayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dateDisplay = dayDate.ToString("d MMM", Indonesian);
            var dayStartMs = new DateTimeOffset(dayDate, nowUtc.Offset).ToUnixTimeMilliseconds();
            var dayEndMs = new DateTimeOffset(dayDate.AddDays(1), nowUtc.Offset).ToUnixTimeMilliseconds() - 1;

            var dayRuns = currentWeekWorkouts
                .Where(w => w.CreatedAtEpochMs >= dayStartMs && w.CreatedAtEpochMs <= dayEndMs)
                .ToList();

            var dayDist = dayRuns.Sum(w => w.DistanceKm);
            var dayDur = dayRuns.Sum(w => w.DurationSeconds);
            var dayPace = dayDist > 0.0 ? (long)Math.Round(dayDur / dayDist) : 0L;

            return new DayStatDto(
                DayIndex: dayIdx,
                DayName: DayNames[dayIdx],
                DateKey: dateKey,
                DateDisplay: dateDisplay,
                DistanceKm: Math.Round(dayDist, 2),
                DurationSec: dayDur,
                AvgPaceSec: dayPace,
                HasRun: dayRuns.Count > 0,
                IsToday: dateKey == todayKey
            );
        }).ToList();

        var coachingTip = totalDistanceKm >= targetDistanceKm
            ? $"Luar biasa! Target mingguan {targetDistanceKm} km telah tercapai. Jaga fase recovery agar otot siap menghadapi minggu berikutnya."
            : totalDistanceKm > 0 && runsCount >= 3
                ? string.Create(CultureInfo.InvariantCulture, $"Konsistensi sangat baik dengan {runsCount} sesi lari. Butuh {Math.Max(0.0, targetDistanceKm - totalDistanceKm):F1} km lagi untuk menuntaskan target minggu ini.")
                : totalDistanceKm > 0
                    ? "Bagus! Sesi awal minggu ini sudah dimulai. Pertahankan easy run santai untuk membangun akumulasi volume tanpa overtraining."
                    : "Minggu baru telah dimulai. Jadwalkan 3-4 sesi lari mingguan Anda untuk menjaga performa aerobik dan membuka tier sepatu baru!";

        return new WeeklyAnalyticsDto(
            WeekRangeDisplay: weekRangeDisplay,
            TotalDistanceKm: Math.Round(totalDistanceKm, 2),
            TargetDistanceKm: targetDistanceKm,
            RunsCount: runsCount,
            TotalDurationSec: totalDurationSec,
            AvgPaceSec: avgPaceSec,
            PrevWeekDistanceKm: Math.Round(prevDist, 2),
            DistanceDiffKm: Math.Round(distanceDiffKm, 2),
            PrevWeekAvgPaceSec: prevAvgPaceSec,
            PaceDiffSec: paceDiffSec,
            DailyStats: dailyStats,
            CoachingTip: coachingTip
        );
    }
}
