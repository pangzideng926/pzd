using System.Reflection;

namespace Pzd.SharedKernel.UnitTests;

public class SmokeTests
{
    [Fact]
    public void SharedKernelAssemblyLoads()
    {
        var assembly = Assembly.Load("Pzd.SharedKernel");

        Assert.Equal("Pzd.SharedKernel", assembly.GetName().Name);
    }
}
