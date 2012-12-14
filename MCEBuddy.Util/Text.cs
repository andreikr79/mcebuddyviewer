using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MCEBuddy.Util
{
    public class Text
    {
        static public string StripNonAlphaNumeric(string SourceString)
        {
            return Regex.Replace(SourceString, "[^a-z0-9\\s]", "", RegexOptions.IgnoreCase);
        }

        public static string ReplaceString(string OldString, string stringToBeReplaced, string newString, bool CaseInsensitive)
        {

            int index = 0;

            if (CaseInsensitive)
                OldString.IndexOf(stringToBeReplaced, 0, StringComparison.OrdinalIgnoreCase);
            else
                index = OldString.IndexOf(stringToBeReplaced, 0);

            if (index == -1)
                return OldString;

            OldString = OldString.Substring(0, index) + newString + OldString.Substring(index + stringToBeReplaced.Length);

            return OldString;
        }

        public static string Slugify(string phrase, int maxLength = 50)
        {
            string str = phrase.ToLower();

            // invalid chars, make into spaces
            str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
            // convert multiple spaces/hyphens into one space
            str = Regex.Replace(str, @"[\s-]+", " ").Trim();
            // cut and trim it
            str = str.Substring(0, str.Length <= maxLength ? str.Length : maxLength).Trim();
            // hyphens
            str = Regex.Replace(str, @"\s", "-");

            return str;
        }

        public static bool ContainsUnicode(string stringToTest)
        {
            const int MaxAnsiCode = 255;

            return stringToTest.ToCharArray().Any(c => c > MaxAnsiCode);

        }
    }
}
