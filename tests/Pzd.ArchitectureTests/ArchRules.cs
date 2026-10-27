using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;

namespace Pzd.ArchitectureTests;

/// <summary>
/// 架构规则定义的唯一来源。生产用例与反向用例调用同一个方法：
/// 规则谓词被改坏时两边一起红，反向用例因此能真正守住生产规则（F1）。
/// 规则④从源树 csproj 的 ProjectReference 判定，不依赖编译产物（F2）。
/// </summary>
internal static class ArchRules
{
    internal const string SqlSugarDependency = "SqlSugar";
    internal const string AspNetCoreDependency = "Microsoft.AspNetCore";

    internal const string SharedKernelProject = "Pzd.SharedKernel";
    internal const string InfrastructureProject = "Pzd.Infrastructure";
    internal const string ApiProject = "Pzd.Api";
    internal const string AppHostProject = "Pzd.AppHost";
    private const string ContractsSuffix = ".Contracts";

    internal static readonly Assembly SharedKernelAssembly = Assembly.Load(SharedKernelProject);
    internal static readonly Assembly InfrastructureAssembly = Assembly.Load(InfrastructureProject);
    internal static readonly Assembly ApiAssembly = Assembly.Load(ApiProject);

    internal static IReadOnlyList<Assembly> ProductAssemblies { get; } =
        [SharedKernelAssembly, InfrastructureAssembly, ApiAssembly];

    /// <summary>规则①②：程序集直接引用的程序集名，用于断言禁止的引用方向。</summary>
    internal static IReadOnlyCollection<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

    /// <summary>规则①②：返回该程序集对禁止项的实际引用（空集 = 规则通过）。</summary>
    internal static IReadOnlyCollection<string> ForbiddenReferences(Assembly assembly, params string[] forbidden) =>
        ReferencedAssemblyNames(assembly)
            .Intersect(forbidden, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// 规则③：Domain 命名空间不得依赖持久化 / Web 库 / 其他分层工程。
    /// 传入程序集集合与禁止项；生产用例传 <see cref="DomainScanAssemblies"/>，反向用例把违规的测试程序集一起传进来。
    /// </summary>
    internal static TestResult DomainDependencyViolations(
        IEnumerable<Assembly> assemblies,
        params string[] forbiddenDependencies) =>
        Types.InAssemblies(assemblies)
            .That()
            .ResideInNamespaceEndingWith(".Domain")
            .Should()
            .NotHaveDependencyOnAny(forbiddenDependencies)
            .GetResult();

    /// <summary>
    /// 规则③的扫描范围：底座程序集 + 源树里发现的全部模块程序集（老登 §2.2）。
    /// 模块程序集不可加载时直接失败并提示接引用，不允许静默漏检。
    /// </summary>
    internal static IReadOnlyCollection<Assembly> DomainScanAssemblies(string repositoryRoot)
    {
        var assemblies = new List<Assembly>(ProductAssemblies);

        foreach (var module in DiscoverModules(repositoryRoot))
        {
            assemblies.Add(LoadModuleAssembly(module.Name));
        }

        return assemblies;
    }

    internal static IReadOnlyCollection<string> FailingTypeNames(TestResult result) =>
        result.FailingTypeNames ?? [];

    /// <summary>
    /// 规则④：从源树枚举业务模块工程（src/Pzd.*/*.csproj），排除底座工程与 *.Contracts，
    /// 并解析其 ProjectReference——不扫描输出目录，未接入测试工程的模块也不会漏检（F2）。
    /// </summary>
    internal static IReadOnlyCollection<ModuleProject> DiscoverModules(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var sourceDirectory = Path.Combine(repositoryRoot, "src");

        if (!Directory.Exists(sourceDirectory))
        {
            // 仓库根定位错误时不能静默返回空集：那会让规则④对任何模块都「绿得毫无提示」（同 F2 的漏检形态）。
            throw new InvalidOperationException(
                $"No 'src' directory under repository root '{repositoryRoot}': module discovery would silently pass.");
        }

        return Directory.GetDirectories(sourceDirectory, "Pzd.*")
            .Select(directory => Path.Combine(directory, Path.GetFileName(directory) + ".csproj"))
            .Where(File.Exists)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(projectName => !IsCoreProject(projectName))
            .Where(projectName => !IsContractsProject(projectName))
            .Select(projectName => new ModuleProject(
                projectName,
                ReadProjectReferences(Path.Combine(sourceDirectory, projectName, projectName + ".csproj"))))
            .ToArray();
    }

    /// <summary>
    /// 规则④：模块只能引用底座工程（SharedKernel / Infrastructure）、自身，以及任意模块的 *.Contracts；
    /// 禁止直接引用其他模块本体、Pzd.Api、Pzd.AppHost（F5）。
    /// </summary>
    internal static IReadOnlyCollection<string> ModuleReferenceViolations(
        string moduleName,
        IEnumerable<string> referencedProjectNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentNullException.ThrowIfNull(referencedProjectNames);

        return referencedProjectNames
            .Where(reference => !IsAllowedModuleReference(moduleName, reference))
            .Select(reference => $"{moduleName} -> {reference}")
            .ToArray();
    }

    /// <summary>
    /// 代码检查：产品源码（<c>src/Pzd.*</c>，含未来模块，排除 *.Contracts 与 bin/obj）里不得出现
    /// <c>.ChangeDatabase(</c> 调用——它会在共享客户端上切库、串租户（F-05-2）。
    /// </summary>
    internal static IReadOnlyCollection<string> ChangeDatabaseViolations(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var sourceRoot = Path.Combine(repositoryRoot, "src");

        if (!Directory.Exists(sourceRoot))
        {
            throw new InvalidOperationException(
                $"No 'src' directory under repository root '{repositoryRoot}': ChangeDatabase scan would silently pass.");
        }

        return Directory.GetDirectories(sourceRoot)
            .Where(directory => Path.GetFileName(directory).StartsWith("Pzd.", StringComparison.Ordinal))
            .Where(directory => !Path.GetFileName(directory).EndsWith(ContractsSuffix, StringComparison.Ordinal))
            .SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutputOf(directory, file)))
            .Where(path => File.ReadAllText(path).Contains(".ChangeDatabase(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 代码检查（F-05-8）：后台路径（<c>Persistence/**</c>、<c>*HostedService.cs</c>、<c>*Dispatcher.cs</c>）
    /// 不得调用请求路径专用的 <c>GetCurrentConnectionScopeAsync</c>——它只信任
    /// <see cref="Pzd.SharedKernel.Abstractions.ITenantContext"/>，手工 Set 停用租户后会绕过状态校验。
    /// <para>
    /// 为什么按 API 形状而不是实参名判断：上一版规则靠 <c>ct</c>/<c>token</c>/<c>stoppingToken</c> 之类
    /// 的变量名白名单，换名（<c>var ct2 = cancellationToken;</c>）即可绕过。现在无参重载已改名为
    /// <see cref="Pzd.Infrastructure.Tenancy.ITenantDbProvider.GetCurrentConnectionScopeAsync"/>，
    /// 而 <c>GetConnectionScopeAsync</c> 只剩带 <c>long tenantId</c> 的重载——类型系统本身就堵住了裸取，
    /// 规则只需禁止后台代码出现请求路径 API。
    /// </para>
    /// </summary>
    internal static IReadOnlyCollection<string> ParameterlessTenantConnectionViolations(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var sourceRoot = Path.Combine(repositoryRoot, "src");

        if (!Directory.Exists(sourceRoot))
        {
            throw new InvalidOperationException(
                $"No 'src' directory under repository root '{repositoryRoot}': tenant connection scan would silently pass.");
        }

        var sources = Directory.GetDirectories(sourceRoot)
            .Where(directory => Path.GetFileName(directory).StartsWith("Pzd.", StringComparison.Ordinal))
            .Where(directory => !Path.GetFileName(directory).EndsWith(ContractsSuffix, StringComparison.Ordinal))
            .SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutputOf(directory, file)))
            .Select(path => (Path: Path.GetRelativePath(repositoryRoot, path), Content: File.ReadAllText(path)));

        return ParameterlessTenantConnectionViolationsFromSource(sources);
    }

    /// <summary>F-05-8 规则本体：纯函数版本，反向用例用合成源码验证规则确实会报。</summary>
    internal static IReadOnlyCollection<string> ParameterlessTenantConnectionViolationsFromSource(
        IEnumerable<(string Path, string Content)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return sources
            .Where(source => IsBackgroundPath(source.Path))
            .Where(source => source.Content.Contains(RequestPathConnectionApi, StringComparison.Ordinal))
            .Select(source => source.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>后台路径判定：Persistence 目录，或文件名以 HostedService / Dispatcher 结尾。</summary>
    internal static bool IsBackgroundPath(string path)
    {
        var normalized = path.Replace('\\', '/');

        return normalized.Contains("/Persistence/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("HostedService.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("Dispatcher.cs", StringComparison.OrdinalIgnoreCase);
    }

    private const string RequestPathConnectionApi = ".GetCurrentConnectionScopeAsync(";

    /// <summary>仓库根目录：从测试输出目录向上查找 Pzd.slnx。</summary>
    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pzd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root (Pzd.slnx) not found above the test output directory.");
    }

    private static bool IsAllowedModuleReference(string moduleName, string referencedProjectName) =>
        string.Equals(referencedProjectName, SharedKernelProject, StringComparison.Ordinal)
        || string.Equals(referencedProjectName, InfrastructureProject, StringComparison.Ordinal)
        || string.Equals(referencedProjectName, moduleName, StringComparison.Ordinal)
        || referencedProjectName.EndsWith(ContractsSuffix, StringComparison.Ordinal);

    private static Assembly LoadModuleAssembly(string moduleName)
    {
        try
        {
            return Assembly.Load(moduleName);
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException)
        {
            throw new InvalidOperationException(
                $"模块工程 {moduleName} 存在但其程序集不可加载：请把 {moduleName} 的 ProjectReference 加进 "
                + "Pzd.ArchitectureTests（或先构建解决方案），否则规则③会漏检该模块。",
                exception);
        }
    }

    private static bool IsCoreProject(string projectName) => projectName is
        SharedKernelProject or InfrastructureProject or ApiProject or AppHostProject;

    private static bool IsBuildOutputOf(string projectDirectory, string file)
    {
        var relative = Path.GetRelativePath(projectDirectory, file);
        var firstSegment = relative.Split(Path.DirectorySeparatorChar, 2)[0];

        return firstSegment is "bin" or "obj";
    }

    private static bool IsContractsProject(string projectName) =>
        projectName.EndsWith(ContractsSuffix, StringComparison.Ordinal);

    private static IReadOnlyCollection<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);

        return document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .ToArray();
    }
}

/// <summary>源树中的一个业务模块工程及其 ProjectReference。</summary>
internal sealed record ModuleProject(string Name, IReadOnlyCollection<string> ReferencedProjects);
