namespace Plutus.Frontend.ClientUI.Core.EventArgs
{
    public class ValueChangedEventArgs<T> : System.EventArgs
    {
        public ValueChangedEventArgs(T oldValue, T newValue)
        {
            OldValue = oldValue;
            NewValue = newValue;
        }

        //
        // Summary:
        //     Gets the new value.
        //
        // Remarks:
        //     To be added.
        public T NewValue { get; }
        //
        // Summary:
        //     Gets the old value.
        //
        // Remarks:
        //     To be added.
        public T OldValue { get; }
    }
}
