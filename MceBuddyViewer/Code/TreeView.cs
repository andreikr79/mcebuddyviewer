﻿using System;
using System.Collections.Generic;
using Microsoft.MediaCenter.UI;

namespace MceBuddyViewer
{
    public class TreeView : ModelItem
    {
        public virtual event EventHandler<TreeNodeEventArgs> CheckedNodeChanged;

        public virtual TreeNode CheckedNode
        {
            get
            {
                if (_checkedNode == null && ChildNodes.Count > 0)
                    _checkedNode = ChildNodes[0] as TreeNode;

                return _checkedNode;
            }
            set
            {
                if (_checkedNode != value)
                {
                    _checkedNode = value;
                    FirePropertyChanged("CheckedNode");
                    if (CheckedNodeChanged != null)
                    {
                        TreeNodeEventArgs e = new TreeNodeEventArgs(CheckedNode);
                        CheckedNodeChanged(this, e);
                    }
                }
            }
        }
        public ArrayListDataSet ChildNodes
        {
            get { return _childNodes; }
            set { _childNodes = value; }
        }

        #region Fields

        private ArrayListDataSet _childNodes = new ArrayListDataSet();
        protected TreeNode _checkedNode = null;

        #endregion

    }
}
