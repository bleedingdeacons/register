using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TheBleedingDeacons.Intergroup.Register.Models;

[DebuggerDisplay("ID={ID}, Name={Name}")]
public class Group
{
    public int ID { get; set; }
    public string? Name { get; set; }
    public List<Meeting> Meetings { get; set; } = new();
}
