using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MCEBuddy.Globals
{
    public class JobStatus
    {
        volatile private string _currentAction = "";
        volatile private float _percentageComplete = 0;
        volatile private string _eta = "";
        volatile private bool _cancelled = false;
        volatile private string _errorMsg = "";
        volatile private string _sourceFile = "";
        volatile private bool _successfulConversion = false;

        public bool SuccessfulConversion
        {
            get { return _successfulConversion; }
            set { _successfulConversion = value; }
        }

        public string CurrentAction
        {
            get { return _currentAction;  }
            set { _currentAction = value; }
        }

        public string ETA
        {
            get { return _eta; }
            set { _eta = value; }
        }

        public float PercentageComplete
        {
            get { return _percentageComplete; }
            set
            {
                if (value < 0)
                {
                    _percentageComplete = 0;
                }
                else if (value > 100)
                {
                    _percentageComplete = 100;
                }
                else
                {
                    _percentageComplete = value;
                }
            }
        }

        public bool Cancelled
        {
            get { return _cancelled; }
            set { _cancelled = value; }
        }

        public string ErrorMsg
        {
            get { return _errorMsg; }
            set { _errorMsg = value; }
        }

        public bool Error
        {
            get { return !String.IsNullOrEmpty(_errorMsg); }
        }

        public string SourceFile
        {
            get { return _sourceFile; }
            set { _sourceFile = value; }
        }
    }
}
