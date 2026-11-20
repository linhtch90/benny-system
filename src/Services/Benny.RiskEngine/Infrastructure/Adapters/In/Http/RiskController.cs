using Benny.Common.Domain.Models;
using Benny.RiskEngine.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace Benny.RiskEngine.Infrastructure.Adapters.In.Http;

[ApiController]
[Route("api/v1/risk")]
public sealed class RiskController(ParallelRiskEvaluationService evaluationService) : ControllerBase
{
    [HttpPost("evaluate")]
    public async Task<ActionResult<RiskDecision>> Evaluate(
        [FromBody] RiskEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var decision = await evaluationService.EvaluateAsync(request, cancellationToken);
        return Ok(decision);
    }
}
