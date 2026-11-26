using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ECommons.DalamudServices;
using HuntBuddy.Models;
using HuntBuddy.Services;
using RebornMaterial;

namespace HuntBuddy.UI;

public partial class MainWindow: M3ShellWindow {
	public const string BillsPageId = "bills";
	public const string LocalBillsPageId = "local";
	public const string RoutePageId = "route";
	public const string MarksAllPageId = "marks_all";
	public const string MarksNeededPageId = "marks_needed";
	public const string MarksZonePageId = "marks_zone";
	public const string MarksArrPageId = "marks_arr";
	public const string MarksHwPageId = "marks_hw";
	public const string MarksSbPageId = "marks_sb";
	public const string MilestonesPageId = "marks_milestones";
	public const string SettingsPageId = "settings";
	public const string AppearancePageId = "appearance";
	public const string AboutPageId = "about";

	private const string SectionBills = "Hunt Bills";
	private const string SectionMarks = "Mark Achievements";
	private const string SectionSettings = "Settings";

	private const string DiscordUrl = "https://discord.gg/p54TZMPnC9";

	private static readonly M3WindowAction[] barActions = [
		new("##hbr_discord", FontAwesomeIcon.Comments, "Join the Discord"),
	];

	private static readonly Vector2 defaultSize = new(900f, 640f);

	private static readonly WindowSizeConstraints defaultConstraints = new() {
		MinimumSize = new Vector2(560f, 420f),
		MaximumSize = new Vector2(4000f, 4000f),
	};

	private readonly HuntBillService huntBillService;
	private readonly MarkAchievementService markService;
	private readonly Dictionary<uint, bool> expandedMarkStates = [];
	private readonly Dictionary<uint, bool> expandedBillZoneStates = [];
	private readonly List<MobHuntEntry> scratchBillEntries = [];
	private readonly List<EliteMarkInfo> scratchMarks = [];
	private readonly List<HuntMilestoneInfo> scratchMilestones = [];
	private readonly List<HuntBillService.HuntRouteStep> cachedRoute = [];
	private readonly StringBuilder scratchText = new();
	private MarkRank? rankFilter;
	private string milestoneExpansionFilter = string.Empty;
	private uint cachedTerritoryId;
	private string cachedTerritoryName = string.Empty;
	private int cachedRouteRevision = -1;
	private DateTime lastRouteRefreshUtc;
	private int cachedRewardsRevision = -1;
	private DateTime lastRewardsRefreshUtc;
	private HuntBillService.HuntRewardsSummary cachedRewards;
	private bool savePending;

	public MainWindow(HuntBillService huntBillService, MarkAchievementService markService) : base(
		"Hunt Buddy Reborn###HuntBuddyRebornMain",
		"hbr",
		"Hunt Buddy Reborn",
		"HBR",
		defaultSize,
		defaultConstraints) {
		this.huntBillService = huntBillService;
		this.markService = markService;
		this.RespectCloseHotkey = !Plugin.Instance.Configuration.IgnoreCloseHotkey;
		this.AllowPinning = false;
		this.AllowClickthrough = false;

		this.AddPage(new M3Page(
			BillsPageId,
			"Hunt Bills",
			FontAwesomeIcon.Scroll,
			SectionBills,
			"Active daily and weekly hunt bills by expansion and zone",
			this.DrawAllBillsPage,
			badge: this.GetActiveBillsBadge));

		this.AddPage(new M3Page(
			LocalBillsPageId,
			"Current Zone",
			FontAwesomeIcon.Crosshairs,
			SectionBills,
			"Active hunt bill targets in your current zone",
			this.DrawLocalBillsPage,
			badge: this.GetLocalBillsBadge));

		this.AddPage(new M3Page(
			RoutePageId,
			"Hunt Route",
			FontAwesomeIcon.Route,
			SectionBills,
			"Optimal multi-zone route with nearest aetherytes and rewards",
			this.DrawRoutePage,
			badge: this.GetActiveBillsBadge));

		this.AddPage(new M3Page(
			MarksAllPageId,
			"All Marks",
			FontAwesomeIcon.Trophy,
			SectionMarks,
			"All 24 Mark of the X achievements across ARR, Heavensward, and Stormblood",
			() => this.DrawAchievementsPage(null, false, false),
			badge: () => this.GetCompletionBadge(null)));

		this.AddPage(new M3Page(
			MarksNeededPageId,
			"Still Needed",
			FontAwesomeIcon.Bullseye,
			SectionMarks,
			"Incomplete achievements and remaining marks",
			() => this.DrawAchievementsPage(null, false, true),
			badge: this.GetNeededMarksBadge));

		this.AddPage(new M3Page(
			MarksZonePageId,
			"Zone Marks",
			FontAwesomeIcon.MapMarkerAlt,
			SectionMarks,
			"Mark of the X targets in your current zone",
			() => this.DrawAchievementsPage(null, true, false),
			badge: this.GetCurrentZoneMarksBadge));

		this.AddPage(new M3Page(
			MarksArrPageId,
			"A Realm Reborn",
			FontAwesomeIcon.Globe,
			SectionMarks,
			"Holt, Desert, Sea, and Lake elite marks",
			() => this.DrawAchievementsPage(0, false, false),
			badge: () => this.GetCompletionBadge(0)));

		this.AddPage(new M3Page(
			MarksHwPageId,
			"Heavensward",
			FontAwesomeIcon.FeatherAlt,
			SectionMarks,
			"Dragon and Cloud & Ice elite marks",
			() => this.DrawAchievementsPage(1, false, false),
			badge: () => this.GetCompletionBadge(1)));

		this.AddPage(new M3Page(
			MarksSbPageId,
			"Stormblood",
			FontAwesomeIcon.Water,
			SectionMarks,
			"Wastes and East elite marks",
			() => this.DrawAchievementsPage(2, false, false),
			badge: () => this.GetCompletionBadge(2)));

		this.AddPage(new M3Page(
			MilestonesPageId,
			"Kill Milestones",
			FontAwesomeIcon.Medal,
			SectionMarks,
			"All expansion A-rank, S-rank, and B-rank hunt kill milestones (ARR through Dawntrail)",
			this.DrawMilestonesPage,
			badge: this.GetMilestonesBadge));

		this.AddPage(new M3Page(
			SettingsPageId,
			"General",
			FontAwesomeIcon.SlidersH,
			SectionSettings,
			"Map flags, local overlay, icons, and integrations",
			this.DrawSettingsPage));

		this.AddPage(new M3Page(
			AppearancePageId,
			"Appearance",
			FontAwesomeIcon.Palette,
			SectionSettings,
			"Accent color, sizes, spacing, animations, and layout",
			this.DrawAppearancePage));

		this.AddPage(new M3Page(
			AboutPageId,
			"About",
			FontAwesomeIcon.InfoCircle,
			SectionSettings,
			"Commands, info, and how Hunt Buddy Reborn works",
			this.DrawAboutPage));
	}

	protected override M3WindowAction[] WindowActions => barActions;
	protected override ImGuiWindowFlags ExtraFlags => Plugin.Instance.Configuration.LockWindowPositions
		? ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
		: ImGuiWindowFlags.None;
	protected override bool SupportsSearch => true;
	protected override string SearchHint => "Search bills, marks, or zones";

	protected override float TopAppBarTrailingWidth {
		get {
			if (this.IsBillsPage()) {
				return IconButtonsWidth(2);
			}

			return this.IsMarksPage() ? IconButtonsWidth(3) : 0f;
		}
	}

	private static float IconButtonsWidth(int count) =>
		(M3Widgets.IconButtonSize * count) + (ImGui.GetStyle().ItemSpacing.X * (count - 1));

	private static float ActionsColumnWidth(int buttonCount) =>
		MathF.Max(ImGui.CalcTextSize("Actions").X, IconButtonsWidth(buttonCount));

	protected override void OnWindowAction(int index) => OpenDiscord();

	protected override void PreDrawFold() {
		if (this.IsPinned || this.IsClickthrough) {
			this.IsPinned = false;
			this.IsClickthrough = false;
		}
	}

	private static void OpenDiscord() => Util.OpenLink(DiscordUrl);

	public void ToggleWindow() {
		if (this.IsOpen && this.IsMinimized) {
			this.Restore();
			this.BringToFront();
			return;
		}

		this.IsOpen = !this.IsOpen;
	}

	public void Open(string? pageId = null) {
		if (pageId != null) {
			this.ShowPage(pageId);
		}

		this.OpenAndRestore();
	}

	protected override void DrawLast() {
		if (this.savePending && !ImGui.IsAnyItemActive()) {
			this.FlushSave();
		}
	}

	public override void OnClose() {
		this.FlushSave();
		base.OnClose();
	}

	private void FlushSave() {
		if (!this.savePending) {
			return;
		}

		Plugin.Instance.Configuration.Save();
		this.savePending = false;
	}

	protected override void DrawTopAppBarTrailing(in M3TopAppBar.Scope bar) {
		ImGui.SetCursorScreenPos(bar.TrailingPosition(M3Widgets.IconButtonSize));

		if (this.IsBillsPage()) {
			if (M3Widgets.IconButton(
				"##hbr_bills_reload",
				FontAwesomeIcon.Redo,
				"Reload daily and weekly hunt bill data",
				M3ButtonStyle.Text,
				enabled: this.huntBillService.MobHuntEntriesReady)) {
				this.huntBillService.RequestReload();
			}

			ImGui.SameLine();

			if (M3Widgets.IconButton(
				"##hbr_bills_next",
				FontAwesomeIcon.Crosshairs,
				"Flag the next hunt target (/hbr next)",
				M3ButtonStyle.Text)) {
				this.huntBillService.FlagNextTarget();
			}

			return;
		}

		if (this.IsMarksPage()) {
			if (M3Widgets.IconButton(
				"##hbr_marks_refresh",
				FontAwesomeIcon.SyncAlt,
				"Refresh progress for incomplete Mark achievements",
				M3ButtonStyle.Text,
				enabled: !this.markService.IsRefreshing)) {
				this.markService.RefreshAll(false);
			}

			ImGui.SameLine();

			if (M3Widgets.IconButton(
				"##hbr_marks_log_all",
				FontAwesomeIcon.FileAlt,
				"Sync and log all 24 Mark achievements",
				M3ButtonStyle.Text,
				enabled: !this.markService.IsRefreshing)) {
				this.markService.RefreshAll(true);
			}

			ImGui.SameLine();

			bool hideCompleted = Plugin.Instance.Configuration.HideCompletedMarkAchievements;
			if (M3Widgets.IconToggle(
				"##hbr_marks_hide_completed",
				FontAwesomeIcon.Eye,
				ref hideCompleted,
				hideCompleted ? "Hiding completed achievements (click to show all)" : "Showing all achievements (click to hide completed)",
				M3ButtonStyle.Text,
				FontAwesomeIcon.EyeSlash)) {
				Plugin.Instance.Configuration.HideCompletedMarkAchievements = hideCompleted;
				Plugin.Instance.Configuration.Save();
			}
		}
	}

	protected override float NavigationFooterHeight(bool expanded) =>
		expanded && Svc.PlayerState.IsLoaded && Svc.PlayerState.ContentId != 0
			? (ImGui.GetTextLineHeight() * 2f) + (M3.Space1 * 2f)
			: 0f;

	protected override void DrawNavigationFooter(float width, bool expanded) {
		if (!expanded || !Svc.PlayerState.IsLoaded || Svc.PlayerState.ContentId == 0) {
			return;
		}

		IReadOnlyList<MarkAchievementInfo> all = this.markService.Achievements;
		int completed = 0;
		int totalMarks = 0;
		int checkedMarks = 0;
		for (int i = 0; i < all.Count; i++) {
			MarkAchievementInfo ach = all[i];
			if (ach.IsComplete) {
				completed++;
			}

			totalMarks += ach.Marks.Count;
			for (int j = 0; j < ach.Marks.Count; j++) {
				if (this.markService.IsMarkChecked(ach.Marks[j])) {
					checkedMarks++;
				}
			}
		}

		M3Text.Draw(Svc.PlayerState.CharacterName, M3TextStyle.Label);
		M3Text.Draw($"{completed}/{all.Count} ach - {checkedMarks}/{totalMarks} marks", M3TextStyle.Faint);
	}

	protected override void DrawPageHeader() {
		if (!this.IsMarksPage() && !this.Searching) {
			return;
		}

		if (!Svc.PlayerState.IsLoaded || Svc.PlayerState.ContentId == 0) {
			return;
		}

		this.DrawMarksHeader();
	}

	protected override void DrawSearchResults(string text) {
		string query = text.Trim();
		bool anyBills = this.DrawBillsSearchResults(query);

		if (Svc.PlayerState.IsLoaded && Svc.PlayerState.ContentId != 0) {
			bool anyMarks = this.DrawAchievementsList(null, false, false, query, showEmptyState: false, sectionHeader: anyBills ? "Mark Achievements" : null);
			bool anyMilestones = this.DrawMilestonesList(query, showEmptyState: false, sectionHeader: (anyBills || anyMarks) ? "Kill Milestones" : null);
			if (!anyBills && !anyMarks && !anyMilestones) {
				M3Widgets.EmptyState(
					"##hbr_search_empty",
					"No matches found",
					"No hunt bills, Mark achievements, or kill milestones match your search.",
					FontAwesomeIcon.Search);
			}
		}
		else if (!anyBills) {
			M3Widgets.EmptyState(
				"##hbr_search_empty_bills",
				"No matching hunt bills",
				"Try another mark or zone name, or log in to search Mark achievements.",
				FontAwesomeIcon.Search);
		}
	}

	private bool IsBillsPage() =>
		this.CurrentPageId is BillsPageId or LocalBillsPageId or RoutePageId;

	private bool IsMarksPage() =>
		this.CurrentPageId is MarksAllPageId or MarksNeededPageId or MarksZonePageId or MarksArrPageId or MarksHwPageId or MarksSbPageId or MilestonesPageId;

	private string? GetActiveBillsBadge() {
		if (!this.huntBillService.MobHuntEntriesReady) {
			return null;
		}

		int remaining = 0;
		foreach (List<HuntZone> zones in this.huntBillService.MobHuntEntries.Values) {
			foreach (HuntZone zone in zones) {
				remaining += zone.Entries.Count - CountCompletedEntries(zone.Entries);
			}
		}

		return remaining > 0 ? M3Widgets.BadgeCount(remaining) : null;
	}

	private string? GetLocalBillsBadge() {
		if (!this.huntBillService.MobHuntEntriesReady) {
			return null;
		}

		List<MobHuntEntry> entries = this.huntBillService.CurrentAreaMobHuntEntries;
		int remaining = entries.Count - CountCompletedEntries(entries);
		return remaining > 0 ? M3Widgets.BadgeCount(remaining) : null;
	}
}
