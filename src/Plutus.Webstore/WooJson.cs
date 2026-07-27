using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plutus.Webstore
{
    /// <summary>One serializer config for every Woo payload we read (webhook bodies, REST poll
    /// responses, tests) — Woo mixes numbers-as-strings freely, so both paths must parse alike.</summary>
    public static class WooJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
    }
}
