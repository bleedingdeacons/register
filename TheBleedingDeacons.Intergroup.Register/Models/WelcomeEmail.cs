using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Models
{
    public class WelcomeEmail
    {
        
        public string FirstName { get; set; }

        public string Location { get; set; }

        public string Address { get; set; }

        public string StartTime { get; set; }
        public string Email { get; set; }
        public string Mobile { get; set; }
        public string GroupName { get; set; }
        public List<GroupContact> GroupContacts { get; set; }

    }
}
