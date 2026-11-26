using System;
using System.Numerics;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.UI;
using HuntBuddy.Models;
using RebornMaterial;

namespace HuntBuddy.UI;

public partial class MainWindow {
	private void DrawSettingsPage() {
		Configuration cfg = Plugin.Instance.Configuration;

		using (M3Card.Begin(
			"##settings_behavior",
			"Behavior & Map",
			FontAwesomeIcon.MapMarkedAlt,
			subtitle: "Controls map flags, hotkeys, and window locking.")) {
			if (M3Widgets.RowSwitch(
				"Include hunt area on map by default",
				ref cfg.IncludeAreaOnMap,
				"Show an approximate general area around the flagged location. Hold [SHIFT] when clicking a map button to toggle.")) {
				cfg.Save();
			}

			if (M3Widgets.RowSwitch(
				"Suppress chat warning about B-rank locations",
				ref cfg.SuppressEliteMarkLocationWarning,
				"Hide the chat notice when '/hbr next' picks a B-rank mark.")) {
				cfg.Save();
			}

			if (M3Widgets.RowSwitch(
				"Ignore close hotkey",
				ref cfg.IgnoreCloseHotkey,
				"Prevent Escape from closing the Hunt Buddy Reborn window.")) {
				this.RespectCloseHotkey = !cfg.IgnoreCloseHotkey;
				cfg.Save();
			}

			if (M3Widgets.RowSwitch(
				"Auto-flag next target on bill completion",
				ref cfg.AutoFlagNextOnComplete,
				"Automatically flag the next incomplete hunt target when you finish a target in your current zone.")) {
				cfg.Save();
			}

			if (M3Widgets.RowSwitch(
				"Lock plugin window positions and sizes",
				ref cfg.LockWindowPositions,
				"Prevent moving or resizing Hunt Buddy Reborn windows.")) {
				cfg.Save();
			}
		}

		using (M3Card.Begin(
			"##settings_alerts",
			"Needed Mark Alerts",
			FontAwesomeIcon.Bell,
			M3.Scheme.Warning,
			subtitle: "Alerts when an uncollected ARR, Heavensward, or Stormblood achievement mark spawns nearby.")) {
			if (M3Widgets.RowSwitch(
				"Alert when a needed achievement mark spawns",
				ref cfg.AlertOnNeededMarkSpawn,
				"Show a toast and echo a clickable map link when a needed B, A, or S-rank mark is detected in your zone.")) {
				cfg.Save();
			}

			if (M3Widgets.RowSwitch(
				"Play sound on needed mark spawn",
				ref cfg.PlaySoundOnNeededMarkSpawn,
				"Play an in-game chat sound effect alongside the spawn alert.",
				cfg.AlertOnNeededMarkSpawn)) {
				cfg.Save();
			}

			float soundId = cfg.AlertSoundEffectId;
			if (M3Widgets.RowDragFloat(
				"Alert sound effect ID",
				ref soundId,
				1f,
				16f,
				"%.0f",
				"In-game chat sound effect ID (1-16) played for needed mark alerts.",
				cfg.AlertOnNeededMarkSpawn && cfg.PlaySoundOnNeededMarkSpawn,
				step: 1f)) {
				cfg.AlertSoundEffectId = Math.Clamp((int)MathF.Round(soundId), 1, 16);
				this.savePending = true;
			}

			if (M3Widgets.Button(
				"##test_alert_sound",
				"Test alert sound",
				M3ButtonStyle.Text,
				FontAwesomeIcon.VolumeUp,
				enabled: cfg.AlertOnNeededMarkSpawn && cfg.PlaySoundOnNeededMarkSpawn)) {
				UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(cfg.AlertSoundEffectId, 1, 16));
			}
		}

		using (M3Card.Begin(
			"##settings_local",
			"Local Hunts Overlay",
			FontAwesomeIcon.LayerGroup,
			M3.Scheme.Tertiary,
			subtitle: "Controls the compact HUD window for targets in your current map zone.")) {
			if (M3Widgets.RowSwitch(
				"Show hunts in local area",
				ref cfg.ShowLocalHunts,
				"Display an overlay window with active hunt targets in your current zone.")) {
				cfg.Save();
			}

			if (M3Widgets.RowSwitch(
				"Show icons of hunts in local area",
				ref cfg.ShowLocalHuntIcons,
				"Display the hunt mark bill illustration below each entry in the local hunts overlay.",
				cfg.ShowLocalHunts)) {
				cfg.Save();
			}

			if (M3Widgets.RowSwitch(
				"Hide background of local hunts window",
				ref cfg.HideLocalHuntBackground,
				"Make the local hunts overlay transparent.",
				cfg.ShowLocalHunts)) {
				cfg.Save();
			}

			if (M3Widgets.RowSwitch(
				"Hide completed targets in local hunts window",
				ref cfg.HideCompletedHunts,
				"Automatically hide targets once their required kills are reached.",
				cfg.ShowLocalHunts)) {
				cfg.Save();
			}
		}

		using (M3Card.Begin(
			"##settings_icons",
			"Hunt Icons",
			FontAwesomeIcon.Image,
			M3.Scheme.Secondary,
			subtitle: "Adjusts hunt bill icon tooltips and local overlay previews.")) {
			if (M3Widgets.RowDragFloat(
				"Hunt icon scale",
				ref cfg.IconScale,
				0.2f,
				2.0f,
				"%.2fx",
				"Scale factor for hunt mark preview icons.",
				step: 0.05f)) {
				this.savePending = true;
			}

			if (M3Widgets.RowColor(
				"Hunt icon background colour",
				ref cfg.IconBackgroundColour,
				new Vector4(0.76f, 0.75f, 0.76f, 0.8f),
				"Background colour drawn behind hunt mark icons.")) {
				this.savePending = true;
			}
		}

		if (Plugin.EspConsumer?.IsAvailable == true) {
			using (M3Card.Begin(
				"##settings_esp",
				"Integrations",
				FontAwesomeIcon.Plug,
				M3.Scheme.Primary,
				subtitle: "Third-party plugin integrations.")) {
				if (M3Widgets.RowSwitch(
					"Enable XivEsp plugin integration",
					ref cfg.EnableXivEspIntegration,
					"Add quick XivEsp search buttons next to hunt marks.")) {
					cfg.Save();
				}

				if (M3Widgets.RowSwitch(
					"Set XivEsp search on '/hbr next'",
					ref cfg.AutoSetEspSearchOnNextHuntCommand,
					"Automatically update XivEsp's search to the mark selected by '/hbr next'.",
					cfg.EnableXivEspIntegration)) {
					cfg.Save();
				}
			}
		}
	}
}
