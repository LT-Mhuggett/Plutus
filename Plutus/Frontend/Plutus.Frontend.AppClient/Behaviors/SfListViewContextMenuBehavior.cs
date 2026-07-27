using Plutus.Frontend.AppClient.Helpers.EventArgs;
using Plutus.Frontend.AppClient.Helpers.Extensions.XAML.ListViewWithContextMenu;
using Syncfusion.Maui.ListView;
using Syncfusion.Maui.Popup;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Behaviors
{
    public class SfListViewContextMenuBehavior : Behavior<ListViewWithContextMenu>
    {
        #region Fields
        private ListViewWithContextMenu _listView;
        private SfPopup _popupLayout = null;
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
            _popupLayout?.Dismiss();
        }

        private void ListView_ScrollStateChanged(object sender, ScrollStateChangedEventArgs e)
        {
            _popupLayout?.Dismiss();
        }

        private void ListView_ItemHolding(object sender, ItemLongPressEventArgs e)
        {
            InitPopupLayout(e.DataItem, e.Position);
        }

        private void ListView_RightTapped(object sender, Plutus.Frontend.AppClient.Helpers.EventArgs.ItemRightTappedEventArgs e)
        {
            InitPopupLayout(e.ItemData, e.Position);
        }

        private void InitPopupLayout(object itemData, Point position)
        {
            ItemContext = itemData;
            _popupLayout = new SfPopup();
            _popupLayout.HeightRequest = Buttons.Sum(b => b.HeightRequest);
            _popupLayout.WidthRequest = Buttons.Max(b => b.Width);
            _popupLayout.PopupStyle = new PopupStyle() { OverlayColor = Colors.Transparent };
            _popupLayout.AnimationMode = PopupAnimationMode.None;
            _popupLayout.ContentTemplate = new DataTemplate(() =>
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

            _popupLayout.ShowHeader = false;
            _popupLayout.ShowFooter = false;


            if (position.Y + Buttons.Sum(b => b.HeightRequest) <= _listView.Height &&
                position.X + Buttons.Max(b => b.Width) > _listView.Width)
                _popupLayout.Show(position.X - Buttons.Max(b => b.Width), position.Y);

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

        private void Dismiss(object sender, EventArgs e)
        {
            _popupLayout.IsOpen = false;
            _popupLayout.Dismiss();
        }
    }
}
