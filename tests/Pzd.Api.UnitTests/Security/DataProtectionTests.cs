using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Pzd.Infrastructure.Security;

namespace Pzd.Api.UnitTests.Security;

/// <summary>
/// 票 08：Data Protection 密钥环路径来自配置；「重启」（第二个 ServiceProvider）后仍能用旧密钥解密。
/// </summary>
public class DataProtectionTests
{
    [Fact]
    public void Keys_are_persisted_to_the_configured_path_and_survive_a_restart()
    {
        var keyPath = Path.Combine(
            AppContext.BaseDirectory,
            "dp-keys-test",
            Guid.NewGuid().ToString("N"));

        try
        {
            var configuration = BuildConfiguration(keyPath);
            var environment = new StubHostEnvironment();
            string protectedPayload;

            using (var first = BuildProvider(configuration, environment))
            {
                protectedPayload = first.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("pzd.test")
                    .Protect("hello-pzd");
            }

            Assert.NotEmpty(Directory.GetFiles(keyPath, "key-*.xml"));

            using var second = BuildProvider(configuration, environment);
            var payload = second.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("pzd.test")
                .Unprotect(protectedPayload);

            Assert.Equal("hello-pzd", payload);
        }
        finally
        {
            if (Directory.Exists(keyPath))
            {
                Directory.Delete(keyPath, recursive: true);
            }
        }
    }

    private static ServiceProvider BuildProvider(IConfiguration configuration, IHostEnvironment environment) =>
        new ServiceCollection()
            .AddLogging()
            .AddPzdDataProtection(configuration, environment)
            .BuildServiceProvider();

    private static IConfigurationRoot BuildConfiguration(string keyPath) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionSetup.KeyPathConfigKey] = keyPath,
            })
            .Build();

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";

        public string ApplicationName { get; set; } = "Pzd.Api";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
