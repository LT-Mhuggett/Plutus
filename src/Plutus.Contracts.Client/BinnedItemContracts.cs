using System;

namespace Plutus.Contracts.Client;

/// <summary>
/// One item in the Bin — WP10 #4, 2026-08-22.
///
/// ⚠⚠ IT COMES FROM THE SERVER OR NOT AT ALL. A binned item is a **tombstone** to a till: the
/// catalogue feed sends it as a removal and the till deletes it locally, so a withdrawn product
/// stops scanning even on a till that has been offline since. There is therefore nothing on the
/// device to build this list from.
///
/// ⚠ ONLY THE FIELDS THE BIN SCREEN SHOWS. The legacy `Index` endpoint returns the whole item; this
/// deserialises the handful an operator needs to recognise what they are putting back, and the rest
/// is ignored rather than modelled — a DTO that mirrors a legacy row is one that has to change with
/// it.
/// </summary>
public sealed record BinnedItemDto(
    string IdOne,
    string Name,
    string Brand,
    decimal Price,
    DateTime? BinnedAtUtc);
