using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace MceBuddyViewer
{
    public class Settings
    {        
        private Localization _language = new Localization();
        private string _fontname;
        private int _fontsize;

        public Settings()
        {
            // Звгружаем из реестра данные о шрифте
            RegistryKey viewerkey = Registry.CurrentUser;
            viewerkey = viewerkey.OpenSubKey(@"SOFTWARE\MCEBuddyViewer", false);
            if (viewerkey.GetValue("Font Name") != null)
            {
                FontName = (string)viewerkey.GetValue("Font Name");
            }
            else
            {
                FontName = "Segoe Media Center";
            }
            if (viewerkey.GetValue("Font Size") != null)
            {
                FontSize = (int)viewerkey.GetValue("Font Size");
            }
            else
            {
                FontSize = 24;
            }
        }

        public void SaveSettings()
        {
            try
            {
                RegistryKey viewerkey = Registry.CurrentUser;
                viewerkey = viewerkey.OpenSubKey(@"SOFTWARE\MCEBuddyViewer", true);
                viewerkey.SetValue("Language", Language.CurrentLanguage);
                viewerkey.SetValue("Font Name", FontName);
                viewerkey.SetValue("Font Size", FontSize);
            }
            catch
            {
            }
        }

        public Localization Language
        {
            get { return _language; }
            set { _language = value; }
        }
        public string FontName
        {
            get { return _fontname; }
            set { _fontname = value; }
        }
        public int FontSize
        {
            get { return _fontsize; }
            set { _fontsize = value; }
        }
    }
}
