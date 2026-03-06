using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Unity.Intergroup.Entities;

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
    }
}
