namespace CommonPOSLibrary.Enums
{
    /// <summary>
    /// Defines the types of POS Object exceptions
    /// </summary>
    public enum POSObjectExceptionType
    {
        /// <summary>
        /// The POS Object has not been found
        /// </summary>
        NotFound = 0,
        /// <summary>
        /// The POS Object is either Off or Offline
        /// </summary>
        OffOrOffline = 1,
        /// <summary>
        /// The POS Object is not currently claimable
        /// </summary>
        NotClaimable = 2,
        /// <summary>
        /// The POS Object is not currently enableable
        /// </summary>
        NotEnableable = 3
    }
}
