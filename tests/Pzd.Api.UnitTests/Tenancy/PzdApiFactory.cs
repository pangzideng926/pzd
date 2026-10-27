using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pzd.Api.UnitTests;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;

namespace Pzd.Api.UnitTests.Tenancy;

/// <summary>API 接缝测试宿主：替换租户库与时钟，挂上测试认证方案。</summary>
internal sealed class PzdApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 10, 27, 2, 0, 0, TimeSpan.Zero);

    public static readonly TenantInfo[] Tenants =
    [
        new(1, "acme", TenantStatus.Active, null, "Server=127.0.0.1;Port=3306;User ID=root;Password=;Database=pzd_t_acme"),
        new(2, "beta", TenantStatus.Active, null, "Server=127.0.0.1;Port=3306;User ID=root;Password=;Database=pzd_t_beta"),
        new(3, "halted", TenantStatus.Disabled, null, "Server=127.0.0.1;Port=3306;User ID=root;Password=;Database=pzd_t_halted"),
        new(4, "lapsed", TenantStatus.Active, Now.AddDays(-1), "Server=127.0.0.1;Port=3306;User ID=root;Password=;Database=pzd_t_lapsed"),
    ];

    public PzdApiFactory()
        : this(new RecordingTenantStore(Tenants))
    {
    }

    private PzdApiFactory(RecordingTenantStore tenantStore) => TenantStore = tenantStore;

    public RecordingTenantStore TenantStore { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 生产环境语义：异常走 IExceptionHandler（开发环境的 DeveloperExceptionPage 会先拦截）。
        builder.UseEnvironment("Production");

        // 沙箱里 EventLog 写入会被拒绝，测试宿主清空日志提供方，避免异常路径被日志失败打断。
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITenantStore>();
            services.AddSingleton<ITenantStore>(TenantStore);

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FixedClock(Now));

            services.RemoveAll<IAuthenticationSchemeProvider>();
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });

            services.AddSingleton<IStartupFilter, TestEndpointsStartupFilter>();
        });
    }

    internal sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }

    internal sealed class RecordingTenantStore : ITenantStore
    {
        private readonly InMemoryTenantStore _inner;
        private readonly List<string> _requestedCodes = [];

        public RecordingTenantStore(IEnumerable<TenantInfo> tenants) => _inner = new InMemoryTenantStore(tenants);

        public int FindByIdCalls { get; private set; }

        public int FindByCodeCalls { get; private set; }

        public IReadOnlyList<string> RequestedCodes => _requestedCodes;

        public Task<TenantInfo?> FindByIdAsync(long tenantId, CancellationToken cancellationToken = default)
        {
            FindByIdCalls++;
            return _inner.FindByIdAsync(tenantId, cancellationToken);
        }

        public Task<TenantInfo?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            FindByCodeCalls++;
            _requestedCodes.Add(code);
            return _inner.FindByCodeAsync(code, cancellationToken);
        }
    }
}
