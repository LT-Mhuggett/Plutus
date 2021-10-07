namespace Plutus.Entities.Models.Interface
{
    public interface IBase<T> : IAuditable
    {
        /// <summary>
        /// The records Database ID
        /// </summary>
        T Id { get; set; }
    }
}
