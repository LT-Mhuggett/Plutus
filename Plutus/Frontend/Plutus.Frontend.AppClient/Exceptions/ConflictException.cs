using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Frontend.AppClient.Exceptions
{
    public class ConflictException : Exception
    {
        public ConflictException(string message) : base(message)
        {
        }
    }
}
