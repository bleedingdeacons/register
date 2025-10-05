using System.ComponentModel;
using System.Globalization;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Utilities
{
    public class GroupCriteriaConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string stringValue)
            {
                // Parse your custom format (e.g., "John,25")
                var parts = stringValue.Split(',');
                return new GroupCriteria
                {
                     Day = parts[0],
                     MeetingType = parts[1]
                };
            }
            return base.ConvertFrom(context, culture, value);
        }
        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is GroupCriteria group)
            {
                return $"{group.Day}|{group.MeetingType}";
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
