namespace StrideApex.Api.Models;

public sealed record OcrExtractRequest(
    string? ImageBase64,
    string? MimeType,
    string? RawText,
    int? PresetIndex
);

public sealed record ExtractedStravaMetricsDto(
    string Title,
    double DistanceKm,
    long DurationSeconds,
    long AvgPaceSecondsPerKm,
    long AvgHeartRate,
    long CadenceSpm,
    long ElevationGainM,
    string SplitsSummary,
    string AiCoachingInsight,
    long? WorkoutDateEpochMs,
    string DetectedDateStr,
    string SourceEngine
);

public sealed record RefreshWorkoutRequest(
    WorkoutLogDto Workout,
    IReadOnlyList<PersonalRecordDto>? Prs = null
);

public sealed record RefreshWorkoutResponse(
    string RecalibratedSplits,
    string AiCoachingInsight,
    string SourceEngine
);

public sealed record PlanAdviceRequest(
    TrainingGoalDto Goal,
    IReadOnlyList<WorkoutLogDto>? Workouts = null
);

public sealed record PlanAdviceResponse(
    string Advice,
    string SourceEngine
);

public sealed record BiomechanicsCoachRequest(
    string Question,
    BiomechanicsModuleDto Module
);

public sealed record BiomechanicsCoachResponse(
    string Answer,
    string SourceEngine
);
