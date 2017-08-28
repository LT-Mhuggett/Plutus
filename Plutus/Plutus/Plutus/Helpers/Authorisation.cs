using System;
using System.Collections.Generic;
using System.Text;
using Plutus.Models;

namespace Plutus.Helpers
{
    public class Authorisation
    {
        public static bool IsAuthorised(string Action)
        {
            foreach (var item in App.EmpsLogged[0].Actions)
            {
                if (item.Id == Action)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsAuthorised(string Action, EmployeeModel eTemp)
        {
            foreach (var item in eTemp.Actions)
            {
                if (item.Id == Action)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
