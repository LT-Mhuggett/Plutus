using System;

namespace Plutus.Frontend.ClientUI.Core.Exceptions
{
    public class ConflictException : Exception
    {
        public ConflictException(string message) : base(message)
        {
        }
    }
}
