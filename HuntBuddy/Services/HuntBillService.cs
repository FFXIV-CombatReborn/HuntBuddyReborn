using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using HuntBuddy.Data;
using HuntBuddy.Models;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Map = Lumina.Excel.Sheets.Map;

namespace HuntBuddy.Services;

public sealed class HuntBillService: IDisposable {
	public readonly record struct HuntRewardsSummary(
		uint AlliedSeals,
		uint CenturioSeals,
		uint SacksOfNuts,
		uint Gil,
		uint Exp,
		int RemainingBillTargets);

	public readonly record struct HuntRouteStep(
		MobHuntEntry Entry,
		string ExpansionName,
		uint NearestAetheryteId,
		string NearestAetheryteName,
		bool HasLocation,
		bool IsCurrentZone);

	private static readonly BillEnum[] billNumbers = Enum.GetValues<BillEnum>();

	private readonly HashSet<uint> completedLocalMobIds = [];
	private int lastObtainedFlags;
	private byte[] lastMarkIds = [];
	private uint lastTrackedLocalTerritory;
	private int reloadGeneration;
	private volatile bool disposed;

	public Dictionary<string, List<HuntZone>> MobHuntEntries { get; private set; } = [];
	public List<MobHuntEntry> CurrentAreaMobHuntEntries { get; private set; } = [];
	public HashSet<uint> ActiveWeeklyEliteMobIds { get; private set; } = [];
	public bool MobHuntEntriesReady { get; private set; } = true;
	public int RouteRevision { get; private set; }

	public HuntBillService() {
		Svc.ClientState.TerritoryChanged += this.OnTerritoryChanged;
		Svc.Framework.Update += this.OnFrameworkUpdate;
	}

	public unsafe void RequestReload() {
		MobHunt* mobHunt = MobHunt.Instance();
		if (mobHunt == null) {
			return;
		}

		int obtainedFlags = mobHunt->ObtainedFlags;
		byte[] markIds = mobHunt->ObtainedMarkId.ToArray();
		int generation = ++this.reloadGeneration;

		this.lastObtainedFlags = obtainedFlags;
		this.lastMarkIds = markIds;
		this.MobHuntEntriesReady = false;
		Task.Run(() => this.ReloadData(generation, obtainedFlags, markIds));
	}

	private void ReloadData(int generation, int obtainedFlags, byte[] markIds) {
		Dictionary<string, List<HuntZone>> nextEntries = [];
		HashSet<uint> nextWeeklyEliteIds = [];

		try {
			ReadBills(obtainedFlags, markIds, nextEntries, nextWeeklyEliteIds);
		}
		catch (Exception e) {
			Svc.Log.Error(e, "Failed to read hunt bill data");
		}

		if (this.disposed) {
			return;
		}

		Svc.Framework.RunOnFrameworkThread(() => {
			if (this.disposed || generation != this.reloadGeneration) {
				return;
			}

			this.MobHuntEntries = nextEntries;
			this.ActiveWeeklyEliteMobIds = nextWeeklyEliteIds;
			this.RefreshCurrentArea();
			this.MobHuntEntriesReady = true;
		});
	}

	private static void ReadBills(
		int obtainedFlags,
		byte[] markIds,
		Dictionary<string, List<HuntZone>> entries,
		HashSet<uint> weeklyEliteIds) {
		ExcelSheet<MobHuntOrderType> orderTypeSheet = Svc.Data.GetExcelSheet<MobHuntOrderType>();
		SubrowExcelSheet<MobHuntOrder> orderSheet = Svc.Data.GetSubrowExcelSheet<MobHuntOrder>();
		Dictionary<uint, MobHuntEntry> entriesByMobId = [];

		foreach (BillEnum billNumber in billNumbers) {
			int billIndex = (int)billNumber;
			if (billIndex >= markIds.Length || markIds[billIndex] == 0 || (obtainedFlags & (1 << billIndex)) == 0) {
				continue;
			}

			if (!orderTypeSheet.TryGetRow((uint)billNumber, out MobHuntOrderType orderType)) {
				Svc.Log.Warning($"Missing hunt order type row for bill {billNumber}");
				continue;
			}

			uint rowId = orderType.OrderStart.RowId + markIds[billIndex] - 1;
			if (!orderSheet.TryGetRow(rowId, out SubrowCollection<MobHuntOrder> orders)) {
				Svc.Log.Warning($"Missing hunt order row {rowId} for bill {billNumber}");
				continue;
			}

			for (int i = 0; i < orders.Count; i++) {
				MobHuntOrder order = orders[i];
				if (order.Target.RowId == 0) {
					continue;
				}

				MobHuntTarget target = order.Target.Value;
				uint mobId = target.Name.RowId;
				if (mobId == 0) {
					continue;
				}

				if (!entriesByMobId.TryGetValue(mobId, out MobHuntEntry? entry)) {
					entry = AddEntry(target, orderType.Type == 2, entries);
					entriesByMobId[mobId] = entry;
					if (entry.IsEliteMark && entry.ExpansionId <= 2) {
						weeklyEliteIds.Add(mobId);
					}
				}

				MobHuntReward? reward = order.MobHuntReward.RowId != 0 ? order.MobHuntReward.ValueNullable : null;
				entry.AddBillSlot(new MobHuntBillSlot(
					(byte)billNumber,
					(byte)order.SubrowId,
					order.NeededKills,
					reward?.ExpReward ?? 0,
					reward?.GilReward ?? 0,
					reward?.CurrencyReward ?? 0,
					reward?.Expansion.RowId ?? entry.ExpansionId));
			}
		}
	}

	private static MobHuntEntry AddEntry(MobHuntTarget target, bool isEliteMark, Dictionary<string, List<HuntZone>> entries) {
		Map map = target.Map.Value;
		TerritoryType territory = map.TerritoryType.Value;
		MobHuntEntry entry = new() {
			Name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(target.Name.Value.Singular.ToString()),
			TerritoryName = map.PlaceName.Value.Name.ToString(),
			ExpansionId = territory.ExVersion.RowId,
			MapId = map.RowId,
			TerritoryType = territory.RowId,
			MobHuntId = target.Name.RowId,
			IsEliteMark = isEliteMark,
			Icon = target.Icon,
		};

		string expansionName = territory.ExVersion.Value.Name.ToString();
		if (!entries.TryGetValue(expansionName, out List<HuntZone>? zones)) {
			zones = [];
			entries[expansionName] = zones;
		}

		GetOrAddZone(zones, entry).Entries.Add(entry);
		return entry;
	}

	private static HuntZone GetOrAddZone(List<HuntZone> zones, MobHuntEntry entry) {
		for (int i = 0; i < zones.Count; i++) {
			if (zones[i].TerritoryType == entry.TerritoryType) {
				return zones[i];
			}
		}

		HuntZone zone = new() {
			TerritoryType = entry.TerritoryType,
			Name = entry.TerritoryName,
		};
		zones.Add(zone);
		return zone;
	}

	public static TimeSpan GetTimeUntilDailyReset(DateTime utcNow) {
		DateTime nextReset = utcNow.Date.AddHours(15);
		if (utcNow >= nextReset) {
			nextReset = nextReset.AddDays(1);
		}

		return nextReset - utcNow;
	}

	public static TimeSpan GetTimeUntilWeeklyReset(DateTime utcNow) {
		int daysUntilTuesday = ((int)DayOfWeek.Tuesday - (int)utcNow.DayOfWeek + 7) % 7;
		DateTime nextReset = utcNow.Date.AddDays(daysUntilTuesday).AddHours(8);
		if (utcNow >= nextReset) {
			nextReset = nextReset.AddDays(7);
		}

		return nextReset - utcNow;
	}

	public unsafe HuntRewardsSummary GetRemainingRewardsSummary() {
		if (!this.MobHuntEntriesReady) {
			return default;
		}

		MobHunt* mobHunt = MobHunt.Instance();
		uint allied = 0;
		uint centurio = 0;
		uint nuts = 0;
		uint gil = 0;
		uint exp = 0;
		int remainingTargets = 0;

		foreach (List<HuntZone> zones in this.MobHuntEntries.Values) {
			for (int z = 0; z < zones.Count; z++) {
				List<MobHuntEntry> entries = zones[z].Entries;
				for (int e = 0; e < entries.Count; e++) {
					MobHuntEntry entry = entries[e];
					if (!entry.GetProgress().IsComplete) {
						remainingTargets++;
					}

					IReadOnlyList<MobHuntBillSlot> slots = entry.BillSlots;
					for (int s = 0; s < slots.Count; s++) {
						MobHuntBillSlot slot = slots[s];
						if (mobHunt != null && mobHunt->GetKillCount(slot.BillNumber, slot.MobIndex) >= slot.NeededKills) {
							continue;
						}

						exp += slot.ExpReward;
						gil += slot.GilReward;
						switch (slot.CurrencyExpansionId) {
							case 0:
								allied += slot.CurrencyReward;
								break;
							case 1:
							case 2:
								centurio += slot.CurrencyReward;
								break;
							default:
								nuts += slot.CurrencyReward;
								break;
						}
					}
				}
			}
		}

		return new HuntRewardsSummary(allied, centurio, nuts, gil, exp, remainingTargets);
	}

	public List<HuntRouteStep> BuildOptimalRoute() {
		List<HuntRouteStep> route = [];
		if (!this.MobHuntEntriesReady || this.MobHuntEntries.Count == 0) {
			return route;
		}

		uint currentTerritory = Svc.ClientState.TerritoryType;
		TerritoryType? currentTerritoryRow = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(currentTerritory);
		string currentExpansion = currentTerritoryRow?.ExVersion.Value.Name.ExtractText() ?? string.Empty;
		Map? currentMap = currentTerritoryRow?.Map.ValueNullable;
		Vector2? playerCoord = Svc.Objects.LocalPlayer is IPlayerCharacter player && currentMap != null
			? MapUtil.WorldToMap(new Vector2(player.Position.X, player.Position.Z), currentMap.Value)
			: null;

		List<(HuntZone Zone, string ExpansionName, int Priority, int IncompleteCount)> orderedZones = [];
		foreach (KeyValuePair<string, List<HuntZone>> pair in this.MobHuntEntries) {
			for (int i = 0; i < pair.Value.Count; i++) {
				HuntZone zone = pair.Value[i];
				int incompleteCount = 0;
				for (int j = 0; j < zone.Entries.Count; j++) {
					if (!zone.Entries[j].GetProgress().IsComplete) {
						incompleteCount++;
					}
				}

				if (incompleteCount == 0) {
					continue;
				}

				int priority = zone.TerritoryType == currentTerritory
					? 0
					: string.Equals(pair.Key, currentExpansion, StringComparison.Ordinal) ? 1 : 2;
				orderedZones.Add((zone, pair.Key, priority, incompleteCount));
			}
		}

		orderedZones.Sort(static (a, b) => {
			int p = a.Priority.CompareTo(b.Priority);
			if (p != 0) {
				return p;
			}

			int c = b.IncompleteCount.CompareTo(a.IncompleteCount);
			return c != 0 ? c : string.CompareOrdinal(a.Zone.Name, b.Zone.Name);
		});

		List<MobHuntEntry> located = [];
		List<MobHuntEntry> unlocated = [];

		for (int z = 0; z < orderedZones.Count; z++) {
			(HuntZone zone, string expansionName, _, _) = orderedZones[z];
			bool isCurrentZone = zone.TerritoryType == currentTerritory;
			located.Clear();
			unlocated.Clear();

			for (int i = 0; i < zone.Entries.Count; i++) {
				MobHuntEntry entry = zone.Entries[i];
				if (entry.GetProgress().IsComplete) {
					continue;
				}

				if (!entry.IsEliteMark && LocationDatabase.Database.ContainsKey(entry.MobHuntId)) {
					located.Add(entry);
				}
				else {
					unlocated.Add(entry);
				}
			}

			if (located.Count > 0) {
				Vector2 anchor;
				if (isCurrentZone && playerCoord.HasValue) {
					anchor = playerCoord.Value;
				}
				else {
					located.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
					anchor = LocationDatabase.Database[located[0].MobHuntId].Coordinate;
				}

				while (located.Count > 0) {
					int bestIndex = 0;
					float bestDist = float.MaxValue;
					for (int i = 0; i < located.Count; i++) {
						Vector2 coord = LocationDatabase.Database[located[i].MobHuntId].Coordinate;
						float dist = Vector2.DistanceSquared(anchor, coord);
						if (dist < bestDist) {
							bestDist = dist;
							bestIndex = i;
						}
					}

					MobHuntEntry next = located[bestIndex];
					located.RemoveAt(bestIndex);
					anchor = LocationDatabase.Database[next.MobHuntId].Coordinate;
					(uint aetheryteId, string aetheryteName) = MapService.GetNearestAetheryteInfo(next.TerritoryType, next.MapId, next.MobHuntId);
					route.Add(new HuntRouteStep(next, expansionName, aetheryteId, aetheryteName, true, isCurrentZone));
				}
			}

			unlocated.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
			for (int i = 0; i < unlocated.Count; i++) {
				MobHuntEntry next = unlocated[i];
				(uint aetheryteId, string aetheryteName) = MapService.GetNearestAetheryteInfo(next.TerritoryType, next.MapId, 0);
				route.Add(new HuntRouteStep(next, expansionName, aetheryteId, aetheryteName, false, isCurrentZone));
			}
		}

		return route;
	}

	public void FlagNextTarget(bool teleport = false) {
		if (!this.MobHuntEntriesReady || Svc.Objects.LocalPlayer is not IPlayerCharacter player) {
			return;
		}

		MapService.OpenType openType = MapService.OpenType.None;
		TerritoryType? currentTerritoryRow = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(Svc.ClientState.TerritoryType);
		Map? map = currentTerritoryRow?.Map.ValueNullable;
		Vector2 playerLocation = map != null
			? MapUtil.WorldToMap(new Vector2(player.Position.X, player.Position.Z), map.Value)
			: Vector2.Zero;

		MobHuntEntry? chosen = null;
		float bestDistance = float.MaxValue;
		foreach (MobHuntEntry entry in this.CurrentAreaMobHuntEntries) {
			if (entry.GetProgress().IsComplete) {
				continue;
			}

			float distance = entry.IsEliteMark || !LocationDatabase.Database.TryGetValue(entry.MobHuntId, out LocationDatabase.PositionInfo? pos)
				? float.MaxValue
				: Vector2.DistanceSquared(pos.Coordinate, playerLocation);

			if (chosen == null || distance < bestDistance) {
				chosen = entry;
				bestDistance = distance;
			}
		}

		if (chosen == null) {
			Svc.Log.Information("No marks in current zone, picking next target from optimal route");
			openType = Plugin.Instance.Configuration.IncludeAreaOnMap
				? MapService.OpenType.ShowOpen
				: MapService.OpenType.MarkerOpen;
			List<HuntRouteStep> route = this.BuildOptimalRoute();
			if (route.Count > 0) {
				chosen = route[0].Entry;
			}
		}

		if (chosen == null) {
			Svc.Log.Information("Unable to find a hunt mark to target");
			Svc.Chat.Print("Couldn't find any hunt marks. Either you have no bills, or this is a bug.");
			return;
		}

		bool hasLocation = !chosen.IsEliteMark && LocationDatabase.Database.ContainsKey(chosen.MobHuntId);
		if (chosen.IsEliteMark) {
			Svc.Chat.Print($"Hunting elite mark {chosen.Name} in {chosen.TerritoryName}");
			if (!Plugin.Instance.Configuration.SuppressEliteMarkLocationWarning) {
				Svc.Chat.Print("Elite mark spawn locations are not available, since there are so many possibilities and the mob will only ever be in one place at a time."
					+ "\n(You can suppress this warning in the plugin settings)");
			}
		}
		else {
			int remaining = chosen.GetProgress().Remaining;
			if (hasLocation) {
				Svc.Chat.Print($"Hunting {remaining}x {chosen.Name} in {chosen.TerritoryName}");
				MapService.CreateMapMarker(
					chosen.TerritoryType,
					chosen.MapId,
					chosen.MobHuntId,
					chosen.Name,
					openType);
			}
			else {
				Svc.Chat.Print($"Hunting {remaining}x {chosen.Name} in {chosen.TerritoryName} (no map location available)");
			}
		}

		if (teleport && chosen.TerritoryType != Svc.ClientState.TerritoryType && Plugin.TeleportConsumer?.IsAvailable == true) {
			if (hasLocation) {
				MapService.TeleportToNearestAetheryte(chosen.TerritoryType, chosen.MapId, chosen.MobHuntId);
			}
			else {
				MapService.TeleportToTerritory(chosen.TerritoryType, chosen.MapId);
			}
		}

		if (Plugin.Instance.Configuration.EnableXivEspIntegration
			&& Plugin.Instance.Configuration.AutoSetEspSearchOnNextHuntCommand
			&& Plugin.EspConsumer?.IsAvailable == true) {
			Plugin.EspConsumer.SearchFor(chosen.Name);
		}
	}

	private static void AddIncompleteEntries(List<HuntZone> zones, List<MobHuntEntry> results) {
		foreach (HuntZone zone in zones) {
			foreach (MobHuntEntry entry in zone.Entries) {
				if (!entry.GetProgress().IsComplete) {
					results.Add(entry);
				}
			}
		}
	}

	public void PrintList() {
		if (!this.MobHuntEntriesReady) {
			Svc.Chat.Print("Hunt bill data is currently reloading. Please try again in a moment.");
			return;
		}

		if (this.MobHuntEntries.Count < 1) {
			Svc.Chat.Print("No hunt marks found. If this doesn't sound right, please use `/hbr reload` and try again.");
			return;
		}

		List<string> names = [];
		foreach (KeyValuePair<string, List<HuntZone>> expansion in this.MobHuntEntries) {
			names.Clear();
			foreach (HuntZone zone in expansion.Value) {
				foreach (MobHuntEntry entry in zone.Entries) {
					names.Add(entry.Name);
				}
			}

			names.Sort(StringComparer.Ordinal);
			Svc.Chat.Print($"{expansion.Key}: {string.Join(", ", names)}");
		}
	}

	private unsafe void OnFrameworkUpdate(IFramework framework) {
		if (!Svc.PlayerState.IsLoaded) {
			if (this.MobHuntEntries.Count > 0
				|| this.CurrentAreaMobHuntEntries.Count > 0
				|| this.ActiveWeeklyEliteMobIds.Count > 0
				|| this.lastObtainedFlags != 0
				|| this.lastMarkIds.Length > 0) {
				this.reloadGeneration++;
				this.lastObtainedFlags = 0;
				this.lastMarkIds = [];
				this.lastTrackedLocalTerritory = 0;
				this.completedLocalMobIds.Clear();
				this.MobHuntEntries = [];
				this.CurrentAreaMobHuntEntries = [];
				this.ActiveWeeklyEliteMobIds = [];
				this.MobHuntEntriesReady = true;
				this.RouteRevision++;
			}
			return;
		}

		MobHunt* mobHunt = MobHunt.Instance();
		if (mobHunt == null) {
			return;
		}

		if (this.lastObtainedFlags != mobHunt->ObtainedFlags || !mobHunt->ObtainedMarkId.SequenceEqual(this.lastMarkIds)) {
			this.RequestReload();
			return;
		}

		if (this.MobHuntEntriesReady && this.CurrentAreaMobHuntEntries.Count > 0) {
			this.CheckLocalCompletionTransitions();
		}
	}

	private void CheckLocalCompletionTransitions() {
		uint currentTerritory = Svc.ClientState.TerritoryType;
		if (this.lastTrackedLocalTerritory != currentTerritory) {
			this.SyncCompletedLocalMobIds(currentTerritory);
			return;
		}

		bool newlyCompleted = false;
		bool anyRemainingLocal = false;
		for (int i = 0; i < this.CurrentAreaMobHuntEntries.Count; i++) {
			MobHuntEntry entry = this.CurrentAreaMobHuntEntries[i];
			if (entry.GetProgress().IsComplete) {
				if (this.completedLocalMobIds.Add(entry.MobHuntId)) {
					newlyCompleted = true;
				}
			}
			else {
				anyRemainingLocal = true;
			}
		}

		if (!newlyCompleted) {
			return;
		}

		this.RouteRevision++;
		if (!Plugin.Instance.Configuration.AutoFlagNextOnComplete) {
			return;
		}

		if (anyRemainingLocal || this.GetRemainingRewardsSummary().RemainingBillTargets > 0) {
			this.FlagNextTarget();
		}
	}

	private void SyncCompletedLocalMobIds(uint currentTerritory) {
		this.lastTrackedLocalTerritory = currentTerritory;
		this.completedLocalMobIds.Clear();
		for (int i = 0; i < this.CurrentAreaMobHuntEntries.Count; i++) {
			MobHuntEntry entry = this.CurrentAreaMobHuntEntries[i];
			if (entry.GetProgress().IsComplete) {
				this.completedLocalMobIds.Add(entry.MobHuntId);
			}
		}
	}

	private void OnTerritoryChanged(uint territoryType) => this.RefreshCurrentArea();

	private void RefreshCurrentArea() {
		List<MobHuntEntry> nextEntries = [];
		uint currentTerritory = Svc.ClientState.TerritoryType;

		foreach (List<HuntZone> zones in this.MobHuntEntries.Values) {
			foreach (HuntZone zone in zones) {
				if (zone.TerritoryType == currentTerritory) {
					nextEntries.AddRange(zone.Entries);
				}
			}
		}

		nextEntries.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
		this.CurrentAreaMobHuntEntries = nextEntries;
		this.SyncCompletedLocalMobIds(currentTerritory);
		this.RouteRevision++;
	}

	public void Dispose() {
		this.disposed = true;
		this.MobHuntEntriesReady = false;
		Svc.ClientState.TerritoryChanged -= this.OnTerritoryChanged;
		Svc.Framework.Update -= this.OnFrameworkUpdate;
	}
}
