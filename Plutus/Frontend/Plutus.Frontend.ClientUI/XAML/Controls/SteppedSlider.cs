using System;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.ClientUI.XAML.Controls
{
    public class SteppedSlider : Slider
    {
        public static readonly BindableProperty StepValueProperty =
            BindableProperty.Create(nameof(StepValue), typeof(double), typeof(SteppedSlider), default(double));

        public double StepValue
        {
            get => (double)GetValue(StepValueProperty);
            set => SetValue(StepValueProperty, value);
        }

        /// <summary>
        /// Set SteppedSlider with default values; StepValue = 1
        /// </summary>
        public SteppedSlider()
        {
            StepValue = 1;
            ValueChanged += BaseValueChanged;
            Value = 0;
        }

        public SteppedSlider(double min, double max, double val, double stepValue) : base(min, max, val)
        {
            StepValue = stepValue;
            ValueChanged += BaseValueChanged;
        }

        private void BaseValueChanged(object sender, ValueChangedEventArgs e)
        {
            Value = Math.Round(e.NewValue / StepValue) * StepValue;
        }
    }
}
