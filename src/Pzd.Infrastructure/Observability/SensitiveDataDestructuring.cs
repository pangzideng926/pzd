using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Pzd.Infrastructure.Observability;

/// <summary>
/// 日志脱敏的唯一出处（架构 §9.2）：password / token / 手机号 / 身份证号在结构化日志里被替换成 <see cref="Mask"/>。
/// <para>
/// 覆盖两种入口：① 对象属性（<c>{@User}</c> 或审计参数这类结构化对象）按属性名与属性值判定；
/// ② 字典键值。判定只看名字/值本身，不依赖调用方自觉传脱敏后的数据。
/// </para>
/// </summary>
public sealed partial class SensitiveDataDestructuring : IDestructuringPolicy
{
    /// <summary>脱敏替换值。</summary>
    public const string Mask = "***";

    private static readonly string[] SensitiveNameFragments =
    [
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "credential",
        "apikey",
        "authorization",
        "cookie",
    ];

    private static readonly HashSet<string> SensitiveExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "phone",
        "mobile",
        "phonenumber",
        "telephone",
        "tel",
        "idcard",
        "idnumber",
        "identitynumber",
        "identitycard",
    };

    /// <summary>中国大陆手机号：1 开头、第二位 3–9、共 11 位。</summary>
    [GeneratedRegex(@"^1[3-9]\d{9}$")]
    private static partial Regex PhoneNumber();

    /// <summary>18 位身份证号（末位可为 X）。</summary>
    [GeneratedRegex(@"^\d{17}[\dXx]$")]
    private static partial Regex IdCardNumber();

    /// <inheritdoc />
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        result = null!;

        // 标量/字符串交给默认处理；集合本身交给默认序列化，元素各自再走本策略。
        var type = value.GetType();

        if (value is string || type.IsPrimitive || type.IsEnum)
        {
            return false;
        }

        if (value is IDictionary dictionary)
        {
            var entries = new List<LogEventProperty>(dictionary.Count);

            foreach (DictionaryEntry entry in dictionary)
            {
                var name = entry.Key?.ToString() ?? string.Empty;
                entries.Add(CreateProperty(name, entry.Value, propertyValueFactory));
            }

            result = new StructureValue(entries, type.Name);
            return true;
        }

        if (value is IEnumerable)
        {
            return false;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        if (properties.Length == 0)
        {
            return false;
        }

        var captured = new List<LogEventProperty>(properties.Length);

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? propertyValue;

            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            captured.Add(CreateProperty(property.Name, propertyValue, propertyValueFactory));
        }

        result = new StructureValue(captured, type.Name);
        return true;
    }

    /// <summary>属性名或值命中敏感规则：明文替换成 <see cref="Mask"/>，其余递归结构化。</summary>
    public static LogEventProperty CreateProperty(string name, object? value, ILogEventPropertyValueFactory propertyValueFactory)
    {
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        return IsSensitiveName(name) || IsSensitiveValue(value)
            ? new LogEventProperty(name, new ScalarValue(Mask))
            : new LogEventProperty(name, propertyValueFactory.CreatePropertyValue(value, destructureObjects: true));
    }

    /// <summary>属性名脱敏判定：password/token/secret 等关键片段，或 phone/idcard 等精确名。</summary>
    public static bool IsSensitiveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (SensitiveExactNames.Contains(normalized))
        {
            return true;
        }

        foreach (var fragment in SensitiveNameFragments)
        {
            if (normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>属性值脱敏判定：手机号 / 身份证号，字符串形态。</summary>
    public static bool IsSensitiveValue(object? value) =>
        value is string text
        && (PhoneNumber().IsMatch(text) || IdCardNumber().IsMatch(text));
}
