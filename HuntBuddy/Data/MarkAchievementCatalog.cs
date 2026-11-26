using System.Collections.Generic;
using HuntBuddy.Models;

namespace HuntBuddy.Data;

public static class MarkAchievementCatalog {
	public sealed record AchievementDefinition(uint AchievementId, MarkRank Rank, string RegionName, uint[] TerritoryTypes);

	public static readonly AchievementDefinition[] Definitions = [
		new(964, MarkRank.B, "The Black Shroud", [148, 152, 153, 154]),
		new(969, MarkRank.A, "The Black Shroud", [148, 152, 153, 154]),
		new(974, MarkRank.S, "The Black Shroud", [148, 152, 153, 154]),
		new(965, MarkRank.B, "Thanalan", [140, 141, 145, 146, 147]),
		new(970, MarkRank.A, "Thanalan", [140, 141, 145, 146, 147]),
		new(975, MarkRank.S, "Thanalan", [140, 141, 145, 146, 147]),
		new(966, MarkRank.B, "La Noscea", [134, 135, 137, 138, 139, 180]),
		new(971, MarkRank.A, "La Noscea", [134, 135, 137, 138, 139, 180]),
		new(976, MarkRank.S, "La Noscea", [134, 135, 137, 138, 139, 180]),
		new(967, MarkRank.B, "Mor Dhona & Coerthas", [155, 156]),
		new(972, MarkRank.A, "Mor Dhona & Coerthas", [155, 156]),
		new(977, MarkRank.S, "Mor Dhona & Coerthas", [155, 156]),
		new(1259, MarkRank.B, "Dravania", [398, 399, 400]),
		new(1261, MarkRank.A, "Dravania", [398, 399, 400]),
		new(1263, MarkRank.S, "Dravania", [398, 399, 400]),
		new(1260, MarkRank.B, "Abalathia & Coerthas WH", [397, 401, 402]),
		new(1262, MarkRank.A, "Abalathia & Coerthas WH", [397, 401, 402]),
		new(1264, MarkRank.S, "Abalathia & Coerthas WH", [397, 401, 402]),
		new(1910, MarkRank.B, "Gyr Abania", [612, 620, 621]),
		new(1911, MarkRank.A, "Gyr Abania", [612, 620, 621]),
		new(1912, MarkRank.S, "Gyr Abania", [612, 620, 621]),
		new(1913, MarkRank.B, "Othard", [613, 614, 622]),
		new(1914, MarkRank.A, "Othard", [613, 614, 622]),
		new(1915, MarkRank.S, "Othard", [613, 614, 622]),
	];

	//TODO: got this stuff from faloop, but it seems correct, idk any way to verify it so this has to be good for now
	public static readonly Dictionary<uint, string> SrankSpawnConditions = new() {
		{ 3, "Laideronnette: Two consecutive cycles of Rain weather (spawns ~6m into the second Rain)." },
		{ 6, "Wulgaru: Initiate Battlecraft Leves in East Shroud." },
		{ 9, "Mindflayer: New Moon nights (00:00-05:00 ET); cross spawn points." },
		{ 12, "Thousand-cast Theda: Catch a Judgeray (Fisher, 17:00-21:00 ET, Fallgourd Float, mooch from Copperfish)." },
		{ 15, "Zona Seeker: Catch a Glimmerscale (Fisher, Clear or Fair weather, Nophica's Wells)." },
		{ 18, "Brontes: Eat food and walk over spawn points while Well Fed." },
		{ 21, "Lampalagua: Initiate Battlecraft or Grand Company Leves in Eastern Thanalan." },
		{ 24, "Nunyunuwi: Complete all FATEs continuously for ~1 hour without any FATE failing or expiring." },
		{ 27, "Minhocao: Defeat Earth Sprites in Northern Thanalan (~100 kills; Core Blimey FATE is fastest)." },
		{ 30, "Croque-mitaine: Mine Grade 3 La Noscean Topsoil (Unspoiled Miner node, 19:00-22:00 ET, Zephyr Gate)." },
		{ 33, "Croakadile: Full Moon (17:00 ET onward during Full Moon phase); pass over spawn points." },
		{ 36, "The Garlok: 200 real-time minutes without Rain or Showers following Rain/Showers weather." },
		{ 39, "Bonnacon: Harvest La Noscean Leeks (Unspoiled Botanist node, 08:00-11:00 ET, Quarterstone)." },
		{ 42, "Nandi: Pass over spawn points with any minion summoned." },
		{ 45, "Chernobog: Player deaths in Outer La Noscea (e.g., Blue Mage Self-Destruct)." },
		{ 48, "Safat: Players dropping to critical/1 HP in Coerthas Central Highlands (e.g., cliff jumps at Witchdrop)." },
		{ 51, "Agrippa the Mighty: Open Peisteskin Treasure Map coffers in Mor Dhona." },
		{ 56, "Kaiser Behemoth: Pass low over spawn points with the Behemoth Heir minion summoned." },
		{ 61, "Senmurv: Complete the FATE 'Cerf's Up' 5 times in a row." },
		{ 66, "The Pale Rider: Open Timeworn Wyvernskin Map coffers in The Dravanian Hinterlands." },
		{ 71, "Gandarewa: Gather Aurum Regis Ore or Seventh Heaven from unspoiled/folklore nodes." },
		{ 76, "Bird of Paradise: Rank B mark Squonk uses its Chirp skill in combat." },
		{ 81, "Leucrotta: Defeat 50 Allagan Chimeras, 50 Meracydian Vouivres, and 50 Lesser Hydras." },
		{ 86, "Okina: Defeat 100 Yumemis and 100 Naked Yumemis, then trigger during Full Moon (12:00 ET onward)." },
		{ 91, "Gamma: Pass over spawn points with the Toy Alexander minion summoned between 17:00 and 08:00 ET." },
		{ 96, "Orghana: Complete the FATE 'Not Just a Tribute' and pass over spawn points." },
		{ 101, "Udumbara: Defeat 100 Leshys (East End) and 100 Diakkas (Dimwold)." },
		{ 106, "Bone Crawler: Ride a Chocobo Porter in The Peaks." },
		{ 111, "Salt and Light: Discard 50 items from your inventory in The Lochs (stacks count as 1 item)." },
	};
}
