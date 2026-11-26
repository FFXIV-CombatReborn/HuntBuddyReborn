using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using HuntBuddy.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using MapType = FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType;

namespace HuntBuddy.Services;

public static class MapService {
	private const uint DravanianHinterlands = 399;
	private const byte AetheryteMarkerType = 3;

	private static readonly Dictionary<uint, int> spawnCycleIndices = [];

	public enum OpenType {
		None,
		ShowOpen,
		MarkerOpen,
	}

	public static void CreateMapMarker(uint territoryType, uint mapId, uint mobHuntId, string? mobHuntName,
		OpenType openType = OpenType.MarkerOpen) {
		if (!LocationDatabase.Database.TryGetValue(mobHuntId, out LocationDatabase.PositionInfo? info)) {
			return;
		}

		CreateMapMarkerAtCoordinate(territoryType, mapId, info.Coordinate, mobHuntName, openType);
	}

	public static unsafe void CreateMapMarkerAtCoordinate(uint territoryType, uint mapId, Vector2 mapCoord, string? label,
		OpenType openType = OpenType.MarkerOpen) {
		AgentMap* map = AgentMap.Instance();
		if (map == null || !Svc.Data.GetExcelSheet<Map>().TryGetRow(mapId, out Map mapRow)) {
			return;
		}

		Vector2 worldPos = MapToWorldCoordinates(mapCoord, mapRow);
		(int X, int Y) = ((int)MathF.Round(worldPos.X), (int)MathF.Round(worldPos.Y));

		map->FlagMarkerCount = 0;
		map->SetFlagMapMarker(territoryType, mapId, worldPos.X, worldPos.Y, 60004);

		switch (openType) {
			case OpenType.None:
				break;
			case OpenType.ShowOpen:
				map->AgentInterface.Hide();
				map->AddGatheringTempMarker(X, Y, 150, 60004, 4, label);
				map->OpenMap(mapId, territoryType, label, MapType.GatheringLog);
				break;
			case OpenType.MarkerOpen:
				map->AgentInterface.Hide();
				map->OpenMap(mapId, territoryType);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(openType), openType, null);
		}
	}

	public static bool CycleZoneSpawnMarker(uint territoryType, uint mapId, string markName, string territoryName,
		OpenType openType = OpenType.MarkerOpen) {
		if (!SpawnPointDatabase.TryGetSpawnPoints(territoryType, out Vector2[]? points) || points.Length == 0) {
			return false;
		}

		int nextIndex = spawnCycleIndices.TryGetValue(territoryType, out int currentIndex)
			? (currentIndex + 1) % points.Length
			: 0;
		spawnCycleIndices[territoryType] = nextIndex;
		Vector2 coord = points[nextIndex];
		CreateMapMarkerAtCoordinate(territoryType, mapId, coord, markName, openType);
		Svc.Chat.Print($"Spawn {nextIndex + 1}/{points.Length} for {markName} in {territoryName} ({coord.X:F1}, {coord.Y:F1})");
		return true;
	}

	public static void EchoMapLink(uint territoryType, uint mapId, string label, Vector2? mapCoord = null, string? territoryName = null) {
		if (mapCoord.HasValue && mapId != 0) {
			CreateMapMarkerAtCoordinate(territoryType, mapId, mapCoord.Value, label, OpenType.None);
			Svc.Chat.Print(new SeStringBuilder()
				.AddText($"{label} ")
				.AddMapLink(territoryType, mapId, mapCoord.Value.X, mapCoord.Value.Y)
				.BuiltString);
			return;
		}

		string zone = string.IsNullOrEmpty(territoryName)
			? Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryType)?.PlaceName.Value.Name.ExtractText() ?? $"Zone {territoryType}"
			: territoryName;
		Svc.Chat.Print($"{label} ({zone})");
	}

	public static void CopyCalloutToClipboard(uint territoryType, uint mapId, string name, string territoryName, Vector2? mapCoord = null) {
		string text;
		if (mapCoord.HasValue && mapId != 0) {
			CreateMapMarkerAtCoordinate(territoryType, mapId, mapCoord.Value, name, OpenType.None);
			text = $"{name} - {territoryName} ({mapCoord.Value.X:F1}, {mapCoord.Value.Y:F1}) <flag>";
		}
		else {
			text = $"{name} - {territoryName}";
		}

		ImGui.SetClipboardText(text);
		Svc.Chat.Print($"Copied callout: {text}");
	}

	public static (uint AetheryteId, string Name) GetNearestAetheryteInfo(uint territoryType, uint mapId, uint mobHuntId) {
		uint aetheryteId = territoryType == DravanianHinterlands || mobHuntId == 0
			? 0
			: FindNearestAetheryte(territoryType, mapId, mobHuntId);
		if (aetheryteId == 0) {
			aetheryteId = FindTerritoryAetheryte(territoryType, mapId);
		}

		if (aetheryteId == 0) {
			return (0, string.Empty);
		}

		string name = Svc.Data.GetExcelSheet<Aetheryte>().GetRowOrDefault(aetheryteId)?.PlaceName.Value.Name.ExtractText() ?? string.Empty;
		return (aetheryteId, name);
	}

	public static unsafe void OpenZoneMap(uint territoryType, uint mapId) {
		AgentMap* map = AgentMap.Instance();
		if (map == null || mapId == 0) {
			return;
		}

		map->AgentInterface.Hide();
		map->OpenMap(mapId, territoryType);
	}

	public static void TeleportToNearestAetheryte(uint territoryType, uint mapId, uint mobHuntId) {
		uint aetheryteId = territoryType == DravanianHinterlands
			? 0
			: FindNearestAetheryte(territoryType, mapId, mobHuntId);
		if (aetheryteId == 0) {
			aetheryteId = FindTerritoryAetheryte(territoryType, mapId);
		}

		Teleport(aetheryteId);
	}

	public static void TeleportToTerritory(uint territoryType, uint mapId) =>
		Teleport(FindTerritoryAetheryte(territoryType, mapId));

	private static void Teleport(uint aetheryteId) {
		if (aetheryteId != 0) {
			Plugin.TeleportConsumer?.Teleport(aetheryteId);
		}
	}

	private static uint FindNearestAetheryte(uint territoryType, uint mapId, uint mobHuntId) {
		if (!LocationDatabase.Database.TryGetValue(mobHuntId, out LocationDatabase.PositionInfo? mobPos)
			|| !Svc.Data.GetExcelSheet<Map>().TryGetRow(mapId, out Map map)
			|| !Svc.Data.GetSubrowExcelSheet<MapMarker>().TryGetRow(map.MapMarkerRange, out SubrowCollection<MapMarker> markers)) {
			return 0;
		}

		ExcelSheet<Aetheryte> aetheryteSheet = Svc.Data.GetExcelSheet<Aetheryte>();
		uint nearestAetheryteId = 0;
		float bestDistance = float.MaxValue;
		for (int i = 0; i < markers.Count; i++) {
			MapMarker marker = markers[i];
			if (marker.DataType != AetheryteMarkerType) {
				continue;
			}

			float distance = Vector2.DistanceSquared(
				mobPos.Coordinate,
				ConvertPixelPositionToMapCoordinate(marker.X, marker.Y, map.SizeFactor));
			if (distance < bestDistance
				&& aetheryteSheet.TryGetRow(marker.DataKey.RowId, out Aetheryte aetheryte)
				&& aetheryte.IsAetheryte
				&& aetheryte.Territory.RowId == territoryType) {
				bestDistance = distance;
				nearestAetheryteId = aetheryte.RowId;
			}
		}

		return nearestAetheryteId;
	}

	private static uint FindTerritoryAetheryte(uint territoryType, uint mapId) {
		if (territoryType == DravanianHinterlands) {
			return Svc.Data.GetExcelSheet<Map>().GetRowOrDefault(mapId)?.TerritoryType.ValueNullable?.Aetheryte.RowId ?? 0;
		}

		foreach (Aetheryte row in Svc.Data.GetExcelSheet<Aetheryte>()) {
			if (row.IsAetheryte && row.Territory.RowId == territoryType) {
				return row.RowId;
			}
		}

		return 0;
	}

	private static Vector2 MapToWorldCoordinates(Vector2 pos, Map map) {
		float scale = map.SizeFactor == 0 ? 100f : map.SizeFactor;
		float num = scale / 100f;
		float x = (float)((((pos.X - 1.0) * num / 41.0 * 2048.0) - 1024.0) / num);
		float y = (float)((((pos.Y - 1.0) * num / 41.0 * 2048.0) - 1024.0) / num);
		return new Vector2(x, y);
	}

	private static Vector2 ConvertPixelPositionToMapCoordinate(int x, int y, float scale) {
		float effectiveScale = scale == 0f ? 100f : scale;
		float mapCoordinatesPerPixel = 2f / effectiveScale;
		return new Vector2((x * mapCoordinatesPerPixel) + 1f, (y * mapCoordinatesPerPixel) + 1f);
	}
}
