using System.Reflection;

namespace Pzd.ArchitectureTests;

public class SmokeTests
{
    [Fact]
    public void Product_assemblies_load()
    {
        Assert.Equal("Pzd.SharedKernel", Assembly.Load("Pzd.SharedKernel").GetName().Name);
        Assert.Equal("Pzd.Infrastructure", Assembly.Load("Pzd.Infrastructure").GetName().Name);
        Assert.Equal("Pzd.Api", Assembly.Load("Pzd.Api").GetName().Name);
    }
}
