using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace HuntBuddy.Models;

public readonly record struct MobHuntBillSlot(
	byte BillNumber,
	byte MobIndex,
	int NeededKills,
	uint ExpReward,
	ushort GilReward,
	ushort CurrencyReward,
	uint CurrencyExpansionId);

public readonly record struct MobHuntProgress(int Kills, int NeededKills) {
	public int Remaining => Math.Max(0, this.NeededKills - this.Kills);

	public bool IsComplete => this.Kills >= this.NeededKills;
}

public sealed class MobHuntEntry {
	private readonly List<MobHuntBillSlot> billSlots = [];

	public IReadOnlyList<MobHuntBillSlot> BillSlots => this.billSlots;

	public string Name { get; init; } = string.Empty;

	public string TerritoryName { get; init; } = string.Empty;

	public uint ExpansionId { get; init; }

	public uint MapId { get; init; }

	public uint TerritoryType { get; init; }

	public uint MobHuntId { get; init; }

	public bool IsEliteMark { get; init; }

	public uint Icon { get; init; }

	public void AddBillSlot(MobHuntBillSlot slot) => this.billSlots.Add(slot);

	public unsafe bool IsSlotComplete(MobHuntBillSlot slot) {
		MobHunt* mobHunt = MobHunt.Instance();
		return mobHunt != null && mobHunt->GetKillCount(slot.BillNumber, slot.MobIndex) >= slot.NeededKills;
	}

	public unsafe MobHuntProgress GetProgress() {
		MobHunt* mobHunt = MobHunt.Instance();
		MobHuntProgress progress = default;
		for (int i = 0; i < this.billSlots.Count; i++) {
			MobHuntBillSlot slot = this.billSlots[i];
			int kills = mobHunt == null ? 0 : mobHunt->GetKillCount(slot.BillNumber, slot.MobIndex);
			MobHuntProgress candidate = new(kills, slot.NeededKills);
			if (i == 0 || candidate.Remaining > progress.Remaining) {
				progress = candidate;
			}
		}

		return progress;
	}
}
