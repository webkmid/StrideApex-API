namespace StrideApex.Api.Models;

/// <summary>
/// Mirrors Android WorkoutLog entity and Firestore /users/{uid}/workouts contract.
/// </summary>
public sealed record WorkoutLogDto(
    string Id,
    string UserId,
    string Title,
    double DistanceKm,
    long DurationSeconds,
    long AvgPaceSecondsPerKm,
    long AvgHeartRate,
    long CadenceSpm,
    long ElevationGainM,
    string SplitsSummary,
    string AiCoachingInsight,
    string SourceType,
    long CreatedAtEpochMs,
    long UpdatedAtEpochMs
);

public sealed record PersonalRecordDto(
    string Id,
    string UserId,
    string DistanceCategory,
    long DistanceMeters,
    long BestTimeSeconds,
    long PreviousTimeSeconds,
    long PaceSecondsPerKm,
    string WorkoutTitle,
    long CreatedAtEpochMs
);

public sealed record TrainingGoalDto(
    string Id,
    string UserId,
    string Title,
    string TargetDistanceCategory,
    long TargetTimeSeconds,
    long WeeklyMileageTargetKm,
    bool ReminderEnabled,
    long ReminderHour,
    long ReminderMinute,
    bool Completed,
    long CreatedAtEpochMs
);

public sealed record CloudBackupSnapshotDto(
    string Id,
    string UserId,
    string BackupLabel,
    long WorkoutCount,
    long PrCount,
    long GoalCount,
    string SummaryNotes,
    long CreatedAtEpochMs
);

public sealed record DistanceCategoryInfo(
    string Key,
    string DisplayName,
    string ShortLabel,
    long Meters,
    double Kilometers
);

public static class DistanceCategoryHelper
{
    public static readonly IReadOnlyList<DistanceCategoryInfo> All =
    [
        new("5K", "5K Run", "5K", 5000L, 5.0),
        new("10K", "10K Run", "10K", 10000L, 10.0),
        new("HALF_MARATHON", "Half Marathon", "21.1K", 21097L, 21.0975),
        new("MARATHON", "Full Marathon", "42.2K", 42195L, 42.195)
    ];

    public static DistanceCategoryInfo FromKey(string? key) =>
        All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? All[1]; // 10K default matching Android DistanceCategory.fromKey
}

public sealed record SplitItemDto(
    double Km,
    string PaceStr,
    long PaceSeconds,
    long DeltaFromAvgSeconds
);

public sealed record RacePredictionDto(
    string CategoryKey,
    string DisplayName,
    string ShortLabel,
    long PredictedTimeSeconds,
    string PredictedTimeFormatted,
    long TargetPaceSecondsPerKm,
    string TargetPaceFormatted,
    double VdotScore,
    string ConfidenceNote
);

public sealed record TrainingPaceZoneDto(
    string ZoneName,
    string Code,
    string PaceRangePerKm,
    string PhysiologicalBenefit,
    string HeartRatePctRange
);

public sealed record ShoeModelDto(
    string Name,
    string Brand,
    string PlateType,
    string StackHeight,
    double DropMm,
    int? WeightG,
    string BestFor,
    string Description,
    string? ImageUrl,
    string Slug = "",
    string? ShopeeAffiliateUrl = null
);

public sealed record ShoeTierDto(
    int Id,
    string Title,
    string Subtitle,
    long UnlockPaceSecondsPerKm,
    string UnlockPaceDisplay,
    string BadgeLabel,
    string Description,
    string BiomechanicalRationale,
    IReadOnlyList<ShoeModelDto> Shoes
);

public sealed record ShoeTierStateDto(
    ShoeTierDto Tier,
    bool IsUnlocked,
    bool IsCurrentHighest
);

public sealed record ShoeUnlockProgressDto(
    long CurrentAvgPaceSec,
    int TotalWorkoutsEvaluated,
    ShoeTierDto CurrentTier,
    ShoeTierDto? NextTier,
    float ProgressToNextTier,
    long SecondsNeededToNextTier,
    IReadOnlyList<ShoeTierStateDto> Tiers
);

public sealed record DayStatDto(
    int DayIndex,
    string DayName,
    string DateKey,
    string DateDisplay,
    double DistanceKm,
    long DurationSec,
    long AvgPaceSec,
    bool HasRun,
    bool IsToday
);

public sealed record WeeklyAnalyticsDto(
    string WeekRangeDisplay,
    double TotalDistanceKm,
    long TargetDistanceKm,
    int RunsCount,
    long TotalDurationSec,
    long AvgPaceSec,
    double PrevWeekDistanceKm,
    double DistanceDiffKm,
    long PrevWeekAvgPaceSec,
    long PaceDiffSec,
    IReadOnlyList<DayStatDto> DailyStats,
    string CoachingTip
);

public sealed record GoalScheduleMetadataDto(
    string CleanTitle,
    string StartDateKey,
    string StartDateDisplay,
    string TargetDateKey,
    string TargetDateDisplay,
    int DaysRemaining,
    IReadOnlyList<int> TrainingDaysIso
);

public sealed record ScheduledSessionDto(
    string DateKey,
    string DisplayDate,
    int DayOfWeekIso,
    string DayName,
    string WorkoutTypeCode,
    string WorkoutTypeDisplay,
    string ShortBadge,
    string ZoneLabel,
    string SessionTitle,
    string SessionDescription,
    double RecommendedDistanceKm,
    long TargetPaceSecondsPerKm,
    string Status, // COMPLETED, TODAY_PENDING, SKIPPED, UPCOMING
    string? MatchedWorkoutId,
    string? MatchedWorkoutTitle,
    string AiReason,
    string DecisionSource
);

public sealed record BiomechanicsModuleDto(
    string Id,
    string Title,
    string Subtitle,
    string FocusMetricLabel,
    string OptimalTargetRange,
    string CommonFault,
    string BiomechanicalCue,
    IReadOnlyList<string> Drills,
    int TargetKneeFlexionDeg,
    int TargetTibialAngleDeg,
    int TargetTrunkLeanDeg,
    int TargetCadenceSpm
);
