using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Extensions
{
    public static class Extensions
    {
        private const string ONLINE_TAG = "online";

        public static bool IsOnline(this Meeting meeting)
        {
            if (meeting == null)
                throw new ArgumentNullException(nameof(meeting));

            return !string.IsNullOrEmpty(meeting.Types) && meeting.Types.ToLower().Contains(ONLINE_TAG);
        }

        public static bool HasAll(this Meeting meeting)
        {
            if (meeting == null)
                throw new ArgumentNullException(nameof(meeting));

            // At least one GSR must have all required fields
            var gsrs = meeting.Group?.Gsrs;
            return gsrs != null
                && gsrs.Any(g => !string.IsNullOrEmpty(g.Name)
                              && !string.IsNullOrEmpty(g.Phone)
                              && !string.IsNullOrEmpty(g.EmailPersonal));
        }

        public static List<MeetingContact> GetContacts(this Meeting meeting)
        {
            var contacts = new List<MeetingContact>();

            if (meeting == null)
                return contacts;

            if (!string.IsNullOrEmpty(meeting.Contact1Name))
            {
                contacts.Add(new MeetingContact
                {
                    Name = meeting.Contact1Name,
                    Email = meeting.Contact1Email ?? string.Empty,
                    Mobile = meeting.Contact1Phone ?? string.Empty
                });
            }

            if (!string.IsNullOrEmpty(meeting.Contact2Name))
            {
                contacts.Add(new MeetingContact
                {
                    Name = meeting.Contact2Name,
                    Email = meeting.Contact2Email ?? string.Empty,
                    Mobile = meeting.Contact2Phone ?? string.Empty
                });
            }

            if (!string.IsNullOrEmpty(meeting.Contact3Name))
            {
                contacts.Add(new MeetingContact
                {
                    Name = meeting.Contact3Name,
                    Email = meeting.Contact3Email ?? string.Empty,
                    Mobile = meeting.Contact3Phone ?? string.Empty
                });
            }

            return contacts;
        }

        public static string GetFirstName(this Meeting meeting)
        {
            string name;

            if (!string.IsNullOrWhiteSpace(meeting.ProxyName))
                name = meeting.ProxyName;
            else
                name = meeting.Group?.Gsrs.FirstOrDefault()?.Name ?? string.Empty;

            return name.Split(' ').First();
        }
    }
}