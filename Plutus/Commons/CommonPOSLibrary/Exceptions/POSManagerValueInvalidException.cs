using System;

namespace CommonPOSLibrary.Exceptions
{
    public class POSManagerValueInvalidException : POSManagerException
    {
        public Type ExpectedType { get; }
        public POSManagerValueInvalidException(Type type) : base()
        {
            ExpectedType = type;
        }

        public POSManagerValueInvalidException(string message, Type type) : base(message)
        {
            ExpectedType = type;
        }
    }
}
