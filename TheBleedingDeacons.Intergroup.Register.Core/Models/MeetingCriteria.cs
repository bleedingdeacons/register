using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Utilities;

namespace TheBleedingDeacons.Intergroup.Register.Models
{
    [TypeConverter(typeof(MeetingCriteriaConverter))]
    public class MeetingCriteria
    {
        public required string Day { get; set; }
        public required string MeetingType { get; set; }
    }
}
