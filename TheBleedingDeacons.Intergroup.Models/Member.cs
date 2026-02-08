using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Models
{
    /// <summary>
    /// Represents a member in the Unity system.
    /// </summary>
    public sealed class Member
    {
        public int Id { get; init; }
        public string PrivateName { get; init; } = string.Empty;
        public string AnonymousName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string PersonalEmail { get; init; } = string.Empty;
        public string MobileNumber { get; init; } = string.Empty;
        public bool ShowAnonymousName { get; init; }
        public bool ShowMemberProfile { get; init; }
        public string AnonymousProfile { get; init; } = string.Empty;
        public int? HomeGroupId { get; init; }
        public string HomeGroupName { get; init; } = string.Empty;

        /// <summary>
        /// Full home group object (when expand=home_group is used).
        /// This will be populated when the API returns home_group.
        /// </summary>
        public Group? HomeGroup { get; init; }

        public bool IsGsr { get; init; }
        public string MeetingPo { get; init; } = string.Empty;
        public int? IntergroupPositionId { get; init; }
        public string IntergroupPositionName { get; init; } = string.Empty;
        public string IntergroupPositionRotation { get; init; } = string.Empty;
        public string Link { get; init; } = string.Empty;

        /// <summary>
        /// Gets whether this member has expanded home group data.
        /// </summary>
        [JsonIgnore]
        public bool HasExpandedHomeGroup => HomeGroup != null;
    }

}
