using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.MediaCenter.UI;
using Microsoft.MediaCenter;
using Microsoft.MediaCenter.Hosting;

namespace MceBuddyViewer
{
    public class ListItemNode : ModelItem
    {
        public string[] fileQueue;
        public double Percent
        {
            get
            {
                return _percent;
            }
            set
            {
                _percent = value;
                FirePropertyChanged("Percent");
            }
        }
        public bool isCurrent
        {
            get
            {
                return _iscurrent;
            }
            set
            {
                _iscurrent = value;
                FirePropertyChanged("isCurrent");
            }
        }
        public override string ToString() { return fileQueue[0]; }
        private double _percent = 0;
        private bool _iscurrent = false;
    }
}
