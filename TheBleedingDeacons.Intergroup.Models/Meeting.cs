using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Models
{
    /// <summary>
    /// Represents a meeting in the Unity system.
    /// </summary>
    public sealed class Meeting
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public Location? Location { get; init; }
        public string Url { get; init; } = string.Empty;
        public int Day { get; init; }
        public string DayOfWeek { get; init; } = string.Empty;
        public string Time { get; init; } = string.Empty;
        public string EndTime { get; init; } = string.Empty;
        public List<string> Types { get; init; } = [];
        public string State { get; init; } = string.Empty;
        public bool IsOnline { get; init; }
        public string OnlineLink { get; init; } = string.Empty;
        public string OnlineNotes { get; init; } = string.Empty;
        public List<Contact> Contacts { get; init; } = [];
        public Dictionary<string, object>? Meta { get; init; }
    }


}
