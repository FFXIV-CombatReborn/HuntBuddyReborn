using System;
using System.Collections.Generic;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace HuntBuddy.Services;

public static class EorzeaEnvironmentService {
	private const long WeatherIntervalSeconds = 1400L;
	private const uint ClearSkiesWeatherId = 1;
	private const uint FairSkiesWeatherId = 2;
	private const uint RainWeatherId = 7;
	private const uint ShowersWeatherId = 8;

	public readonly record struct EorzeaClock(int Hour, int Minute, int DayOfMonth, bool IsFullMoon, bool IsNewMoon);
	public readonly record struct SrankWindowStatus(bool IsOpen, string BadgeText, string Tooltip);

	private static readonly Dictionary<uint, string> weatherNameCache = [];

	public static EorzeaClock GetEorzeaClock(DateTimeOffset utcNow) {
		long unixSeconds = utcNow.ToUnixTimeSeconds();
		long eorzeaSeconds = (unixSeconds * 3600L) / 175L;
		int minute = (int)((eorzeaSeconds / 60L) % 60L);
		int hour = (int)((eorzeaSeconds / 3600L) % 24L);
		int dayOfMonth = (int)(((eorzeaSeconds / 86400L) % 32L) + 1L);
		bool isFullMoon = (dayOfMonth == 16 && hour >= 12) || dayOfMonth is 17 or 18 or 19 || (dayOfMonth == 20 && hour < 12);
		bool isNewMoon = (dayOfMonth == 32 && hour >= 12) || dayOfMonth is 1 or 2 or 3 || (dayOfMonth == 4 && hour < 12);
		return new EorzeaClock(hour, minute, dayOfMonth, isFullMoon, isNewMoon);
	}

	public static (uint WeatherId, string WeatherName) GetWeatherAt(uint territoryType, DateTimeOffset utcNow) {
		TerritoryType? tt = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryType);
		WeatherRate? weatherRate = tt?.WeatherRate.ValueNullable;
		if (weatherRate == null) {
			return (0, "Unknown");
		}

		long unixSeconds = utcNow.ToUnixTimeSeconds();
		long bell = unixSeconds / 175L;
		long increment = (bell + 8L - (bell % 8L)) % 24L;
		long totalDays = unixSeconds / 4200L;
		uint calcBase = (uint)((totalDays * 100L) + increment);
		uint step1 = (calcBase << 11) ^ calcBase;
		uint step2 = (step1 >> 8) ^ step1;
		int chance = (int)(step2 % 100U);

		int cumulative = 0;
		WeatherRate rate = weatherRate.Value;
		int count = Math.Min(rate.Rate.Count, rate.Weather.Count);
		for (int i = 0; i < count; i++) {
			cumulative += rate.Rate[i];
			if (chance < cumulative) {
				uint weatherId = rate.Weather[i].RowId;
				return (weatherId, GetWeatherName(weatherId));
			}
		}

		if (count > 0) {
			uint fallbackId = rate.Weather[0].RowId;
			return (fallbackId, GetWeatherName(fallbackId));
		}

		return (0, "Unknown");
	}

	public static SrankWindowStatus? EvaluateSrankWindow(uint notoriousMonsterId, uint territoryType, DateTimeOffset utcNow) {
		EorzeaClock clock = GetEorzeaClock(utcNow);
		switch (notoriousMonsterId) {
			case 3: {
					(uint curId, string curName) = GetWeatherAt(territoryType, utcNow);
					(uint prevId, _) = GetWeatherAt(territoryType, utcNow.AddSeconds(-WeatherIntervalSeconds));
					if (curId == RainWeatherId && prevId == RainWeatherId) {
						return new SrankWindowStatus(true, "Open", $"Two consecutive Rain cycles active ({curName}).");
					}

					(uint nextId, string nextName) = GetWeatherAt(territoryType, utcNow.AddSeconds(WeatherIntervalSeconds));
					if (curId == RainWeatherId && nextId == RainWeatherId) {
						return new SrankWindowStatus(false, "Soon", $"Rain is active now; next weather is {nextName} (2nd Rain cycle next).");
					}

					return new SrankWindowStatus(false, curName, $"Needs 2 consecutive Rain cycles. Current weather: {curName}.");
				}
			case 9:
				return clock.IsNewMoon && clock.Hour < 5
					? new SrankWindowStatus(true, "Open", $"New Moon night active (ET {clock.Hour:D2}:{clock.Minute:D2}, Day {clock.DayOfMonth}).")
					: new SrankWindowStatus(false, clock.IsNewMoon ? "New Moon" : $"Day {clock.DayOfMonth}", "Requires New Moon night (00:00-05:00 ET).");
			case 12:
				return clock.Hour is >= 17 and < 21
					? new SrankWindowStatus(true, "Open", $"Judgeray fishing window open (ET {clock.Hour:D2}:{clock.Minute:D2}).")
					: new SrankWindowStatus(false, $"ET {clock.Hour:D2}:{clock.Minute:D2}", "Judgeray window is 17:00-21:00 ET.");
			case 15: {
					(uint curId, string curName) = GetWeatherAt(territoryType, utcNow);
					return curId is ClearSkiesWeatherId or FairSkiesWeatherId
						? new SrankWindowStatus(true, "Open", $"Glimmerscale weather active ({curName}).")
						: new SrankWindowStatus(false, curName, $"Requires Clear or Fair Skies. Current weather: {curName}.");
				}
			case 30:
				return clock.Hour is >= 19 and < 22
					? new SrankWindowStatus(true, "Open", $"Topsoil mining node active (ET {clock.Hour:D2}:{clock.Minute:D2}).")
					: new SrankWindowStatus(false, $"ET {clock.Hour:D2}:{clock.Minute:D2}", "Unspoiled Miner node spawns 19:00-22:00 ET.");
			case 33: {
					bool open = clock.IsFullMoon && (clock.DayOfMonth > 16 || clock.Hour >= 17);
					return open
						? new SrankWindowStatus(true, "Open", $"Full Moon window active (ET {clock.Hour:D2}:{clock.Minute:D2}, Day {clock.DayOfMonth}).")
						: new SrankWindowStatus(false, clock.IsFullMoon ? "Full Moon" : $"Day {clock.DayOfMonth}", "Requires Full Moon (17:00 ET on Day 16 onward).");
				}
			case 36: {
					int dryIntervals = 0;
					for (int i = 0; i < 9; i++) {
						(uint wId, _) = GetWeatherAt(territoryType, utcNow.AddSeconds(-i * WeatherIntervalSeconds));
						if (wId is RainWeatherId or ShowersWeatherId) {
							break;
						}

						dryIntervals++;
					}

					return dryIntervals >= 9
						? new SrankWindowStatus(true, "Open", "200+ real-time minutes without Rain or Showers.")
						: new SrankWindowStatus(false, $"Dry {dryIntervals}/9", $"Dry streak: {dryIntervals}/9 weather windows (~{dryIntervals * 23}m) without Rain/Showers.");
				}
			case 39:
				return clock.Hour is >= 8 and < 11
					? new SrankWindowStatus(true, "Open", $"Leek harvesting node active (ET {clock.Hour:D2}:{clock.Minute:D2}).")
					: new SrankWindowStatus(false, $"ET {clock.Hour:D2}:{clock.Minute:D2}", "Unspoiled Botanist node spawns 08:00-11:00 ET.");
			case 86:
				return clock.IsFullMoon
					? new SrankWindowStatus(true, "Open", $"Full Moon active (Day {clock.DayOfMonth}, ET {clock.Hour:D2}:{clock.Minute:D2}).")
					: new SrankWindowStatus(false, $"Day {clock.DayOfMonth}", "Final trigger requires Full Moon (Days 16-20).");
			case 91:
				return clock.Hour is >= 17 or < 8
					? new SrankWindowStatus(true, "Open", $"Gamma night window active (ET {clock.Hour:D2}:{clock.Minute:D2}).")
					: new SrankWindowStatus(false, $"ET {clock.Hour:D2}:{clock.Minute:D2}", "Requires 17:00-08:00 ET with Toy Alexander minion.");
			default:
				return null;
		}
	}

	private static string GetWeatherName(uint weatherId) {
		if (weatherNameCache.TryGetValue(weatherId, out string? cached)) {
			return cached;
		}

		ExcelSheet<Weather>? weatherSheet = Svc.Data.GetExcelSheet<Weather>();
		string name = weatherSheet?.GetRowOrDefault(weatherId)?.Name.ExtractText() ?? $"Weather {weatherId}";
		if (string.IsNullOrEmpty(name)) {
			name = $"Weather {weatherId}";
		}

		weatherNameCache[weatherId] = name;
		return name;
	}
}
