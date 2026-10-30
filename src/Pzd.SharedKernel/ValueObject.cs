namespace Pzd.SharedKernel;

/// <summary>
/// 值对象基类：不可变、按值相等（架构书 §3.3）。
/// 子类把所有参与相等判断的分量放进 <see cref="GetEqualityComponents"/>，类型不同一律不相等。
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>参与相等与哈希的分量，按顺序逐项比较，两侧顺序必须一致。</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null || other.GetType() != GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);
}
