using System;
using System.Diagnostics;

namespace TheBleedingDeacons.Intergroup.Register.Models;

[DebuggerDisplay("ID={ID}, PositionName={PositionName}")]
public class Position
{
    public int ID { get; set; }
    public string? PositionName { get; set; }
    public string? PositionLongName { get; set; }
    public string? PositionGenericEmail { get; set; }
    public string? MemberAnonymousName { get; set; }
    public string? MemberPersonalEmail { get; set; }
    public string? MemberMobile { get; set; }
    public string? PositionDuration { get; set; }
    public DateTime? StartedService { get; set; }
    public DateTime? Updated { get; set; }
    public bool? Attended { get; set; }
}