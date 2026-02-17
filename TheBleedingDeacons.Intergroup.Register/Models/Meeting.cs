using System;
using System.Diagnostics;

namespace TheBleedingDeacons.Intergroup.Register.Models;

[DebuggerDisplay("ID={ID}, Name={Name}")]
public class Meeting
{
    public int ID { get; set; }
    public string? Time { get; set; }
    public string? EndTime { get; set; }
    public string? Day { get; set; }
    public string? Name { get; set; }
    public string? GsrName { get; set; }
    public string? GsrEmailPersonal { get; set; }
    public string? GsrPhone { get; set; }
    public string? MeetingGenericEmail { get; set; }
    public bool? UsingGeneric { get; set; }
    public string? Location { get; set; }
    public string? Address { get; set; }
    public string? Contact1Name { get; set; }
    public string? Contact1Email { get; set; }
    public string? Contact1Phone { get; set; }
    public string? Contact2Name { get; set; }
    public string? Contact2Email { get; set; }
    public string? Contact2Phone { get; set; }
    public string? Contact3Name { get; set; }
    public string? Contact3Email { get; set; }
    public string? Contact3Phone { get; set; }
    public string? Types { get; set; }
    public string? ProxyEmail { get; set; }
    public string? ProxyName { get; set; }
    public bool? ProxyAttendance { get; set; }
    public DateTime? Updated { get; set; }
    public bool? Attended { get; set; }

    // Foreign key to parent Group
    public int? GroupId { get; set; }
    public Group? Group { get; set; }
}
