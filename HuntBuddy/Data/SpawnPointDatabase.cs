using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace HuntBuddy.Data;

public static class SpawnPointDatabase {
	public static readonly Dictionary<uint, Vector2[]> ZoneSpawnPoints = new() {
		{ 134, [new(13.0f, 18.0f), new(20.0f, 15.0f), new(24.0f, 24.0f), new(31.0f, 20.0f), new(22.0f, 32.0f)] },
		{ 135, [new(17.0f, 24.0f), new(24.0f, 18.0f), new(31.0f, 20.0f), new(32.0f, 30.0f), new(19.0f, 33.0f)] },
		{ 137, [new(20.0f, 27.0f), new(26.0f, 21.0f), new(31.0f, 25.0f), new(33.0f, 32.0f), new(27.0f, 34.0f)] },
		{ 138, [new(14.0f, 20.0f), new(18.0f, 29.0f), new(25.0f, 23.0f), new(31.0f, 25.0f), new(27.0f, 15.0f)] },
		{ 139, [new(10.0f, 24.0f), new(14.0f, 12.0f), new(19.0f, 17.0f), new(23.0f, 27.0f), new(15.0f, 32.0f)] },
		{ 180, [new(13.0f, 14.0f), new(16.0f, 10.0f), new(22.0f, 14.0f), new(26.0f, 12.0f), new(24.0f, 18.0f)] },
		{ 140, [new(11.0f, 21.0f), new(17.0f, 14.0f), new(23.0f, 23.0f), new(27.0f, 17.0f), new(18.0f, 29.0f)] },
		{ 141, [new(13.0f, 17.0f), new(19.0f, 22.0f), new(24.0f, 13.0f), new(29.0f, 20.0f), new(21.0f, 28.0f)] },
		{ 145, [new(12.0f, 21.0f), new(18.0f, 16.0f), new(23.0f, 26.0f), new(29.0f, 20.0f), new(26.0f, 12.0f)] },
		{ 146, [new(13.0f, 15.0f), new(19.0f, 23.0f), new(26.0f, 17.0f), new(31.0f, 26.0f), new(22.0f, 32.0f)] },
		{ 147, [new(16.0f, 14.0f), new(21.0f, 20.0f), new(25.0f, 26.0f), new(19.0f, 30.0f), new(24.0f, 16.0f)] },
		{ 148, [new(11.0f, 22.0f), new(17.0f, 15.0f), new(23.0f, 24.0f), new(29.0f, 18.0f), new(24.0f, 30.0f)] },
		{ 152, [new(11.0f, 18.0f), new(16.0f, 27.0f), new(22.0f, 21.0f), new(28.0f, 15.0f), new(26.0f, 28.0f)] },
		{ 153, [new(14.0f, 19.0f), new(21.0f, 14.0f), new(25.0f, 25.0f), new(31.0f, 21.0f), new(28.0f, 31.0f)] },
		{ 154, [new(15.0f, 21.0f), new(20.0f, 15.0f), new(25.0f, 26.0f), new(30.0f, 20.0f), new(24.0f, 31.0f)] },
		{ 155, [new(10.0f, 19.0f), new(16.0f, 12.0f), new(22.0f, 24.0f), new(29.0f, 17.0f), new(32.0f, 27.0f)] },
		{ 156, [new(12.0f, 13.0f), new(18.0f, 11.0f), new(24.0f, 15.0f), new(29.0f, 12.0f), new(28.0f, 17.0f)] },
		{ 397, [new(12.0f, 14.0f), new(17.0f, 26.0f), new(24.0f, 16.0f), new(30.0f, 25.0f), new(23.0f, 32.0f)] },
		{ 398, [new(15.0f, 14.0f), new(18.0f, 29.0f), new(26.0f, 22.0f), new(32.0f, 14.0f), new(33.0f, 28.0f)] },
		{ 399, [new(11.0f, 21.0f), new(15.0f, 32.0f), new(22.0f, 18.0f), new(29.0f, 24.0f), new(34.0f, 21.0f)] },
		{ 400, [new(10.0f, 21.0f), new(18.0f, 28.0f), new(23.0f, 14.0f), new(29.0f, 31.0f), new(34.0f, 19.0f)] },
		{ 401, [new(12.0f, 31.0f), new(18.0f, 12.0f), new(23.0f, 24.0f), new(31.0f, 12.0f), new(35.0f, 24.0f)] },
		{ 402, [new(11.0f, 14.0f), new(14.0f, 31.0f), new(26.0f, 14.0f), new(31.0f, 25.0f), new(34.0f, 10.0f)] },
		{ 612, [new(11.0f, 13.0f), new(14.0f, 27.0f), new(23.0f, 14.0f), new(28.0f, 25.0f), new(34.0f, 24.0f)] },
		{ 613, [new(12.0f, 15.0f), new(11.0f, 29.0f), new(25.0f, 12.0f), new(28.0f, 32.0f), new(34.0f, 21.0f)] },
		{ 614, [new(17.0f, 12.0f), new(21.0f, 29.0f), new(26.0f, 21.0f), new(31.0f, 14.0f), new(31.0f, 32.0f)] },
		{ 620, [new(12.0f, 11.0f), new(14.0f, 27.0f), new(24.0f, 14.0f), new(25.0f, 31.0f), new(32.0f, 11.0f)] },
		{ 621, [new(10.0f, 21.0f), new(17.0f, 12.0f), new(22.0f, 28.0f), new(27.0f, 16.0f), new(24.0f, 30.0f)] },
		{ 622, [new(12.0f, 27.0f), new(18.0f, 14.0f), new(25.0f, 14.0f), new(31.0f, 19.0f), new(29.0f, 31.0f)] },
	};

	public static bool TryGetSpawnPoints(uint territoryType, [NotNullWhen(true)] out Vector2[]? points) =>
		ZoneSpawnPoints.TryGetValue(territoryType, out points);
}
