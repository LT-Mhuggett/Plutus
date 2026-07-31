using System.Linq;
using System.Text;
using Plutus.TillAgent.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// FE3 ESC/POS rendering. This is the part of the hardware agent that can be pinned WITHOUT a
/// printer, and it is where a silent bug does the most damage: a missed mode-reset bleeds
/// double-height text down the rest of the receipt, and a wrong £ byte makes every price line
/// unreadable on paper while looking perfect on screen.
/// </summary>
public class EscPosTests
{
    private const byte ESC = 0x1B;
    private const byte GS = 0x1D;

    private static byte[] Render(params PrintOp[] ops) =>
        EscPos.Render(new PrintDocument { Ops = ops.ToList(), Columns = 42 });

    /// <summary>Every job initialises the printer — otherwise it inherits whatever mode the last
    /// job left behind.</summary>
    [Fact]
    public void Every_document_starts_by_initialising_the_printer()
    {
        var bytes = Render(PrintOp.Line("hi"));
        Assert.Equal(ESC, bytes[0]);
        Assert.Equal((byte)'@', bytes[1]);
    }

    /// <summary>⚠ The bleed test. Bold/underline/double-height must be turned back OFF after the
    /// line that asked for them.</summary>
    [Fact]
    public void Text_attributes_are_reset_after_the_line_that_used_them()
    {
        var bytes = Render(PrintOp.Line("TOTAL", bold: true, large: true, underline: true));

        Assert.True(Contains(bytes, ESC, (byte)'E', 1), "bold on");
        Assert.True(Contains(bytes, ESC, (byte)'E', (byte)'0'), "bold OFF");
        Assert.True(Contains(bytes, ESC, (byte)'-', 1), "underline on");
        Assert.True(Contains(bytes, ESC, (byte)'-', (byte)'0'), "underline OFF");
        Assert.True(Contains(bytes, GS, (byte)'!', 0x11), "double width+height on");
        Assert.True(Contains(bytes, GS, (byte)'!', (byte)'0'), "double width+height OFF");
    }

    /// <summary>A plain line must not emit any attribute command at all.</summary>
    [Fact]
    public void A_plain_line_emits_no_attribute_commands()
    {
        var bytes = Render(PrintOp.Line("plain"));
        Assert.False(Contains(bytes, ESC, (byte)'E', 1));
        Assert.False(Contains(bytes, GS, (byte)'!', 0x11));
        Assert.False(Contains(bytes, ESC, (byte)'-', 1));
    }

    /// <summary>Centring is restored to left afterwards, or the whole rest of the receipt centres.</summary>
    [Fact]
    public void Alignment_returns_to_left_after_a_centred_line()
    {
        var bytes = Render(PrintOp.Line("Kapow Comics", PrintAlign.Centre));
        Assert.True(Contains(bytes, ESC, (byte)'a', 1), "centre");
        Assert.True(Contains(bytes, ESC, (byte)'a', (byte)'0'), "back to left");
    }

    /// <summary>
    /// ⚠ £ is 0x9C in CP437, NOT the UTF-8 pair. Getting this wrong prints garbage on every price
    /// line while the on-screen receipt looks perfect — exactly the bug that survives a demo.
    /// </summary>
    [Fact]
    public void Pound_signs_and_accents_encode_to_cp437_not_utf8()
    {
        Assert.Equal(new byte[] { 0x9C }, EscPos.Encode("£"));
        Assert.Equal(new byte[] { 0x9C, (byte)'1', (byte)'2', (byte)'.', (byte)'3', (byte)'4' }, EscPos.Encode("£12.34"));
        Assert.Equal(new byte[] { 0x82 }, EscPos.Encode("é"));

        // and NOT what a naive UTF-8 encoder would produce
        Assert.NotEqual(Encoding.UTF8.GetBytes("£"), EscPos.Encode("£"));
    }

    /// <summary>Typographic characters the web UI produces must degrade to something printable
    /// rather than a stream of '?'.</summary>
    [Theory]
    [InlineData("2 × Batman", "2 x Batman")]
    [InlineData("don’t", "don't")]
    [InlineData("“quoted”", "\"quoted\"")]
    [InlineData("a — b", "a - b")]
    public void Typographic_characters_degrade_to_ascii(string input, string expected)
        => Assert.Equal(EscPos.Encode(expected), EscPos.Encode(input));

    [Fact]
    public void An_unmappable_character_becomes_a_question_mark_not_a_wrong_glyph()
        => Assert.Equal(new byte[] { (byte)'?' }, EscPos.Encode("漢"));

    /// <summary>The cut feeds first — cutting flush with the last line takes the text with it.</summary>
    [Fact]
    public void Cut_feeds_the_paper_clear_of_the_cutter_first()
    {
        var bytes = Render(PrintOp.Cut());
        var cutAt = IndexOf(bytes, GS, (byte)'V');
        Assert.True(cutAt > 0, "cut command present");
        Assert.True(bytes.Skip(2).Take(cutAt - 2).Count(b => b == 0x0A) >= 3, "feeds before the cut");
    }

    [Fact]
    public void The_drawer_kick_is_the_standard_pulse()
    {
        Assert.Equal(new byte[] { ESC, (byte)'p', 0, 25, 250 }, EscPos.DrawerKick());
        // and a document flagged OpenDrawer ends with it, so a cash sale is one round trip
        var doc = new PrintDocument { Ops = { PrintOp.Line("x") }, OpenDrawer = true };
        var bytes = EscPos.Render(doc);
        Assert.True(Contains(bytes, ESC, (byte)'p', 0));
    }

    [Fact]
    public void A_rule_spans_the_configured_paper_width()
    {
        var bytes = EscPos.Render(new PrintDocument { Ops = { PrintOp.Rule() }, Columns = 32 });
        Assert.Equal(32, bytes.Count(b => b == (byte)'-'));
    }

    [Fact]
    public void Barcodes_are_code39_centred_and_carry_their_length()
    {
        var bytes = EscPos.Render(new PrintDocument { Ops = { PrintOp.Barcode("C0004821", 80) } });
        var at = IndexOf(bytes, GS, (byte)'k');
        Assert.True(at > 0, "barcode command present");
        Assert.Equal(69, bytes[at + 2]);          // CODE39
        Assert.Equal(8, bytes[at + 3]);           // length-prefixed, "C0004821"
        Assert.True(Contains(bytes, GS, (byte)'h', 80), "height honoured");
    }

    /// <summary>Two-column lines are how money lines up on a receipt. The item name gets truncated
    /// when the line is too long — never the amount.</summary>
    [Fact]
    public void Two_column_lines_pad_to_width_and_never_truncate_the_money()
    {
        Assert.Equal("Total" + new string(' ', 42 - 5 - 6) + "£12.34", EscPos.TwoColumn("Total", "£12.34", 42));

        var longName = new string('A', 60);
        var line = EscPos.TwoColumn(longName, "£999.99", 42);
        Assert.Equal(42, line.Length);
        Assert.EndsWith("£999.99", line);
    }

    private static bool Contains(byte[] haystack, params byte[] needle) => IndexOf(haystack, needle) >= 0;

    private static int IndexOf(byte[] haystack, params byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { hit = false; break; }
            if (hit) return i;
        }
        return -1;
    }
}
