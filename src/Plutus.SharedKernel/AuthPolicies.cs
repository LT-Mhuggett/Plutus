namespace Plutus.SharedKernel;

/// <summary>Authorization policy / scope names for the platform's scope-based endpoints (T1.2).
/// Shared here so both the Identity module (which defines the policies) and feature modules
/// (which apply [Authorize(Policy = …)]) reference one source without a module→module dependency.</summary>
public static class PlutusPolicies
{
    public const string PlatformAdmin = "platform-admin";
    public const string PortalTillsEnrol = "portal.tills.enrol";
    public const string Device = "device";

    /// <summary>Scope granting sale submission (operator web-POS).</summary>
    public const string PosSell = "pos.sell";
    /// <summary>Ingest policy: satisfied by a device token (scope "device") OR an operator
    /// token with "pos.sell".</summary>
    public const string SalesIngest = "sales.ingest";

    /// <summary>WP11.1 rename policy: satisfied by a portal admin ("portal.tills.enrol") OR the
    /// till's own device token ("device"), so a till can be named from the portal AND from itself.</summary>
    public const string TillsName = "tills.name";

    /// <summary>
    /// Un-enrolment REQUEST policy: a portal admin OR the till's own device token.
    ///
    /// ⚠ The endpoint's own doc comment says *"A till asks to be un-enrolled"*, and it was gated on
    /// `portal.tills.enrol` — a scope a DEVICE token cannot carry. So the one caller it was written
    /// for could not call it, and the till's "remove this till" button had nothing to talk to.
    /// ⚠ A device token may only request removal of ITSELF; the controller enforces that, because
    /// otherwise any enrolled till could start the removal of every other till in the estate.
    /// </summary>
    public const string TillsUnenrolRequest = "tills.unenrol-request";
}
