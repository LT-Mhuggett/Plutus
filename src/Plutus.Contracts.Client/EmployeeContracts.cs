using System;

namespace Plutus.Contracts.Client;

/// <summary>
/// An employee as the till's Users screen needs them — the LEGACY `/api/Employee` shape, narrowed
/// to the same six fields the web till reads (`api.ts Employee`).
///
/// ⚠ NARROWED ON PURPOSE. The legacy entity carries wage, NIN, contracted hours and a full address;
/// none of it belongs on a till screen, and a till that deserialised them would be one careless
/// binding away from putting somebody's National Insurance number on a shop floor.
///
/// ⚠ ROLES AND PERMISSIONS ARE NOT HERE, and that is the parity target, not an omission. The till's
/// surface is the web till's — who exists and what their password is. **Who may do what stays in the
/// portal**, where the change is audited and where the person making it can see the whole estate.
/// </summary>
public sealed record EmployeeDto(
    Guid Id,
    string FName,
    string LName,
    string Email,
    string Mobile,
    bool Active)
{
    /// <summary>⚠ Trimmed, because the legacy rows are full of padding and a list of names with
    /// ragged leading spaces reads as broken.</summary>
    public string DisplayName => $"{FName} {LName}".Trim();
}

/// <summary>
/// Create an employee on the legacy controller.
///
/// ⚠⚠ THE ID IS MINTED BY THE CLIENT. The legacy POST does not generate one, so the web till does
/// `crypto.randomUUID()` and sends it (`api.ts createEmployee`); a till that omitted it would get a
/// row keyed on `Guid.Empty` — and the SECOND such create would collide with the first.
///
/// ⚠⚠ AND THE PLACEHOLDERS ARE LOAD-BEARING. `nin`, `adLine1`, `city`, `postCode`, `country` are
/// NOT NULL on the legacy table, so they go as <c>"-"</c> exactly as the web till sends them.
/// Leaving them out fails the insert with a message about a column nobody on a shop floor has heard
/// of. ⚠ They are deliberately NOT real values: an address typed into a till would be wrong and
/// nobody would ever correct it. The portal is where an employee record gets filled in properly.
/// </summary>
public sealed record CreateEmployeeRequest(
    Guid Id,
    string FName,
    string LName,
    string Email,
    string Mobile,
    Guid BusinessId,
    int StoreId)
{
    public string Nin => "-";
    public decimal Wage => 0;
    public decimal ContractedHours => 0;
    public bool Active => true;
    public string AdLine1 => "-";
    public string AdLine2 => "";
    public string City => "-";
    public string PostCode => "-";
    public string Country => "-";
}

/// <summary>
/// Set or reset an employee's password — `POST /api/Auth/SetPassword`.
///
/// ⚠ THE EMAIL GOES TOO, not just the id. The legacy endpoint matches on both (the web till sends
/// both), and sending an id alone silently sets nothing.
/// </summary>
public sealed record SetPasswordRequest(Guid EmployeeId, string Email, string Password);
