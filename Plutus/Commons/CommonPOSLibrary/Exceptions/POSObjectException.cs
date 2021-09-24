using CommonPOSLibrary.Enums;
using System;

namespace CommonPOSLibrary.Exceptions
{
    public class POSObjectException : Exception
    {
        public POSObjectExceptionType POSObjectExceptionType { get; }
        public POSTargetObjectType POSTargetObjectType { get; }

        public POSObjectException(POSObjectExceptionType pOSObjectExceptionType, POSTargetObjectType pOSTargetObjectType) : base()
        {
            POSObjectExceptionType = pOSObjectExceptionType;
            POSTargetObjectType = pOSTargetObjectType;
        }

        public POSObjectException(POSObjectExceptionType pOSObjectExceptionType, POSTargetObjectType pOSTargetObjectType, string message) : base(message)
        {
            POSObjectExceptionType = pOSObjectExceptionType;
            POSTargetObjectType = pOSTargetObjectType;
        }
    }
}
