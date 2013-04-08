using System;
using System.Net.Mail;

using MCEBuddy.Globals;

namespace MCEBuddy.Util
{
    public static class eMail
    {
        private class EmailUserState {
            public Log JobLog { get; set; }
            public object Context { get; set; }
        }

        /// <summary>
        /// Send email immediately (synchronously)
        /// </summary>
        /// <returns>True if eMail is sent successfully</returns>
        public static bool SendEMail(EMailOptions emailOptions, string subject, string message, ref Log jobLog, object context)
        {
            return SendEMail(emailOptions, subject, message, ref jobLog, context, null);
        }

        /// <summary>
        /// Send email in the background and callback handler when completed (asynchronously).
        /// Check the error in the callback handler for eMail status (success or error or cancelled)
        /// </summary>
        /// <returns>False if an error is encountered crafting the eMail message</returns>
        public static bool SendEMail(EMailOptions emailOptions, string subject, string message, ref Log jobLog, object context, SendCompletedEventHandler asyncCallback)
        {
            string smtpServer = emailOptions.smtpServer;
            int portNo = emailOptions.port; // default port is 25
            bool ssl = emailOptions.ssl;
            string fromAddress = emailOptions.fromAddress;
            string toAddresses = emailOptions.toAddresses;
            string bccAddresses = emailOptions.bccAddress;
            string username = emailOptions.userName;
            string password = emailOptions.password;

            jobLog.WriteEntry(context, Localise.GetPhrase("Request to send eMail"), Log.LogEntryType.Information, true);
            jobLog.WriteEntry(context, "Server -> " + smtpServer, Log.LogEntryType.Debug, true);
            jobLog.WriteEntry(context, "Port -> " + portNo.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug, true);
            jobLog.WriteEntry(context, "SSL -> " + ssl.ToString(System.Globalization.CultureInfo.InvariantCulture), Log.LogEntryType.Debug, true);
            jobLog.WriteEntry(context, "Username -> " + username, Log.LogEntryType.Debug, true);
            jobLog.WriteEntry(context, "From -> " + fromAddress, Log.LogEntryType.Debug, true);
            jobLog.WriteEntry(context, "To -> " + toAddresses, Log.LogEntryType.Debug, true);
            jobLog.WriteEntry(context, "Subject -> " + subject, Log.LogEntryType.Debug, true);
            jobLog.WriteEntry(context, "Message -> " + message, Log.LogEntryType.Debug, true);

            try
            {
                // Create the eMail message
                MailMessage eMailMessage = new MailMessage();
                eMailMessage.Subject = subject;
                eMailMessage.Body = message;
                eMailMessage.From = new MailAddress(fromAddress);
                if (!String.IsNullOrWhiteSpace(toAddresses)) // Avoid an exception, since to is not mandatory
                {
                    string[] addresses = toAddresses.Split(';');
                    for (int i = 0; i < addresses.Length; i++)
                        eMailMessage.To.Add(addresses[i]); // Add the To recipients
                }
                if (!String.IsNullOrWhiteSpace(bccAddresses)) // Avoid an exception, since bcc is not mandatory
                {
                    string[] bccToAddresses = bccAddresses.Split(';');
                    for (int i = 0; i < bccToAddresses.Length; i++)
                        eMailMessage.Bcc.Add(bccToAddresses[i]); // Add the Bcc recipients
                }
                eMailMessage.BodyEncoding = System.Text.Encoding.UTF8;
                eMailMessage.SubjectEncoding = System.Text.Encoding.UTF8;

                // Create the client to send the message
                SmtpClient eMailClient = new SmtpClient(smtpServer, portNo);
                if (username != "")
                    eMailClient.Credentials = new System.Net.NetworkCredential(username, password); // add the authentication details
                if (ssl)
                    eMailClient.EnableSsl = true;// Set the SSL if required
                eMailClient.Timeout = GlobalDefs.SMTP_TIMEOUT; // Set the timeout

                // Send the eMail - check for Async or Sync email sending
                if (asyncCallback == null)
                {
                    eMailClient.Send(eMailMessage);
                    jobLog.WriteEntry(context, Localise.GetPhrase("Successfully send eMail"), Log.LogEntryType.Information, true);
                }
                else
                {
                    eMailClient.SendCompleted += eMailClient_SendCompleted + asyncCallback; // Register call back
                    eMailClient.SendAsync(eMailMessage, new EmailUserState { Context = context, JobLog = jobLog });
                }
                
                return true;
            }
            catch (Exception e)
            {
                jobLog.WriteEntry(context, Localise.GetPhrase("Error sending eMail") + " -> " + e.ToString(), Log.LogEntryType.Error, true);
                return false;
            }
        }

        /// <summary>
        /// Call back handler for aSynchronous eMail, checks for success or failure of email sending
        /// </summary>
        private static void eMailClient_SendCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            EmailUserState userState = (EmailUserState)e.UserState;

            if (userState == null)
                return;

            if (e.Error == null)
                userState.JobLog.WriteEntry(userState.Context, Localise.GetPhrase("Successfully send eMail"), Log.LogEntryType.Information, true);
            else
                userState.JobLog.WriteEntry(userState.Context, Localise.GetPhrase("Error sending eMail") + " -> " + e.ToString(), Log.LogEntryType.Error, true);

            return;
        }
    }
}
