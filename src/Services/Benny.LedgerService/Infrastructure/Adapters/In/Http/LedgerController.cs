using Benny.Common.Domain.Models;
using Benny.LedgerService.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace Benny.LedgerService.Infrastructure.Adapters.In.Http;

[ApiController]
[Route("api/v1/ledger")]
public sealed class LedgerController(LedgerCommandService commandService) : ControllerBase
{
    [HttpPost("entries")]
    public async Task<ActionResult<LedgerEntryDto>> RecordEntry(
        [FromBody] RecordLedgerRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await commandService.RecordTransactionAsync(request, cancellationToken);
        return Created($"/api/v1/ledger/entries/{entry.EntryId}", entry);
    }
}
