using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Plutus.Models
{
    public class Basket : ItemModel, INotifyPropertyChanged
    {
        private int _amount { get; set; }
        private bool _return { get; set; }
        public string Reason { get; set; }
        public string SaleId { get; set; }

        public Basket(ItemModel item)
        {
            ItemId = item.ItemId;
            Name = item.Name;
            Desc = item.Desc;
            Brand = item.Brand;
            Cost = item.Cost;
            Price = item.Price;
            Image = item.Image;
            VatId = item.VatId;
            CatId = item.CatId;
            Transactions = item.Transactions;
            Stock = item.Stock;
            Vat = item.Vat;
            Cat = item.Cat;

            _amount = 1;
        }

        public Basket(Basket item)
        {
            ItemId = item.ItemId;
            Name = item.Name;
            Desc = item.Desc;
            Brand = item.Brand;
            Cost = item.Cost;
            Price = item.Price;
            Image = item.Image;
            VatId = item.VatId;
            CatId = item.CatId;
            Transactions = item.Transactions;
            Stock = item.Stock;
            Vat = item.Vat;
            Cat = item.Cat;
            _amount = item.Amount;
        }

        public int Amount
        {
            get { return _amount; }
            set
            {
                if (_amount != value)
                {
                    _amount = value;
                    OnPropertyChanged("Amount");
                }
            }
        }

        public bool Return
        {
            get { return _return; }
            set
            {
                if (_return != value)
                {
                    _return = value;
                    OnPropertyChanged("Return");
                }
            }
        }

        private void OnPropertyChanged(string v)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(v));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
