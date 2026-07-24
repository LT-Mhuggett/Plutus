using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Helpers.Validators
{
    public interface IValidatorReqReference : IValidator
    {
        Entry ReferenceEntry { get; set; }
    }
}
