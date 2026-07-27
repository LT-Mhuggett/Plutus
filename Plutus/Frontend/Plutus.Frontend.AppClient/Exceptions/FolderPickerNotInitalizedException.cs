using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace Plutus.Frontend.AppClient.Exceptions
{
    [Serializable]
    public class FolderPickerNotInitalizedException : Exception
    {
        public FolderPickerNotInitalizedException()
        {
        }

        public FolderPickerNotInitalizedException(string message) : base(message)
        {
        }

        public FolderPickerNotInitalizedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected FolderPickerNotInitalizedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
