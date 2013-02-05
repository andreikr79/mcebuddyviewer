using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Microsoft.MediaCenter.UI;

namespace MceBuddyViewer
{
    public class FileTreeNode : TreeNode
    {
        public string FullPath
        {
            get { return _fullPath; }
            set
            {
                _fullPath = value;
                if (Directory.Exists(_fullPath))
                {
                    HasChildNodes = (Directory.GetDirectories(_fullPath).Length > 0) || (Directory.GetFiles(_fullPath).Length > 0);
                }
                else
                {
                    HasChildNodes = false;
                }
                //HasChildNodes = (Directory.Exists(_fullPath) && Directory.GetDirectories(_fullPath).Length > 0);
            }
        }

        public FileTreeNode(String title, String fullPath, TreeView treeView)
            : base(title)
        {
            FullPath = fullPath;
            TreeView = treeView;
            TreeView.CheckedNodeChanged += new EventHandler<TreeNodeEventArgs>(TreeView_OnCheckedNodeChanged);
        }

        public override void GetChildNodes()
        {
            if (!String.IsNullOrEmpty(FullPath))
            {
                ChildNodes.Clear();
                string[] directories = Directory.GetDirectories(FullPath);
                string[] files = Directory.GetFiles(FullPath);

                foreach (string directory in directories)
                {
                    try
                    {
                        string folder = Path.Combine(FullPath, directory);
                        DirectoryInfo info = new DirectoryInfo(folder);
                        if ((info.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
                        {                            
                            FileTreeNode node = new FileTreeNode(Path.GetFileName(directory), directory, TreeView);
                            node.Level = Level + 1;
                            node.TreeView = TreeView;
                            TreeView.CheckedNodeChanged += new EventHandler<TreeNodeEventArgs>(TreeView_OnCheckedNodeChanged);
                            node.HasChildNodes = ((Directory.GetDirectories(node.FullPath).Length > 0) || (Directory.GetFiles(node.FullPath).Length > 0));
                            ChildNodes.Add(node);
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Debug.WriteLine(ex);
                    }
                    catch (DriveNotFoundException ex)
                    {
                        Debug.WriteLine(ex);
                    }
                    catch (IOException ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }

                foreach (string thefile in files)
                {
                    try
                    {
                        string hfile = Path.Combine(FullPath, thefile);
                        FileInfo info = new FileInfo(hfile);
                        if ((info.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
                        {
                            FileTreeNode node = new FileTreeNode(Path.GetFileName(thefile), thefile, TreeView);
                            node.Level = Level + 1;
                            node.TreeView = TreeView;
                            TreeView.CheckedNodeChanged += new EventHandler<TreeNodeEventArgs>(TreeView_OnCheckedNodeChanged);
                            node.HasChildNodes = false;
                            ChildNodes.Add(node);
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Debug.WriteLine(ex);
                    }
                    catch (DriveNotFoundException ex)
                    {
                        Debug.WriteLine(ex);
                    }
                    catch (IOException ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }

                HasChildNodes = (ChildNodes.Count > 0);
            }

            base.GetChildNodes();
        }

        public override string ToString()
        {
            return FullPath;
        }

        private void TreeView_OnCheckedNodeChanged(object sender, TreeNodeEventArgs e)
        {
            Checked.Value = (e.Node == this);
        }

        #region

        private string _fullPath = String.Empty;

        #endregion

    }
}
