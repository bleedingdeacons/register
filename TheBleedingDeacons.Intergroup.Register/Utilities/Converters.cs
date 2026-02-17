using CommunityToolkit.Maui.Converters;
using System.Globalization;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.ViewModels;

namespace TheBleedingDeacons.Intergroup.Register.Utilities;

public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }
}

public class IsEqualConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() == parameter?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class IsNotEqualConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() != parameter?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class IsNotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null && !string.IsNullOrWhiteSpace(value.ToString());
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class IsNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null || string.IsNullOrWhiteSpace(value?.ToString());
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class CountdownToProgressConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int countdown)
        {
            // Convert countdown (5 to 0) to progress (1 to 0)
            // When countdown is 5, progress should be 1 (full)
            // When countdown is 0, progress should be 0 (empty)
            double progress = countdown / 5.0;

            // Ensure progress is between 0 and 1
            return Math.Max(0.0, Math.Min(1.0, progress));
        }
        return 1.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToYesSaveConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isEditing)
            return isEditing ? "Save" : "Yes";
        return "Yes";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToCancelBackConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isEditing)
            return isEditing ? "Cancel" : "Back";
        return "Back";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class QuestionVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2) return false;

        var isEditing = values[0] is bool editing && editing;
        var hasValidationErrors = values[1] is bool errors && errors;

        // Show question when not editing AND no validation errors
        return !isEditing && !hasValidationErrors;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ValidationVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2) return false;

        var isEditing = values[0] is bool editing && editing;
        var hasValidationErrors = values[1] is bool errors && errors;

        // Show validation when not editing AND has validation errors
        return !isEditing && hasValidationErrors;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter that checks if any string value is not empty for MultiBinding scenarios
/// </summary>
public class StringNotEmptyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length == 0)
            return false;

        // Check if any of the string values is not null or empty
        foreach (var value in values)
        {
            if (value is string str && !string.IsNullOrWhiteSpace(str))
                return true;
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter for showing "Using Generic" display label when not editing and group email has value
/// </summary>
public class UsingGenericVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length != 2)
            return false;

        // values[0] = IsEditing (bool)
        // values[1] = DisplayMeetingGenericEmail (string)

        if (values[0] is bool isEditing && values[1] is string displayEmail)
        {
            // Show when NOT editing AND email has value
            return !isEditing && !string.IsNullOrWhiteSpace(displayEmail);
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter for showing "Using Generic" checkbox when editing and group email has value
/// </summary>
public class UsingGenericEditVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length != 2)
            return false;

        // values[0] = IsEditing (bool)
        // values[1] = EditMeetingGenericEmail (string)

        if (values[0] is bool isEditing && values[1] is string editEmail)
        {
            // Show when editing AND email has value
            return isEditing && !string.IsNullOrWhiteSpace(editEmail);
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Alternative simplified converter that handles both label and checkbox visibility
/// Pass "label" or "checkbox" as converter parameter to specify which control
/// </summary>
public class UsingGenericUnifiedVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length != 3)
            return false;

        // values[0] = IsEditing (bool)
        // values[1] = DisplayMeetingGenericEmail (string)
        // values[2] = EditMeetingGenericEmail (string)

        if (values[0] is bool isEditing)
        {
            string controlType = parameter?.ToString()?.ToLower();

            if (controlType == "label")
            {
                // Show label when NOT editing AND display email has value
                string displayEmail = values[1] as string;
                return !isEditing && !string.IsNullOrWhiteSpace(displayEmail);
            }
            else if (controlType == "checkbox")
            {
                // Show checkbox when editing AND edit email has value
                string editEmail = values[2] as string;
                return isEditing && !string.IsNullOrWhiteSpace(editEmail);
            }
            else if (controlType == "labeltext")
            {
                // Show "Using Generic:" label when either display or edit email has value
                string displayEmail = values[1] as string;
                string editEmail = values[2] as string;

                if (isEditing)
                    return !string.IsNullOrWhiteSpace(editEmail);
                else
                    return !string.IsNullOrWhiteSpace(displayEmail);
            }
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean values to "Yes" or "No" strings
/// </summary>
public class BoolToYesNoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "Yes" : "No";
        }
        return "No";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string stringValue)
        {
            return string.Equals(stringValue, "Yes", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

/// <summary>
/// Multi-value converter that returns true when string is not empty AND not editing
/// </summary>
public class StringNotEmptyAndNotEditingConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values?.Length >= 2)
        {
            var stringValue = values[0] as string;
            var isEditing = values[1] is bool editing && editing;

            return !string.IsNullOrWhiteSpace(stringValue) && !isEditing;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Multi-value converter that returns true when string is not empty AND editing
/// </summary>
public class StringNotEmptyAndEditingConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values?.Length >= 2)
        {
            var stringValue = values[0] as string;
            var isEditing = values[1] is bool editing && editing;

            return !string.IsNullOrWhiteSpace(stringValue) && isEditing;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringToBoolConverter : BaseConverterOneWay<string, bool>
{
    public override bool DefaultConvertReturnValue { get; set; } = false;

    public override bool ConvertFrom(string value, CultureInfo? culture)
    {
        return !string.IsNullOrEmpty(value);
    }
}

public class CountToBoolConverter : BaseConverterOneWay<int, bool>
{
    public override bool DefaultConvertReturnValue { get; set; } = false;

    public override bool ConvertFrom(int value, CultureInfo? culture)
    {
        return value == 0; // Returns true when count is 0 (for "No files found" message)
    }
}

public class DatabaseFileToPathConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // This converts from DatabaseFileInfo to string path for selection
        if (value is DatabaseFileInfo dbFile)
        {
            return dbFile.FullPath;
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // This converts from selected path back to DatabaseFileInfo
        return value;
    }
}

public class OnlineStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isOnline)
        {
            return isOnline ? Colors.Green : Colors.Red;
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// Converter for email status to color
public class EmailStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is EmailStatus status)
        {
            return status switch
            {
                EmailStatus.Sent => Colors.Green,
                EmailStatus.Failed => Colors.Red,
                EmailStatus.Pending => Colors.Orange,
                EmailStatus.Sending => Colors.Blue,
                _ => Colors.Gray
            };
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// Converter for online status to text
public class OnlineStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isOnline)
        {
            return isOnline ? "Online" : "Offline";
        }
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// Converter for online status to icon
public class OnlineStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isOnline)
        {
            return isOnline ? "📶" : "📴";
        }
        return "❓";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
