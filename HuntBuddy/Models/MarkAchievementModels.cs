using System;
using System.Collections.Generic;

namespace HuntBuddy.Models;

public enum MarkRank: byte {
	B = 1,
	A = 2,
	S = 3,
}

public sealed class EliteMarkInfo {
	public required MarkAchievementInfo Achievement { get; init; }
	public uint NotoriousMonsterId { get; init; }
	public uint BNpcNameId { get; init; }
	public string Name { get; init; } = string.Empty;
	public MarkRank Rank { get; init; }
	public uint TerritoryType { get; init; }
	public string TerritoryName { get; init; } = string.Empty;
	public uint MapId { get; init; }
	public string? SpawnCondition { get; init; }
}

public sealed class MarkAchievementInfo {
	public uint AchievementId { get; init; }
	public string Name { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public uint Icon { get; init; }
	public uint RequiredCount { get; init; }
	public string DataEntriesText { get; init; } = string.Empty;
	public uint ExpansionId { get; init; }
	public string ExpansionName { get; init; } = string.Empty;
	public string RegionName { get; init; } = string.Empty;
	public MarkRank Rank { get; init; }
	public uint[] TerritoryTypes { get; init; } = [];
	public List<EliteMarkInfo> Marks { get; } = [];
	public bool IsComplete { get; set; }
	public bool ProgressLoaded { get; set; }
	public uint ProgressCurrent { get; set; }
	public uint ProgressMax { get; set; }
	public DateTime? LastProgressUpdatedUtc { get; set; }

	public bool HasTerritory(uint territoryType) {
		for (int i = 0; i < this.TerritoryTypes.Length; i++) {
			if (this.TerritoryTypes[i] == territoryType) {
				return true;
			}
		}

		return false;
	}
}

public sealed class CharacterMarkProgress {
	public HashSet<uint> CompletedMarks { get; set; } = [];
	public Dictionary<uint, uint> LastKnownProgress { get; set; } = [];
	public Dictionary<uint, uint> LastKnownProgressMax { get; set; } = [];
}

public sealed class HuntMilestoneInfo {
	public uint AchievementId { get; init; }
	public string Name { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public uint Icon { get; init; }
	public uint Order { get; init; }
	public uint RequiredCount { get; init; }
	public string ExpansionLabel { get; init; } = string.Empty;
	public string RankLabel { get; init; } = string.Empty;
	public bool IsComplete { get; set; }
	public bool ProgressLoaded { get; set; }
	public uint ProgressCurrent { get; set; }
	public uint ProgressMax { get; set; }
	public DateTime? LastProgressUpdatedUtc { get; set; }
}
