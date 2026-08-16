using Plutus.Client.Core;
using Plutus.Entities.Models;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// What a support ticket says to an operator (OP4 / WP6.3).
///
/// ⚠⚠ The point of these is the UNKNOWN value. The web till indexes a literal array by the enum
/// byte, so a member added anywhere but the end of the server enum shifts every label after it and a
/// **Closed** ticket silently reads as **Open**. Mapping by value means an unknown one says so.
/// </summary>
public class SupportLabelsTests
{
    /// <summary>
    /// ⚠⚠ THE LABELS ARE PINNED TO THE SERVER ENUM ITSELF, not to the numbers I happened to type.
    /// If somebody renumbers `SupportStatus`, this fails here rather than mislabelling a ticket on
    /// a shop floor.
    /// </summary>
    [Fact]
    public void Every_status_the_server_defines_has_its_own_word()
    {
        Assert.Equal("Open", SupportLabels.Status((byte)SupportStatus.Open));
        Assert.Equal("Waiting on you", SupportLabels.Status((byte)SupportStatus.WaitingOnClient));
        Assert.Equal("Closed", SupportLabels.Status((byte)SupportStatus.Closed));
    }

    [Fact]
    public void Every_severity_the_server_defines_has_its_own_word()
    {
        Assert.Equal("Question", SupportLabels.Severity((byte)SupportSeverity.Question));
        Assert.Equal("Problem", SupportLabels.Severity((byte)SupportSeverity.Problem));
        Assert.Equal("Urgent", SupportLabels.Severity((byte)SupportSeverity.Urgent));
    }

    /// <summary>⚠ No two statuses may share a word — that is exactly the failure the positional
    /// array produces, and it is invisible on screen.</summary>
    [Fact]
    public void No_two_statuses_read_the_same()
    {
        var words = new[] { SupportLabels.Status(0), SupportLabels.Status(1), SupportLabels.Status(2) };

        Assert.Equal(words.Length, words.Distinct().Count());
    }

    /// <summary>
    /// ⚠⚠ AN UNKNOWN VALUE SAYS SO, and never borrows its neighbour's word. A later backend adding
    /// a status this build has not been taught must not turn into a wrong-but-plausible label — the
    /// number is at least reportable.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(255)]
    public void An_unknown_status_shows_its_number_rather_than_a_wrong_word(byte unknown)
    {
        var label = SupportLabels.Status(unknown);

        Assert.Contains(unknown.ToString(), label);
        Assert.NotEqual("Open", label);
        Assert.NotEqual("Closed", label);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(255)]
    public void An_unknown_severity_shows_its_number_rather_than_a_wrong_word(byte unknown)
    {
        var label = SupportLabels.Severity(unknown);

        Assert.Contains(unknown.ToString(), label);
        Assert.NotEqual("Urgent", label);
    }

    /// <summary>⚠ Only Closed is closed. Marking "Waiting on you" as closed would hide the reply box
    /// at the exact moment the ticket is waiting for the operator to type in it.</summary>
    [Fact]
    public void Only_a_closed_ticket_counts_as_closed()
    {
        Assert.True(SupportLabels.IsClosed((byte)SupportStatus.Closed));
        Assert.False(SupportLabels.IsClosed((byte)SupportStatus.Open));
        Assert.False(SupportLabels.IsClosed((byte)SupportStatus.WaitingOnClient));
    }

    /// <summary>⚠ And an unknown status is NOT closed — the reply box stays. Refusing to let someone
    /// answer a ticket because this build does not recognise its status is the worse mistake.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(255)]
    public void An_unknown_status_still_takes_a_reply(byte unknown) =>
        Assert.False(SupportLabels.IsClosed(unknown));

    /// <summary>
    /// ⚠ The default severity matches the web till, which sends 1 unless Urgent is ticked (binding
    /// default 10). Somebody interrupting a shift to type a ticket has a Problem, not a Question.
    /// </summary>
    [Fact]
    public void The_default_and_urgent_severities_match_the_server_enum()
    {
        Assert.Equal((byte)SupportSeverity.Problem, SupportLabels.DefaultSeverity);
        Assert.Equal((byte)SupportSeverity.Urgent, SupportLabels.UrgentSeverity);
    }
}
