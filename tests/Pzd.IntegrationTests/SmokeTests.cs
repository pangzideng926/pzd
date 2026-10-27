using System.Reflection;

namespace Pzd.IntegrationTests;

public class SmokeTests
{
    [Fact]
    public void ApiAssemblyLoads()
    {
        var assembly = Assembly.Load("Pzd.Api");

        Assert.Equal("Pzd.Api", assembly.GetName().Name);
    }
}
