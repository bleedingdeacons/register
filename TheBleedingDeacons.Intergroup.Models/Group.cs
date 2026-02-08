using System.Text.Json.Serialization;

namespace TheBleedingDeacons.Intergroup.Models
{
    /// <summary>
    /// Represents a group in the Unity system.
    /// </summary>
    public sealed class Group
    {
        public int Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string Website { get; init; } = string.Empty;
        public string Link { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
        public int? DistrictId { get; init; }
        public string? LastContact { get; init; }

        /// <summary>
        /// Meeting IDs associated with this group (when expand=meetings is not used).
        /// This will be populated when the API returns meeting_ids.
        /// </summary>
        public List<int> MeetingIds { get; init; } = [];

        /// <summary>
        /// Full meeting objects associated with this group (when expand=meetings is used).
        /// This will be populated when the API returns meetings.
        /// </summary>
        public List<Meeting> Meetings { get; init; } = [];

        public List<Contact> Contacts { get; init; } = [];
        public ContributionOptions? ContributionOptions { get; init; }

        /// <summary>
        /// Gets whether this group has expanded meeting data.
        /// </summary>
        [JsonIgnore]
        public bool HasExpandedMeetings => Meetings.Count > 0;
    }

}
