using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using MCEBuddy.Globals;

namespace MCEBuddy.Util
{
    public static class FilePaths
    {
        public static string GetFullPathWithoutExtension( string FileName )
        {
            if ("" == FileName) return "";
            return Path.Combine(Path.GetDirectoryName(FileName), Path.GetFileNameWithoutExtension(FileName));
        }

        public static string RemoveIllegalFilePathChars(string filePath)
        {
            foreach (char lDisallowed in System.IO.Path.GetInvalidFileNameChars())
            {
                filePath = filePath.Replace(lDisallowed.ToString(System.Globalization.CultureInfo.InvariantCulture), "");
            }

            return filePath.Trim();
        }

        public static string CleanExt( string filePath )
        {
            return Path.GetExtension(filePath).ToLower().Trim();
        }

        public static string FixSpaces( string path )
        {
            // Code below is not required as command prompt takes care of these somehow
            /*if (path == "")
                return @"""";

            // Check for backslash
            path = path.Replace("\\", "\\\\");

            // Check for double quotes
            path = path.Replace("\"", "\\\"");*/
            
            // Encapsulate in double quotes
            path = "\"" + path + "\"";

            return path;
        }

        public static void CreateDir( string path )
        {
            if (! Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception)
                {
                }
            }
        }

        public static string WildcardToRegex(string wildcard)
        {
            StringBuilder sb = new StringBuilder(wildcard.Length + 8);

            sb.Append("^");

            for (int i = 0; i < wildcard.Length; i++)
            {
                char c = wildcard[i];
                switch (c)
                {
                    case '*':
                        sb.Append(".*");
                        break;
                    case '?':
                        sb.Append(".");
                        break;
                    case '\\':
                        if (i < wildcard.Length - 1)
                            sb.Append(Regex.Escape(wildcard[++i].ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        break;
                    case ';':
                        sb.Append("$|^");
                        break;
                    default:
                        sb.Append(Regex.Escape(wildcard[i].ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        break;
                }
            }

            sb.Append("$");

            return sb.ToString();
        }

        public static bool WildcardVideoMatch(string fileName, string pattern)
        {
            bool returnMatch = false;

            // Check against wildcard -> file path regex
            string wildcardRegex = pattern.ToLower();
            wildcardRegex = wildcardRegex.Replace("[video]", GlobalDefs.DEFAULT_VIDEO_FILE_TYPES);
            
            if (wildcardRegex.Contains("regex:")) // check if it's a RegEx pattern, then handle directly
            {
                wildcardRegex = wildcardRegex.Replace("regex:", "");
                Regex rxFileMatch = new Regex(wildcardRegex, RegexOptions.IgnoreCase);
                returnMatch = rxFileMatch.IsMatch(Path.GetFileName(fileName).ToLower());
            }
            else foreach (string wildcardPattern in wildcardRegex.Split(';')) // check each pattern individually, allows us to have a not (~) match for each pattern
            {
                bool avoidList = false; // a NOT pattern
                string wildcard = wildcardPattern;

                if (String.IsNullOrEmpty(wildcard)) // incase a smart person ended the pattern with a ;
                    continue;

                // Check if this is a NOT list, i.e. select all files except... (starts with a ~)
                if (wildcardPattern.Contains("~"))
                {   
                    wildcard = wildcardPattern.Replace("~", ""); // remove the ~ character from the expression
                    avoidList = true;
                }

                wildcard = Util.FilePaths.WildcardToRegex(wildcard); // convert to RegEx for matching
                Regex rxFileMatch = new Regex(wildcard, RegexOptions.IgnoreCase); // Create a Regex for matching

                if (avoidList) // Check if it's a NOT pattern
                {
                    if (rxFileMatch.IsMatch(Path.GetFileName(fileName).ToLower())) // If even one NOT pattern matches, we return false (no need to continue)
                        return false;
                }
                else
                    returnMatch = returnMatch || rxFileMatch.IsMatch(Path.GetFileName(fileName).ToLower()); // Keep looking for atleast one match or incase we find a NOT match else where in the pattern loop
            }

            return returnMatch;
        }
    }

    public class Wildcard : Regex
    {
        /// <summary>

        /// Initializes a wildcard with the given search pattern.

        /// </summary>

        /// <param name="pattern">The wildcard pattern to match.</param>

        public Wildcard(string pattern)
            : base(WildcardToRegex(pattern))
        {
        }

        /// <summary>

        /// Initializes a wildcard with the given search pattern and options.

        /// </summary>

        /// <param name="pattern">The wildcard pattern to match.</param>

        /// <param name="options">A combination of one or more

        /// <see cref="System.Text.RegexOptions"/>.</param>

        public Wildcard(string pattern, RegexOptions options)
            : base(WildcardToRegex(pattern), options)
        {
        }

        /// <summary>

        /// Converts a wildcard to a regex.

        /// </summary>

        /// <param name="pattern">The wildcard pattern to convert.</param>

        /// <returns>A regex equivalent of the given wildcard.</returns>

        public static string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern).
            Replace(";", "$|^").
            Replace("\\*", ".*").
            Replace("\\?", ".") + "$";
        }

   }
}
