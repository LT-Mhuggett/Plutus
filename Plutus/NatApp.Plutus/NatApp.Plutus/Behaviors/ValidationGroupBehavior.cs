using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Behaviors
{
    public class ValidationGroupBehavior : Behavior<View>, INotifyPropertyChanged
    {
        private IList<ValidationBehavior> _validationBehaviors;

        public static readonly BindableProperty IsValidProperty = BindableProperty.Create(
            "IsValid",
            typeof(bool),
            typeof(ValidationGroupBehavior),
            false);
        
        public ValidationGroupBehavior()
        {
            _validationBehaviors = new List<ValidationBehavior>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="validationBehavior"></param>
        public void Add(ValidationBehavior validationBehavior)
        {
            _validationBehaviors.Add(validationBehavior);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="validationBehavior"></param>
        public void Remove(ValidationBehavior validationBehavior)
        {
            _validationBehaviors.Remove(validationBehavior);
        }

        /// <summary>
        /// 
        /// </summary>
        public void Update()
        {
            bool isValid = true;

            foreach(ValidationBehavior validationItem in _validationBehaviors)
            {
                isValid = isValid && validationItem.Validate();
            }

            IsValid = isValid;
        }

        /// <summary>
        /// 
        /// </summary>
        public bool IsValid
        {
            get
            {
                return (bool)GetValue(IsValidProperty);
            }
            set
            {
                SetValue(IsValidProperty, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsValid"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
