using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using HuntBuddy.Data;
using HuntBuddy.Models;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using AchievementSheet = Lumina.Excel.Sheets.Achievement;
using ClientAchievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;

namespace HuntBuddy.Services;

public sealed unsafe class MarkAchievementService: IDisposable {
	private const int SlainMarkWindowSeconds = 45;

	private readonly record struct SlainMarkRecord(EliteMarkInfo Mark, float Distance, DateTime TimestampUtc);

	private delegate void ReceiveAchievementProgressDelegate(ClientAchievement* thisPtr, uint id, uint current, uint max);

	private readonly HuntBillService huntBillService;
	private readonly List<MarkAchievementInfo> achievements = [];
	private readonly Dictionary<uint, MarkAchievementInfo> achievementsById = [];
	private readonly List<HuntMilestoneInfo> milestones = [];
	private readonly Dictionary<uint, HuntMilestoneInfo> milestonesById = [];
	private readonly HashSet<uint> markTerritories = [];
	private readonly Dictionary<uint, EliteMarkInfo> marksByBNpcNameId = [];
	private readonly Dictionary<string, EliteMarkInfo> marksByName = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<(uint AchievementId, DateTime EarliestRequestUtc)> progressRequestQueue = [];
	private readonly Dictionary<uint, bool> activeMarks = [];
	private readonly HashSet<uint> seenEntityIds = [];
	private readonly List<uint> staleEntityIds = [];
	private readonly HashSet<uint> alertedEntityIds = [];
	private readonly List<SlainMarkRecord> recentSlainMarks = [];
	private readonly Hook<ReceiveAchievementProgressDelegate>? receiveAchievementProgressHook;

	private ulong activeContentId;
	private CharacterMarkProgress? activeProgress;
	private bool initialSyncRequested;
	private bool requestedAchievementBitmap;
	private uint? activeRequestId;
	private DateTime activeRequestSentUtc;
	private DateTime lastRequestCompletedUtc;
	private DateTime lastObjectScanUtc;
	private DateTime lastBillCheckUtc;

	public IReadOnlyList<MarkAchievementInfo> Achievements => this.achievements;
	public IReadOnlyList<HuntMilestoneInfo> Milestones => this.milestones;
	public bool IsRefreshing => this.activeRequestId.HasValue || this.progressRequestQueue.Count > 0;

	public MarkAchievementService(HuntBillService huntBillService) {
		this.huntBillService = huntBillService;
		this.LoadDefinitions();

		nint receiveAddr = (nint)ClientAchievement.MemberFunctionPointers.ReceiveAchievementProgress;
		if (receiveAddr != nint.Zero) {
			this.receiveAchievementProgressHook = Svc.Hook.HookFromAddress<ReceiveAchievementProgressDelegate>(
				receiveAddr,
				this.OnReceiveAchievementProgress);
			this.receiveAchievementProgressHook.Enable();
			Svc.Log.Information($"[MarkAchievement] Hooked ReceiveAchievementProgress at 0x{receiveAddr:X}");
		}
		else {
			Svc.Log.Warning("[MarkAchievement] ReceiveAchievementProgress pointer was zero; falling back to polling.");
		}

		Svc.ClientState.TerritoryChanged += this.OnTerritoryChanged;
		Svc.Framework.Update += this.OnFrameworkUpdate;
	}

	private void LoadDefinitions() {
		ExcelSheet<AchievementSheet>? achSheet = Svc.Data.GetExcelSheet<AchievementSheet>();
		ExcelSheet<TerritoryType>? ttSheet = Svc.Data.GetExcelSheet<TerritoryType>();
		if (achSheet == null || ttSheet == null) {
			Svc.Log.Error("[MarkAchievement] Failed to load Achievement or TerritoryType sheets.");
			return;
		}

		int markCount = 0;
		foreach (MarkAchievementCatalog.AchievementDefinition def in MarkAchievementCatalog.Definitions) {
			AchievementSheet? achRow = achSheet.GetRowOrDefault(def.AchievementId);
			if (achRow == null) {
				Svc.Log.Warning($"[MarkAchievement] Missing Achievement row {def.AchievementId}");
				continue;
			}

			List<uint> dataEntries = [];
			for (int i = 0; i < achRow.Value.Data.Count; i++) {
				uint rawId = achRow.Value.Data[i].RowId;
				if (rawId != 0) {
					dataEntries.Add(rawId);
				}
			}

			TerritoryType? firstTt = def.TerritoryTypes.Length > 0 ? ttSheet.GetRowOrDefault(def.TerritoryTypes[0]) : null;

			MarkAchievementInfo info = new() {
				AchievementId = def.AchievementId,
				Name = achRow.Value.Name.ExtractText(),
				Description = achRow.Value.Description.ExtractText(),
				Icon = achRow.Value.Icon,
				RequiredCount = achRow.Value.Key.RowId,
				DataEntriesText = string.Join(", ", dataEntries),
				ExpansionId = firstTt?.ExVersion.RowId ?? 0,
				ExpansionName = firstTt?.ExVersion.Value.Name.ExtractText() ?? "Unknown",
				RegionName = def.RegionName,
				Rank = def.Rank,
				TerritoryTypes = def.TerritoryTypes,
			};

			foreach (uint territoryId in def.TerritoryTypes) {
				TerritoryType? ttRow = ttSheet.GetRowOrDefault(territoryId);
				if (ttRow == null) {
					continue;
				}

				string territoryName = ttRow.Value.PlaceName.Value.Name.ExtractText();
				uint mapId = ttRow.Value.Map.RowId;
				NotoriousMonsterTerritory? nmtRow = ttRow.Value.NotoriousMonsterTerritory.ValueNullable;
				if (nmtRow == null) {
					continue;
				}

				foreach (RowRef<NotoriousMonster> nmRef in nmtRow.Value.NotoriousMonsters) {
					if (nmRef.RowId == 0) {
						continue;
					}

					NotoriousMonster nm = nmRef.Value;
					if (nm.Rank != (byte)def.Rank) {
						continue;
					}

					string rawSingular = nm.BNpcName.Value.Singular.ExtractText().Trim();
					EliteMarkInfo mark = new() {
						Achievement = info,
						NotoriousMonsterId = nm.RowId,
						BNpcNameId = nm.BNpcName.RowId,
						Name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(rawSingular),
						Rank = def.Rank,
						TerritoryType = territoryId,
						TerritoryName = territoryName,
						MapId = mapId,
						SpawnCondition = MarkAchievementCatalog.SrankSpawnConditions.GetValueOrDefault(nm.RowId),
					};

					info.Marks.Add(mark);
					markCount++;
					this.marksByBNpcNameId[mark.BNpcNameId] = mark;
					if (!string.IsNullOrEmpty(rawSingular)) {
						this.marksByName[rawSingular] = mark;
					}
				}

				this.markTerritories.Add(territoryId);
			}

			this.achievements.Add(info);
			this.achievementsById[info.AchievementId] = info;
		}

		uint huntCategoryId = achSheet.GetRowOrDefault(964)?.AchievementCategory.RowId ?? 0;
		if (huntCategoryId != 0) {
			foreach (AchievementSheet row in achSheet) {
				if (row.AchievementCategory.RowId != huntCategoryId || this.achievementsById.ContainsKey(row.RowId)) {
					continue;
				}

				string name = row.Name.ExtractText().Trim();
				if (string.IsNullOrEmpty(name)) {
					continue;
				}

				string description = row.Description.ExtractText().Trim();
				HuntMilestoneInfo milestone = new() {
					AchievementId = row.RowId,
					Name = name,
					Description = description,
					Icon = row.Icon,
					Order = row.Order,
					RequiredCount = row.Key.RowId,
					ExpansionLabel = ClassifyMilestoneExpansion(name, description),
					RankLabel = ClassifyMilestoneRank(description),
				};

				this.milestones.Add(milestone);
				this.milestonesById[milestone.AchievementId] = milestone;
			}

			this.milestones.Sort(static (a, b) => {
				int order = a.Order.CompareTo(b.Order);
				return order != 0 ? order : a.AchievementId.CompareTo(b.AchievementId);
			});
		}

		Svc.Log.Information($"[MarkAchievement] Loaded {this.achievements.Count} Mark achievements, {this.milestones.Count} hunt milestones, and {markCount} elite marks.");
	}

	private static string ClassifyMilestoneExpansion(string name, string description) {
		string combined = $"{name} {description}";
		if (combined.Contains("Tural", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Dawntrail", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Urqopacha", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Kozama'uka", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Yak T'el", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Shaaloani", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Heritage Found", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Living Memory", StringComparison.OrdinalIgnoreCase)) {
			return "Dawntrail";
		}

		if (combined.Contains("Ilsabard", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Endwalker", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Thavnair", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Elpis", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Ultima Thule", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Mare Lamentorum", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Labyrinthos", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Garlemald", StringComparison.OrdinalIgnoreCase)) {
			return "Endwalker";
		}

		if (combined.Contains("Norvrandt", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Shadowbringers", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Lakeland", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Kholusia", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Amh Araeng", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Il Mheg", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Rak'tika", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Tempest", StringComparison.OrdinalIgnoreCase)) {
			return "Shadowbringers";
		}

		if (combined.Contains("Gyr Abania", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Othard", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Stormblood", StringComparison.OrdinalIgnoreCase)) {
			return "Stormblood";
		}

		if (combined.Contains("Dravania", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Abalathia", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Coerthas Western", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Heavensward", StringComparison.OrdinalIgnoreCase)) {
			return "Heavensward";
		}

		if (combined.Contains("Eorzea", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Black Shroud", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("Thanalan", StringComparison.OrdinalIgnoreCase)
			|| combined.Contains("La Noscea", StringComparison.OrdinalIgnoreCase)) {
			return "A Realm Reborn";
		}

		return "General";
	}

	private static string ClassifyMilestoneRank(string description) {
		if (description.Contains("rank S", StringComparison.OrdinalIgnoreCase)
			|| description.Contains("S-rank", StringComparison.OrdinalIgnoreCase)) {
			return "Rank S";
		}

		if (description.Contains("rank A", StringComparison.OrdinalIgnoreCase)
			|| description.Contains("A-rank", StringComparison.OrdinalIgnoreCase)) {
			return "Rank A";
		}

		if (description.Contains("rank B", StringComparison.OrdinalIgnoreCase)
			|| description.Contains("B-rank", StringComparison.OrdinalIgnoreCase)) {
			return "Rank B";
		}

		return "General";
	}

	private void OnTerritoryChanged(uint territoryType) {
		this.activeMarks.Clear();
		this.alertedEntityIds.Clear();
	}

	private void OnFrameworkUpdate(IFramework framework) {
		if (!Svc.PlayerState.IsLoaded || Svc.PlayerState.ContentId == 0) {
			if (this.activeContentId != 0) {
				this.ResetSessionState();
			}
			return;
		}

		ulong contentId = Svc.PlayerState.ContentId;
		if (this.activeContentId != contentId) {
			this.ResetSessionState();
			this.activeContentId = contentId;
			this.activeProgress = GetOrCreateCharacterProgress(contentId);
			this.RestoreSavedProgress(this.activeProgress);
		}

		ClientAchievement* ach = ClientAchievement.Instance();
		if (ach == null) {
			return;
		}

		if (!ach->IsLoaded()) {
			if (!this.requestedAchievementBitmap && ach->State == ClientAchievement.AchievementState.Invalid) {
				this.requestedAchievementBitmap = true;
				ach->RequestCompletedAchievements();
			}
			return;
		}

		if (!this.initialSyncRequested) {
			this.initialSyncRequested = true;
			this.SyncCompletionStates();
			this.EnqueueIncompleteAchievements();
		}

		DateTime now = DateTime.UtcNow;

		if ((now - this.lastObjectScanUtc).TotalMilliseconds >= 250) {
			this.lastObjectScanUtc = now;
			this.ScanCurrentZoneMarks(now);
		}

		if ((now - this.lastBillCheckUtc).TotalMilliseconds >= 1000) {
			this.lastBillCheckUtc = now;
			this.CheckWeeklyEliteBills(now);
		}

		this.ProcessProgressQueue(ach, now);
	}

	private void ResetSessionState() {
		this.activeContentId = 0;
		this.activeProgress = null;
		this.initialSyncRequested = false;
		this.requestedAchievementBitmap = false;
		this.activeRequestId = null;
		this.progressRequestQueue.Clear();
		this.activeMarks.Clear();
		this.alertedEntityIds.Clear();
		this.recentSlainMarks.Clear();

		foreach (MarkAchievementInfo info in this.achievements) {
			info.IsComplete = false;
			info.ProgressLoaded = false;
			info.ProgressCurrent = 0;
			info.ProgressMax = 0;
			info.LastProgressUpdatedUtc = null;
		}

		foreach (HuntMilestoneInfo milestone in this.milestones) {
			milestone.IsComplete = false;
			milestone.ProgressLoaded = false;
			milestone.ProgressCurrent = 0;
			milestone.ProgressMax = 0;
			milestone.LastProgressUpdatedUtc = null;
		}
	}

	private void RestoreSavedProgress(CharacterMarkProgress progress) {
		foreach (MarkAchievementInfo info in this.achievements) {
			if (progress.LastKnownProgress.TryGetValue(info.AchievementId, out uint lastCur)) {
				info.ProgressCurrent = lastCur;
				info.ProgressMax = progress.LastKnownProgressMax.TryGetValue(info.AchievementId, out uint lastMax)
					? lastMax
					: info.RequiredCount;
				info.ProgressLoaded = true;
			}
		}

		foreach (HuntMilestoneInfo milestone in this.milestones) {
			if (progress.LastKnownProgress.TryGetValue(milestone.AchievementId, out uint lastCur)) {
				milestone.ProgressCurrent = lastCur;
				milestone.ProgressMax = progress.LastKnownProgressMax.TryGetValue(milestone.AchievementId, out uint lastMax)
					? lastMax
					: milestone.RequiredCount;
				milestone.ProgressLoaded = true;
			}
		}
	}

	private static CharacterMarkProgress GetOrCreateCharacterProgress(ulong contentId) {
		Plugin.Instance.Configuration.CharacterMarkProgress ??= [];
		if (!Plugin.Instance.Configuration.CharacterMarkProgress.TryGetValue(contentId, out CharacterMarkProgress? progress)) {
			progress = new CharacterMarkProgress();
			Plugin.Instance.Configuration.CharacterMarkProgress[contentId] = progress;
		}

		progress.CompletedMarks ??= [];
		progress.LastKnownProgress ??= [];
		progress.LastKnownProgressMax ??= [];
		return progress;
	}

	public bool IsMarkChecked(EliteMarkInfo mark) =>
		mark.Achievement.IsComplete || this.activeProgress?.CompletedMarks.Contains(mark.NotoriousMonsterId) == true;

	public bool TryGetNeededAchievement(uint bnpcNameId, [NotNullWhen(true)] out MarkAchievementInfo? achievement) {
		if (this.marksByBNpcNameId.TryGetValue(bnpcNameId, out EliteMarkInfo? mark) && !this.IsMarkChecked(mark)) {
			achievement = mark.Achievement;
			return true;
		}

		achievement = null;
		return false;
	}

	public bool TryGetMarkByName(string name, [NotNullWhen(true)] out EliteMarkInfo? mark) =>
		this.marksByName.TryGetValue(name, out mark);

	public void SetMarkChecked(EliteMarkInfo mark, bool isChecked) {
		if (this.activeProgress == null) {
			return;
		}

		if (SetMarkCompleted(this.activeProgress, mark, isChecked)) {
			Plugin.Instance.Configuration.Save();
		}
	}

	public void SetAllMarksChecked(MarkAchievementInfo info, bool isChecked) {
		if (this.activeProgress == null) {
			return;
		}

		if (SetAllMarksCompleted(this.activeProgress, info, isChecked)) {
			Plugin.Instance.Configuration.Save();
		}
	}

	private static bool SetMarkCompleted(CharacterMarkProgress progress, EliteMarkInfo mark, bool isCompleted) =>
		isCompleted
			? progress.CompletedMarks.Add(mark.NotoriousMonsterId)
			: progress.CompletedMarks.Remove(mark.NotoriousMonsterId);

	private static bool SetAllMarksCompleted(CharacterMarkProgress progress, MarkAchievementInfo info, bool isCompleted) {
		bool changed = false;
		foreach (EliteMarkInfo mark in info.Marks) {
			changed |= SetMarkCompleted(progress, mark, isCompleted);
		}

		return changed;
	}

	public bool RefreshAll(bool includeCompleted) {
		ClientAchievement* ach = ClientAchievement.Instance();
		if (this.activeContentId == 0 || ach == null || !ach->IsLoaded()) {
			return false;
		}

		this.SyncCompletionStates();
		if (includeCompleted) {
			DateTime now = DateTime.UtcNow;
			foreach (MarkAchievementInfo info in this.achievements) {
				this.EnqueueProgressRequest(info.AchievementId, now, prioritize: false);
			}
			foreach (HuntMilestoneInfo milestone in this.milestones) {
				this.EnqueueProgressRequest(milestone.AchievementId, now, prioritize: false);
			}
			Svc.Log.Information($"[MarkAchievement] Queued progress refresh and data logging for all {this.achievements.Count + this.milestones.Count} hunt achievements.");
		}
		else {
			this.EnqueueIncompleteAchievements();
		}

		return true;
	}

	private void SyncCompletionStates() {
		ClientAchievement* ach = ClientAchievement.Instance();
		if (ach == null || !ach->IsLoaded()) {
			return;
		}

		CharacterMarkProgress? charProgress = this.activeProgress;
		bool configChanged = false;

		foreach (MarkAchievementInfo info in this.achievements) {
			bool isComplete = ach->IsComplete((int)info.AchievementId);
			bool newlyCompleted = isComplete && !info.IsComplete;
			info.IsComplete = isComplete;

			if (!isComplete || charProgress == null) {
				continue;
			}

			uint max = info.ProgressMax > 0 ? info.ProgressMax : info.RequiredCount;
			info.ProgressCurrent = max;
			info.ProgressMax = max;
			info.ProgressLoaded = true;
			configChanged |= SetAllMarksCompleted(charProgress, info, true);

			if (newlyCompleted) {
				StringBuilder markList = new();
				for (int i = 0; i < info.Marks.Count; i++) {
					if (i > 0) {
						markList.Append(", ");
					}
					EliteMarkInfo mark = info.Marks[i];
					markList.Append(mark.NotoriousMonsterId).Append(':').Append(mark.Name);
				}

				Svc.Log.Information(
					$"[MarkAchievement] Completed AchievementId={info.AchievementId} Name=\"{info.Name}\" Rank={info.Rank} Region=\"{info.RegionName}\" Data=[{info.DataEntriesText}] Marks=[{markList}]");
			}
		}

		foreach (HuntMilestoneInfo milestone in this.milestones) {
			bool isComplete = ach->IsComplete((int)milestone.AchievementId);
			milestone.IsComplete = isComplete;
			if (!isComplete) {
				continue;
			}

			uint max = milestone.ProgressMax > 0 ? milestone.ProgressMax : milestone.RequiredCount;
			milestone.ProgressCurrent = max;
			milestone.ProgressMax = max;
			milestone.ProgressLoaded = true;
		}

		if (configChanged) {
			Plugin.Instance.Configuration.Save();
		}
	}

	private void EnqueueIncompleteAchievements() {
		DateTime now = DateTime.UtcNow;
		int count = 0;
		foreach (MarkAchievementInfo info in this.achievements) {
			if (!info.IsComplete) {
				this.EnqueueProgressRequest(info.AchievementId, now, prioritize: false);
				count++;
			}
		}

		foreach (HuntMilestoneInfo milestone in this.milestones) {
			if (!milestone.IsComplete) {
				this.EnqueueProgressRequest(milestone.AchievementId, now, prioritize: false);
				count++;
			}
		}

		if (count > 0) {
			Svc.Log.Information($"[MarkAchievement] Queued progress requests for {count} incomplete hunt achievements.");
		}
	}

	private void EnqueueProgressRequest(uint achievementId, DateTime earliestRequestUtc, bool prioritize) {
		int queuedIndex = -1;
		for (int i = 0; i < this.progressRequestQueue.Count; i++) {
			if (this.progressRequestQueue[i].AchievementId == achievementId) {
				queuedIndex = i;
				break;
			}
		}

		if (prioritize) {
			if (queuedIndex >= 0) {
				this.progressRequestQueue.RemoveAt(queuedIndex);
			}

			this.progressRequestQueue.Insert(0, (achievementId, earliestRequestUtc));
			return;
		}

		if (queuedIndex < 0 && this.activeRequestId != achievementId) {
			this.progressRequestQueue.Add((achievementId, earliestRequestUtc));
		}
	}

	private void ProcessProgressQueue(ClientAchievement* ach, DateTime now) {
		if (this.activeRequestId.HasValue) {
			uint currentId = this.activeRequestId.Value;
			if (ach->ProgressAchievementId == currentId && ach->ProgressRequestState == ClientAchievement.AchievementState.Loaded) {
				if (this.receiveAchievementProgressHook == null) {
					this.HandleAchievementProgress(currentId, ach->ProgressCurrent, ach->ProgressMax);
				}
				this.activeRequestId = null;
				this.lastRequestCompletedUtc = now;
			}
			else if ((now - this.activeRequestSentUtc).TotalMilliseconds >= 2000) {
				Svc.Log.Warning($"[MarkAchievement] Progress request timed out for AchievementId={currentId}");
				this.activeRequestId = null;
				this.lastRequestCompletedUtc = now;
			}
			else {
				return;
			}
		}

		if (this.progressRequestQueue.Count == 0) {
			return;
		}

		if ((now - this.lastRequestCompletedUtc).TotalMilliseconds < 150) {
			return;
		}

		(uint nextId, DateTime earliestUtc) = this.progressRequestQueue[0];
		if (now < earliestUtc) {
			return;
		}

		this.progressRequestQueue.RemoveAt(0);
		this.activeRequestId = nextId;
		this.activeRequestSentUtc = now;
		ach->RequestAchievementProgress(nextId);
	}

	private void OnReceiveAchievementProgress(ClientAchievement* thisPtr, uint id, uint current, uint max) {
		this.receiveAchievementProgressHook?.Original(thisPtr, id, current, max);

		try {
			Svc.Log.Debug($"[MarkAchievement] ReceiveAchievementProgress raw: AchievementId={id}, Current={current}, Max={max}");

			if (this.activeRequestId == id) {
				this.activeRequestId = null;
				this.lastRequestCompletedUtc = DateTime.UtcNow;
			}

			this.HandleAchievementProgress(id, current, max);
		}
		catch (Exception e) {
			Svc.Log.Error(e, $"[MarkAchievement] Failed to handle progress for AchievementId={id}");
		}
	}

	private void HandleAchievementProgress(uint id, uint current, uint max) {
		if (this.milestonesById.TryGetValue(id, out HuntMilestoneInfo? milestone)) {
			this.HandleMilestoneProgress(milestone, current, max);
			return;
		}

		if (!this.achievementsById.TryGetValue(id, out MarkAchievementInfo? info)) {
			return;
		}

		DateTime now = DateTime.UtcNow;
		ClientAchievement* ach = ClientAchievement.Instance();
		bool isComplete = (ach != null && ach->IsLoaded() && ach->IsComplete((int)id)) || (max > 0 && current >= max);
		info.IsComplete = isComplete;

		CharacterMarkProgress? charProgress = this.activeProgress;
		uint prev = 0;
		bool hadPrevious = charProgress != null && charProgress.LastKnownProgress.TryGetValue(id, out prev);
		if (!hadPrevious) {
			prev = info.ProgressLoaded ? info.ProgressCurrent : 0;
			hadPrevious = info.ProgressLoaded;
		}

		info.ProgressCurrent = current;
		info.ProgressMax = max;
		info.ProgressLoaded = true;
		info.LastProgressUpdatedUtc = now;

		string prevDisplay = hadPrevious ? prev.ToString(CultureInfo.InvariantCulture) : "none";
		Svc.Log.Information(
			$"[MarkAchievement] Progress: AchievementId={info.AchievementId} Name=\"{info.Name}\" Rank={info.Rank} Region=\"{info.RegionName}\" Complete={isComplete} Current={current}/{max} (Prev={prevDisplay}) Key={info.RequiredCount} Data=[{info.DataEntriesText}]");

		if (charProgress == null) {
			return;
		}

		bool configChanged = false;
		if (!charProgress.LastKnownProgress.TryGetValue(id, out uint storedPrev) || storedPrev != current) {
			charProgress.LastKnownProgress[id] = current;
			configChanged = true;
		}
		if (!charProgress.LastKnownProgressMax.TryGetValue(id, out uint storedMax) || storedMax != max) {
			charProgress.LastKnownProgressMax[id] = max;
			configChanged = true;
		}

		if (isComplete) {
			configChanged |= SetAllMarksCompleted(charProgress, info, true);
		}
		else if (current == 0) {
			configChanged |= SetAllMarksCompleted(charProgress, info, false);
		}
		else if (hadPrevious && current >= prev) {
			configChanged |= this.ApplySlainMarks(charProgress, info, prev, current, max, now);
		}

		if (configChanged) {
			Plugin.Instance.Configuration.Save();
		}
	}

	private void HandleMilestoneProgress(HuntMilestoneInfo milestone, uint current, uint max) {
		DateTime now = DateTime.UtcNow;
		ClientAchievement* ach = ClientAchievement.Instance();
		bool isComplete = (ach != null && ach->IsLoaded() && ach->IsComplete((int)milestone.AchievementId)) || (max > 0 && current >= max);
		milestone.IsComplete = isComplete;
		milestone.ProgressCurrent = current;
		milestone.ProgressMax = max;
		milestone.ProgressLoaded = true;
		milestone.LastProgressUpdatedUtc = now;

		CharacterMarkProgress? charProgress = this.activeProgress;
		if (charProgress == null) {
			return;
		}

		bool configChanged = false;
		if (!charProgress.LastKnownProgress.TryGetValue(milestone.AchievementId, out uint storedPrev) || storedPrev != current) {
			charProgress.LastKnownProgress[milestone.AchievementId] = current;
			configChanged = true;
		}

		if (!charProgress.LastKnownProgressMax.TryGetValue(milestone.AchievementId, out uint storedMax) || storedMax != max) {
			charProgress.LastKnownProgressMax[milestone.AchievementId] = max;
			configChanged = true;
		}

		if (configChanged) {
			Plugin.Instance.Configuration.Save();
		}
	}

	private bool ApplySlainMarks(CharacterMarkProgress charProgress, MarkAchievementInfo info, uint prev, uint current, uint max, DateTime now) {
		this.PruneSlainMarks(now);

		List<SlainMarkRecord> matchingSlain = [];
		uint uncheckedCount = 0;
		foreach (SlainMarkRecord record in this.recentSlainMarks) {
			if (record.Mark.Achievement != info) {
				continue;
			}

			matchingSlain.Add(record);
			if (!charProgress.CompletedMarks.Contains(record.Mark.NotoriousMonsterId)) {
				uncheckedCount++;
			}
		}

		if (current == prev) {
			if (matchingSlain.Count > 0) {
				Svc.Log.Information(
					$"[MarkAchievement] ProgressUnchanged: AchievementId={info.AchievementId} Name=\"{info.Name}\" remained at {current}/{max} after CandidateMarks=[{FormatSlainRecords(matchingSlain)}] Data=[{info.DataEntriesText}]");
			}
			return false;
		}

		string slainDetails = matchingSlain.Count > 0
			? FormatSlainRecords(matchingSlain)
			: "none detected in recent window";
		Svc.Log.Information(
			$"[MarkAchievement] ProgressDelta: AchievementId={info.AchievementId} Name=\"{info.Name}\" {prev}->{current}/{max} Data=[{info.DataEntriesText}] CandidateMarks=[{slainDetails}]");

		if (uncheckedCount != 1 && uncheckedCount != current - prev) {
			return false;
		}

		bool changed = false;
		foreach (SlainMarkRecord slain in matchingSlain) {
			if (charProgress.CompletedMarks.Add(slain.Mark.NotoriousMonsterId)) {
				changed = true;
				Svc.Log.Information(
					$"[MarkAchievement] AutoChecked: NM#{slain.Mark.NotoriousMonsterId} (BNpc#{slain.Mark.BNpcNameId}) \"{slain.Mark.Name}\" for AchievementId={info.AchievementId} \"{info.Name}\" (Data=[{info.DataEntriesText}])");
			}
		}

		return changed;
	}

	private void ScanCurrentZoneMarks(DateTime now) {
		uint territoryType = Svc.ClientState.TerritoryType;
		if (!this.markTerritories.Contains(territoryType)) {
			return;
		}

		Vector3 playerPos = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
		Map? mapRow = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryType)?.Map.ValueNullable;
		this.seenEntityIds.Clear();

		foreach (IGameObject gameObject in Svc.Objects.CharacterManagerObjects) {
			if (gameObject is not IBattleNpc battleNpc
				|| !this.marksByBNpcNameId.TryGetValue(battleNpc.NameId, out EliteMarkInfo? mark)
				|| mark.TerritoryType != territoryType) {
				continue;
			}

			uint entityId = battleNpc.EntityId;
			this.seenEntityIds.Add(entityId);
			bool isAlive = !battleNpc.IsDead && battleNpc.CurrentHp > 0;
			bool wasAlive = this.activeMarks.TryGetValue(entityId, out bool tracked) && tracked;
			this.activeMarks[entityId] = isAlive;

			if (isAlive && !tracked && !this.IsMarkChecked(mark) && this.alertedEntityIds.Add(entityId)) {
				NotifyNeededMarkSpotted(mark, battleNpc.Position, mapRow);
			}

			if (wasAlive && !isAlive) {
				this.RecordSlainMark(mark, Vector3.Distance(playerPos, battleNpc.Position), now);
			}
		}

		if (this.activeMarks.Count == this.seenEntityIds.Count) {
			return;
		}

		this.staleEntityIds.Clear();
		foreach (uint entityId in this.activeMarks.Keys) {
			if (!this.seenEntityIds.Contains(entityId)) {
				this.staleEntityIds.Add(entityId);
			}
		}

		for (int i = 0; i < this.staleEntityIds.Count; i++) {
			this.activeMarks.Remove(this.staleEntityIds[i]);
		}
	}

	private static void NotifyNeededMarkSpotted(EliteMarkInfo mark, Vector3 worldPos, Map? mapRow) {
		Models.Configuration cfg = Plugin.Instance.Configuration;
		if (cfg.AlertOnNeededMarkSpawn) {
			Vector2? mapCoord = mapRow.HasValue
				? MapUtil.WorldToMap(new Vector2(worldPos.X, worldPos.Z), mapRow.Value)
				: null;
			MapService.EchoMapLink(mark.TerritoryType, mark.MapId, $"Needed [{mark.Rank}] {mark.Name} spotted!", mapCoord, mark.TerritoryName);
			Svc.Toasts.ShowQuest($"Needed [{mark.Rank}] {mark.Name} spotted in {mark.TerritoryName}!");
		}

		if (cfg.PlaySoundOnNeededMarkSpawn) {
			UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(cfg.AlertSoundEffectId, 1, 16));
		}
	}

	private void RecordSlainMark(EliteMarkInfo mark, float distance, DateTime now) {
		this.PruneSlainMarks(now);
		for (int i = this.recentSlainMarks.Count - 1; i >= 0; i--) {
			if (this.recentSlainMarks[i].Mark == mark) {
				this.recentSlainMarks.RemoveAt(i);
			}
		}

		this.recentSlainMarks.Add(new SlainMarkRecord(mark, distance, now));

		Svc.Log.Information(
			$"[MarkAchievement] SlainMarkDetected: NM#{mark.NotoriousMonsterId} BNpc#{mark.BNpcNameId} \"{mark.Name}\" Rank={mark.Rank} Zone=\"{mark.TerritoryName}\"({mark.TerritoryType}) Dist={distance:F1}y -> Requesting progress for AchievementId={mark.Achievement.AchievementId}");

		this.SyncCompletionStates();
		if (!mark.Achievement.IsComplete) {
			this.EnqueueProgressRequest(mark.Achievement.AchievementId, now.AddMilliseconds(350), prioritize: true);
		}
	}

	private void PruneSlainMarks(DateTime now) {
		DateTime cutoff = now.AddSeconds(-SlainMarkWindowSeconds);
		for (int i = this.recentSlainMarks.Count - 1; i >= 0; i--) {
			if (this.recentSlainMarks[i].TimestampUtc < cutoff) {
				this.recentSlainMarks.RemoveAt(i);
			}
		}
	}

	private void CheckWeeklyEliteBills(DateTime now) {
		CharacterMarkProgress? charProgress = this.activeProgress;
		if (charProgress == null
			|| !this.huntBillService.MobHuntEntriesReady
			|| this.huntBillService.ActiveWeeklyEliteMobIds.Count == 0) {
			return;
		}

		bool changed = false;
		foreach (List<HuntZone> zones in this.huntBillService.MobHuntEntries.Values) {
			foreach (HuntZone zone in zones) {
				foreach (MobHuntEntry entry in zone.Entries) {
					if (!entry.IsEliteMark
						|| entry.ExpansionId > 2
						|| !this.marksByBNpcNameId.TryGetValue(entry.MobHuntId, out EliteMarkInfo? mark)
						|| charProgress.CompletedMarks.Contains(mark.NotoriousMonsterId)) {
						continue;
					}

					MobHuntProgress progress = entry.GetProgress();
					if (!progress.IsComplete) {
						continue;
					}

					charProgress.CompletedMarks.Add(mark.NotoriousMonsterId);
					changed = true;
					Svc.Log.Information(
						$"[MarkAchievement] WeeklyBillCredit: NM#{mark.NotoriousMonsterId} BNpc#{mark.BNpcNameId} \"{mark.Name}\" Kills={progress.Kills}/{progress.NeededKills} -> Checked off for AchievementId={mark.Achievement.AchievementId}");
					if (!mark.Achievement.IsComplete) {
						this.EnqueueProgressRequest(mark.Achievement.AchievementId, now, prioritize: false);
					}
				}
			}
		}

		if (changed) {
			Plugin.Instance.Configuration.Save();
		}
	}

	private static string FormatSlainRecords(List<SlainMarkRecord> records) {
		StringBuilder sb = new();
		for (int i = 0; i < records.Count; i++) {
			if (i > 0) {
				sb.Append("; ");
			}

			EliteMarkInfo mark = records[i].Mark;
			sb.Append($"NM#{mark.NotoriousMonsterId}:BNpc#{mark.BNpcNameId} \"{mark.Name}\" Zone={mark.TerritoryName}({mark.TerritoryType}) Dist={records[i].Distance:F1}y");
		}

		return sb.ToString();
	}

	public void Dispose() {
		Svc.ClientState.TerritoryChanged -= this.OnTerritoryChanged;
		Svc.Framework.Update -= this.OnFrameworkUpdate;
		this.receiveAchievementProgressHook?.Dispose();
	}
}
