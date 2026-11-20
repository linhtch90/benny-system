using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using Xunit;

namespace Benny.UnitTests;

public sealed class ArchitectureGuardrailTests
{
    [Theory]
    [InlineData(typeof(Benny.EdgeGateway.Infrastructure.Adapters.In.Http.EdgeGatewayController))]
    [InlineData(typeof(Benny.IngestionService.Infrastructure.Adapters.In.Http.IngestionController))]
    [InlineData(typeof(Benny.RiskEngine.Infrastructure.Adapters.In.Http.RiskController))]
    [InlineData(typeof(Benny.LedgerService.Infrastructure.Adapters.In.Http.LedgerController))]
    [InlineData(typeof(Benny.QueryService.Infrastructure.Adapters.In.Http.AnalyticsController))]
    public void HttpControllers_MustInheritControllerBaseAndHaveApiControllerAttribute(Type controllerType)
    {
        Assert.True(typeof(ControllerBase).IsAssignableFrom(controllerType));
        Assert.NotNull(controllerType.GetCustomAttribute<ApiControllerAttribute>());
    }

    [Fact]
    public void DomainLayer_InIngestionService_ShouldNotDependOnInfrastructure()
    {
        var result = Types.InAssembly(typeof(Benny.IngestionService.Domain.Ports.Out.IRiskEngineClient).Assembly)
            .That()
            .ResideInNamespace("Benny.IngestionService.Domain")
            .ShouldNot()
            .HaveDependencyOn("Benny.IngestionService.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }
}
