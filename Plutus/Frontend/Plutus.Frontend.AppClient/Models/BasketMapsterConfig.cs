using Mapster;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.AppClient.Models
{
    internal static class BasketMapsterConfig
    {
        [ModuleInitializer]
        internal static void Configure()
        {
            // ⚠⚠ ONE CONFIG, BasketItem → BasketItem, SINCE STEP 11b (2026-08-22). There were two —
            // `BasketItem ↔ BasketReturnItem`, one per direction — and their whole reason for
            // existing was the inheritance chain: Mapster's base/derived fast path would otherwise
            // attempt a direct CAST instead of a member-wise map, and a cast up that chain throws.
            // The subclass is gone, so both the hop and the hazard go with it.
            //
            // ⚠ THE CONFIG THAT REMAINS IS STILL LOAD-BEARING. `BasketItem` has no parameterless
            // constructor, and Mapster does not attempt constructor-parameter matching unless told
            // to — without `MapToConstructor` the copy in `ExecuteReturnSelected` throws at runtime,
            // which is the refund flow.
            TypeAdapterConfig<BasketItem, BasketItem>.NewConfig()
                .MapToConstructor(true);
        }
    }
}
