using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;
using Xamarin.Forms.Internals;

namespace NatApp.Plutus.Helpers.Validators.ErrorHandling
{
    class BasicErrorStyle : IErrorStyle
    {
        /// <summary>
        /// Remove the error label from the StackLayout
        /// </summary>
        /// <remarks>
        /// Must be used with StackLayout
        /// </remarks>
        /// <param name="view">Bindable input Element</param>
        public void RemoveError(View view)
        {
            Layout<View> layout = view.Parent as Layout<View>;
            int viewIndex = layout.Children.IndexOf(view);

            if(viewIndex + 1 < layout.Children.Count)
            {
                View sibling = layout.Children[viewIndex + 1];
                string siblingStyleId = view.Id.ToString();
                if (sibling.StyleId == siblingStyleId)
                    sibling.IsVisible = false;
            }
        }

        /// <summary>
        /// Add or activate the error label to/in the StackLayout
        /// </summary>
        /// <remarks>
        /// Must be used with StackLayout
        /// </remarks>
        /// <param name="view">Bindable input Element</param>
        /// <param name="message">Message to display</param>
        public void ShowError(View view, string message)
        {
            Layout<View> layout = view.Parent as Layout<View>;
            int viewIndex = layout.Children.IndexOf(view);

            //Check if error label already exists
            if (viewIndex + 1 < layout.Children.Count)
            {
                View sibling = layout.Children[viewIndex + 1];
                string siblingStyleId = view.Id.ToString();
                //Reuse error label
                if (sibling.StyleId == siblingStyleId)
                {
                    Label errorLabel = sibling as Label;
                    errorLabel.Text = message;
                    errorLabel.IsVisible = true;
                    return;
                }
            }
            //Add label if doesn't exist
            layout.Children.Insert(viewIndex + 1, new Label
            {
                Text = message,
                FontSize = 10,
                StyleId = view.Id.ToString(),
                TextColor = Color.Red
            });
        }
    }
}
