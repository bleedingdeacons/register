using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace TheBleedingDeacons.Intergroup.Register.Models;

/// <summary>
/// Represents a dated intergroup meeting instance downloaded from the Unity API.
/// Each record is a specific meeting occurrence identified by its date.
/// </summary>
[DebuggerDisplay("ID={ID}, Date={Date}")]
public class IntergroupMeeting : INotifyPropertyChanged
{
    public int ID { get; set; }

    /// <summary>Title of the intergroup meeting as returned by the Unity API.</summary>
    public string? Title { get; set; }

    /// <summary>ISO date string (yyyy-MM-dd) as returned by the Unity API.</summary>
    public string? Date { get; set; }

    /// <summary>
    /// Comma-separated member IDs of group attendees (GSRs).
    /// Stored flat to avoid a join table for this read-only import data.
    /// </summary>
    public string? GroupAttendeeIds { get; set; }

    /// <summary>Comma-separated display names of group attendees (GSRs).</summary>
    public string? GroupAttendeeNames { get; set; }

    /// <summary>Comma-separated member IDs of officer attendees.</summary>
    public string? OfficerAttendeeIds { get; set; }

    /// <summary>Comma-separated display names of officer attendees.</summary>
    public string? OfficerAttendeeNames { get; set; }

    /// <summary>
    /// Not persisted. Set by the ViewModel to drive active-row highlighting in the UI.
    /// </summary>
    [NotMapped]
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
    private bool _isActive;

    public event PropertyChangedEventHandler? PropertyChanged;


}