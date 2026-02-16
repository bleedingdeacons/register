using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Extensions
{
    public static class Extensions
    {
        private const string ONLINE_TAG = "online";

        public static bool IsOnline(this Group group)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            return !string.IsNullOrEmpty(group.Types) && group.Types.ToLower().Contains(ONLINE_TAG);
        }

        public static bool HasAll(this Group group)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            return !string.IsNullOrEmpty(group.GsrName)
                && !string.IsNullOrEmpty(group.GsrPhone)
                && !string.IsNullOrEmpty(group.GsrEmailPersonal);
        }

        public static List<GroupContact> GetContacts(this Group group)
        {
            var contacts = new List<GroupContact>();

            if (group == null)
                return contacts;

            if (!string.IsNullOrEmpty(group.Contact1Name))
            {
                contacts.Add(new GroupContact
                {
                    Name = group.Contact1Name,
                    Email = group.Contact1Email ?? string.Empty,
                    Mobile = group.Contact1Phone ?? string.Empty
                });
            }

            if (!string.IsNullOrEmpty(group.Contact2Name))
            {
                contacts.Add(new GroupContact
                {
                    Name = group.Contact2Name,
                    Email = group.Contact2Email ?? string.Empty,
                    Mobile = group.Contact2Phone ?? string.Empty
                });
            }

            if (!string.IsNullOrEmpty(group.Contact3Name))
            {
                contacts.Add(new GroupContact
                {
                    Name = group.Contact3Name,
                    Email = group.Contact3Email ?? string.Empty,
                    Mobile = group.Contact3Phone ?? string.Empty
                });
            }

            return contacts;
        }

        public static string GetFirstName(this Group group)
        {
            string name;

            if (!string.IsNullOrWhiteSpace(group.ProxyName))
                name = group.ProxyName;
            else
                name = group.GsrName ?? string.Empty;

            return name.Split(' ').First();
        }
    }
}