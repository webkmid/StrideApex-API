using Microsoft.AspNetCore.Mvc;
using StrideApex.Api.Models;
using StrideApex.Api.Services;

namespace StrideApex.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class WorkoutsAiController(GeminiAiService geminiService) : ControllerBase
{
    [HttpPost("ocr/extract")]
    public async Task<ActionResult<ExtractedStravaMetricsDto>> ExtractOcr([FromBody] OcrExtractRequest request, CancellationToken ct)
    {
        var result = await geminiService.ExtractStravaScreenshotAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("ai/refresh-workout")]
    public async Task<ActionResult<RefreshWorkoutResponse>> RefreshWorkoutAi([FromBody] RefreshWorkoutRequest request, CancellationToken ct)
    {
        var result = await geminiService.RefreshWorkoutInsightAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("ai/plan-advice")]
    public async Task<ActionResult<PlanAdviceResponse>> GetPlanAdvice([FromBody] PlanAdviceRequest request, CancellationToken ct)
    {
        var result = await geminiService.GeneratePlanAdviceAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("ai/biomechanics-coach")]
    public async Task<ActionResult<BiomechanicsCoachResponse>> AskBiomechanicsCoach([FromBody] BiomechanicsCoachRequest request, CancellationToken ct)
    {
        var result = await geminiService.AnswerBiomechanicsQuestionAsync(request, ct);
        return Ok(result);
    }
}
