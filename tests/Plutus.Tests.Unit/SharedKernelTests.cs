using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

public class Uuid7Tests
{
    [Fact]
    public void Sequential_calls_are_strictly_ordered_by_leading_timestamp_bytes()
    {
        var ids = new Guid[1000];
        for (var i = 0; i < ids.Length; i++) ids[i] = Uuid7.New();

        for (var i = 1; i < ids.Length; i++)
        {
            var prev = ids[i - 1].ToByteArray(bigEndian: true).AsSpan(0, 6);
            var curr = ids[i].ToByteArray(bigEndian: true).AsSpan(0, 6);
            Assert.True(curr.SequenceCompareTo(prev) > 0,
                $"UUIDv7 #{i} timestamp bytes not strictly greater than #{i - 1}");
        }
    }

    [Fact]
    public void Version_nibble_is_7_and_variant_is_rfc4122()
    {
        for (var i = 0; i < 100; i++)
        {
            var b = Uuid7.New().ToByteArray(bigEndian: true);
            Assert.Equal(0x70, b[6] & 0xF0);          // version 7
            Assert.Equal(0x80, b[8] & 0xC0);          // variant 10xx
        }
    }

    [Fact]
    public void Ids_are_unique()
    {
        var set = new HashSet<Guid>();
        for (var i = 0; i < 10_000; i++) Assert.True(set.Add(Uuid7.New()));
    }
}

public class PenceTests
{
    [Fact]
    public void Add_subtract_multiply_operate_in_integer_pence()
    {
        Assert.Equal(new Pence(300), new Pence(100) + new Pence(200));
        Assert.Equal(new Pence(150), new Pence(400) - new Pence(250));
        Assert.Equal(new Pence(597), new Pence(199) * 3);
        Assert.Equal(Pence.Zero, new Pence(0));
    }

    [Fact]
    public void ToString_is_bare_integer_no_currency_formatting()
    {
        Assert.Equal("1299", new Pence(1299).ToString());
    }
}
