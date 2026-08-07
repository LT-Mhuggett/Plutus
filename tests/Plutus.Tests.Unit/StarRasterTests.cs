using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Plutus.TillAgent.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// FE3.1 Star raster rendering — the language of the TSP100/TSP143 (futurePRNT) family, which
/// has no text mode at all. These bytes go straight to hardware, so the framing is pinned
/// byte-for-byte against Star's Graphic Mode spec (Rev 2.32): ASCII-decimal parameters,
/// continuous page length ("P0" — the property that ends the blank-paper feeds), buffer-empty
/// ordering rules, MSB-leftmost row packing.
/// </summary>
public class StarRasterTests
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] Param(char cmd, string asciiDigits) =>
        new byte[] { 0x1B, (byte)'*', (byte)'r', (byte)cmd }.Concat(Ascii(asciiDigits)).Append((byte)0x00).ToArray();

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { hit = false; break; }
            if (hit) return true;
        }
        return false;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { hit = false; break; }
            if (hit) return i;
        }
        return -1;
    }

    [Fact]
    public void Job_frame_is_enter_settings_rows_eot_quit_in_that_order()
    {
        var row = new byte[] { 0xFF, 0x01 };
        var job = StarRaster.RenderJob(new List<byte[]> { row }, new StarRaster.Options());

        var enterInit = IndexOf(job, new byte[] { 0x1B, (byte)'*', (byte)'r', (byte)'R' });
        var enter = IndexOf(job, new byte[] { 0x1B, (byte)'*', (byte)'r', (byte)'A' });
        var pageLen = IndexOf(job, Param('P', "0"));
        var eotMode = IndexOf(job, Param('E', "13"));
        var data = IndexOf(job, new byte[] { (byte)'b', 2, 0, 0xFF, 0x01 });
        var eot = IndexOf(job, new byte[] { 0x1B, 0x0C, 0x04 });
        var quit = IndexOf(job, new byte[] { 0x1B, (byte)'*', (byte)'r', (byte)'B' });

        Assert.True(enterInit >= 0, "initialise raster mode (ESC*rR)");
        Assert.True(enter > enterInit, "enter raster mode after initialise");
        // ⚠ ordering rule from the spec: settings are IGNORED once row data is buffered
        Assert.True(pageLen > enter && pageLen < data, "continuous page length between enter and data");
        Assert.True(eotMode > enter && eotMode < data, "EOT/cut mode between enter and data");
        Assert.True(eot > data, "ESC FF EOT after the rows");
        Assert.True(quit > eot, "quit raster mode last");
    }

    /// <summary>"P0" (continuous) is what makes the printer feed only what was printed — the
    /// whole reason the raster path exists. A fixed page length here would reintroduce the
    /// blank-paper tail the browser/driver path suffered from.</summary>
    [Fact]
    public void Page_length_is_ascii_zero_continuous_not_binary()
    {
        var job = StarRaster.RenderJob(new List<byte[]> { new byte[] { 0x80 } }, new StarRaster.Options());
        Assert.True(Contains(job, Param('P', "0")));
    }

    /// <summary>The cut parameter is ASCII "13" (0x31 0x33), never a binary 13 — the spec's most
    /// missable detail, and the difference between a cut and a hung job.</summary>
    [Fact]
    public void Cut_mode_is_ascii_decimal_thirteen()
    {
        var job = StarRaster.RenderJob(new List<byte[]> { new byte[] { 0x80 } }, new StarRaster.Options());
        Assert.True(Contains(job, new byte[] { 0x1B, (byte)'*', (byte)'r', (byte)'E', 0x31, 0x33, 0x00 }));
        Assert.False(Contains(job, new byte[] { 0x1B, (byte)'*', (byte)'r', (byte)'E', 13, 0x00 }));
    }

    [Fact]
    public void Blank_row_runs_become_one_move_down_command_and_trailing_blanks_are_dropped()
    {
        var ink = new byte[] { 0xF0 };
        var blank = Array.Empty<byte>();
        var job = StarRaster.RenderJob(new List<byte[]> { ink, blank, blank, blank, ink, blank, blank }, new StarRaster.Options());

        Assert.True(Contains(job, Param('Y', "3")), "3-row blank run coalesced into Y3");
        // trailing blanks produce nothing: no Y2 anywhere
        Assert.False(Contains(job, Param('Y', "2")), "trailing blanks are dropped, not fed");
        // exactly two data rows
        var count = 0;
        for (var i = 0; i < job.Length - 1; i++)
            if (job[i] == (byte)'b' && job[i + 1] == 1 && job[i + 2] == 0) count++;
        Assert.Equal(2, count);
    }

    [Fact]
    public void Drawer_job_kicks_via_raster_D_command_with_pulse_setup()
    {
        var job = StarRaster.DrawerOnlyJob();
        Assert.True(Contains(job, new byte[] { 0x1B, 0x07, 0x14, 0x14 }), "pulse timing 200ms/200ms");
        Assert.True(Contains(job, Param('D', "1")), "drive drawer 1");
        // and a print job WITHOUT the drawer flag must not kick it
        var quiet = StarRaster.RenderJob(new List<byte[]> { new byte[] { 0x80 } }, new StarRaster.Options { OpenDrawer = false });
        Assert.False(Contains(quiet, Param('D', "1")));
    }

    [Fact]
    public void Row_packing_is_msb_leftmost_and_trims_trailing_white()
    {
        // dot 0 and dot 9 black → byte0 = 1000_0000, byte1 = 0100_0000
        var dots = new bool[24];
        dots[0] = true;
        dots[9] = true;
        var packed = StarRaster.PackRow(dots);
        Assert.Equal(new byte[] { 0x80, 0x40 }, packed); // byte 2 (all white) trimmed

        Assert.Empty(StarRaster.PackRow(new bool[16])); // all-white row = empty
    }

    [Fact]
    public void Code39_star_sentinel_pattern_is_correct_and_fits_shrink_to_width()
    {
        // '*' = 010010100: n W n n W n W n n (bar,space,bar,space,bar,space,bar,space,bar)
        // at (narrow=2, wide=6): bars at 0-1(n), spaces 2-7(W), bar 8-9(n), space 10-11, bar 12-17(W)...
        var dots = Code39.Dots("A", 576)!;
        // widest candidate (2,6) fits "A" easily: 3 chars * (3*6+7*2) - 2 = 94 dots
        Assert.Equal(94, dots.Length);
        Assert.True(dots[0] && dots[1] && !dots[2], "leading narrow bar of '*'");

        // a 36-char UUID must still fit 576 dots by dropping to thinner modules
        var uuid = Code39.Dots("0198aaaa-bbbb-cccc-dddd-eeeeffff0000", 576);
        Assert.NotNull(uuid);
        Assert.True(uuid!.Length <= 576, $"uuid barcode {uuid.Length} dots must fit 576");

        // impossible width → null, not an exception
        Assert.Null(Code39.Dots("0198aaaa-bbbb-cccc-dddd-eeeeffff0000", 100));
    }

    [Fact]
    public void Emulation_resolver_recognises_the_futureprnt_family_only()
    {
        Assert.Equal(EmulationResolver.StarRasterMode, EmulationResolver.Resolve("auto", "Star TSP100 Cutter (TSP143)"));
        Assert.Equal(EmulationResolver.StarRasterMode, EmulationResolver.Resolve("auto", "Star TSP113 (TSP100)"));
        // the TSP100IV is StarPRNT/ESC-POS — NOT raster-only futurePRNT
        Assert.Equal(EmulationResolver.EscPos, EmulationResolver.Resolve("auto", "Star TSP100IV (STR-001)"));
        Assert.Equal(EmulationResolver.EscPos, EmulationResolver.Resolve("auto", "EPSON TM-T20III Receipt"));
        Assert.Equal(EmulationResolver.EscPos, EmulationResolver.Resolve("auto", ""));
        // explicit settings win over the name
        Assert.Equal(EmulationResolver.EscPos, EmulationResolver.Resolve("escpos", "Star TSP100 Cutter (TSP143)"));
        Assert.Equal(EmulationResolver.StarRasterMode, EmulationResolver.Resolve("star-raster", "Some Renamed Queue"));
    }
}
