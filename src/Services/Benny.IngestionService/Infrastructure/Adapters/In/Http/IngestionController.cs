using Benny.Common.Domain.Models;
using Benny.IngestionService.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace Benny.IngestionService.Infrastructure.Adapters.In.Http;

[ApiController]
[Route("api/v1/ingestion")]
public sealed class IngestionController(IngestionOrchestrationUseCase useCase) : ControllerBase
{
    [HttpPost("transactions")]
    public async Task<ActionResult<IngestionAcceptedResponse>> IngestTransaction(
        [FromBody] TransactionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Amount <= 0)
        {
            return ValidationProblem(new ValidationProblemDetails
            {
                Title = "Invalid Transaction Amount",
                Detail = "Amount must be greater than zero."
            });
        }

        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.IdempotentReplay ? Ok(result) : Accepted(result);
    }
}
