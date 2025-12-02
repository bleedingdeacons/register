using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Converters
{
    public class ObscurePhoneNumberConverter : IValueConverter
    {
        /// <summary>
        /// Character to use for obscuring (default: ●)
        /// </summary>
        public char ObscureCharacter { get; set; } = '●';

        /// <summary>
        /// Number of digits visible from the end (default: 4)
        /// </summary>
        public int VisibleDigitsFromEnd { get; set; } = 4;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && !string.IsNullOrEmpty(str))
            {
                // Extract only digits from the string (handles formatted phone numbers)
                string digitsOnly = new string(str.Where(char.IsDigit).ToArray());

                // If string has fewer or equal digits than visible count, obscure all
                if (digitsOnly.Length <= VisibleDigitsFromEnd)
                {
                    return new string(ObscureCharacter, digitsOnly.Length);
                }

                // Obscure all except last N digits
                string lastDigits = digitsOnly.Substring(digitsOnly.Length - VisibleDigitsFromEnd);
                string obscured = new string(ObscureCharacter, digitsOnly.Length - VisibleDigitsFromEnd);

                return $"{obscured} {lastDigits}";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}