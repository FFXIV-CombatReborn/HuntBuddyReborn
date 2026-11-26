using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using ECommons.DalamudServices;
using RebornMaterial;

namespace HuntBuddy.Models;

public class Configuration: IPluginConfiguration, IM3Settings {
	public const int CurrentVersion = 1;
	public static readonly Vector4 DefaultAccent = M3ColorMath.FromRgb(0x8B5917);

	public int Version {
		get;
		set;
	}

	public bool EnableXivEspIntegration;
	public bool AutoSetEspSearchOnNextHuntCommand;
	public bool IncludeAreaOnMap;
	public bool LockWindowPositions;
	public bool ShowLocalHunts;
	public bool ShowLocalHuntIcons;
	public bool HideLocalHuntBackground;
	public bool HideCompletedHunts;
	public bool SuppressEliteMarkLocationWarning;
	public bool IgnoreCloseHotkey;
	public bool HideCompletedMarkAchievements;
	public bool AutoFlagNextOnComplete;
	public bool AlertOnNeededMarkSpawn = true;
	public bool PlaySoundOnNeededMarkSpawn;
	public int AlertSoundEffectId = 6;
	public float IconScale = 1f;
	public Vector4 IconBackgroundColour = new(0.76f, 0.75f, 0.76f, 0.8f);
	public Dictionary<ulong, CharacterMarkProgress> CharacterMarkProgress = [];

	public Vector4 UiAccentColor { get; set; } = DefaultAccent;
	public float UiTextScale { get; set; } = 1f;
	public float UiElementScale { get; set; } = 1f;
	public float UiPaddingScale { get; set; } = 1f;
	public float UiMotionScale { get; set; } = 1f;
	public bool UiCompactLayout { get; set; }
	public bool UiInlineDescriptions { get; set; } = true;
	public bool UiIconSidebar { get; set; }

	bool IM3Settings.UiDescriptionTooltips => !this.UiInlineDescriptions;

	public void Save() => Svc.PluginInterface.SavePluginConfig(this);
}
