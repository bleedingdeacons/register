using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace TheBleedingDeacons.Intergroup.Register.Models;

[DebuggerDisplay("ID={ID}, PositionName={PositionName}")]
[Table("Positions")]
public class Position
{
    [Key]
    public int ID { get; set; }

    [MaxLength(100)]
    [Column("Position Name")]
    public string? PositionName { get; set; }

    [MaxLength(255)]
    [Column("Position Long Name")]
    public string? PositionLongName { get; set; }

    [MaxLength(255)]
    [Column("Position Generic Email")]
    public string? PositionGenericEmail { get; set; }

    [MaxLength(255)]
    [Column("Member Anonymous Name")]
    public string? MemberAnonymousName { get; set; }

    [MaxLength(255)]
    [Column("Member Personal Email")]
    public string? MemberPersonalEmail { get; set; }

    [MaxLength(20)]
    [Column("Member Mobile")]
    public string? MemberMobile { get; set; }

    [MaxLength(50)]
    [Column("Position Duration")]
    public string? PositionDuration { get; set; }

    [Column("Started Service")]
    public DateTime? StartedService { get; set; }

    [Column("Updated")]
    public DateTime? Updated { get; set; }

    [Column("Attended")]
    public bool? Attended { get; set; }
}