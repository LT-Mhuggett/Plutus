using System;

namespace CommonPOSLibrary.Exceptions
{
    public class POSManagerException : Exception
    {
        public POSManagerException() : base()
        {

        }

        public POSManagerException(string message) : base(message)
        {

        }
    }
}
