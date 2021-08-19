using System;

namespace Plutus.Entities.Models.Interface
{
    public interface ITill : IAuditable
    {
        decimal CashFloat { get; set; }
        DateTime LastOnline { get; set; }
    }
}
