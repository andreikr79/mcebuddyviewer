using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MCEBuddy.Util
{
    /// <summary>
    /// Used to open the firewall to allow MCEBuddy to communicate
    /// </summary>
    static public class Firewall
    {
        public enum NET_FW_ACTION
        {
            NET_FW_ACTION_BLOCK = 0,
            NET_FW_ACTION_ALLOW = 1,
        }

        public enum NET_FW_IP_PROTOCOL
        {
            NET_FW_IP_PROTOCOL_TCP = 6,
            NET_FW_IP_PROTOCOL_UDP = 17,
            NET_FW_IP_PROTOCOL_ANY = 256,
        }

        public enum NET_FW_IP_VERSION
        {
            NET_FW_IP_VERSION_V4 = 0,
            NET_FW_IP_VERSION_V6 = 1,
            NET_FW_IP_VERSION_ANY = 2,
        }

        public enum NET_FW_SCOPE
        {
            NET_FW_SCOPE_ALL = 0,
            NET_FW_SCOPE_LOCAL_SUBNET = 1,
            NET_FW_SCOPE_CUSTOM = 2,
        }

        // ProgID for the AuthorizedApplication object
        private const string PROGID_AUTHORIZED_APPLICATION = "HNetCfg.FwAuthorizedApplication";
        private const string PROGID_FIREWALL_MANAGER = "HNetCfg.FwMgr";
        private const string PROGID_OPEN_PORT = "HNetCfg.FWOpenPort";
        private const string PROGID_FW_RULE = "HNetCfg.FWRule";
        private const string PROGID_FW_POLICY = "HNetCfg.FwPolicy2";

        /// <summary>
        /// Creates a COM Object by name.
        /// </summary>
        /// <param name="comName">The Application name of the COM Object to create.</param>
        /// <returns>The created COM object or null if not available.</returns>
        private static System.Object CreateCOMObject(string comName)
        {
            // Get the type
            System.Type ltCOMType = System.Type.GetTypeFromProgID(comName);

            try
            {
                // If type found
                if (ltCOMType != null)
                    // Create the instance
                    return System.Activator.CreateInstance(ltCOMType);
                else
                    // Otherwise return null
                    return null;
            }
            catch
            {
                // Any errors and we return null.
                return null;
            }
        }

        /// <summary>
        /// Authorize an application to work through the firewall
        /// </summary>
        /// <param name="title">Name of application</param>
        /// <param name="applicationPath">Path to executable</param>
        /// <param name="scope">Scope (All, Local, Custom)</param>
        /// <param name="ipVersion">IPv4, IpV6 or both</param>
        /// <returns>True if it succeeds</returns>
        public static bool AuthorizeApplication(string title, string applicationPath, NET_FW_SCOPE scope, NET_FW_ACTION action, NET_FW_IP_VERSION ipVersion)
        {
            try
            {               
                    //INetFwRule2 firewallRule = (INetFwRule2)Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));
                    ////dynamic firewallRule = CreateCOMObject(PROGID_FW_RULE);
                    //firewallRule.Action = action;
                    //firewallRule.Name = title;
                    //firewallRule.ApplicationName = applicationPath;
                    //firewallRule.Enabled = true;
                    //firewallRule.InterfaceTypes = "All";
                    //firewallRule.EdgeTraversal = true;

                    //dynamic firewallPolicy = CreateCOMObject(PROGID_FW_POLICY);
                    //firewallPolicy.Rules.Add(firewallRule);
                
            }
            catch (Exception e)
            {
                System.Diagnostics.EventLog.WriteEntry("MCEBuddy2x", "Error authorizing firewall application -> " + e.ToString(), System.Diagnostics.EventLogEntryType.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Opens a port on a the Windows Firewall
        /// </summary>
        /// <param name="title">Name of Firewall Entry</param>
        /// <param name="portNo">Port number</param>
        /// <param name="scope">All, Subnet, Custom</param>
        /// <param name="protocol">TCP, IP, Both</param>
        /// <param name="ipVersion">IPv4, IPv6, Both</param>
        /// <returns>True if successful</returns>
        public static bool AuthorizePort(string title, int portNo, NET_FW_SCOPE scope, NET_FW_IP_PROTOCOL protocol, NET_FW_IP_VERSION ipVersion)
        {
            try
            {
                
                    //dynamic firewallRule = CreateCOMObject(PROGID_FW_RULE);
                    //firewallRule.Name = title;
                    //firewallRule.Protocol = protocol;
                    //firewallRule.LocalPorts = portNo.ToString();
                    //firewallRule.Enabled = true;
                    //firewallRule.InterfaceTypes = "All";
                    //firewallRule.EdgeTraversal = true;

                    //dynamic firewallPolicy = CreateCOMObject(PROGID_FW_POLICY);
                    //firewallPolicy.Rules.Add(firewallRule);
                
            }
            catch (Exception e)
            {
                System.Diagnostics.EventLog.WriteEntry("MCEBuddy2x", "Error enabling firewall port -> " + e.ToString(), System.Diagnostics.EventLogEntryType.Warning);
                return false;
            }

            return true;
        }
    }
}
