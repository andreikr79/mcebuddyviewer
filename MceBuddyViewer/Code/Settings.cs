using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MceBuddyViewer
{
    public class Settings
    {
        private Localization _language = new Localization();
        private string _fontname;
        private int _fontsize;

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
