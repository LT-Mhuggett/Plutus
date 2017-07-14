using System.Text.RegularExpressions;

namespace Plutus.Helpers
{
    public class Validate
    {
        /// <summary>
        /// This uses the Web Standard regex to ensure that the email provided is logically correct
        /// </summary>
        /// <param name="email">email to test</param>
        /// <returns>True or false depending of test outcome</returns>
        public static bool IsEmailValid(string email)
        {
            var regex = @"^[\w!#$%&'*+\-/=?\^_`{|}~]+(\.[\w!#$%&'*+\-/=?\^_`{|}~]+)*" + "@" + @"((([\-\w]+\.)+[a-zA-Z]{2,4})|(([0-9]{1,3}\.){3}[0-9]{1,3}))$";
            return Regex.IsMatch(email, regex);
        }

        /// <summary>
        /// This uses the UK Government provided regex to ensure that the email provided is logically correct
        /// </summary>
        /// <param name="PC">PostCode to test</param>
        /// <returns>True or false depending of test outcome</returns>
        public static bool IsPostCodeValid(string PC)
        {
            var regex = @"^([Gg][Ii][Rr] 0[Aa]{2})|((([A-Za-z][0-9]{1,2})|(([A-Za-z][A-Ha-hJ-Yj-y][0-9]{1,2})|(([AZa-z][0-9][A-Za-z])|([A-Za-z][A-Ha-hJ-Yj-y][0-9]?[A-Za-z])))) [0-9][A-Za-z]{2})$";
            return Regex.IsMatch(PC, regex);
        }
    }
}
