using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using TheBleedingDeacons.Intergroup.Register.Models;


namespace TheBleedingDeacons.Intergroup.Register.Utilities;

public class RegisterData
{
    public List<Meeting> Meetings { get; set; } = new List<Meeting>();
    public List<Position> Positions { get; set; } = new List<Position>();

    public int TotalMeetings => Meetings.Count;
    public int TotalPositions => Positions.Count;

    public RegisterData() { }

    public RegisterData(List<Meeting> meetings, List<Position> positions)
    {
        Meetings = meetings ?? new List<Meeting>();
        Positions = positions ?? new List<Position>();
    }
}

public static class ExcelSerializer
{
    static ExcelSerializer()
    {
        ExcelPackage.License.SetNonCommercialOrganization("Intergroup");
    }

    // Combined method to deserialize both Groups and Positions from one Excel file
    public static RegisterData DeserializeFromExcel(Stream excelStream)
    {
        
        using var package = new ExcelPackage(excelStream);

        var meetings = DeserializeWorksheet<Meeting>(package, "Meetings");
        var positions = DeserializeWorksheet<Position>(package, "Positions");

        return new RegisterData(meetings, positions);
    }

    // Internal method to deserialize a specific worksheet from an already loaded package
    private static List<T> DeserializeWorksheet<T>(ExcelPackage package, string worksheetName) where T : new()
    {
        var items = new List<T>();
        var worksheet = package.Workbook.Worksheets[worksheetName];

        if (worksheet == null)
        {
            throw new InvalidOperationException($"{worksheetName} worksheet not found");
        }

        if (worksheet.Dimension == null)
        {
            return items; // Empty worksheet
        }

        int rowCount = worksheet.Dimension.Rows;

        // Skip header row, start from row 2
        for (int row = 2; row <= rowCount; row++)
        {
            var item = DeserializeRow<T>(worksheet, row);
            if (item != null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    // Row deserialization based on type
    private static T DeserializeRow<T>(ExcelWorksheet worksheet, int row) where T : new()
    {
        try
        {

            if (typeof(T) == typeof(Meeting))
            {
                return (T)(object)new Meeting
                {
                    ID = GetCellValue<int>(worksheet, row, 1),
                    Day = GetCellValue<string>(worksheet, row, 2),
                    Time = GetCellValue<string>(worksheet, row, 3),
                    EndTime = GetCellValue<string>(worksheet, row, 4),
                    Name = FixText(GetCellValue<string>(worksheet, row, 5)),
                    GsrName = GetCellValue<string>(worksheet, row, 6),
                    GsrEmailPersonal = GetCellValue<string>(worksheet, row, 7),
                    GsrPhone = GetCellValue<string>(worksheet, row, 8),
                    MeetingGenericEmail = GetCellValue<string>(worksheet, row, 9),
                    UsingGeneric = GetCellValue<bool?>(worksheet, row, 10),
                    Location = GetCellValue<string>(worksheet, row, 11),
                    Address = GetCellValue<string>(worksheet, row, 12),
                    Contact1Name = GetCellValue<string>(worksheet, row, 13),
                    Contact1Email = GetCellValue<string>(worksheet, row, 14),
                    Contact1Phone = FixPhone(GetCellValue<string>(worksheet, row, 15)),
                    Contact2Name = GetCellValue<string>(worksheet, row, 16),
                    Contact2Email = GetCellValue<string>(worksheet, row, 17),
                    Contact2Phone = FixPhone(GetCellValue<string>(worksheet, row, 18)),
                    Contact3Name = GetCellValue<string>(worksheet, row, 19),
                    Contact3Email = GetCellValue<string>(worksheet, row, 20),
                    Contact3Phone = FixPhone(GetCellValue<string>(worksheet, row, 21)),
                    Types = GetCellValue<string?>(worksheet, row, 22),
                    Updated = GetCellValue<DateTime?>(worksheet, row, 23),
                    Attended = GetCellValue<bool?>(worksheet, row, 24)
                };
            }
            else if (typeof(T) == typeof(Position))
            {
                return (T)(object)new Position
                {
                    ID = GetCellValue<int>(worksheet, row, 1),
                    PositionName = GetCellValue<string>(worksheet, row, 2),
                    PositionLongName = GetCellValue<string>(worksheet, row, 3),
                    PositionGenericEmail = GetCellValue<string>(worksheet, row, 4),
                    MemberAnonymousName = GetCellValue<string>(worksheet, row, 5),
                    MemberPersonalEmail = GetCellValue<string>(worksheet, row, 6),
                    MemberMobile = GetCellValue<string>(worksheet, row, 7),
                    PositionDuration = GetCellValue<string>(worksheet, row, 8),
                    StartedService = GetCellValue<DateTime?>(worksheet, row, 9),
                    Updated = GetCellValue<DateTime?>(worksheet, row, 10),
                    Attended = GetCellValue<bool?>(worksheet, row, 11)
                };
            }
        } catch (Exception ex)
        {

        }

        throw new NotSupportedException($"Type {typeof(T).Name} is not supported for deserialization");
    }



    // Combined serialization method - creates one Excel file with multiple tabs
    public static byte[] SerializeToExcel(List<Meeting> meetings, List<Position> positions)
    {
        using var package = new ExcelPackage();

        // Create Meetings tab
        var meetingsWorksheet = package.Workbook.Worksheets.Add("Meetings");
        SerializeMeetingsToWorksheet(meetingsWorksheet, meetings);
        meetingsWorksheet.Cells.AutoFitColumns();

        // Create Positions tab
        var positionsWorksheet = package.Workbook.Worksheets.Add("Positions");
        SerializePositionsToWorksheet(positionsWorksheet, positions);
        positionsWorksheet.Cells.AutoFitColumns();

        return package.GetAsByteArray();
    }

    // Generic serialization method for single tab
    public static byte[] SerializeToExcel<T>(List<T> items, string worksheetName)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add(worksheetName);

        if (typeof(T) == typeof(Meeting))
        {
            SerializeMeetingsToWorksheet(worksheet, items.Cast<Meeting>().ToList());
        }
        else if (typeof(T) == typeof(Position))
        {
            SerializePositionsToWorksheet(worksheet, items.Cast<Position>().ToList());
        }
        else
        {
            throw new NotSupportedException($"Type {typeof(T).Name} is not supported for serialization");
        }

        // Auto-fit columns
        worksheet.Cells.AutoFitColumns();
        return package.GetAsByteArray();
    }

    // Specific serialization methods for single tabs
    public static byte[] SerializeMeetingsToExcel(List<Meeting> meetings)
    {
        return SerializeToExcel(meetings, "Meetings");
    }

    public static byte[] SerializePositionsToExcel(List<Position> positions)
    {
        return SerializeToExcel(positions, "Positions");
    }

    private static void SerializeMeetingsToWorksheet(ExcelWorksheet worksheet, List<Meeting> meetings)
    {
        // Add headers
        worksheet.Cells[1, 1].Value = "ID";
        worksheet.Cells[1, 2].Value = "Day";
        worksheet.Cells[1, 3].Value = "Name";
        worksheet.Cells[1, 4].Value = "GSR Name";
        worksheet.Cells[1, 5].Value = "GSR Email Personal";
        worksheet.Cells[1, 6].Value = "GSR Phone";
        worksheet.Cells[1, 7].Value = "Meeting Generic Email";
        worksheet.Cells[1, 8].Value = "Using Generic";
        worksheet.Cells[1, 9].Value = "Location";
        worksheet.Cells[1, 10].Value = "Address"; 
        worksheet.Cells[1, 11].Value = "Contact 1 Name";
        worksheet.Cells[1, 12].Value = "Contact 1 Email";
        worksheet.Cells[1, 13].Value = "Contact 1 Phone";
        worksheet.Cells[1, 14].Value = "Contact 2 Name";
        worksheet.Cells[1, 15].Value = "Contact 2 Email";
        worksheet.Cells[1, 16].Value = "Contact 2 Phone";
        worksheet.Cells[1, 17].Value = "Contact 3 Name";
        worksheet.Cells[1, 18].Value = "Contact 3 Email";
        worksheet.Cells[1, 19].Value = "Contact 3 Phone";
        worksheet.Cells[1, 20].Value = "Types";
        worksheet.Cells[1, 21].Value = "Updated";
        worksheet.Cells[1, 22].Value = "Attended";

        // Add data rows
        for (int i = 0; i < meetings.Count; i++)
        {
            int row = i + 2; // Start from row 2 (after headers)
            var meeting = meetings[i];

            worksheet.Cells[row, 1].Value = meeting.ID;
            worksheet.Cells[row, 2].Value = meeting.Day;
            worksheet.Cells[row, 3].Value = meeting.Name;
            worksheet.Cells[row, 4].Value = meeting.GsrName;
            worksheet.Cells[row, 5].Value = meeting.GsrEmailPersonal;
            worksheet.Cells[row, 6].Value = meeting.GsrPhone;
            worksheet.Cells[row, 7].Value = meeting.MeetingGenericEmail;
            worksheet.Cells[row, 8].Value = meeting.UsingGeneric;
            worksheet.Cells[row, 9].Value = meeting.Location;
            worksheet.Cells[row, 10].Value = meeting.Address;
            worksheet.Cells[row, 11].Value = meeting.Contact1Name;
            worksheet.Cells[row, 12].Value = meeting.Contact1Email;
            worksheet.Cells[row, 13].Value = meeting.Contact1Phone;
            worksheet.Cells[row, 14].Value = meeting.Contact2Name;
            worksheet.Cells[row, 15].Value = meeting.Contact2Email;
            worksheet.Cells[row, 16].Value = meeting.Contact2Phone;
            worksheet.Cells[row, 17].Value = meeting.Contact3Name;
            worksheet.Cells[row, 18].Value = meeting.Contact3Email;
            worksheet.Cells[row, 19].Value = meeting.Contact3Phone;
            worksheet.Cells[row, 20].Value = meeting.Types;
            worksheet.Cells[row, 21].Value = meeting.Updated?.ToString("yyyy-MM-dd");
            worksheet.Cells[row, 22].Value = meeting.Attended;
        }
    }

    private static void SerializePositionsToWorksheet(ExcelWorksheet worksheet, List<Position> positions)
    {
        // Add headers
        worksheet.Cells[1, 1].Value = "ID";
        worksheet.Cells[1, 2].Value = "Position Name";
        worksheet.Cells[1, 3].Value = "Position Long Name";
        worksheet.Cells[1, 4].Value = "Position Generic Email";
        worksheet.Cells[1, 5].Value = "Member Anonymous Name";
        worksheet.Cells[1, 6].Value = "Member Personal Email";
        worksheet.Cells[1, 7].Value = "Member Mobile";
        worksheet.Cells[1, 8].Value = "Position Duration";
        worksheet.Cells[1, 9].Value = "Started Service";
        worksheet.Cells[1, 10].Value = "Attended";

        // Add data rows
        for (int i = 0; i < positions.Count; i++)
        {
            int row = i + 2; // Start from row 2 (after headers)
            var position = positions[i];

            worksheet.Cells[row, 1].Value = position.ID;
            worksheet.Cells[row, 2].Value = position.PositionName;
            worksheet.Cells[row, 3].Value = position.PositionLongName;
            worksheet.Cells[row, 4].Value = position.PositionGenericEmail;
            worksheet.Cells[row, 5].Value = position.MemberAnonymousName;
            worksheet.Cells[row, 6].Value = position.MemberPersonalEmail;
            worksheet.Cells[row, 7].Value = position.MemberMobile;
            worksheet.Cells[row, 8].Value = position.PositionDuration;
            worksheet.Cells[row, 9].Value = position.StartedService?.ToString("yyyy-MM-dd");
            worksheet.Cells[row, 10].Value = position.Attended;
        }
    }

    // Helper method to safely get cell values with type conversion
    private static T GetCellValue<T>(ExcelWorksheet worksheet, int row, int col)
    {
        
        var cellValue = worksheet.Cells[row, col].Value;

        if (cellValue == null)
        {
            return default;
        }

        try
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)cellValue.ToString();
            }
            else if (typeof(T) == typeof(int))
            {
                return (T)(object)Convert.ToInt32(cellValue);
            }
            else if (typeof(T) == typeof(bool?))
            {
                if (cellValue is bool boolValue)
                    return (T)(object)boolValue;
                if (bool.TryParse(cellValue.ToString(), out bool parsedBool))
                    return (T)(object)parsedBool;
                return default;
            }
            else if (typeof(T) == typeof(DateTime?))
            {
                if (cellValue is DateTime dateValue)
                    return (T)(object)dateValue;
                if (DateTime.TryParse(cellValue.ToString(), out DateTime parsedDate))
                    return (T)(object)parsedDate;
                return default;
            }
            else
            {
                return (T)Convert.ChangeType(cellValue, typeof(T));
            }
        }
        catch
        {
            return default;
        }
    }

    private static string FixPhone(string input) {
        if (string.IsNullOrEmpty(input))
            return input;
        if (!input.StartsWith('0'))
            return "0" + input;
        else return input;
    }

    private static string FixText(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        
        else return input.Replace("&amp;", "&");
    }
}