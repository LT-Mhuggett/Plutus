using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NatApp.Plutus.Helpers.Extensions
{
    public static class DecimalExtensions
    {
        public static decimal Normalize(this decimal value)
        {
            int count = BitConverter.GetBytes(decimal.GetBits(value)[3])[2];
            string normaliserDivider = $"1.0{string.Concat(Enumerable.Repeat(decimal.Zero.ToString(), count))}";
            return value / decimal.Parse(normaliserDivider);
        }
    }
}
