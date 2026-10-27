using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pzd.SharedKernel.Results;

namespace Pzd.Infrastructure.Errors;

/// <summary>traceId 的来源：优先 <see cref="Activity.Current"/>，否则用请求的 TraceIdentifier。</summary>
public static class TraceIds
{
    public const string HeaderName = "X-Trace-Id";

    public static string Current(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;
}

/// <summary>
/// 统一 ProblemDetails 的唯一实现：<c>type</c> = <c>https://pzd/errors/{kebab-code}</c>、<c>code</c> 原样、
/// <c>traceId</c> 来自当前请求、<c>errors</c> 键 camelCase，状态码映射也在这里。
/// 中间件短路（租户 400/403）、<c>ToHttpResult</c>、异常处理、框架状态码兜底共用同一份，
/// 避免各写一套 ProblemDetails（老登 F-06-1）。
/// </summary>
public static class ProblemDetailsWriter
{
    /// <summary>架构 §8.3 的状态码映射。</summary>
    public static int MapStatusCode(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.ClientTooOld => StatusCodes.Status426UpgradeRequired,
        ErrorType.TenantUpgrading => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>按错误与状态码构造统一 ProblemDetails（含 code / traceId / camelCase errors）。</summary>
    public static ProblemDetails Create(Error error, int statusCode, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(context);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = error.Message,
            Type = $"https://pzd/errors/{ToKebabCase(error.Code)}",
        };

        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = TraceIds.Current(context);

        if (error.Metadata.TryGetValue("errors", out var errors) && errors is not null)
        {
            // FluentValidation 的 key 是 PascalCase 属性名；对前端契约统一转 camelCase（AC4 / F-06-1）。
            problem.Extensions["errors"] = ToCamelCaseKeys(errors);
        }

        return problem;
    }

    /// <summary>供 <c>AddProblemDetails</c> 的 CustomizeProblemDetails 使用：补齐 code/traceId。</summary>
    public static void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ProblemDetails.Extensions.TryAdd("traceId", TraceIds.Current(context.HttpContext));

        if (!context.ProblemDetails.Extensions.ContainsKey("code"))
        {
            context.ProblemDetails.Extensions["code"] = "Http.Error";
        }

        context.HttpContext.Response.Headers[TraceIds.HeaderName] = TraceIds.Current(context.HttpContext);
    }

    /// <summary>写出 ProblemDetails：状态码、<c>X-Trace-Id</c> 响应头与 JSON body 一次完成。</summary>
    public static async Task WriteAsync(
        HttpContext context,
        ProblemDetails problem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problem);

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.Headers[TraceIds.HeaderName] =
            problem.Extensions.TryGetValue("traceId", out var traceId) ? traceId?.ToString() ?? string.Empty : string.Empty;

        await context.Response
            .WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>错误码 → URL 片段：<c>Identity.User.NameDuplicated</c> → <c>identity.user.name-duplicated</c>。</summary>
    public static string ToKebabCase(string code) =>
        string.Join(
            '.',
            code.Split('.').Select(segment =>
            {
                var builder = new StringBuilder(segment.Length + 4);

                for (var index = 0; index < segment.Length; index++)
                {
                    var character = segment[index];

                    if (index > 0 && char.IsUpper(character))
                    {
                        builder.Append('-');
                    }

                    builder.Append(char.ToLowerInvariant(character));
                }

                return builder.ToString();
            }));

    /// <summary>
    /// 把错误字典的键转成 camelCase：用非泛型 <see cref="IDictionary"/> 重建，覆盖
    /// <c>Dictionary&lt;string, string[]&gt;</c>、<c>ReadOnlyDictionary</c>、<c>Dictionary&lt;string, List&lt;string&gt;&gt;</c>
    /// 等任意字典形态；camelCase 后撞键按「后写覆盖」去重，不抛异常。非字典值原样返回。
    /// </summary>
    private static object? ToCamelCaseKeys(object? errors)
    {
        if (errors is not IDictionary dictionary)
        {
            return errors;
        }

        var converted = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
            {
                return errors;
            }

            converted[JsonNamingPolicy.CamelCase.ConvertName(key)] = entry.Value;
        }

        return converted;
    }
}
