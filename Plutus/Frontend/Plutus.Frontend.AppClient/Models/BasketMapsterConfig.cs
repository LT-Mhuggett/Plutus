using Mapster;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.AppClient.Models
{
    internal static class BasketMapsterConfig
    {
        [ModuleInitializer]
        internal static void Configure()
        {
            // Neither type has a parameterless constructor, and Mapster (unlike AutoMapper)
            // doesn't attempt constructor-parameter matching unless told to. Explicit configs
            // per direction also avoid Mapster's base/derived-type fast path, which can otherwise
            // attempt an invalid direct cast instead of a member-wise map for types in the same
            // inheritance chain (BasketReturnItem : BasketItem).
            TypeAdapterConfig<BasketItem, BasketReturnItem>.NewConfig()
                .MapToConstructor(true);
            TypeAdapterConfig<BasketReturnItem, BasketItem>.NewConfig()
                .MapToConstructor(true);
        }
    }
}
