namespace CommonPOSLibrary.Exceptions
{
    public class POSManagerCommandNotSupportedException : POSManagerException
    {
        public POSManagerCommandNotSupportedException() : base()
        {

        }

        public POSManagerCommandNotSupportedException(string message) : base(message)
        {

        }
    }
}
