using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Helpers
{
    public class Authorisation
    {
        public static bool IsAuthorised(string Action)
        {
            if (App.EmpsLogged.Count == 1)
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
            else
            {
                //implement multi user authorization
                throw new NotImplementedException();
            }
        }
    }
}
