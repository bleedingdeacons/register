using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Display-time wrapper for a Member shown in the Edit Group / Edit Position
/// member lists. Adds a per-card <see cref="IsPending"/> flag so a single
/// horizontal CollectionView can render both active members and members staged
/// for removal — with the strikethrough / Undo card variant driven by an
/// <see cref="IsPending"/> trigger in XAML rather than two separate lists.
///
/// Mutability: <see cref="IsPending"/> is set when constructing the item and
/// not modified after. Removing or undoing a removal swaps the item out of
/// the displayed collection rather than flipping the flag in place — keeps
/// the change-notification story simple (no INotifyPropertyChanged needed
/// because the CollectionView reacts to add/remove, not item-level updates).
/// </summary>
public sealed class MemberCardItem
{
	public MemberCardItem(Member member, bool isPending)
	{
		Member = member;
		IsPending = isPending;
	}

	public Member Member { get; }

	public bool IsPending { get; }

	/// <summary>
	/// Convenience factory for an active card.
	/// </summary>
	public static MemberCardItem Active(Member member) => new(member, isPending: false);

	/// <summary>
	/// Convenience factory for a card staged for removal.
	/// </summary>
	public static MemberCardItem Pending(Member member) => new(member, isPending: true);
}
