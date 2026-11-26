using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using HuntBuddy.Data;
using HuntBuddy.Models;
using HuntBuddy.Services;
using Lumina.Excel.Sheets;
using RebornMaterial;

namespace HuntBuddy.UI;

public partial class MainWindow {
	private void DrawMarksHeader() {
		if (M3Widgets.Chip("##rank_all", "All Ranks", this.rankFilter == null)) {
			this.rankFilter = null;
		}

		ImGui.SameLine();
		if (M3Widgets.Chip("##rank_b", "Rank B", this.rankFilter == MarkRank.B)) {
			this.rankFilter = MarkRank.B;
		}

		ImGui.SameLine();
		if (M3Widgets.Chip("##rank_a", "Rank A", this.rankFilter == MarkRank.A)) {
			this.rankFilter = MarkRank.A;
		}

		ImGui.SameLine();
		if (M3Widgets.Chip("##rank_s", "Rank S", this.rankFilter == MarkRank.S)) {
			this.rankFilter = MarkRank.S;
		}

		EorzeaEnvironmentService.EorzeaClock clock = EorzeaEnvironmentService.GetEorzeaClock(DateTimeOffset.UtcNow);
		string moon = clock.IsFullMoon ? " - Full Moon" : clock.IsNewMoon ? " - New Moon" : string.Empty;
		string clockText = $"ET {clock.Hour:D2}:{clock.Minute:D2} - Day {clock.DayOfMonth}{moon}";
		if (ImGui.GetContentRegionAvail().X >= 220f * M3.Scale) {
			ImGui.SameLine();
		}
		_ = M3Widgets.Pill(
			"##hbr_eorzea_clock",
			clockText,
			clock.IsFullMoon || clock.IsNewMoon ? M3.Scheme.Tertiary : M3.Scheme.Secondary,
			FontAwesomeIcon.Clock,
			"Current Eorzea Time and lunar phase for S-rank spawn windows",
			false);

		if (this.CurrentPageId == MilestonesPageId && !this.Searching) {
			ImGui.Spacing();
			this.DrawMilestoneExpansionChips();
		}

		if (!this.Searching && this.CurrentPageId != MarksZonePageId && this.CurrentPageId != MilestonesPageId) {
			uint currentTerritory = Svc.ClientState.TerritoryType;
			this.scratchText.Clear();

			IReadOnlyList<MarkAchievementInfo> achievements = this.markService.Achievements;
			for (int i = 0; i < achievements.Count; i++) {
				MarkAchievementInfo ach = achievements[i];
				if (ach.IsComplete || !ach.HasTerritory(currentTerritory)) {
					continue;
				}

				for (int j = 0; j < ach.Marks.Count; j++) {
					EliteMarkInfo mark = ach.Marks[j];
					if (mark.TerritoryType != currentTerritory || this.markService.IsMarkChecked(mark)) {
						continue;
					}

					if (this.scratchText.Length > 0) {
						this.scratchText.Append(", ");
					}

					this.scratchText.Append('[').Append(mark.Rank).Append("] ").Append(mark.Name);
				}
			}

			if (this.scratchText.Length > 0) {
				ImGui.Spacing();
				string zoneName = this.GetTerritoryName(currentTerritory);
				if (M3Widgets.Banner(
					"##current_zone_needed",
					$"Needed in {zoneName}: {this.scratchText}",
					M3Severity.Info,
					FontAwesomeIcon.Crosshairs,
					"View Zone")) {
					this.ShowPage(MarksZonePageId);
				}
			}
		}

		ImGui.Spacing();
	}

	private void DrawAchievementsPage(uint? expansionId, bool currentZoneOnly, bool neededOnly) {
		if (!Svc.PlayerState.IsLoaded || Svc.PlayerState.ContentId == 0) {
			M3Widgets.EmptyState(
				"##mark_ach_not_logged_in",
				"No character loaded",
				"Log in with a character to track Mark of the X achievements.",
				FontAwesomeIcon.UserSlash);
			return;
		}

		this.DrawAchievementsList(expansionId, currentZoneOnly, neededOnly, string.Empty, true);
	}

	private bool DrawAchievementsList(uint? expansionId, bool currentZoneOnly, bool neededOnly, string query, bool showEmptyState, string? sectionHeader = null) {
		uint currentTerritory = Svc.ClientState.TerritoryType;
		bool showExpansionInSubtitle = expansionId == null;
		bool hideCompleted = Plugin.Instance.Configuration.HideCompletedMarkAchievements || neededOnly;
		bool anyShown = false;

		IReadOnlyList<MarkAchievementInfo> achievements = this.markService.Achievements;
		for (int i = 0; i < achievements.Count; i++) {
			MarkAchievementInfo info = achievements[i];
			if (expansionId.HasValue && info.ExpansionId != expansionId.Value) {
				continue;
			}

			if (this.rankFilter.HasValue && info.Rank != this.rankFilter.Value) {
				continue;
			}

			if (hideCompleted && info.IsComplete) {
				continue;
			}

			bool hasCurrentTerritory = info.HasTerritory(currentTerritory);
			if (currentZoneOnly && !hasCurrentTerritory) {
				continue;
			}

			bool achievementMatches = string.IsNullOrEmpty(query)
				|| info.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| info.RegionName.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| info.ExpansionName.Contains(query, StringComparison.OrdinalIgnoreCase);

			this.scratchMarks.Clear();
			int checkedCount = 0;
			for (int j = 0; j < info.Marks.Count; j++) {
				EliteMarkInfo mark = info.Marks[j];
				bool isChecked = this.markService.IsMarkChecked(mark);
				if (isChecked) {
					checkedCount++;
				}

				if (currentZoneOnly && mark.TerritoryType != currentTerritory) {
					continue;
				}

				if (neededOnly && isChecked) {
					continue;
				}

				if (!achievementMatches
					&& !mark.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
					&& !mark.TerritoryName.Contains(query, StringComparison.OrdinalIgnoreCase)) {
					continue;
				}

				this.scratchMarks.Add(mark);
			}

			if (this.scratchMarks.Count == 0) {
				continue;
			}

			if (!anyShown && sectionHeader != null) {
				ImGui.Spacing();
				M3Widgets.SectionLabel(sectionHeader, M3.Scheme.Primary);
			}

			anyShown = true;
			bool expanded = this.expandedMarkStates.GetValueOrDefault(info.AchievementId, !info.IsComplete && hasCurrentTerritory);
			bool wasExpanded = expanded;

			string badgeText;
			Vector4 badgeAccent;
			Vector4 cardAccent;

			if (info.IsComplete) {
				badgeText = "Complete";
				badgeAccent = M3.Scheme.Success;
				cardAccent = M3.Scheme.Success;
			}
			else if (info.ProgressLoaded) {
				badgeText = $"{info.ProgressCurrent}/{info.ProgressMax} game - {checkedCount}/{info.Marks.Count} local";
				badgeAccent = checkedCount == info.ProgressCurrent ? M3.Scheme.Primary : M3.Scheme.Warning;
				cardAccent = hasCurrentTerritory ? M3.Scheme.Tertiary : M3.Scheme.Primary;
			}
			else {
				badgeText = $"?/{info.RequiredCount} game - {checkedCount}/{info.Marks.Count} local";
				badgeAccent = M3.Scheme.Secondary;
				cardAccent = M3.Scheme.Primary;
			}

			string subtitle = showExpansionInSubtitle
				? $"{info.ExpansionName} - {info.RegionName} - Rank {info.Rank}"
				: $"{info.RegionName} - Rank {info.Rank}";

			using ImRaii.IdDisposable id = ImRaii.PushId((int)info.AchievementId);
			using M3ExpandableCard.Scope card = M3ExpandableCard.Begin(
				"##ach_card",
				info.Name,
				ref expanded,
				FontAwesomeIcon.Trophy,
				cardAccent,
				badgeText,
				badgeAccent,
				subtitle);
			if (expanded != wasExpanded) {
				this.expandedMarkStates[info.AchievementId] = expanded;
			}

			if (card.Expanded) {
				this.DrawAchievementBody(info, this.scratchMarks, checkedCount, currentTerritory);
			}
		}

		if (!anyShown && showEmptyState) {
			M3Widgets.EmptyState(
				"##mark_ach_empty",
				currentZoneOnly ? "No Mark achievements for this zone" : "No matching achievements",
				currentZoneOnly
					? "Travel to an ARR, Heavensward, or Stormblood hunt zone or switch pages."
					: "Try adjusting your rank filter, completed toggle, or search query.",
				currentZoneOnly ? FontAwesomeIcon.MapMarkerAlt : FontAwesomeIcon.Filter);
		}

		return anyShown;
	}

	private void DrawAchievementBody(
		MarkAchievementInfo info,
		List<EliteMarkInfo> visibleMarks,
		int checkedCount,
		uint currentTerritory) {
		IDalamudTextureWrap? iconWrap = LoadAchievementIcon(info.Icon);
		if (iconWrap != null) {
			ImGui.Image(iconWrap.Handle, new Vector2(48f) * M3.Scale);
			ImGui.SameLine();
		}

		ImGui.BeginGroup();
		M3Text.Draw(info.Description, M3TextStyle.Muted, true);
		M3Text.Draw(
			$"Achievement #{info.AchievementId} - Key: {info.RequiredCount} - Data: [{info.DataEntriesText}]",
			M3TextStyle.Faint);
		ImGui.EndGroup();

		if (!info.IsComplete && info.ProgressLoaded && info.ProgressCurrent > 0 && checkedCount != info.ProgressCurrent) {
			ImGui.Spacing();
			M3Widgets.Banner(
				"##mismatch",
				$"In-game progress is {info.ProgressCurrent}/{info.ProgressMax}, with {checkedCount}/{info.Marks.Count} checked locally. FFXIV does not send individual past kills; toggle marks you already killed or hunt the remaining marks.",
				M3Severity.Warning,
				FontAwesomeIcon.InfoCircle);
		}

		if (!info.IsComplete) {
			ImGui.Spacing();
			if (M3Widgets.Button("##check_all", "Check All", M3ButtonStyle.Text, FontAwesomeIcon.CheckDouble)) {
				this.markService.SetAllMarksChecked(info, true);
			}

			ImGui.SameLine();
			if (M3Widgets.Button("##clear_all", "Clear Local", M3ButtonStyle.Text, FontAwesomeIcon.Eraser)) {
				this.markService.SetAllMarksChecked(info, false);
			}
		}

		ImGui.Spacing();

		bool canTeleport = Plugin.TeleportConsumer?.IsAvailable == true;
		bool canSearchEsp = Plugin.Instance.Configuration.EnableXivEspIntegration && Plugin.EspConsumer?.IsAvailable == true;
		int maxRowButtons = 0;
		for (int i = 0; i < visibleMarks.Count; i++) {
			bool hasSpawnPoints = SpawnPointDatabase.TryGetSpawnPoints(visibleMarks[i].TerritoryType, out _);
			int rowButtons = 2 + (hasSpawnPoints ? 1 : 0) + (canTeleport ? 1 : 0) + (canSearchEsp ? 1 : 0);
			if (rowButtons > maxRowButtons) {
				maxRowButtons = rowButtons;
			}
		}

		DateTimeOffset utcNow = DateTimeOffset.UtcNow;

		using M3Table.Scope tableScope = M3Table.PushStyle();
		if (ImGui.BeginTable("##marks_table", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) {
			ImGui.TableSetupColumn("Done", ImGuiTableColumnFlags.WidthFixed, MathF.Max(ImGui.CalcTextSize("Done").X, M3Widgets.CheckboxHitSize().X));
			ImGui.TableSetupColumn("Mark", ImGuiTableColumnFlags.WidthStretch, 1.4f);
			ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 1.1f);
			ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, ActionsColumnWidth(Math.Max(1, maxRowButtons)));
			ImGui.TableHeadersRow();

			for (int i = 0; i < visibleMarks.Count; i++) {
				EliteMarkInfo mark = visibleMarks[i];
				using ImRaii.IdDisposable id = ImRaii.PushId((int)mark.NotoriousMonsterId);
				ImGui.TableNextRow();

				ImGui.TableSetColumnIndex(0);
				bool isChecked = this.markService.IsMarkChecked(mark);
				if (M3Widgets.Checkbox("##cb", ref isChecked, enabled: !info.IsComplete)) {
					this.markService.SetMarkChecked(mark, isChecked);
				}

				ImGui.TableSetColumnIndex(1);
				M3Text.Draw(mark.Name, isChecked ? M3TextStyle.Muted : M3TextStyle.Body);
				if (!string.IsNullOrEmpty(mark.SpawnCondition)) {
					ImGui.SameLine();
					M3Badge.Draw("Spawn", M3.Scheme.Warning, mark.SpawnCondition);
				}
				if (mark.Rank == MarkRank.S) {
					EorzeaEnvironmentService.SrankWindowStatus? window = EorzeaEnvironmentService.EvaluateSrankWindow(
						mark.NotoriousMonsterId,
						mark.TerritoryType,
						utcNow);
					if (window.HasValue) {
						ImGui.SameLine();
						Vector4 badgeColor = window.Value.IsOpen
							? M3.Scheme.Success
							: window.Value.BadgeText == "Soon"
								? M3.Scheme.Warning
								: M3.Scheme.OnSurfaceVariant;
						M3Badge.Draw(window.Value.BadgeText, badgeColor, window.Value.Tooltip);
					}
				}
				if (mark.TerritoryType == currentTerritory && !isChecked) {
					ImGui.SameLine();
					M3Badge.Draw("Here", M3.Scheme.Tertiary);
				}
				if (this.huntBillService.MobHuntEntriesReady && this.huntBillService.ActiveWeeklyEliteMobIds.Contains(mark.BNpcNameId)) {
					ImGui.SameLine();
					M3Badge.Draw("Bill", M3.Scheme.Secondary);
				}

				ImGui.TableSetColumnIndex(2);
				M3Text.Draw(mark.TerritoryName, M3TextStyle.Muted);

				ImGui.TableSetColumnIndex(3);
				if (M3Widgets.IconButton("##map", FontAwesomeIcon.MapMarkedAlt, $"Open {mark.TerritoryName} map")) {
					MapService.OpenZoneMap(mark.TerritoryType, mark.MapId);
				}

				if (SpawnPointDatabase.TryGetSpawnPoints(mark.TerritoryType, out _)) {
					ImGui.SameLine();
					if (M3Widgets.IconButton("##spawn", FontAwesomeIcon.Compass, "Cycle known zone spawn points on the map")) {
						MapService.CycleZoneSpawnMarker(mark.TerritoryType, mark.MapId, mark.Name, mark.TerritoryName, MapService.OpenType.MarkerOpen);
					}
				}

				ImGui.SameLine();
				if (M3Widgets.IconButton("##share", FontAwesomeIcon.ShareAlt, "Echo zone callout to chat\nHold [SHIFT] to copy callout")) {
					if (ImGui.IsKeyDown(ImGuiKey.ModShift)) {
						MapService.CopyCalloutToClipboard(mark.TerritoryType, mark.MapId, $"[{mark.Rank}] {mark.Name}", mark.TerritoryName);
					}
					else {
						MapService.EchoMapLink(mark.TerritoryType, mark.MapId, $"[{mark.Rank}] {mark.Name}", null, mark.TerritoryName);
					}
				}

				if (canTeleport) {
					ImGui.SameLine();
					if (M3Widgets.IconButton("##tp", FontAwesomeIcon.StreetView, $"Teleport to {mark.TerritoryName}")) {
						MapService.TeleportToTerritory(mark.TerritoryType, mark.MapId);
					}
				}

				if (canSearchEsp) {
					ImGui.SameLine();
					if (M3Widgets.IconButton("##esp", FontAwesomeIcon.Search, $"Set XivEsp search to {mark.Name}")) {
						Plugin.EspConsumer?.SearchFor(mark.Name);
					}
				}
			}

			ImGui.EndTable();
		}
	}

	private void DrawMilestoneExpansionChips() {
		string[] expansions = [
			string.Empty,
			"A Realm Reborn",
			"Heavensward",
			"Stormblood",
			"Shadowbringers",
			"Endwalker",
			"Dawntrail",
		];

		for (int i = 0; i < expansions.Length; i++) {
			string exp = expansions[i];
			if (i > 0) {
				ImGui.SameLine();
			}

			string label = string.IsNullOrEmpty(exp) ? "All Expansions" : exp;
			if (M3Widgets.Chip($"##ms_exp_{i}", label, string.Equals(this.milestoneExpansionFilter, exp, StringComparison.Ordinal))) {
				this.milestoneExpansionFilter = exp;
			}
		}
	}

	private void DrawMilestonesPage() {
		if (!Svc.PlayerState.IsLoaded || Svc.PlayerState.ContentId == 0) {
			M3Widgets.EmptyState(
				"##milestones_not_logged_in",
				"No character loaded",
				"Log in with a character to track hunt kill milestones.",
				FontAwesomeIcon.UserSlash);
			return;
		}

		this.DrawMilestonesList(string.Empty, true);
	}

	private bool DrawMilestonesList(string query, bool showEmptyState, string? sectionHeader = null) {
		bool hideCompleted = Plugin.Instance.Configuration.HideCompletedMarkAchievements;
		string rankText = this.rankFilter switch {
			MarkRank.B => "Rank B",
			MarkRank.A => "Rank A",
			MarkRank.S => "Rank S",
			_ => string.Empty,
		};

		this.scratchMilestones.Clear();
		IReadOnlyList<HuntMilestoneInfo> all = this.markService.Milestones;
		for (int i = 0; i < all.Count; i++) {
			HuntMilestoneInfo ms = all[i];
			if (hideCompleted && ms.IsComplete) {
				continue;
			}

			if (!string.IsNullOrEmpty(rankText) && !string.Equals(ms.RankLabel, rankText, StringComparison.Ordinal)) {
				continue;
			}

			if (string.IsNullOrEmpty(query)
				&& !string.IsNullOrEmpty(this.milestoneExpansionFilter)
				&& !string.Equals(ms.ExpansionLabel, this.milestoneExpansionFilter, StringComparison.Ordinal)) {
				continue;
			}

			if (!string.IsNullOrEmpty(query)
				&& !ms.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
				&& !ms.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
				&& !ms.ExpansionLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
				&& !ms.RankLabel.Contains(query, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			this.scratchMilestones.Add(ms);
		}

		if (this.scratchMilestones.Count == 0) {
			if (showEmptyState) {
				M3Widgets.EmptyState(
					"##milestones_empty",
					"No matching hunt milestones",
					"Try adjusting your rank or expansion filter, or toggle completed achievements.",
					FontAwesomeIcon.Medal);
			}
			return false;
		}

		if (sectionHeader != null) {
			ImGui.Spacing();
			M3Widgets.SectionLabel(sectionHeader, M3.Scheme.Primary);
		}

		using (M3Card.Begin("##milestones_card", "Hunt Kill Milestones", FontAwesomeIcon.Medal, M3.Scheme.Primary, subtitle: $"{this.scratchMilestones.Count} shown")) {
			using M3Table.Scope tableScope = M3Table.PushStyle();
			if (ImGui.BeginTable("##milestones_table", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) {
				ImGui.TableSetupColumn("Achievement", ImGuiTableColumnFlags.WidthStretch, 1.35f);
				ImGui.TableSetupColumn("Scope", ImGuiTableColumnFlags.WidthStretch, 1.05f);
				ImGui.TableSetupColumn("Requirement", ImGuiTableColumnFlags.WidthStretch, 1.4f);
				ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("00000 / 00000").X);
				ImGui.TableHeadersRow();

				for (int i = 0; i < this.scratchMilestones.Count; i++) {
					HuntMilestoneInfo ms = this.scratchMilestones[i];
					using ImRaii.IdDisposable id = ImRaii.PushId((int)ms.AchievementId);
					ImGui.TableNextRow();

					ImGui.TableSetColumnIndex(0);
					M3Text.Draw(ms.Name, ms.IsComplete ? M3TextStyle.Muted : M3TextStyle.Body);

					ImGui.TableSetColumnIndex(1);
					M3Badge.Draw(ms.ExpansionLabel, M3.Scheme.Primary);
					if (!string.Equals(ms.RankLabel, "General", StringComparison.Ordinal)) {
						ImGui.SameLine();
						M3Badge.Draw(ms.RankLabel, ms.RankLabel == "Rank S" ? M3.Scheme.Warning : M3.Scheme.Secondary);
					}

					ImGui.TableSetColumnIndex(2);
					M3Text.Draw(ms.Description, M3TextStyle.Muted, true);

					ImGui.TableSetColumnIndex(3);
					if (ms.IsComplete) {
						M3Badge.Draw("Complete", M3.Scheme.Success);
					}
					else if (ms.ProgressLoaded) {
						uint max = ms.ProgressMax > 0 ? ms.ProgressMax : ms.RequiredCount;
						M3Text.Draw($"{ms.ProgressCurrent} / {max}", M3TextStyle.Body);
					}
					else {
						M3Text.Draw($"? / {ms.RequiredCount}", M3TextStyle.Muted);
					}
				}

				ImGui.EndTable();
			}
		}

		return true;
	}

	private string? GetMilestonesBadge() {
		if (!Svc.PlayerState.IsLoaded || Svc.PlayerState.ContentId == 0) {
			return null;
		}

		IReadOnlyList<HuntMilestoneInfo> all = this.markService.Milestones;
		if (all.Count == 0) {
			return null;
		}

		int completed = 0;
		for (int i = 0; i < all.Count; i++) {
			if (all[i].IsComplete) {
				completed++;
			}
		}

		return $"{completed}/{all.Count}";
	}

	private string? GetCompletionBadge(uint? expansionId) {
		if (!Svc.PlayerState.IsLoaded || Svc.PlayerState.ContentId == 0) {
			return null;
		}

		int total = 0;
		int completed = 0;
		IReadOnlyList<MarkAchievementInfo> achievements = this.markService.Achievements;
		for (int i = 0; i < achievements.Count; i++) {
			MarkAchievementInfo ach = achievements[i];
			if (expansionId.HasValue && ach.ExpansionId != expansionId.Value) {
				continue;
			}

			total++;
			if (ach.IsComplete) {
				completed++;
			}
		}

		return total > 0 ? $"{completed}/{total}" : null;
	}

	private string? GetNeededMarksBadge() {
		if (!Svc.PlayerState.IsLoaded || Svc.PlayerState.ContentId == 0) {
			return null;
		}

		int needed = 0;
		IReadOnlyList<MarkAchievementInfo> achievements = this.markService.Achievements;
		for (int i = 0; i < achievements.Count; i++) {
			MarkAchievementInfo ach = achievements[i];
			if (ach.IsComplete) {
				continue;
			}

			for (int j = 0; j < ach.Marks.Count; j++) {
				if (!this.markService.IsMarkChecked(ach.Marks[j])) {
					needed++;
				}
			}
		}

		return needed > 0 ? M3Widgets.BadgeCount(needed) : null;
	}

	private string? GetCurrentZoneMarksBadge() {
		if (!Svc.PlayerState.IsLoaded || Svc.PlayerState.ContentId == 0) {
			return null;
		}

		uint currentTerritory = Svc.ClientState.TerritoryType;
		int needed = 0;
		IReadOnlyList<MarkAchievementInfo> achievements = this.markService.Achievements;
		for (int i = 0; i < achievements.Count; i++) {
			MarkAchievementInfo ach = achievements[i];
			if (ach.IsComplete || !ach.HasTerritory(currentTerritory)) {
				continue;
			}

			for (int j = 0; j < ach.Marks.Count; j++) {
				EliteMarkInfo mark = ach.Marks[j];
				if (mark.TerritoryType == currentTerritory && !this.markService.IsMarkChecked(mark)) {
					needed++;
				}
			}
		}

		return needed > 0 ? M3Widgets.BadgeCount(needed) : null;
	}

	private string GetTerritoryName(uint territoryType) {
		if (this.cachedTerritoryId != territoryType || this.cachedTerritoryName.Length == 0) {
			TerritoryType? tt = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryType);
			string name = tt?.PlaceName.Value.Name.ExtractText() ?? string.Empty;
			this.cachedTerritoryName = string.IsNullOrEmpty(name) ? $"Zone {territoryType}" : name;
			this.cachedTerritoryId = territoryType;
		}

		return this.cachedTerritoryName;
	}

	private static IDalamudTextureWrap? LoadAchievementIcon(uint iconId) {
		if (iconId == 0) {
			return null;
		}

		return Svc.Texture.GetFromGameIcon(new GameIconLookup {
			IconId = iconId,
			HiRes = false,
		}).GetWrapOrDefault();
	}
}
