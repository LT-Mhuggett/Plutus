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
/// </summary>
public sealed record ActiveGatewayDto(string Provider, string Label, bool Integrated);
