using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Models
{
    /// <summary>
    /// Represents a position in the Unity system.
    /// </summary>
    public sealed class Position
    {
        public int Id { get; init; }
        public string LongName { get; init; } = string.Empty;
        public string ShortDescription { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public int MinimumSobriety { get; init; }
        public int TermYears { get; init; }
        public string Link { get; init; } = string.Empty;
    }

}
