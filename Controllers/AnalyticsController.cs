using Microsoft.AspNetCore.Mvc;
using StrideApex.Api.Models;
using StrideApex.Api.Services;

namespace StrideApex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AnalyticsController(
    DeepAnalyticsService deepAnalytics,
    GoalTrainingPlanService goalPlanService,
    BiomechanicsCatalogService biomechanicsCatalog,
    ShoeUnlockService shoeUnlockService) : ControllerBase
{
    [HttpPost("comprehensive")]
    public ActionResult<ComprehensiveAnalyticsResponse> ComputeComprehensive([FromBody] ComprehensiveAnalyticsRequest request)
    {
        return Ok(deepAnalytics.ComputeComprehensive(request));
    }

    [HttpPost("workout-deep")]
    public ActionResult<WorkoutDeepMetricsDto> ComputeWorkoutDeep([FromBody] WorkoutDeepRequest request)
    {
        return Ok(deepAnalytics.AnalyzeSingleWorkout(
            request.Workout,
            request.ThresholdPaceSec,
            request.MaxHr,
            request.RestingHr
        ));
    }

    [HttpPost("goal-schedule")]
    public ActionResult<GoalScheduleWithSummaryDto> ComputeGoalSchedule([FromBody] GoalScheduleRequest request)
    {
        return Ok(goalPlanService.BuildGoalScheduleWithSummary(
            request.Goal,
            request.Workouts ?? [],
            request.NowEpochMs
        ));
    }

    [HttpGet("biomechanics")]
    public ActionResult<IReadOnlyList<BiomechanicsModuleDto>> GetBiomechanicsModules()
    {
        return Ok(biomechanicsCatalog.Modules);
    }

    [HttpGet("shoes-catalog")]
    public ActionResult<IReadOnlyList<ShoeTierDto>> GetShoesCatalog()
    {
        return Ok(shoeUnlockService.Tiers);
    }
}
