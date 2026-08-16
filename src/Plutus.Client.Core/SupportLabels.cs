namespace Plutus.Client.Core;

/// <summary>
/// What a support ticket's status and severity SAY to an operator (OP4 / WP6.3).
///
/// ⚠ A CLIENT RULE, not a server one. The wire carries the raw enum bytes; turning them into words
/// is a display decision, and it belongs in one place for the same reason `TaxBandLabel` does — two
/// tills in one shop describing the same ticket differently is a support call about the support
/// screen.
///
/// ⚠⚠ MAPPED BY VALUE, NOT BY POSITION. The web till indexes a literal array
/// (`SUPPORT_STATUS[t.status]`), so the day the server's enum gains a member anywhere but the end,
/// every label after it silently shifts by one and a **Closed** ticket reads as **Open**. Naming the
/// values means an unknown one is reported as unknown instead of borrowing its neighbour's word.
/// See till-design.md C2.
/// </summary>
public static class SupportLabels
{
    /// <summary>`Plutus.Entities.Models.SupportStatus` — Open 0, WaitingOnClient 1, Closed 2.</summary>
    public static string Status(byte status) => status switch
    {
        0 => "Open",
        1 => "Waiting on you",   // ⚠ The CLIENT's word for WaitingOnClient — from the till, "the
                                 // client" is the person reading the screen.
        2 => "Closed",

        // ⚠ NEVER a blank or a wrong word. A status this build has not been taught is still a fact
        // about the ticket, and showing the number lets somebody report it usefully.
        _ => $"Status {status}",
    };

    /// <summary>`Plutus.Entities.Models.SupportSeverity` — Question 0, Problem 1, Urgent 2.</summary>
    public static string Severity(byte severity) => severity switch
    {
        0 => "Question",
        1 => "Problem",
        2 => "Urgent",
        _ => $"Severity {severity}",
    };

    /// <summary>
    /// ⚠ A CLOSED TICKET TAKES NO REPLIES, and the till says so rather than offering a box whose
    /// Send would be refused. Named here so the check is not a `== 2` scattered across screens.
    /// </summary>
    public static bool IsClosed(byte status) => status == 2;

    /// <summary>The severity a till raises when the operator has not said it is urgent.</summary>
    /// <remarks>⚠ `Problem`, not `Question` — matching the web till, which sends 1 unless the Urgent
    /// box is ticked. Somebody interrupting a shift to type a ticket has a problem.</remarks>
    public const byte DefaultSeverity = 1;

    /// <summary>The severity for "this is stopping us trading".</summary>
    public const byte UrgentSeverity = 2;
}
