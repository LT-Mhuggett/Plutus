namespace Plutus.Entities.Models.Interface
{
    public interface IDiscount : IBase<int>
    {
        string Name { get; set; }
        bool AllApplicable { get; set; }
        bool CanUseWithOtherDiscounts { get; set; }
        bool AutoApply { get; set; }

        /// <summary>
        /// <para>
        /// Type = 0 -> Fixed Cash off
        /// </para>
        /// <para>
        /// Type = 1 -> Percentage off
        /// </para>
        /// </summary> 
        int Type { get; set; }
        decimal Amount { get; set; }
        int UsesPerTransaction { get; set; }
        int RequiredNumOfItems { get; set; }
    }
}
