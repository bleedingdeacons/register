using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Models
{
    /// <summary>
    /// Represents an attendee of an intergroup meeting.
    /// </summary>
    public sealed class IntergroupMeetingAttendee
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }
}
