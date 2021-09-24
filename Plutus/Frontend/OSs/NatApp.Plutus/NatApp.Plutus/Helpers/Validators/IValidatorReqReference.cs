using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace NatApp.Plutus.Helpers.Validators
{
    public interface IValidatorReqReference : IValidator
    {
        Entry ReferenceEntry { get; set; }
    }
}
