using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace Plutus.CustomRender
{
    internal class PickerCell : ViewCell
    {
        private Label _label { get; set; }
        private View _picker { get; set; }
        private Grid _base;

        internal string Label
        {
            get
            {
                return _label.Text;
            }
            set
            {
                _label.Text = value;
            }
        }

        internal View Picker
        {
            set
            {
                if (_picker != null)
                    _base.Children.Remove(_picker);
                _picker = value;
                _base.Children.Add(_picker);
            }
        }

        internal PickerCell()
        {
            _label = new Label()
            {
                VerticalOptions = LayoutOptions.Center
            };

            _base = new Grid()
            {
                ColumnDefinitions = new ColumnDefinitionCollection()
                {
                    new ColumnDefinition()
                    {
                        Width=new GridLength(2, GridUnitType.Star)
                    },
                    new ColumnDefinition()
                    {
                        Width=new GridLength(8, GridUnitType.Star)
                    }
                },
                Padding = 15
            };
            _base.Children.Add(_label, 0, 0);

            this.View = _base;
        }
    }
}
