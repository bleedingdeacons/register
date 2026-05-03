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
        public string MeetingName { get; set; }
        public List<MeetingContact> MeetingContacts { get; set; }

        /// <summary>
        /// The privacy / GDPR statement the recipient accepted at
        /// registration, included in the welcome email so they have a
        /// written record of the wording they consented to. Resolves
        /// into the <c>{{Policy}}</c> placeholder in
        /// <c>Templates/WelcomeEmail.html</c>.
        ///
        /// Populated per-recipient from <c>Member.GdprAcceptanceStatement</c>
        /// (set by <c>ComplianceService.RecordAcceptance</c>) rather than
        /// from a freshly-loaded copy of the policy file — that way each
        /// person sees exactly what they agreed to, even if the policy
        /// file has been edited since. The sender wraps plain-text
        /// statements in <c>&lt;p&gt;</c> blocks so paragraphs render in
        /// HTML mail clients; statements that already contain HTML are
        /// passed through untouched.
        /// </summary>
        public string Policy { get; set; }

    }
}
