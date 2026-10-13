using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Moves one member to, from or between groups and positions. Opened from
/// the Members tab of the Registrations page with a <c>memberId</c>.
///
/// Writes go to the local <see cref="UnityDbContext"/> only, exactly as the
/// Edit Group and Edit Position pages do. The reconciliation service sees
/// the changed <see cref="Member.HomeGroupId"/>, <see cref="Member.IsGsr"/>,
/// <see cref="Member.IntergroupPositionId"/> and
/// <see cref="Member.IntergroupPositionRotation"/> in its snapshot diff and
/// sends them to Unity when the meeting is finished.
///
/// Registration state is left alone. A registered position whose holder is
/// moved away stays registered, and the Positions tab still lets it be
/// turned off — the same as when a holder is removed on Edit Position.
/// </summary>
public partial class MoveMemberViewModel : BaseViewModel
{
	private const string RotationFormat = "yyyy-MM-dd";

	private static readonly ILogger Logger = AppLogger.ForContext<MoveMemberViewModel>();

	private readonly IMemberRepository _memberRepository;
	private readonly IGroupRepository _groupRepository;
	private readonly IPositionRepository _positionRepository;
	private readonly IDbContextFactory<UnityDbContext> _contextFactory;
	private readonly IPopupNotification _popupService;

	private Member? _member;

	/// <summary>Every other member, for the "already held by" hints.</summary>
	private List<Member> _otherMembers = [];

	private DateTime _originalRotationDate = DateTime.Today;

	public ObservableCollection<AssignmentOption> GroupOptions { get; } = new();

	public ObservableCollection<AssignmentOption> PositionOptions { get; } = new();

	[ObservableProperty]
	private bool isLoading;

	[ObservableProperty]
	private string memberName = string.Empty;

	[ObservableProperty]
	private string currentGroupText = string.Empty;

	[ObservableProperty]
	private string currentPositionText = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasGroup))]
	[NotifyPropertyChangedFor(nameof(GroupNote))]
	[NotifyPropertyChangedFor(nameof(HasGroupNote))]
	private AssignmentOption? selectedGroup;

	[ObservableProperty]
	private bool isGsr;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasPosition))]
	[NotifyPropertyChangedFor(nameof(PositionNote))]
	[NotifyPropertyChangedFor(nameof(HasPositionNote))]
	private AssignmentOption? selectedPosition;

	[ObservableProperty]
	private DateTime rotationDate = DateTime.Today;

	public MoveMemberViewModel(
		IMemberRepository memberRepository,
		IGroupRepository groupRepository,
		IPositionRepository positionRepository,
		IDbContextFactory<UnityDbContext> contextFactory,
		IPopupNotification popupService)
	{
		_memberRepository = memberRepository;
		_groupRepository = groupRepository;
		_positionRepository = positionRepository;
		_contextFactory = contextFactory;
		_popupService = popupService;
		Title = "Move Member";
	}

	public bool HasGroup => SelectedGroup?.Id is not null;

	public bool HasPosition => SelectedPosition?.Id is not null;

	/// <summary>
	/// Who else is a GSR of the chosen group. Groups may have several GSRs,
	/// so this informs rather than blocks.
	/// </summary>
	public string GroupNote
	{
		get
		{
			if (SelectedGroup?.Id is not int groupId) return string.Empty;
			var gsrs = _otherMembers
				.Where(m => m.HomeGroupId == groupId && m.IsGsr)
				.Select(m => m.AnonymousName)
				.ToList();
			return gsrs.Count == 0 ? "This group has no other GSR." : $"Other GSRs: {string.Join(", ", gsrs)}";
		}
	}

	public bool HasGroupNote => !string.IsNullOrEmpty(GroupNote);

	/// <summary>
	/// Who else holds the chosen position. Positions may have several
	/// holders, so this informs rather than blocks.
	/// </summary>
	public string PositionNote
	{
		get
		{
			if (SelectedPosition?.Id is not int positionId) return string.Empty;
			var holders = _otherMembers
				.Where(m => m.IntergroupPositionId == positionId)
				.Select(m => m.AnonymousName)
				.ToList();
			return holders.Count == 0 ? "Nobody else holds this position." : $"Also held by: {string.Join(", ", holders)}";
		}
	}

	public bool HasPositionNote => !string.IsNullOrEmpty(PositionNote);

	public override void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue("memberId", out var idObj) &&
			idObj is string idStr &&
			int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var memberId))
		{
			LoadAsync(memberId).SafeFireAndForget("LoadMemberToMove");
		}
	}

	private async Task LoadAsync(int memberId)
	{
		IsLoading = true;
		try
		{
			var member = await _memberRepository.GetByIdAsync(memberId, Token);
			if (member is null)
			{
				Logger.Warning("Move Member opened for unknown member {MemberId}", memberId);
				await _popupService.ShowErrorAsync("Member Not Found", "That member is no longer in the local database.");
				await Shell.Current.GoToAsync("..");
				return;
			}

			var groups = await _groupRepository.GetAllAsync(Token);
			var positions = await _positionRepository.GetAllAsync(Token);
			var allMembers = await _memberRepository.GetAllAsync(Token);

			_member = member;
			_otherMembers = allMembers.Where(m => m.Id != member.Id).ToList();

			MemberName = member.AnonymousName;
			Title = $"Move {member.AnonymousName}";
			CurrentGroupText = new OverviewMember(member).DisplayGroup;
			CurrentPositionText = new OverviewMember(member).DisplayPosition;

			GroupOptions.Clear();
			GroupOptions.Add(new AssignmentOption(null, "No group"));
			foreach (var g in groups.OrderBy(g => g.Name, StringComparer.CurrentCulture))
				GroupOptions.Add(new AssignmentOption(g.Id, g.Name));

			PositionOptions.Clear();
			PositionOptions.Add(new AssignmentOption(null, "No position"));
			foreach (var p in positions.OrderBy(p => p.ShortDescription, StringComparer.CurrentCulture))
				PositionOptions.Add(new AssignmentOption(p.Id, p.ShortDescription));

			SelectedGroup = GroupOptions.FirstOrDefault(o => o.Id == member.HomeGroupId) ?? GroupOptions[0];
			SelectedPosition = PositionOptions.FirstOrDefault(o => o.Id == member.IntergroupPositionId) ?? PositionOptions[0];
			IsGsr = member.IsGsr;

			_originalRotationDate = DateTime.TryParseExact(
				member.IntergroupPositionRotation,
				RotationFormat,
				CultureInfo.InvariantCulture,
				DateTimeStyles.None,
				out var rotation)
				? rotation
				: DateTime.Today;
			RotationDate = _originalRotationDate;
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to load member {MemberId} to move", memberId);
			await _popupService.ShowErrorAsync("Load Failed", "Could not load this member. Go back and try again.");
		}
		finally
		{
			IsLoading = false;
		}
	}

	[RelayCommand]
	private async Task Save()
	{
		if (_member is null || IsLoading) return;

		int? newGroupId = SelectedGroup?.Id;
		bool newIsGsr = newGroupId is not null && IsGsr;
		int? newPositionId = SelectedPosition?.Id;
		string? newRotation = newPositionId is null
			? null
			: RotationDate.ToString(RotationFormat, CultureInfo.InvariantCulture);

		// A member who keeps their position but never had a rotation date
		// recorded would otherwise pick one up just by being saved. Only
		// write the date if it was actually changed, or the position was.
		if (newPositionId == _member.IntergroupPositionId
			&& _member.IntergroupPositionRotation is null
			&& RotationDate == _originalRotationDate)
		{
			newRotation = null;
		}

		var changes = new List<string>();
		if (newGroupId != _member.HomeGroupId)
			changes.Add($"Group: {CurrentGroupText} → {SelectedGroup?.Name}");
		if (newIsGsr != _member.IsGsr && newGroupId is not null)
			changes.Add(newIsGsr ? "Becomes GSR" : "No longer GSR");
		if (newPositionId != _member.IntergroupPositionId)
			changes.Add($"Position: {_member.IntergroupPosition?.ShortDescription ?? "No position"} → {SelectedPosition?.Name}");
		else if (newPositionId is not null && newRotation != _member.IntergroupPositionRotation)
			changes.Add($"Rotation: {_member.IntergroupPositionRotation ?? "none"} → {newRotation}");

		if (changes.Count == 0)
		{
			await Shell.Current.GoToAsync("..");
			return;
		}

		bool confirmed = await Shell.Current.DisplayAlertAsync(
			$"Move {_member.AnonymousName}?",
			string.Join(Environment.NewLine, changes),
			"Move", "Cancel");
		if (!confirmed) return;

		try
		{
			IsLoading = true;

			using var context = _contextFactory.CreateDbContext();
			var tracked = await context.Members.FindAsync([_member.Id], Token);
			if (tracked is null)
			{
				await _popupService.ShowErrorAsync("Member Not Found", "That member is no longer in the local database.");
				return;
			}

			tracked.HomeGroupId = newGroupId;
			tracked.IsGsr = newIsGsr;
			tracked.IntergroupPositionId = newPositionId;
			tracked.IntergroupPositionRotation = newRotation;
			await context.SaveChangesAsync(Token);

			Logger.Information(
				"Moved member {MemberName} (ID={MemberId}): group {OldGroup}→{NewGroup}, GSR {OldGsr}→{NewGsr}, position {OldPosition}→{NewPosition}, rotation {OldRotation}→{NewRotation}",
				_member.AnonymousName,
				_member.Id,
				_member.HomeGroupId,
				newGroupId,
				_member.IsGsr,
				newIsGsr,
				_member.IntergroupPositionId,
				newPositionId,
				_member.IntergroupPositionRotation,
				newRotation);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to move member {MemberId}", _member.Id);
			await _popupService.ShowErrorAsync("Move Failed", ex.Message);
			return;
		}
		finally
		{
			IsLoading = false;
		}

		await Shell.Current.GoToAsync("..");
	}

	[RelayCommand]
	private Task Cancel() => Shell.Current.GoToAsync("..");
}

/// <summary>
/// One choice in the group or position picker. A null <see cref="Id"/> is
/// the "No group" / "No position" choice.
/// </summary>
public sealed record AssignmentOption(int? Id, string Name);
