using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Unity.Data.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Extensions
{
    public static class Extensions
    {
        private const string ONLINE_TAG = "online";

        public static bool IsOnline(this Meeting meeting)
        {
            if (meeting == null)
                throw new ArgumentNullException(nameof(meeting));

            return meeting.IsOnline ||
                   (!string.IsNullOrEmpty(meeting.Types) &&
                    meeting.Types.ToLowerInvariant().Contains(ONLINE_TAG));
        }

        public static List<MeetingContact> GetContacts(this Meeting meeting)
        {
            // Unity.Data.Entities.Meeting doesn't carry contacts directly.
            // Contacts live on the Unity.Models.Meeting / Group level.
            // This is a placeholder — the register may need to resolve contacts
            // from the group or from cached API data.
            return new List<MeetingContact>();
        }

        /// <summary>
        /// Gets the first name from the first GSR member of the meeting's group,
        /// or from a proxy name if provided.
        /// </summary>
        public static string GetFirstName(this Meeting meeting, string? proxyName = null)
        {
            string name;

            if (!string.IsNullOrWhiteSpace(proxyName))
                name = proxyName;
            else
                name = meeting.Group?.Members.FirstOrDefault(m => m.IsGsr)?.AnonymousName ?? string.Empty;

            return name.Split(' ').First();
        }
    }
}
