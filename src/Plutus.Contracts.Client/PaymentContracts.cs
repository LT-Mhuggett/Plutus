namespace Plutus.Contracts.Client;

/// <summary>
/// GET /api/v1/payments/gateway/active — which card gateway this tenant has selected, and whether
/// a terminal integration is actually wired for it.
///
/// ⚠ <see cref="Integrated"/> is the field that matters, and it is <b>false for every provider
/// today</b> — terminal integration is greenfield across the whole platform. A non-standalone
/// <see cref="Provider"/> therefore means "this tenant has *chosen* Worldpay", not "this till can
/// drive a Worldpay terminal". A till that read <see cref="Provider"/> alone would wait for a
/// terminal that is never going to answer, with a customer standing in front of it.
///
/// ⚠ Never carries credentials. The configuration endpoint that does is gated on
/// <c>portal.company.manage</c> and a till has no business calling it.
///
/// <para><see cref="SurchargeBp"/> / <see cref="SurchargeFlatPence"/> are the tenant's card
/// surcharge (percent in basis points + flat pence; both zero = none, the default — surcharging
/// consumer cards is banned in the UK). ⚠ The fee's VAT is NOT carried here and must never be: it
/// follows the basket the fee rides on (<c>SharedKernel.CardSurchargeVat</c>), so it cannot be
/// known until the basket exists.</para>
/// </summary>
public sealed record ActiveGatewayDto(
    string Provider, string Label, bool Integrated,
    int SurchargeBp = 0, long SurchargeFlatPence = 0);
