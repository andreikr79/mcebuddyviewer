using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.MediaCenter.UI;
using Microsoft.MediaCenter;
using Microsoft.MediaCenter.Hosting;

namespace MceBuddyViewer
{
    public class ListSelection : ModelItem
    {
        private int _index;
        private object _item;

        public int Index
        {
            get
            {
                return _index;
            }
            set
            {
                _index = value;
                FirePropertyChanged("Index");
            }
        }

        public object Item
        {
            get
            {
                return _item;
            }
            set
            {
                _item = value;
                FirePropertyChanged("Item");
            }
        }
    }
}
