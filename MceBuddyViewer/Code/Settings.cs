using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using Microsoft.MediaCenter.UI;
using Microsoft.MediaCenter;
using Microsoft.MediaCenter.Hosting;

namespace MceBuddyViewer
{
    public class Settings : ModelItem
    {        
        private Localization _language = new Localization();
        private FontType _fontname;       

        public Settings()
        {
            // Звгружаем из реестра данные о шрифте
            RegistryKey viewerkey = Registry.CurrentUser;
            viewerkey = viewerkey.OpenSubKey(@"SOFTWARE\MCEBuddyViewer", false);            
            if (viewerkey.GetValue("Font") != null)
            {
                string fontname=(string)viewerkey.GetValue("Font");
                if (Enum.IsDefined(typeof(FontType), fontname))
                    FontName = (FontType)Enum.Parse(typeof(FontType), fontname, true);                
            }
            else
            {
                FontName = FontType.Normal;
            }
        }

        public void SaveSettings()
        {
            try
            {
                RegistryKey viewerkey = Registry.CurrentUser;
                viewerkey = viewerkey.OpenSubKey(@"SOFTWARE\MCEBuddyViewer", true);
                viewerkey.SetValue("Language", Language.CurrentLanguage);
                viewerkey.SetValue("Font", FontName.ToString());
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

        public FontType FontName
        {
            get { return _fontname; }
            set 
            {
                if (_fontname != value)
                {
                    _fontname = value;
                    FirePropertyChanged("FontName");
                }
            }
        }

        public enum FontType
        {
            Small,
            Normal,
            Large
        }
    }
}
