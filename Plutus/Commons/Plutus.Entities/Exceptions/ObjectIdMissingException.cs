using System;

namespace Plutus.Entities.Exceptions
{
    public class ObjectIdMissingException : Exception
    {
        // The default constructor needs to be defined
        // explicitly now since it would be gone otherwise.

        public ObjectIdMissingException()
        {
        }

        public ObjectIdMissingException(string message) : base(message)
        {
        }
    }
}
