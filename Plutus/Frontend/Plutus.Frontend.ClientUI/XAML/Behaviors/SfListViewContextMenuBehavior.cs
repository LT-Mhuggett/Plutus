/*using Plutus.Frontend.ClientUI.XAML.EventArgs;
using Plutus.Frontend.ClientUI.XAML.Extensions.ListViewWithContextMenu;
using Syncfusion.Maui.ListView;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using CommunityToolkit.Maui.Views;

namespace Plutus.Frontend.ClientUI.XAML.Behaviors
{
    
    public class SfListViewContextMenuBehavior : Behavior<ListViewWithContextMenu>
    {
        #region Fields
        private ListViewWithContextMenu _listView;
        private Popup _popupLayout = null;
        #endregion

        #region Properties
        public object ItemContext { get; private set; }
        #region Button
        public IList<Button> Buttons { get; set; } = new List<Button>();
        public IList<string> ItemBindings { get; set; } = new List<string>();
        #endregion
        public Color BackgroundColor { get; set; }
        #endregion

        protected override void OnAttachedTo(ListViewWithContextMenu listView)
        {
            _listView = listView;
            _listView.ItemLongPress += ListView_ItemHolding;
            _listView.ScrollStateChanged += ListView_ScrollStateChanged;
            _listView.ItemTapped += ListView_ItemTapped;
            _listView.ItemRightTapped += ListView_RightTapped;
            base.OnAttachedTo(listView);
        }

        private void ListView_ItemTapped(object sender, Syncfusion.Maui.ListView.ItemTappedEventArgs e)
        {
            _popupLayout?.Close();
        }

        private void ListView_ScrollStateChanged(object sender, ScrollStateChangedEventArgs e)
        {
            _popupLayout?.Close();
        }

        private void ListView_ItemHolding(object sender, ItemLongPressEventArgs e)
        {
            InitPopupLayout(e.ItemData, e.Position);
        }

        private void ListView_RightTapped(object sender, ItemRightTappedEventArgs e)
        {
            InitPopupLayout(e.ItemData, e.Position);
        }

        private void InitPopupLayout(object itemData, Point position)
        {
            ItemContext = itemData;
            _popupLayout = new Popup();
            _popupLayout.Content.HeightRequest = Buttons.Sum(b => b.HeightRequest);
            _popupLayout.Content.WidthRequest = Buttons.Max(b => b.Width);
            _popupLayout.Content.PopupStyle = new PopupStyle() { OverlayColor = Color.Transparent, OverlayOpacity = 0 };
            _popupLayout.Content.AnimationMode = AnimationMode.None;
            _popupLayout.Content.ContentTemplate = new DataTemplate(() =>
            {
                var mainStack = new StackLayout();
                if (BackgroundColor != null)
                    mainStack.BackgroundColor = BackgroundColor;
                mainStack.Spacing = 0;

                for (int i = 0; i < Buttons.Count; i++)
                {
                    Buttons[i].SetBinding(Button.CommandParameterProperty, new Binding(ItemBindings[i], source: ItemContext));
                    Buttons[i].Clicked -= Dismiss;
                    Buttons[i].Clicked += Dismiss;
                    mainStack.Children.Add(Buttons[i]);
                }
                return mainStack;
            });


            if (position.Y + Buttons.Sum(b => b.HeightRequest) <= _listView.Height &&
                position.X + Buttons.Max(b => b.Width) > _listView.Width)
                _popupLayout.(position.X - Buttons.Max(b => b.Width), position.Y);

            else if (position.Y + Buttons.Sum(b => b.HeightRequest) > _listView.Height &&
                position.X + Buttons.Max(b => b.Width) < _listView.Width)
                _popupLayout.Show(position.X, position.Y - Buttons.Sum(b => b.HeightRequest));

            else if (position.Y + Buttons.Sum(b => b.HeightRequest) > _listView.Height &&
                position.X + Buttons.Max(b => b.Width) > _listView.Width)
                _popupLayout.Show(
                    position.X - Buttons.Max(b => b.Width),
                    position.Y - Buttons.Sum(b => b.HeightRequest));
            else
                _popupLayout.Show(position.X, position.Y);
        }

        private void Dismiss(object sender, System.EventArgs e)
        {
            _popupLayout.IsVisible = false;
            _popupLayout.Dismiss();
        }
    }
}*/
