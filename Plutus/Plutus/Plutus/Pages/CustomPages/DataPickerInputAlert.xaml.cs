using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Database.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.CustomPages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DataPickerInputAlert : ContentView
    {
        public EventHandler ConfirmButtonEHandler { get; set; }
        public EventHandler CloseButtonEHandler { get; set; }

        public object obj { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public DataPickerInputAlert(string titleText, string confirmButText, string validationText,
            List<CategoryModel> list)
        {
            Initalise(titleText, confirmButText, validationText, list);
        }

        public DataPickerInputAlert(string titleText, string confirmButText, string validationText,
            List<ItemModel> list)
        {
            Initalise(titleText, confirmButText, validationText, list);
        }

        private void Initalise<T>(string tText, string confirmBText, string vText, List<T> list)
        {
            InitializeComponent();

            TitleL.Text = tText;
            ValidationL.Text = vText;
            Picker.ItemsSource = list;
            ConfirmBut.Text = confirmBText;
            Picker.PropertyChanged += Picker_PropertyChanged;
            SDate.PropertyChanged += SDate_PropertyChanged;
            EDate.PropertyChanged += EDate_PropertyChanged;
            ConfirmBut.Clicked += ConfirmBut_Clicked;
            CButton.Clicked += CButton_Clicked;
        }

        private void CButton_Clicked(object sender, EventArgs e)
        {
            CloseButtonEHandler?.Invoke(this, e);
        }

        private void ConfirmBut_Clicked(object sender, EventArgs e)
        {
            ConfirmButtonEHandler?.Invoke(this, e);
        }

        private void EDate_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            EndDate = EDate.Date;
        }

        private void SDate_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            StartDate = SDate.Date;
        }

        private void Picker_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            obj = Picker.SelectedItem;
        }

        private static readonly BindableProperty IsValidationLVisibleProp = BindableProperty.Create(
            nameof(IsValidationLVisibleProp),
            typeof(bool),
            typeof(DataPickerInputAlert),
            false,
            BindingMode.OneWay,
            null,
            (bindable, value, newValue) =>
            {
                if ((bool) newValue)
                {
                    ((DataPickerInputAlert) bindable).ValidationL.IsVisible = true;
                }
                else
                {
                    ((DataPickerInputAlert) bindable).ValidationL.IsVisible = false;
                }
            }
        );

        public bool IsValidationLVisable
        {
            get => (bool) GetValue(IsValidationLVisibleProp);
            set => SetValue(IsValidationLVisibleProp, value);
        }
    }
}