namespace Plutus.Frontend.ClientUI.Core.Validators
{
    public interface IValidator
    {
        /// <summary>
        /// Message to give to user, based on I10N_L18N
        /// </summary>
        string Message { get; set; }

        /// <summary>
        /// Check that value meets conditions
        /// </summary>
        /// <param name="value">Current value in form</param>
        /// <returns>True = Value is conditionaly right; False = value is not conditionaly right</returns>
        bool Check(string value);
    }
}
