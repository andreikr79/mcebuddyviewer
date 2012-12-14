using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using MCEBuddy.Globals;

namespace MCEBuddy.Util
{
    public static class Localise
    {
        private static SortedList _phrases = new SortedList();
        private static bool _initialised = false;
        private static string _localCulture = System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
        private static string _localCulture3Letter = System.Globalization.CultureInfo.CurrentCulture.ThreeLetterISOLanguageName;
        private static CultureInfo _ci = System.Globalization.CultureInfo.CurrentCulture;

        public static void Init( string cultureStr)
        {
            _initialised = true;

            if (cultureStr != "")
            {
                try
                {
                    _ci = new CultureInfo(cultureStr);
                    Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = _ci; // Change the culture of the current application
                    _localCulture = _ci.TwoLetterISOLanguageName;
                    _localCulture3Letter = _ci.ThreeLetterISOLanguageName;
                }
                catch (Exception)
                {
                }
            }
            if (_localCulture == "en")
            {
                _phrases.Clear(); // Clear it
                return;
            }

            string phraseFileName = Path.Combine(GlobalDefs.LocalisationPath, _localCulture + ".txt");
            Monitor.Enter(_phrases); // Get a lock before updating the list
            if (File.Exists(phraseFileName))
            {
                try
                {
                    using (CsvFileReader reader = new CsvFileReader(phraseFileName))
                    {
                        _phrases.Clear(); // Reset the list since we are building it again

                        CsvRow row = new CsvRow();
                        while (reader.ReadRow(row))
                        {
                            string key = row[0].ToLower().Trim();

                            // Take care of special characters
                            key = key.Replace("\\\\", "\\"); //Order matters first check for \\
                            key = key.Replace("\\r", "\r");
                            key = key.Replace("\\n", "\n");
                            key = key.Replace("\\\'", "\'");

                            if (!_phrases.ContainsKey(key))
                            {
                                _phrases.Add(key, row[1]);                                   
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.AppLog.WriteEntry(null, "Unable to read localisation file " + phraseFileName + "\n" + e.Message, Log.LogEntryType.Error);
                }
            }
            Monitor.Exit(_phrases);
        }

        public static string GetPhrase( string englishPhrase)
        {
            if (!_initialised) Init(""); // This will get the lock
            
            string cleanPhrase = englishPhrase.ToLower().Trim();
            string returnPhrase = englishPhrase;
            
            Monitor.Enter(_phrases); // Always get a lock before translating incase the lists are being updated
            if (_phrases.ContainsKey(cleanPhrase)) 
                returnPhrase = (string)_phrases[cleanPhrase];
            Monitor.Exit(_phrases);

            // Take care of special characters
            returnPhrase = returnPhrase.Replace("\\\\", "\\");  // Order matters first check for \\
            returnPhrase = returnPhrase.Replace("\\r", "\r");
            returnPhrase = returnPhrase.Replace("\\n", "\n");
            returnPhrase = returnPhrase.Replace("\\\'", "\'");

            return returnPhrase;
        }

        public static string TwoLetterISO()
        {
            return _localCulture;
        }

        public static string ThreeLetterISO()
        {
            return _localCulture3Letter;
        }

        public static CultureInfo MCEBuddyCulture
        { get { return _ci; } }
    }
}
