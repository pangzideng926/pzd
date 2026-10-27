using System.Reflection;

namespace Pzd.Infrastructure.UnitTests;

public class SmokeTests
{
    [Fact]
    public void InfrastructureAssemblyLoads()
    {
        var assembly = Assembly.Load("Pzd.Infrastructure");

        Assert.Equal("Pzd.Infrastructure", assembly.GetName().Name);
    }
}
