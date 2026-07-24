namespace Plutus.SharedKernel;

/// <summary>
/// Money is integer pence end-to-end (architecture §4.1 / spec §1). No decimal/double
/// for money in any layer; formatting is a client concern. EF maps this via a
/// ValueConverter&lt;Pence, long&gt; registered by convention in the owning module's DbContext.
/// </summary>
public readonly record struct Pence(long Value)
{
    public static Pence Zero => new(0);
    public static Pence operator +(Pence a, Pence b) => new(a.Value + b.Value);
    public static Pence operator -(Pence a, Pence b) => new(a.Value - b.Value);
    public static Pence operator *(Pence a, int qty) => new(a.Value * qty);
    public override string ToString() => Value.ToString();
}
