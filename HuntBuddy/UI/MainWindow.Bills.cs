using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using HuntBuddy.Data;
using HuntBuddy.Models;
using HuntBuddy.Services;
using HuntBuddy.Utils;
using RebornMaterial;

namespace HuntBuddy.UI;

public partial class MainWindow {
	private void DrawAllBillsPage() {
		if (!this.huntBillService.MobHuntEntriesReady) {
			M3Widgets.EmptyState(
				"##bills_reloading",
				"Reloading data...",
				"Reading active hunt bills from the game.",
				FontAwesomeIcon.SyncAlt);
			return;
		}

		DrawBillsNoticeBanner();
		this.DrawResetAndRewardsCard();

		if (this.huntBillService.MobHuntEntries.Count == 0) {
			M3Widgets.EmptyState(
				"##bills_none",
				"No active hunt bills",
				"Pick up daily or weekly hunt bills from a hunt board to track them here.",
				FontAwesomeIcon.Scroll);
			return;
		}

		foreach (KeyValuePair<string, List<HuntZone>> expansion in this.huntBillService.MobHuntEntries) {
			this.DrawExpansionBillsSection(expansion.Key, expansion.Value);
		}
	}

	private void DrawLocalBillsPage() {
		if (!this.huntBillService.MobHuntEntriesReady) {
			M3Widgets.EmptyState(
				"##local_bills_reloading",
				"Reloading data...",
				"Reading active hunt bills from the game.",
				FontAwesomeIcon.SyncAlt);
			return;
		}

		DrawBillsNoticeBanner();

		List<MobHuntEntry> entries = this.huntBillService.CurrentAreaMobHuntEntries;
		if (entries.Count == 0) {
			if (M3Widgets.EmptyState(
				"##local_bills_empty",
				"No hunt bills in this zone",
				"Travel to a zone with active hunt bill targets or view All Hunt Bills.",
				FontAwesomeIcon.MapMarkerAlt,
				"View All Bills")) {
				this.ShowPage(BillsPageId);
			}
			return;
		}

		int completed = CountCompletedEntries(entries);

		using (M3Card.Begin(
			"##local_bills_card",
			"Targets in Current Zone",
			FontAwesomeIcon.Crosshairs,
			completed == entries.Count ? M3.Scheme.Success : M3.Scheme.Primary,
			subtitle: $"{completed}/{entries.Count} completed")) {
			this.DrawBillEntriesTable("##local_bills_table", entries);
		}
	}

	private void DrawRoutePage() {
		if (!this.huntBillService.MobHuntEntriesReady) {
			M3Widgets.EmptyState(
				"##route_reloading",
				"Reloading data...",
				"Reading active hunt bills from the game.",
				FontAwesomeIcon.SyncAlt);
			return;
		}

		this.DrawResetAndRewardsCard();

		DateTime now = DateTime.UtcNow;
		if (this.cachedRouteRevision != this.huntBillService.RouteRevision
			|| (now - this.lastRouteRefreshUtc).TotalSeconds >= 2d) {
			this.cachedRoute.Clear();
			this.cachedRoute.AddRange(this.huntBillService.BuildOptimalRoute());
			this.cachedRouteRevision = this.huntBillService.RouteRevision;
			this.lastRouteRefreshUtc = now;
		}

		if (this.cachedRoute.Count == 0) {
			M3Widgets.EmptyState(
				"##route_empty",
				"No incomplete hunt bills",
				"Pick up daily or weekly hunt bills from a hunt board to generate a route.",
				FontAwesomeIcon.Route);
			return;
		}

		Configuration cfg = Plugin.Instance.Configuration;
		bool canTeleport = Plugin.TeleportConsumer?.IsAvailable == true;
		bool canSearchEsp = cfg.EnableXivEspIntegration && Plugin.EspConsumer?.IsAvailable == true;

		using (M3Card.Begin(
			"##route_card",
			"Optimal Hunt Route",
			FontAwesomeIcon.Route,
			M3.Scheme.Primary,
			subtitle: $"{this.cachedRoute.Count} incomplete targets ordered by current zone and nearest coordinates")) {
			if (M3Widgets.Button("##route_flag_next", "Flag Next Target", M3ButtonStyle.Filled, FontAwesomeIcon.Crosshairs)) {
				this.huntBillService.FlagNextTarget();
			}

			if (canTeleport) {
				ImGui.SameLine();
				if (M3Widgets.Button("##route_flag_tp", "Flag & Teleport Next", M3ButtonStyle.Tonal, FontAwesomeIcon.StreetView)) {
					this.huntBillService.FlagNextTarget(teleport: true);
				}
			}

			ImGui.Spacing();

			int maxButtons = 0;
			for (int i = 0; i < this.cachedRoute.Count; i++) {
				HuntBillService.HuntRouteStep step = this.cachedRoute[i];
				bool hasSpawnPoints = !step.HasLocation && SpawnPointDatabase.TryGetSpawnPoints(step.Entry.TerritoryType, out _);
				int rowButtons = (step.HasLocation ? 2 : (hasSpawnPoints ? 2 : 1)) + 1 + (canTeleport ? 1 : 0) + (canSearchEsp ? 1 : 0);
				if (rowButtons > maxButtons) {
					maxButtons = rowButtons;
				}
			}

			using M3Table.Scope tableScope = M3Table.PushStyle();
			if (!ImGui.BeginTable("##route_table", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) {
				return;
			}

			ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("000").X);
			ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch, 1.3f);
			ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 1.0f);
			ImGui.TableSetupColumn("Aetheryte", ImGuiTableColumnFlags.WidthStretch, 0.9f);
			ImGui.TableSetupColumn("Kills", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("000/000").X);
			ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, ActionsColumnWidth(Math.Max(1, maxButtons)));
			ImGui.TableHeadersRow();

			for (int i = 0; i < this.cachedRoute.Count; i++) {
				HuntBillService.HuntRouteStep step = this.cachedRoute[i];
				MobHuntEntry entry = step.Entry;
				using ImRaii.IdDisposable id = ImRaii.PushId((int)entry.MobHuntId);
				ImGui.TableNextRow();

				ImGui.TableSetColumnIndex(0);
				M3Text.Draw($"{i + 1}", step.IsCurrentZone ? M3TextStyle.Body : M3TextStyle.Muted);

				ImGui.TableSetColumnIndex(1);
				MobHuntProgress progress = entry.GetProgress();
				M3Text.Draw(entry.Name, M3TextStyle.Body);
				if (ImGui.IsItemHovered()) {
					using (ImRaii.PushColor(ImGuiCol.PopupBg, Vector4.Zero))
					using (ImRaii.Tooltip()) {
						InterfaceUtil.DrawHuntIcon(entry);
					}
				}

				if (this.markService.TryGetNeededAchievement(entry.MobHuntId, out MarkAchievementInfo? achievement)) {
					ImGui.SameLine();
					M3Badge.Draw("Achievement", M3.Scheme.Warning, $"Needed for {achievement.Name}");
				}

				ImGui.TableSetColumnIndex(2);
				M3Text.Draw(entry.TerritoryName, step.IsCurrentZone ? M3TextStyle.Body : M3TextStyle.Muted);
				if (step.IsCurrentZone) {
					ImGui.SameLine();
					M3Badge.Draw("Here", M3.Scheme.Tertiary);
				}

				ImGui.TableSetColumnIndex(3);
				M3Text.Draw(string.IsNullOrEmpty(step.NearestAetheryteName) ? "-" : step.NearestAetheryteName, M3TextStyle.Muted);

				ImGui.TableSetColumnIndex(4);
				M3Text.Draw($"{progress.Kills}/{progress.NeededKills}", M3TextStyle.Body);

				ImGui.TableSetColumnIndex(5);
				DrawBillRowActions(entry, step.HasLocation, canTeleport, canSearchEsp, cfg);
			}

			ImGui.EndTable();
		}
	}

	private void DrawResetAndRewardsCard() {
		DateTime now = DateTime.UtcNow;
		if (this.cachedRewardsRevision != this.huntBillService.RouteRevision
			|| (now - this.lastRewardsRefreshUtc).TotalSeconds >= 1d) {
			this.cachedRewards = this.huntBillService.GetRemainingRewardsSummary();
			this.cachedRewardsRevision = this.huntBillService.RouteRevision;
			this.lastRewardsRefreshUtc = now;
		}

		TimeSpan daily = HuntBillService.GetTimeUntilDailyReset(now);
		TimeSpan weekly = HuntBillService.GetTimeUntilWeeklyReset(now);
		HuntBillService.HuntRewardsSummary rewards = this.cachedRewards;

		using (M3Card.Begin("##bills_summary", null, style: M3CardStyle.Outlined)) {
			_ = M3Widgets.Pill(
				"##reset_daily",
				$"Daily reset: {(int)daily.TotalHours}h {daily.Minutes:D2}m",
				M3.Scheme.Secondary,
				FontAwesomeIcon.Clock,
				"Daily hunt bills reset at 15:00 UTC",
				false);
			ImGui.SameLine(0f, M3.Space1);
			_ = M3Widgets.Pill(
				"##reset_weekly",
				$"Weekly reset: {weekly.Days}d {weekly.Hours}h {weekly.Minutes:D2}m",
				M3.Scheme.Tertiary,
				FontAwesomeIcon.CalendarAlt,
				"Weekly elite hunt bills reset Tuesday at 08:00 UTC",
				false);

			bool hasRewards = rewards.AlliedSeals > 0
				|| rewards.CenturioSeals > 0
				|| rewards.SacksOfNuts > 0
				|| rewards.Gil > 0
				|| rewards.Exp > 0;
			if (hasRewards) {
				bool firstReward = true;
				if (rewards.AlliedSeals > 0) {
					_ = M3Widgets.Pill("##reward_allied", $"{rewards.AlliedSeals:N0} Allied", M3.Scheme.Primary, FontAwesomeIcon.Coins, "Remaining Allied Seals from active bills", false);
					firstReward = false;
				}

				if (rewards.CenturioSeals > 0) {
					if (!firstReward) {
						ImGui.SameLine(0f, M3.Space1);
					}
					_ = M3Widgets.Pill("##reward_centurio", $"{rewards.CenturioSeals:N0} Centurio", M3.Scheme.Primary, FontAwesomeIcon.Coins, "Remaining Centurio Seals from active bills", false);
					firstReward = false;
				}

				if (rewards.SacksOfNuts > 0) {
					if (!firstReward) {
						ImGui.SameLine(0f, M3.Space1);
					}
					_ = M3Widgets.Pill("##reward_nuts", $"{rewards.SacksOfNuts:N0} Nuts", M3.Scheme.Primary, FontAwesomeIcon.Coins, "Remaining Sacks of Nuts from active bills", false);
					firstReward = false;
				}

				if (rewards.Gil > 0) {
					if (!firstReward) {
						ImGui.SameLine(0f, M3.Space1);
					}
					_ = M3Widgets.Pill("##reward_gil", $"{rewards.Gil:N0} Gil", M3.Scheme.Success, FontAwesomeIcon.MoneyBillWave, "Remaining Gil from active bills", false);
					firstReward = false;
				}

				if (rewards.Exp > 0) {
					if (!firstReward) {
						ImGui.SameLine(0f, M3.Space1);
					}
					_ = M3Widgets.Pill("##reward_exp", $"{rewards.Exp:N0} EXP", M3.Scheme.Success, FontAwesomeIcon.Star, "Remaining EXP from active bills", false);
				}
			}
		}

		ImGui.Spacing();
	}

	private static void DrawBillsNoticeBanner() {
		M3Widgets.Banner(
			"##bills_brank_arr_notice",
			"B-rank and ARR hunt mark coordinates are not supported because B-ranks roam across random spawn points and several ARR targets are FATEs.",
			M3Severity.Info,
			FontAwesomeIcon.InfoCircle);
		ImGui.Spacing();
	}

	private void DrawExpansionBillsSection(string expansionName, List<HuntZone> zones) {
		M3Widgets.SectionLabel(expansionName, M3.Scheme.Primary);

		uint currentTerritory = Svc.ClientState.TerritoryType;

		foreach (HuntZone zone in zones) {
			List<MobHuntEntry> entries = zone.Entries;
			int killedCount = CountCompletedEntries(entries);
			bool complete = killedCount == entries.Count;
			bool isCurrentZone = zone.TerritoryType == currentTerritory;
			bool expanded = this.expandedBillZoneStates.GetValueOrDefault(zone.TerritoryType, !complete);
			bool wasExpanded = expanded;

			Vector4 accent = complete
				? M3.Scheme.Success
				: isCurrentZone
					? M3.Scheme.Tertiary
					: M3.Scheme.Primary;

			using ImRaii.IdDisposable id = ImRaii.PushId((int)zone.TerritoryType);
			using M3ExpandableCard.Scope card = M3ExpandableCard.Begin(
				"##bill_zone",
				zone.Name,
				ref expanded,
				FontAwesomeIcon.MapMarkedAlt,
				accent,
				$"{killedCount}/{entries.Count}",
				complete ? M3.Scheme.Success : M3.Scheme.Primary,
				isCurrentZone ? "Current zone" : null);
			if (expanded != wasExpanded) {
				this.expandedBillZoneStates[zone.TerritoryType] = expanded;
			}

			if (card.Expanded) {
				this.DrawBillEntriesTable("##bill_table", entries);
			}
		}
	}

	private bool DrawBillsSearchResults(string query) {
		if (!this.huntBillService.MobHuntEntriesReady) {
			return false;
		}

		this.scratchBillEntries.Clear();
		foreach (KeyValuePair<string, List<HuntZone>> expansion in this.huntBillService.MobHuntEntries) {
			bool expansionMatches = expansion.Key.Contains(query, StringComparison.OrdinalIgnoreCase);
			foreach (HuntZone zone in expansion.Value) {
				if (expansionMatches || zone.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) {
					this.scratchBillEntries.AddRange(zone.Entries);
					continue;
				}

				foreach (MobHuntEntry entry in zone.Entries) {
					if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) {
						this.scratchBillEntries.Add(entry);
					}
				}
			}
		}

		if (this.scratchBillEntries.Count == 0) {
			return false;
		}

		M3Widgets.SectionLabel("Active Hunt Bills", M3.Scheme.Primary);
		using (M3Card.Begin("##bills_search_card", "Matching Hunt Targets", FontAwesomeIcon.Scroll, M3.Scheme.Primary)) {
			this.DrawBillEntriesTable("##bills_search_table", this.scratchBillEntries, showZoneColumn: true);
		}

		return true;
	}

	private static int CountCompletedEntries(List<MobHuntEntry> entries) {
		int count = 0;
		for (int i = 0; i < entries.Count; i++) {
			if (entries[i].GetProgress().IsComplete) {
				count++;
			}
		}

		return count;
	}

	private void DrawBillEntriesTable(string tableId, List<MobHuntEntry> entries, bool showZoneColumn = false) {
		Configuration cfg = Plugin.Instance.Configuration;
		bool canTeleport = Plugin.TeleportConsumer?.IsAvailable == true;
		bool canSearchEsp = cfg.EnableXivEspIntegration && Plugin.EspConsumer?.IsAvailable == true;
		int maxRowButtons = 0;
		for (int i = 0; i < entries.Count; i++) {
			MobHuntEntry entry = entries[i];
			bool rowHasLocation = LocationDatabase.Database.ContainsKey(entry.MobHuntId);
			bool rowHasSpawnPoints = !rowHasLocation && SpawnPointDatabase.TryGetSpawnPoints(entry.TerritoryType, out _);
			int rowButtons = (rowHasLocation ? 2 : (rowHasSpawnPoints ? 2 : 1)) + 1 + (canTeleport ? 1 : 0) + (canSearchEsp ? 1 : 0);
			if (rowButtons > maxRowButtons) {
				maxRowButtons = rowButtons;
			}
		}

		using M3Table.Scope tableScope = M3Table.PushStyle();
		if (!ImGui.BeginTable(tableId, showZoneColumn ? 4 : 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) {
			return;
		}

		ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch, 1.5f);
		if (showZoneColumn) {
			ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 1.0f);
		}
		ImGui.TableSetupColumn("Kills", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("000/000").X);
		ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, ActionsColumnWidth(Math.Max(1, maxRowButtons)));
		ImGui.TableHeadersRow();

		for (int i = 0; i < entries.Count; i++) {
			MobHuntEntry entry = entries[i];
			using ImRaii.IdDisposable id = ImRaii.PushId((int)entry.MobHuntId);
			ImGui.TableNextRow();
			int col = 0;

			ImGui.TableSetColumnIndex(col++);
			MobHuntProgress progress = entry.GetProgress();
			M3Text.Draw(entry.Name, progress.IsComplete ? M3TextStyle.Muted : M3TextStyle.Body);
			if (ImGui.IsItemHovered()) {
				using (ImRaii.PushColor(ImGuiCol.PopupBg, Vector4.Zero))
				using (ImRaii.Tooltip()) {
					InterfaceUtil.DrawHuntIcon(entry);
				}
			}

			if (this.markService.TryGetNeededAchievement(entry.MobHuntId, out MarkAchievementInfo? achievement)) {
				ImGui.SameLine();
				M3Badge.Draw("Achievement", M3.Scheme.Warning, $"Needed for {achievement.Name}");
			}

			if (showZoneColumn) {
				ImGui.TableSetColumnIndex(col++);
				M3Text.Draw(entry.TerritoryName, M3TextStyle.Muted);
			}

			ImGui.TableSetColumnIndex(col++);
			M3Text.Draw($"{progress.Kills}/{progress.NeededKills}", progress.IsComplete ? M3TextStyle.Success : M3TextStyle.Body);

			ImGui.TableSetColumnIndex(col);
			bool hasLocation = LocationDatabase.Database.ContainsKey(entry.MobHuntId);
			DrawBillRowActions(entry, hasLocation, canTeleport, canSearchEsp, cfg);
		}

		ImGui.EndTable();
	}

	private static void DrawBillRowActions(
		MobHuntEntry entry,
		bool hasLocation,
		bool canTeleport,
		bool canSearchEsp,
		Configuration cfg) {
		if (hasLocation) {
			if (M3Widgets.IconButton("##pin", FontAwesomeIcon.MapMarkerAlt, "Place marker on the map")) {
				MapService.CreateMapMarker(
					entry.TerritoryType,
					entry.MapId,
					entry.MobHuntId,
					entry.Name,
					MapService.OpenType.None);
			}

			ImGui.SameLine();

			string openTooltip = cfg.IncludeAreaOnMap
				? "Show hunt area on the map\nHold [SHIFT] to show location only"
				: "Show hunt location on the map\nHold [SHIFT] to include area";

			if (M3Widgets.IconButton("##open", FontAwesomeIcon.MapMarkedAlt, openTooltip)) {
				bool includeArea = cfg.IncludeAreaOnMap != ImGui.IsKeyDown(ImGuiKey.ModShift);
				MapService.CreateMapMarker(
					entry.TerritoryType,
					entry.MapId,
					entry.MobHuntId,
					entry.Name,
					includeArea ? MapService.OpenType.ShowOpen : MapService.OpenType.MarkerOpen);
			}
		}
		else {
			if (M3Widgets.IconButton("##map_zone", FontAwesomeIcon.Map, $"Open {entry.TerritoryName} map")) {
				MapService.OpenZoneMap(entry.TerritoryType, entry.MapId);
			}

			if (SpawnPointDatabase.TryGetSpawnPoints(entry.TerritoryType, out _)) {
				ImGui.SameLine();
				if (M3Widgets.IconButton("##spawn", FontAwesomeIcon.Compass, "Cycle known zone spawn points on the map")) {
					MapService.CycleZoneSpawnMarker(entry.TerritoryType, entry.MapId, entry.Name, entry.TerritoryName, MapService.OpenType.MarkerOpen);
				}
			}
		}

		ImGui.SameLine();
		if (M3Widgets.IconButton("##share", FontAwesomeIcon.ShareAlt, "Echo clickable map link to chat\nHold [SHIFT] to copy <flag> callout")) {
			Vector2? coord = hasLocation ? LocationDatabase.Database[entry.MobHuntId].Coordinate : null;
			if (ImGui.IsKeyDown(ImGuiKey.ModShift)) {
				MapService.CopyCalloutToClipboard(entry.TerritoryType, entry.MapId, entry.Name, entry.TerritoryName, coord);
			}
			else {
				MapService.EchoMapLink(entry.TerritoryType, entry.MapId, entry.Name, coord, entry.TerritoryName);
			}
		}

		if (canTeleport) {
			ImGui.SameLine();
			string teleportTooltip = hasLocation ? "Teleport to nearest aetheryte" : $"Teleport to {entry.TerritoryName}";
			if (M3Widgets.IconButton("##tp", FontAwesomeIcon.StreetView, teleportTooltip)) {
				if (hasLocation) {
					MapService.TeleportToNearestAetheryte(entry.TerritoryType, entry.MapId, entry.MobHuntId);
				}
				else {
					MapService.TeleportToTerritory(entry.TerritoryType, entry.MapId);
				}
			}
		}

		if (canSearchEsp) {
			ImGui.SameLine();
			if (M3Widgets.IconButton("##esp", FontAwesomeIcon.Search, "Set XivEsp search to this target")) {
				Plugin.EspConsumer?.SearchFor(entry.Name);
			}
		}
	}
}
