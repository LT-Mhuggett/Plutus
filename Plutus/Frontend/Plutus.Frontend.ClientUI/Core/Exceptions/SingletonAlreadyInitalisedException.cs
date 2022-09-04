using System;

namespace Plutus.Frontend.ClientUI.Core.Exceptions
{
    public class SingletonAlreadyInitialisedException : Exception
    {
        public SingletonAlreadyInitialisedException(string message) : base(message)
        {
        }
    }
}
