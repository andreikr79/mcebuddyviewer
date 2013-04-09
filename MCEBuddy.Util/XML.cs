using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using System.IO;

namespace MCEBuddy.Util
{
    public static class XML
    {
        /// <summary>
        /// Gets the value of a Tag from a XML stream
        /// </summary>
        /// <param name="Tag">Tag from which to read the value</param>
        /// <param name="Source">XML Stream</param>
        /// <returns>Value of the Tag</returns>
        public static string GetXMLTagValue(string Tag, string Source)
        {
            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(Source);
                XmlNode xmlRootNode = xmlDoc.FirstChild;
                XmlNode xmlNode = xmlRootNode.SelectSingleNode(Tag);

                if (xmlNode != null)
                    return xmlNode.InnerText;
                else
                    return "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Gets all the Level 2 SubTags values of a Tag from a XML stream (assuming all SubTags have the same name)
        /// </summary>
        /// <param name="Tag">Tag</param>
        /// <param name="SubTag">SubTag in Tag from which to read the values</param>
        /// <param name="Source">XML Stream</param>
        /// <returns>Array of Values of the SubTag</returns>
        public static string[] GetXMLSubTagValues(string Tag, string SubTag, string Source)
        {
            try
            {
                using (var reader = new StringReader(Source))
                using (var Tree = XmlReader.Create(reader))
                {
                    List<string> retVal = new List<string>();

                    while (Tree.Read())
                    {
                        if ((Tree.NodeType == XmlNodeType.Element) && (Tree.Name == Tag))
                        {
                            XmlReader subTree = Tree.ReadSubtree();
                            while (subTree.Read())
                            {
                                if ((subTree.NodeType == XmlNodeType.Element) && (subTree.Name == SubTag))
                                {
                                    string val = subTree.ReadElementContentAsString();
                                    if (!String.IsNullOrEmpty(val))
                                        retVal.Add(val);
                                }
                            }
                        }
                    }

                    if (retVal.Count == 0)
                        return null;
                    else
                        return retVal.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets all the Level 3 SubTag values of a Tag from a XML stream (assuming all SubTags have the same name)
        /// </summary>
        /// <param name="Tag">Tag</param>
        /// <param name="SubTag">SubTag in Tag</param>
        /// <param name="SubTag2">SubTag in SubTag from which to read the values</param>
        /// <param name="Source">XML Stream</param>
        /// <returns>Array of Values of the SubTag</returns>
        public static string[] GetXMLSubTagValues(string Tag, string SubTag, string SubTag2, string Source)
        {
            try
            {
                using (var reader = new StringReader(Source))
                using (var Tree = XmlReader.Create(reader))
                {
                    List<string> retVal = new List<string>();

                    while (Tree.Read())
                    {
                        if ((Tree.NodeType == XmlNodeType.Element) && (Tree.Name == Tag))
                        {
                            XmlReader subTree = Tree.ReadSubtree();
                            while (subTree.Read())
                            {
                                if ((subTree.NodeType == XmlNodeType.Element) && (subTree.Name == SubTag))
                                {
                                    XmlReader subTree2 = subTree.ReadSubtree();
                                    while (subTree2.Read())
                                    {
                                        if ((subTree2.NodeType == XmlNodeType.Element) && (subTree2.Name == SubTag2))
                                        {
                                            string val = subTree.ReadElementContentAsString();
                                            if (!String.IsNullOrEmpty(val))
                                                retVal.Add(val);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (retVal.Count == 0)
                        return null;
                    else
                        return retVal.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
