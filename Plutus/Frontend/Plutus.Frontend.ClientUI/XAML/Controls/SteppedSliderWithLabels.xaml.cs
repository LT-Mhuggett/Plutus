using Plutus.Frontend.ClientUI.Core.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Microsoft.Maui.Controls;

namespace Plutus.Frontend.ClientUI.XAML.Controls
{
    /*
    public partial class SteppedSliderWithLabels : StackLayout
    {
        #region Properties
        #region Binders
        public static readonly BindableProperty ValuesProperty =
            BindableProperty.Create(nameof(Values), typeof(IList<string>), typeof(SteppedSliderWithLabels), default(IList<string>), BindingMode.OneWay);

        public static readonly BindableProperty ValueProperty =
            BindableProperty.Create(nameof(Value), typeof(string), typeof(SteppedSliderWithLabels), default(string), BindingMode.TwoWay);

        #endregion
        public IList<string> Values
        {
            get => (IList<string>)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }
        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set
            {
                var oldVal = Value;
                SetValue(ValueProperty, value);
                ValueChanged?.Invoke(this, new ValueChangedEventArgs<string>(oldVal, value));
            }
        }
        #region Events
        public event EventHandler<ValueChangedEventArgs<string>> ValueChanged;
        #endregion
        #endregion
        public SteppedSliderWithLabels()
        {
            InitializeComponent();

            slider.Value = 0;
        }

        protected override void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            base.OnPropertyChanged(propertyName);
            if (propertyName == ValueProperty.PropertyName)
                if (Values.Contains(Value))
                    slider.Value = Values.IndexOf(Value);
                else
                    slider.Value = 0;
            else if (propertyName == ValuesProperty.PropertyName)
                _ = BuildSlider();
        }

        private async Task BuildSlider()
        {
            if (slider.Height < 0)
            {
                do
                {
                    await Task.Delay(50);
                } while (slider.Height < 0);
            }

            if (Values != null)
            {
                slider.Maximum = Values.Count - 1;
                slider.Minimum = 0;

                var textService = DependencyService.Get<IText>();

                var ballSize = slider.Height;
                var labelWidth = (slider.Width - ballSize) / (Values.Count - 1);

                for (var i = 0; i < Values.Count; i++)
                {
                    var textWidth = textService.CalculateWidth(Values[i]);
                    var margin = (ballSize / 2) - (textWidth / 2);
                    margin = margin > 0 ? margin : 0;

                    var label = new Label
                    {
                        Text = Values[i],
                        WidthRequest = i == Values.Count - 1 ? ballSize - margin : labelWidth - margin,
                        HorizontalTextAlignment = i == Values.Count - 1 ? TextAlignment.End : TextAlignment.Start,
                        Margin = i == Values.Count - 1 ? new Thickness(0, 0, margin, 0) : new Thickness(margin, 0, 0, 0),
                        LineBreakMode = LineBreakMode.NoWrap
                    };

                    label.BindingContext = this;
                    labelStack.Children.Add(label);
                }
            }
        }

        private void Slider_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            if((e.NewValue % 1) == 0)
                Value = Values.ElementAt((int)e.NewValue);
        }
    }*/
}