using Plutus.Frontend.AppClient.Controls;
using Microsoft.Maui.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.CustomViews
{
    public partial class SliderAlert : ContentView
    {
        public EventHandler ConfirmButtonEHandler { get; set; }
        public SortedDictionary<int, string> SliderResults { get; set; } = new SortedDictionary<int, string>();
        public List<Tuple<Label, SteppedSliderWithLabels>> ViewElements { get; set; } = new List<Tuple<Label, SteppedSliderWithLabels>>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="viewElements"></param>
        /// <param name="confrimBut"></param>
        /// <param name="titleText"></param>
        public SliderAlert(Queue<Tuple<string, List<string>, string>> viewElements, string confrimBut, string titleText)
        {
            InitializeComponent();

            if (titleText != null)
                StackContainer.Children.Insert(0, new Label()
                {
                    Text = titleText,
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    FontSize = new Label().FontSize,
                    FontAttributes = FontAttributes.Bold
                });

            for (int i = 0; i <= viewElements.Count - 1;)
            {
                ViewElements.Add(CreateLabelSteppedSlider(viewElements.Dequeue()));
            }

            var confBut = new Button { Text = confrimBut };
            confBut.Clicked += ConfBut_Clicked;
            StackContainer.Children.Add(confBut);

            foreach(var view in MainLayout.Children)
            {
                if (view is SteppedSliderWithLabels element)
                    SliderResults.Add(MainLayout.Children.IndexOf(element), element.Value);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="elementValues"></param>
        /// <returns></returns>
        public Tuple<Label, SteppedSliderWithLabels> CreateLabelSteppedSlider(Tuple<string, List<string>, string> elementValues)
        {
            var label = new Label { Text = elementValues.Item1, FontAttributes = FontAttributes.Bold };
            var steppedSlider = new SteppedSliderWithLabels { Values = elementValues.Item2, Value = elementValues.Item3 ?? "None", Margin = new Thickness(0, 0, 0, 15) };

            steppedSlider.ValueChanged += SteppedSlider_ValueChanged;

            MainLayout.Children.Add(label);
            MainLayout.Children.Add(steppedSlider);
            return Tuple.Create(label, steppedSlider);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SteppedSlider_ValueChanged(object sender, Helpers.EventArgs.ValueChangedEventArgs<string> e)
        {
            var updatedSteppedSlider = sender as SteppedSliderWithLabels;
            SliderResults[MainLayout.Children.IndexOf(updatedSteppedSlider)] = e.NewValue;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ConfBut_Clicked(object sender, EventArgs e)
        {
            ConfirmButtonEHandler?.Invoke(this, e);
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            //Setup window width
            StackContainer.WidthRequest = Application.Current.MainPage.Width / 2;
            StackContainer.HeightRequest = Application.Current.MainPage.Height / 2;
        }
    }
}