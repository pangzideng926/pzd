using System.Diagnostics;
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
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }

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
