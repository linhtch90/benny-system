using System.Text.Json;
using Benny.Common.Domain.Context;
using Benny.Common.Domain.Models;
using Benny.Common.Domain.Serialization;
using Xunit;

namespace Benny.UnitTests;

public sealed class DomainAndContextTests
{
    [Fact]
    public async Task AsyncLocalRequestContext_PropagatesAcrossAsyncContinuations_AndRestoresScope()
    {
        var outer = RequestContext.Create("corr-outer", "tenant-a", "idem-1");
        Assert.Null(RequestContextHolder.Current);

        using (RequestContextHolder.Push(outer))
        {
            await Task.Yield();
            Assert.Equal("corr-outer", RequestContextHolder.Current?.CorrelationId);
            Assert.Equal("tenant-a", RequestContextHolder.Current?.TenantId);

            var inner = RequestContext.Create("corr-inner", "tenant-b", "idem-2");
            using (RequestContextHolder.Push(inner))
            {
                await Task.Delay(1);
                Assert.Equal("corr-inner", RequestContextHolder.Current?.CorrelationId);
            }

            Assert.Equal("corr-outer", RequestContextHolder.Current?.CorrelationId);
        }

        Assert.Null(RequestContextHolder.Current);
    }

    [Fact]
    public void RiskDecision_PolymorphicSourceGenSerialization_RoundTripsCorrectly()
    {
        RiskDecision decision = new RiskDecision.Rejected(0.99m, "Blocked country", "BLOCKED_COUNTRY");

        var json = JsonSerializer.Serialize(decision, BennyJsonContext.Default.RiskDecision);
        var roundTripped = JsonSerializer.Deserialize(json, BennyJsonContext.Default.RiskDecision);

        var rejected = Assert.IsType<RiskDecision.Rejected>(roundTripped);
        Assert.Equal("BLOCKED_COUNTRY", rejected.RuleCode);
        Assert.Equal(0.99m, rejected.Score);
    }
}
