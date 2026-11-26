using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using ECommons.DalamudServices;

using HuntBuddy.Data;
using HuntBuddy.Models;
using HuntBuddy.Services;
using HuntBuddy.Utils;

using RebornMaterial;

namespace HuntBuddy.UI.Overlays;

public class LocalHuntsWindow: M3Window {
	private readonly HuntBillService huntBillService;
	private readonly MarkAchievementService markService;

	public LocalHuntsWindow(HuntBillService huntBillService, MarkAchievementService markService) : base(
		"Hunts in current area",
		ImGuiWindowFlags.NoNavInputs | ImGuiWindowFlags.NoDocking,
		true) {
		this.huntBillService = huntBillService;
		this.markService = markService;
		this.Size = Vector2.Zero;
		this.SizeCondition = ImGuiCond.Always;

		this.IsOpen = true;
		this.ShowCloseButton = false;
		this.RespectCloseHotkey = false;
	}

	public override void PreOpenCheck() {
		if (Plugin.Instance.Configuration.HideLocalHuntBackground) {
			this.Flags |= ImGuiWindowFlags.NoBackground;
		}
		else {
			this.Flags &= ~ImGuiWindowFlags.NoBackground;
		}

		if (Plugin.Instance.Configuration.LockWindowPositions) {
			this.Flags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
		}
		else {
			this.Flags &= ~(ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);
		}
	}

	public override bool DrawConditions() {
		if (!Plugin.Instance.Configuration.ShowLocalHunts
			|| !Svc.PlayerState.IsLoaded
			|| !this.huntBillService.MobHuntEntriesReady
			|| Svc.Condition[ConditionFlag.WatchingCutscene]
			|| Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]) {
			return false;
		}

		List<MobHuntEntry> entries = this.huntBillService.CurrentAreaMobHuntEntries;
		for (int i = 0; i < entries.Count; i++) {
			if (!entries[i].GetProgress().IsComplete) {
				return true;
			}
		}

		return false;
	}

	public override void Draw() {
		Configuration cfg = Plugin.Instance.Configuration;
		bool canSearchEsp = cfg.EnableXivEspIntegration && Plugin.EspConsumer?.IsAvailable == true;

		List<MobHuntEntry> entries = this.huntBillService.CurrentAreaMobHuntEntries;
		for (int i = 0; i < entries.Count; i++) {
			MobHuntEntry entry = entries[i];
			MobHuntProgress progress = entry.GetProgress();
			if (cfg.HideCompletedHunts && progress.IsComplete) {
				continue;
			}

			using ImRaii.IdDisposable id = ImRaii.PushId((int)entry.MobHuntId);
			bool hasLocation = LocationDatabase.Database.ContainsKey(entry.MobHuntId);
			bool hasSpawnPoints = !hasLocation && SpawnPointDatabase.TryGetSpawnPoints(entry.TerritoryType, out _);
			bool hasButtons = hasLocation || hasSpawnPoints || canSearchEsp;

			if (hasLocation) {
				if (M3Widgets.IconButton("##local_pin", FontAwesomeIcon.MapMarkerAlt, "Place marker on the map")) {
					MapService.CreateMapMarker(
						entry.TerritoryType,
						entry.MapId,
						entry.MobHuntId,
						entry.Name,
						MapService.OpenType.None);
				}

				ImGui.SameLine();

				string openTooltip = cfg.IncludeAreaOnMap
					? "Show hunt area on the map\nHold [SHIFT] to show the location only"
					: "Show hunt location on the map\nHold [SHIFT] to include the area";

				if (M3Widgets.IconButton("##local_open", FontAwesomeIcon.MapMarkedAlt, openTooltip)) {
					bool includeArea = cfg.IncludeAreaOnMap != ImGui.IsKeyDown(ImGuiKey.ModShift);
					MapService.CreateMapMarker(
						entry.TerritoryType,
						entry.MapId,
						entry.MobHuntId,
						entry.Name,
						includeArea ? MapService.OpenType.ShowOpen : MapService.OpenType.MarkerOpen);
				}
			}
			else if (hasSpawnPoints) {
				if (M3Widgets.IconButton("##local_spawn", FontAwesomeIcon.Compass, "Cycle known zone spawn points on the map")) {
					MapService.CycleZoneSpawnMarker(
						entry.TerritoryType,
						entry.MapId,
						entry.Name,
						entry.TerritoryName,
						MapService.OpenType.MarkerOpen);
				}
			}

			if (canSearchEsp) {
				if (hasLocation || hasSpawnPoints) {
					ImGui.SameLine();
				}

				if (M3Widgets.IconButton("##local_esp", FontAwesomeIcon.Search, "Set XivEsp search to this target")) {
					Plugin.EspConsumer?.SearchFor(entry.Name);
				}
			}

			if (hasButtons) {
				ImGui.SameLine();
			}

			M3Text.Draw(
				$"{entry.Name} ({progress.Kills}/{progress.NeededKills})",
				progress.IsComplete ? M3TextStyle.Success : M3TextStyle.Body);

			if (this.markService.TryGetNeededAchievement(entry.MobHuntId, out MarkAchievementInfo? achievement)) {
				ImGui.SameLine();
				M3Badge.Draw("Achievement", M3.Scheme.Warning, $"Needed for {achievement.Name}");
			}

			if (cfg.ShowLocalHuntIcons) {
				InterfaceUtil.DrawHuntIcon(entry);
			}
		}
	}
}
