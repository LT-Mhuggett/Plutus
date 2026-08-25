using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// ⚠⚠ THE FAILURE MODE HERE IS SILENT AND ONE-WAY. If the pattern misses a spelling, the credential
/// is printed and nothing complains; the only signal is somebody reading a log months later. So the
/// vectors below are deliberately about the spellings and the shapes, not about the happy path.
///
/// The real string this was written for is the live one:
/// `Server=/tmp/mysql.sock;ConnectionProtocol=unix;User=plutus;Password=…;Database=plutus;…`
/// </summary>
public class RedactTests
{
    [Fact]
    public void The_live_connection_string_shape_loses_its_password_and_keeps_everything_else()
    {
        const string cs = "Server=/tmp/mysql.sock;ConnectionProtocol=unix;User=plutus;"
                        + "Password=1e6653614dfec642a83f045da3d8ee6c;Database=plutus;Persist Security Info=false";

        var red = Redact.ConnectionString(cs);

        Assert.DoesNotContain("1e6653614dfec642a83f045da3d8ee6c", red);
        Assert.Contains(Redact.Mask, red);
        // ⚠ The half worth keeping — this line exists to answer "which database am I pointing at".
        Assert.Contains("Server=/tmp/mysql.sock", red);
        Assert.Contains("ConnectionProtocol=unix", red);
        Assert.Contains("Database=plutus", red);
    }

    [Theory]
    [InlineData("Password=hunter2")]
    [InlineData("password=hunter2")]
    [InlineData("PASSWORD=hunter2")]
    [InlineData("Pwd=hunter2")]
    [InlineData("pwd=hunter2")]
    [InlineData("User Password=hunter2")]
    [InlineData("Password = hunter2")]
    [InlineData("Password  =hunter2")]
    public void Every_spelling_of_the_secret_is_caught(string fragment)
    {
        var red = Redact.ConnectionString("Server=x;" + fragment + ";Database=y");
        Assert.DoesNotContain("hunter2", red);
        Assert.Contains("Server=x", red);
        Assert.Contains("Database=y", red);
    }

    [Fact]
    public void The_match_stops_at_the_separator_and_does_not_eat_the_rest()
    {
        // ⚠ A greedy `.*` here would hide Database= too, which is the diagnostic being preserved.
        var red = Redact.ConnectionString("Password=abc;Database=plutus;Connect Timeout=300");
        Assert.Contains("Database=plutus", red);
        Assert.Contains("Connect Timeout=300", red);
        Assert.DoesNotContain("abc", red);
    }

    [Fact]
    public void A_password_at_the_very_end_with_no_trailing_separator_is_still_caught()
    {
        var red = Redact.ConnectionString("Server=x;Password=abc");
        Assert.DoesNotContain("abc", red);
    }

    [Fact]
    public void An_empty_password_is_handled_without_throwing()
    {
        var red = Redact.ConnectionString("Server=x;Password=;Database=y");
        Assert.Contains("Database=y", red);
    }

    [Fact]
    public void Null_and_empty_are_returned_unchanged_rather_than_throwing()
    {
        // ⚠ This runs at boot, before anything else. It must never be the reason the app fails to
        // start — an unset connection string is a config problem to report, not an exception here.
        Assert.Equal(string.Empty, Redact.ConnectionString(null));
        Assert.Equal(string.Empty, Redact.ConnectionString(""));
    }

    [Fact]
    public void A_string_with_no_secret_is_left_alone()
    {
        const string cs = "Server=/tmp/mysql.sock;Database=plutus";
        Assert.Equal(cs, Redact.ConnectionString(cs));
    }

    [Fact]
    public void A_field_that_merely_CONTAINS_the_word_is_not_mangled()
    {
        // ⚠ `\b` on the alternation: "Persist Security Info" and friends must survive intact, or the
        // redaction quietly corrupts the diagnostic it exists to preserve.
        var red = Redact.ConnectionString("Server=x;Persist Security Info=false;Database=y");
        Assert.Contains("Persist Security Info=false", red);
    }
}
