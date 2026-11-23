namespace StrideApex.Api.Models;

public sealed record ComprehensiveAnalyticsRequest(
    IReadOnlyList<WorkoutLogDto>? Workouts,
    IReadOnlyList<PersonalRecordDto>? Prs,
    IReadOnlyList<TrainingGoalDto>? Goals,
    double FatigueExponent = 1.06,
    int MaxHr = 190,
    int RestingHr = 55,
    long? NowEpochMs = null
);

public sealed record GoalScheduleRequest(
    TrainingGoalDto Goal,
    IReadOnlyList<WorkoutLogDto>? Workouts,
    long? NowEpochMs = null
);

public sealed record WorkoutDeepRequest(
    WorkoutLogDto Workout,
    double ThresholdPaceSec = 300.0,
    int MaxHr = 190,
    int RestingHr = 55
);

public sealed record OverviewSummaryDto(
    int TotalWorkouts,
    double TotalDistanceKm,
    long TotalDurationSeconds,
    long TotalElevationM,
    long AvgPaceSec,
    long AvgHeartRate,
    long AvgCadenceSpm,
    double CurrentVdot,
    double VdotDelta30d,
    int CurrentStreakDays,
    int LongestStreakDays,
    long EstimatedCaloriesKcal,
    double LongestRunKm,
    long FastestPaceSec
);

public sealed record GoalScheduleWithSummaryDto(
    string GoalId,
    TrainingGoalDto Goal,
    GoalScheduleMetadataDto Metadata,
    int CompletedSessions,
    int TodayPendingSessions,
    int SkippedSessions,
    int UpcomingSessions,
    double CompliancePct,
    IReadOnlyList<ScheduledSessionDto> Sessions
);

public sealed record DailyFitnessPointDto(
    string DateKey,
    double DailyLoad,
    double CtlFitness,
    double AtlFatigue,
    double TsbForm,
    double DistanceKm
);

public sealed record WorkloadStatusDto(
    double Acwr,
    double Acute7dLoad,
    double Chronic28dWeeklyAvgLoad,
    string Zone,
    string Label,
    string Recommendation
);

public sealed record PolarizedDistributionDto(
    double EasyPct,
    double ModeratePct,
    double HardPct,
    double EasyKm,
    double ModerateKm,
    double HardKm,
    int AlignmentScore,
    string CoachingNote
);

public sealed record EfficiencyPointDto(
    string WorkoutId,
    string DateKey,
    string Title,
    double DistanceKm,
    long AvgPaceSec,
    long AvgHeartRate,
    double EfficiencyFactor,
    double Vdot,
    long CadenceSpm,
    double StrideLengthM
);

public sealed record PacingConsistencyDto(
    double AvgSplitCvPct,
    int NegativeSplitCount,
    int EvenSplitCount,
    int PositiveSplitCount,
    string Grade,
    string BestPacedWorkoutTitle,
    string Summary
);

public sealed record RaceReadinessDto(
    int Score,
    string Grade,
    string GoalTitle,
    string TargetDistanceLabel,
    double VolumeReadinessPct,
    long VdotGapSecPerKm,
    bool AcwrSafe,
    double LongRunCoveragePct,
    string Bottleneck,
    IReadOnlyList<string> ActionItems
);

public sealed record MonthlyTrendDto(
    string MonthKey,
    string MonthLabel,
    int RunsCount,
    double TotalDistanceKm,
    long AvgPaceSec,
    long ElevationGainM,
    double AvgLoad
);

public sealed record HeatmapDayDto(
    string DateKey,
    double DistanceKm,
    int RunsCount,
    int IntensityLevel // 0..4
);

public sealed record CadenceDiagnosticsDto(
    long AvgCadenceSpm,
    double EstimatedAvgStrideLengthM,
    string OptimalRange,
    string Status,
    string RecommendedModuleId,
    string CoachingNote
);

public sealed record WorkoutDeepMetricsDto(
    string WorkoutId,
    IReadOnlyList<SplitItemDto> Splits,
    double SplitCvPct,
    long FirstHalfAvgPaceSec,
    long SecondHalfAvgPaceSec,
    long SplitDeltaSec,
    string SplitStrategy,
    SplitItemDto? FastestSplit,
    SplitItemDto? SlowestSplit,
    double EstimatedRtss,
    double AerobicEfficiency,
    double StrideLengthM,
    double Vdot,
    string ZoneCode,
    string ZoneName,
    long PaceSpreadSec
);

public sealed record ComprehensiveAnalyticsResponse(
    OverviewSummaryDto Overview,
    IReadOnlyList<RacePredictionDto> RacePredictions,
    IReadOnlyList<TrainingPaceZoneDto> PaceZones,
    ShoeUnlockProgressDto ShoeUnlock,
    WeeklyAnalyticsDto WeeklyProgress,
    IReadOnlyList<GoalScheduleWithSummaryDto> GoalSchedules,
    IReadOnlyList<DailyFitnessPointDto> FitnessLoadSeries,
    WorkloadStatusDto WorkloadStatus,
    PolarizedDistributionDto PolarizedDistribution,
    IReadOnlyList<EfficiencyPointDto> EfficiencyTrend,
    PacingConsistencyDto PacingConsistency,
    RaceReadinessDto RaceReadiness,
    IReadOnlyList<MonthlyTrendDto> MonthlyTrends,
    IReadOnlyList<HeatmapDayDto> ActivityHeatmap,
    CadenceDiagnosticsDto CadenceDiagnostics,
    IReadOnlyDictionary<string, WorkoutDeepMetricsDto> WorkoutDeepMetrics
);
