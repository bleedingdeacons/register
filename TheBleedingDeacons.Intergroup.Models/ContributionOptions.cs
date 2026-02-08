using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Models
{
    /// <summary>
    /// Digital contribution options for a group.
    /// </summary>
    public sealed class ContributionOptions
    {
        public string Venmo { get; init; } = string.Empty;
        public string Paypal { get; init; } = string.Empty;
        public string Square { get; init; } = string.Empty;
        public bool HasOptions { get; init; }
    }

}
