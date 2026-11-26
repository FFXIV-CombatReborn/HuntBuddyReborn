using System.Collections.Generic;

namespace HuntBuddy.Models;

public sealed class HuntZone {
	public uint TerritoryType { get; init; }

	public string Name { get; init; } = string.Empty;

	public List<MobHuntEntry> Entries { get; } = [];
}
