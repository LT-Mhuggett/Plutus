using CommonPOSLibrary.Enums;
using System;

namespace CommonPOSLibrary.Exceptions
{
    public class POSObjectException : Exception
    {
        public POSObjectExceptionType POSObjectExceptionType { get; }

        public POSObjectException(POSObjectExceptionType pOSObjectExceptionType) : base()
        {
            POSObjectExceptionType = pOSObjectExceptionType;
        }

        public POSObjectException(POSObjectExceptionType pOSObjectExceptionType, string message) : base(message)
        {
            POSObjectExceptionType = pOSObjectExceptionType;
        }
    }
}
