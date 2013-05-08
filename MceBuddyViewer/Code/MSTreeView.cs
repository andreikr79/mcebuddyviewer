using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.MediaCenter.UI;

namespace MceBuddyViewer
{
    public class MSTreeView : TreeView
    {
        private ArrayListDataSet _checkedNodes=new ArrayListDataSet();
        public ArrayListDataSet CheckedNodes
        {
            get { return _checkedNodes; }
            set { _checkedNodes = value; }
        }
        public override event EventHandler<TreeNodeEventArgs> CheckedNodeChanged;
        public override TreeNode CheckedNode
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
                        TreeNodeEventArgs e = new TreeNodeEventArgs(CheckedNode, true);
                        CheckedNodeChanged(this, e);
                    }
                }
            }
        }
    }
}
