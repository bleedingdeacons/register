using System.Net;
using System.Net.Mail;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.Extensions
{
    public static class Extensions
    {
        private const string ONLINE_TAG = "online";
        public static bool IsOnline(this Group group)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));
            // Check if the group has a generic email and is using it
            return !string.IsNullOrEmpty(group.Types) && group.Types.ToLower().Contains(ONLINE_TAG);
        }

        public static bool HasAll(this Group group)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            // For online groups, we only require a GSR name and email
            return (!string.IsNullOrEmpty(group.GsrName) && !string.IsNullOrEmpty(group.GsrPhone) && !string.IsNullOrEmpty(group.GsrEmailPersonal));

        }

        public static async Task<bool> TestSmtpConnectionAsync(this IMailService ms, SmtpConfiguration config)
        {
            try
            {
                using var testClient = new SmtpClient(config.Host, config.Port)
                {
                    Credentials = new NetworkCredential(config.Username, config.Password),
                    EnableSsl = config.EnableSsl,
                    Timeout = config.TimeoutSeconds * 1000
                };

                // Just test the connection without sending
                await testClient.SendMailAsync(
                    config.Username,
                    config.Username,
                    "Test Connection",
                    "This is a test email to verify SMTP settings."
                );

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static List<GroupContact> GetContacts(this Group group)
        {
            var contacts = new List<GroupContact>();

            if (group != null)
            {
                if (!string.IsNullOrEmpty(group.Contact1Name))
                {

                    var contact1 = new GroupContact
                    {
                        Name = group.Contact1Name ?? string.Empty,
                        Email = group.Contact1Email ?? string.Empty,
                        Mobile = group.Contact1Phone ?? string.Empty
                    };

                    contacts.Add(contact1);
                }


                if (!string.IsNullOrEmpty(group.Contact2Name))
                {
                    var contact2 = new GroupContact
                    {
                        Name = group.Contact2Name ?? string.Empty,
                        Email = group.Contact2Email ?? string.Empty,
                        Mobile = group.Contact2Phone ?? string.Empty
                    };

                    contacts.Add(contact2);
                }

                if (!string.IsNullOrEmpty(group.Contact3Name))
                {
                    var contact3 = new GroupContact
                    {
                        Name = group.Contact3Name ?? string.Empty,
                        Email = group.Contact3Email ?? string.Empty,
                        Mobile = group.Contact3Phone ?? string.Empty
                    };

                    contacts.Add(contact3);
                }

            }

            return contacts;
        }


        public static string GetGsrFirstName(this Group group)
        {
            return group.GsrName.Split(' ').First();
        }

    }

}

