using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.MediaCenter.UI;

namespace MceBuddyViewer
{
    public class FileTreeNode : TreeNode
    {
        public static string[] GetFiles(string path, string searchPattern)
        { 
            string[] m_arExt = searchPattern.Split(';'); 
            List<string> strFiles = new List<string>(); 
            foreach (string filter in m_arExt) 
            { 
                strFiles.AddRange(System.IO.Directory.GetFiles(path, filter)); 
            } 
            return strFiles.ToArray(); 
        }

        public string Filter
        {
            get { return _filter; }
            set
            {
                _filter = value;
            }
        }

        public string FullPath
        {
            get { return _fullPath; }
            set
            {
                _fullPath = value;
                if (Directory.Exists(_fullPath))
                {
                    HasChildNodes = (Directory.GetDirectories(_fullPath).Length > 0) || (FileTreeNode.GetFiles(_fullPath, Filter).Length > 0);
                }
                else
                {
                    HasChildNodes = false;
                }                
            }
        }

        public FileTreeNode(String title, String fullPath, TreeView treeView)
            : base(title)
        {
            FullPath = fullPath;
            TreeView = treeView;
            TreeView.CheckedNodeChanged += new EventHandler<TreeNodeEventArgs>(TreeView_OnCheckedNodeChanged);
        }

        public FileTreeNode(String title, String fullPath, String filter, TreeView treeView)
            : this(title, fullPath, treeView)
        {
            Filter = filter;
        }

        public override void GetChildNodes()
        {
            if (!String.IsNullOrEmpty(FullPath))
            {
                ChildNodes.Clear();
                string[] directories = Directory.GetDirectories(FullPath);
                string[] files = FileTreeNode.GetFiles(FullPath, Filter);

                foreach (string directory in directories)
                {
                    try
                    {
                        string folder = Path.Combine(FullPath, directory);
                        DirectoryInfo info = new DirectoryInfo(folder);
                        if ((info.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
                        {
                            FileTreeNode node = new FileTreeNode(Path.GetFileName(directory), directory, Filter, TreeView);
                            node.Level = Level + 1;
                            node.TreeView = TreeView;
                            node.Selectable = false;
                            TreeView.CheckedNodeChanged += new EventHandler<TreeNodeEventArgs>(TreeView_OnCheckedNodeChanged);
                            node.HasChildNodes = ((Directory.GetDirectories(node.FullPath).Length > 0) || (FileTreeNode.GetFiles(node.FullPath, Filter).Length > 0));
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
                            FileTreeNode node = new FileTreeNode(Path.GetFileName(thefile), thefile, Filter, TreeView);
                            node.Level = Level + 1;
                            node.TreeView = TreeView;
                            node.NodeColor = new Color(Colors.YellowGreen);
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
        private string _filter = "*.*";

        #endregion

    }
}
