using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TheBleedingDeacons.Intergroup.Register.Models;

[Table("Groups")]
public class Group
{
    [Key]
    public int ID { get; set; }

    [MaxLength(7)]
    public string? Time { get; set; }

    [MaxLength(7)]
    public string? EndTime { get; set; }

    [MaxLength(50)]
    public string? Day { get; set; }

    [MaxLength(255)]
    public string? Name { get; set; }

    [MaxLength(255)]
    [Column("Gsr Name")]
    public string? GsrName { get; set; }

    [MaxLength(255)]
    [Column("Gsr Email Personal")]
    public string? GsrEmailPersonal { get; set; }

    [MaxLength(60)]
    [Column("Gsr Phone")]
    public string? GsrPhone { get; set; }

    [MaxLength(255)]
    [Column("Group Generic Email")]
    public string? GroupGenericEmail { get; set; }

    [Column("Using Generic")]
    public bool? UsingGeneric { get; set; }

    [MaxLength(100)]
    [Column("Location")]
    public string? Location { get; set; }

    [MaxLength(255)]
    [Column("Address")]
    public string? Address { get; set; }


    [MaxLength(255)]
    [Column("Contact 1 Name")]
    public string? Contact1Name { get; set; }

    [MaxLength(255)]
    [Column("Contact 1 Email")]
    public string? Contact1Email { get; set; }

    [MaxLength(20)]
    [Column("Contact 1 Phone")]
    public string? Contact1Phone { get; set; }

    [MaxLength(255)]
    [Column("Contact 2 Name")]
    public string? Contact2Name { get; set; }

    [MaxLength(255)]
    [Column("Contact 2 Email")]
    public string? Contact2Email { get; set; }

    [MaxLength(20)]
    [Column("Contact 2 Phone")]
    public string? Contact2Phone { get; set; }

    [MaxLength(255)]
    [Column("Contact 3 Name")]
    public string? Contact3Name { get; set; }

    [MaxLength(255)]
    [Column("Contact 3 Email")]
    public string? Contact3Email { get; set; }

    [MaxLength(20)]
    [Column("Contact 3 Phone")]
    public string? Contact3Phone { get; set; }

    [MaxLength(255)]
    [Column("Types")]
    public string? Types { get; set; }


    [Column("Updated")]
    public DateTime? Updated { get; set; }

    [Column("Attended")]
    public bool? Attended { get; set; }
}