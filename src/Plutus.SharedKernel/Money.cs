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

    /// <summary>
    /// MAUI retrofit WP2: convert a LEGACY decimal amount to pence, for the one place decimals
    /// legitimately still arrive — reading the old Kapow-schema database during cutover.
    ///
    /// Away-from-zero, because banker's rounding (.NET's default) would turn £0.125 into 12p and
    /// £0.135 into 14p: defensible statistically, indefensible on a receipt where the customer can
    /// see the arithmetic. Not for new code — money is minted in pence everywhere else.
    /// </summary>
    public static long FromDecimal(decimal amount) =>
        (long)System.Math.Round(amount * 100m, System.MidpointRounding.AwayFromZero);
}
