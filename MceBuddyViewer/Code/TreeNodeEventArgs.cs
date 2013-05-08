using System;

namespace MceBuddyViewer
{
    public class TreeNodeEventArgs : EventArgs
    {
        public TreeNodeEventArgs(TreeNode node, bool multiselect = false)
        {
            Node = node;
            MultiSelect = multiselect;
        }

        public TreeNode Node
        {
            get { return _node; }
            set { _node = value; }
        }

        public bool MultiSelect
        {
            get { return _multiselect; }
            set { _multiselect = value; }
        }

        #region Fields

        private TreeNode _node = null;
        private bool _multiselect = false;

        #endregion

    }
}
