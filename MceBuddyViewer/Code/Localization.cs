using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using Microsoft.Win32;

namespace MceBuddyViewer
{
    public sealed class Localization
    {
        List<string> languages = new List<string>();

        private string _currentlanguage;

        public string CurrentLanguage
        {
            get { return _currentlanguage; }
            set 
            {
                if (_currentlanguage != value)
                {
                    if (ChangeLanguage(value)) _currentlanguage = value;
                }
            }
        }

        private Dictionary<string, string> _translate = new Dictionary<string, string>();
        public Dictionary<string, string> Translate
        {
            get { return _translate; }
        }

        public Localization()
        {
            // default language english
            string defaultlanguage = "english";
            string LocalFolderPath = "";
            // get install folder from registry
            try
            {
                RegistryKey viewerkey = Registry.CurrentUser;
                viewerkey = viewerkey.OpenSubKey(@"SOFTWARE\MCEBuddyViewer");
                if (viewerkey.GetValue("Install Folder") != null)
                {
                    LocalFolderPath = (string)viewerkey.GetValue("Install Folder")+"localization";
                }
                if (viewerkey.GetValue("Language") != null)
                {
                    defaultlanguage = (string)viewerkey.GetValue("Language");
                }
            }
            catch
            {
            }
            
            Translate.Clear();
            languages.Clear();
            try
            {
                // Get all xml files in folder
                string[] xmlfiles = Directory.GetFiles(LocalFolderPath, "*.xml");
                foreach (string xmlfile in xmlfiles)
                {
                    try
                    {
                        XmlDocument langdoc = new XmlDocument();
                        langdoc.Load(xmlfile);
                        string langname = langdoc.GetElementsByTagName("language").Item(0).Attributes.GetNamedItem("name").Value;
                        languages.Add(langname);
                        if (defaultlanguage == langname)
                        {
                            if (LoadLanguageFromXML(xmlfile)) CurrentLanguage = langname;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        public bool ChangeLanguage(string newlanguage)
        {            
            string LocalFolderPath = "";
            // get install folder from registry
            try
            {
                RegistryKey viewerkey = Registry.CurrentUser;
                viewerkey = viewerkey.OpenSubKey(@"SOFTWARE\MCEBuddyViewer");
                if (viewerkey.GetValue("Install Folder") != null)
                {
                    LocalFolderPath = (string)viewerkey.GetValue("Install Folder") + "localization";
                }
            }
            catch
            {
            }
                        
            try
            {
                // Get all xml files in folder
                string[] xmlfiles = Directory.GetFiles(LocalFolderPath, "*.xml");
                foreach (string xmlfile in xmlfiles)
                {
                    try
                    {
                        XmlDocument langdoc = new XmlDocument();
                        langdoc.Load(xmlfile);
                        string langname = langdoc.GetElementsByTagName("language").Item(0).Attributes.GetNamedItem("name").Value;                        
                        if (newlanguage == langname)
                        {
                            Translate.Clear();
                            if (LoadLanguageFromXML(xmlfile))
                            {                                
                                return true;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public bool LoadLanguageFromXML(string xmlfile)
        {
            try
            {
                XmlDocument langdoc = new XmlDocument();
                langdoc.Load(xmlfile);
                XmlNode langnode = langdoc.GetElementsByTagName("language").Item(0);
                Translate.Clear();
                foreach (XmlNode itemnode in langnode.ChildNodes)
                {
                    Translate.Add(itemnode.Attributes.GetNamedItem("name").Value, itemnode.Attributes.GetNamedItem("value").Value);                    
                }
            }
            catch
            {
                return false;
            }
            return true;
        }
    }
}
