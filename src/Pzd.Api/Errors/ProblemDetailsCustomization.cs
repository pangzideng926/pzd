using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pzd.SharedKernel.Results;

namespace Pzd.Api.Errors;

/// <summary>traceId 的来源：优先 <see cref="Activity.Current"/>，否则用请求的 TraceIdentifier。</summary>
public static class TraceIds
{
    public const string HeaderName = "X-Trace-Id";

    public static string Current(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;
}

/// <summary>
/// 统一构造 ProblemDetails：<c>type</c> = <c>https://pzd/errors/{kebab-code}</c>、<c>code</c> 原样、
/// <c>traceId</c> 来自当前请求，供 <c>ToHttpResult</c> / 异常处理 / 框架状态码共用。
/// </summary>
public static class ProblemDetailsCustomization
{
    public static ProblemDetails Create(Error error, int statusCode, HttpContext context)
    {
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

    /// <summary>把错误字典的键转成 camelCase；只处理字典形态，其它值原样返回。</summary>
    private static object? ToCamelCaseKeys(object? errors) => errors switch
    {
        null => null,
        IDictionary<string, object?> map => map.ToDictionary(
            pair => JsonNamingPolicy.CamelCase.ConvertName(pair.Key),
            pair => pair.Value,
            StringComparer.Ordinal),
        IDictionary<string, string[]> map => map.ToDictionary(
            pair => JsonNamingPolicy.CamelCase.ConvertName(pair.Key),
            pair => pair.Value,
            StringComparer.Ordinal),
        _ => errors,
    };

    /// <summary>供 <c>AddProblemDetails</c> 的 CustomizeProblemDetails 使用：补齐 code/traceId。</summary>
    public static void Customize(ProblemDetailsContext context)
    {
        context.ProblemDetails.Extensions.TryAdd("traceId", TraceIds.Current(context.HttpContext));

        if (!context.ProblemDetails.Extensions.ContainsKey("code"))
        {
            context.ProblemDetails.Extensions["code"] = "Http.Error";
        }

        context.HttpContext.Response.Headers[TraceIds.HeaderName] = TraceIds.Current(context.HttpContext);
    }

    public static string ToKebabCase(string code) =>
        string.Join(
            '.',
            code.Split('.').Select(segment =>
            {
                var builder = new System.Text.StringBuilder(segment.Length + 4);

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
}
