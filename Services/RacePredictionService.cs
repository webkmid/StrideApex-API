using System.Globalization;
using StrideApex.Api.Models;

namespace StrideApex.Api.Services;

/// <summary>
/// Port of Android RacePredictionEngine: Jack Daniels VDOT, Riegel race predictions,
/// and 5 Lactate Threshold-anchored training pace zones.
/// </summary>
public sealed class RacePredictionService
{
    public double CalculateVdot(double distanceMeters, long durationSeconds)
    {
        if (distanceMeters <= 0.0 || durationSeconds <= 0)
            return 48.0;

        var timeMinutes = durationSeconds / 60.0;
        var velocityMetersPerMin = distanceMeters / timeMinutes;
        var vo2 = -4.60 + 0.182258 * velocityMetersPerMin + 0.000104 * Math.Pow(velocityMetersPerMin, 2);
        var pctMax = 0.8 + 0.1894393 * Math.Exp(-0.012778 * timeMinutes) + 0.2989558 * Math.Exp(-0.1932605 * timeMinutes);
        if (pctMax <= 0.05) return 48.0;
        return Math.Clamp(vo2 / pctMax, 28.0, 85.0);
    }

    public IReadOnlyList<RacePredictionDto> ComputeRacePredictions(
        IReadOnlyList<WorkoutLogDto> workouts,
        IReadOnlyList<PersonalRecordDto> prs,
        double fatigueExponent = 1.06)
    {
        var safeExponent = Math.Clamp(fatigueExponent, 1.03, 1.10);
        var bestPr = prs
            .OrderByDescending(p => CalculateVdot(p.DistanceMeters, p.BestTimeSeconds))
            .FirstOrDefault();

        var bestWorkout = workouts
            .Where(w => w.DistanceKm >= 3.0 && w.DurationSeconds > 0)
            .OrderByDescending(w => CalculateVdot(w.DistanceKm * 1000.0, w.DurationSeconds))
            .FirstOrDefault();

        var fallbackWorkout = bestPr == null && bestWorkout == null
            ? workouts
                .Where(w => w.DistanceKm >= 0.5 && w.DurationSeconds >= 60)
                .OrderByDescending(w => CalculateVdot(w.DistanceKm * 1000.0, w.DurationSeconds))
                .FirstOrDefault()
            : null;

        if (workouts.Count == 0 || (bestPr == null && bestWorkout == null && fallbackWorkout == null))
        {
            return [];
        }

        double baseDistanceM;
        long baseTimeSec;

        if (bestPr != null && bestWorkout != null)
        {
            var prVdot = CalculateVdot(bestPr.DistanceMeters, bestPr.BestTimeSeconds);
            var wkVdot = CalculateVdot(bestWorkout.DistanceKm * 1000.0, bestWorkout.DurationSeconds);
            if (prVdot >= wkVdot)
            {
                baseDistanceM = bestPr.DistanceMeters;
                baseTimeSec = bestPr.BestTimeSeconds;
            }
            else
            {
                baseDistanceM = bestWorkout.DistanceKm * 1000.0;
                baseTimeSec = bestWorkout.DurationSeconds;
            }
        }
        else if (bestPr != null)
        {
            baseDistanceM = bestPr.DistanceMeters;
            baseTimeSec = bestPr.BestTimeSeconds;
        }
        else if (bestWorkout != null)
        {
            baseDistanceM = bestWorkout.DistanceKm * 1000.0;
            baseTimeSec = bestWorkout.DurationSeconds;
        }
        else
        {
            baseDistanceM = fallbackWorkout!.DistanceKm * 1000.0;
            baseTimeSec = fallbackWorkout.DurationSeconds;
        }

        var vdot = CalculateVdot(baseDistanceM, baseTimeSec);
        var totalVolumeKm = workouts.Sum(w => w.DistanceKm);

        return DistanceCategoryHelper.All.Select(category =>
        {
            var ratio = category.Meters / baseDistanceM;
            var distancePenaltyFactor = category.Key switch
            {
                "MARATHON" => totalVolumeKm < 45.0 ? 1.022 : 1.0,
                "HALF_MARATHON" => totalVolumeKm < 30.0 ? 1.01 : 1.0,
                _ => 1.0
            };

            var predictedSeconds = (long)Math.Round(baseTimeSec * Math.Pow(ratio, safeExponent) * distancePenaltyFactor);
            var pacePerKm = (long)Math.Round(predictedSeconds / category.Kilometers);
            var note = category.Key switch
            {
                "5K" => "VO2 Max ceiling & neuromuscular turnover",
                "10K" => "Lactate threshold sustainability benchmark",
                "HALF_MARATHON" => "Aerobic threshold & glycogen sparing index",
                _ => string.Create(CultureInfo.InvariantCulture, $"Fatigue exponent {safeExponent:F2} + volume durability")
            };

            return new RacePredictionDto(
                CategoryKey: category.Key,
                DisplayName: category.DisplayName,
                ShortLabel: category.ShortLabel,
                PredictedTimeSeconds: predictedSeconds,
                PredictedTimeFormatted: FormatDuration(predictedSeconds),
                TargetPaceSecondsPerKm: pacePerKm,
                TargetPaceFormatted: FormatPace(pacePerKm),
                VdotScore: Math.Round(vdot, 1),
                ConfidenceNote: note
            );
        }).ToList();
    }

    public IReadOnlyList<TrainingPaceZoneDto> ComputeTrainingPaceZones(long tenKPaceSecondsPerKm)
    {
        var basePace = Math.Clamp(tenKPaceSecondsPerKm, 150L, 540L);
        var tPace = basePace * 1.03;

        return
        [
            new(
                "Zone 1-2: Easy / Aerobic Base",
                "E",
                $"{FormatPaceShort((long)Math.Round(tPace * 1.14))} - {FormatPaceShort((long)Math.Round(tPace * 1.30))} /km",
                "Mitochondrial density, capillary growth & active recovery",
                "65% - 78% HRmax"
            ),
            new(
                "Zone 3: Marathon / Aerobic Tempo",
                "M",
                $"{FormatPaceShort((long)Math.Round(tPace * 1.04))} - {FormatPaceShort((long)Math.Round(tPace * 1.13))} /km",
                "Lipid oxidation efficiency & race-pace biomechanical economy",
                "79% - 84% HRmax"
            ),
            new(
                "Zone 4: Lactate Threshold",
                "T",
                $"{FormatPaceShort((long)Math.Round(tPace * 0.97))} - {FormatPaceShort((long)Math.Round(tPace * 1.03))} /km",
                "Raises lactate clearance shuttle ceiling (60-min race effort)",
                "85% - 90% HRmax"
            ),
            new(
                "Zone 5A: VO2 Max Intervals",
                "I",
                $"{FormatPaceShort(Math.Max(135L, (long)Math.Round(tPace * 0.90)))} - {FormatPaceShort(Math.Max(145L, (long)Math.Round(tPace * 0.95)))} /km",
                "Maximizes cardiac stroke volume & central oxygen uptake",
                "93% - 98% HRmax"
            ),
            new(
                "Zone 5B: Neuromuscular Repetition",
                "R",
                $"{FormatPaceShort(Math.Max(120L, (long)Math.Round(tPace * 0.83)))} - {FormatPaceShort(Math.Max(130L, (long)Math.Round(tPace * 0.89)))} /km",
                "Fast-twitch fiber recruitment, tendon stiffness & running economy",
                "98% - 100% Effort"
            )
        ];
    }

    public string FormatDuration(long seconds)
    {
        var safe = Math.Max(0L, seconds);
        var hrs = safe / 3600;
        var mins = (safe % 3600) / 60;
        var secs = safe % 60;
        return hrs > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hrs}:{mins:00}:{secs:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{mins:00}:{secs:00}");
    }

    public string FormatPace(long secondsPerKm)
    {
        var safe = Math.Max(0L, secondsPerKm);
        return string.Create(CultureInfo.InvariantCulture, $"{safe / 60}:{safe % 60:00} /km");
    }

    public string FormatPaceShort(long secondsPerKm)
    {
        var safe = Math.Max(0L, secondsPerKm);
        return string.Create(CultureInfo.InvariantCulture, $"{safe / 60}:{safe % 60:00}");
    }
}
