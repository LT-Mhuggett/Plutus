namespace Plutus.Models
{
    /// <summary>
    /// This is the store model.
    /// It is used to store all sotre details and is used to get store details form DB.
    /// This is setup to allow physical expansion with keeping one united system.
    /// </summary>
    public class StoreModel
    {
        public string StoreName { get; set; }
        public string StoreAbbr { get; set; }
    }

}
