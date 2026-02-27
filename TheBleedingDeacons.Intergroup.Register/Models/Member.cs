using System.Diagnostics;

namespace TheBleedingDeacons.Intergroup.Register.Models;

[DebuggerDisplay("ID={ID}, Name={Name}, MarkedForDeletion={IsMarkedForDeletion}")]
public class Member
{
    public int ID { get; set; }
    public string? Name { get; set; }
    public string? EmailPersonal { get; set; }
    public string? Phone { get; set; }

    /// <summary>
    /// When true the member has been replaced and should be removed on the next Unity sync.
    /// </summary>
    public bool IsMarkedForDeletion { get; set; }

    /// <summary>
    /// UTC timestamp of when the member was marked for deletion.
    /// </summary>
    public DateTime? MarkedForDeletionDate { get; set; }

    // FK back to Group (many-to-one)
    public int GroupId { get; set; }
    public Group? Group { get; set; }
}
