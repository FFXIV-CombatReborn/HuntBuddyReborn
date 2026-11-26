using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HuntBuddy.Models;
using HuntBuddy.Services;
using RebornMaterial;
using Bounds = FFXIVClientStructs.FFXIV.Common.Math.Bounds;

namespace HuntBuddy.UI.Overlays;

public unsafe class HuntBillOverlayWindow: M3Window {
	private static readonly string[] billAddonNames = ["Mobhunt", "Mobhunt2", "Mobhunt3"];

	private readonly MarkAchievementService markService;
	private byte[] cachedRawName = [];
	private string cachedMobName = string.Empty;
	private Bounds cachedBounds;

	public HuntBillOverlayWindow(MarkAchievementService markService) : base(
		"##HuntBuddyHuntBillOverlay",
		ImGuiWindowFlags.NoDecoration
		| ImGuiWindowFlags.NoMove
		| ImGuiWindowFlags.NoSavedSettings
		| ImGuiWindowFlags.NoFocusOnAppearing
		| ImGuiWindowFlags.NoNav
		| ImGuiWindowFlags.AlwaysAutoResize
		| ImGuiWindowFlags.NoDocking,
		false) {
		this.markService = markService;
		this.Size = Vector2.Zero;
		this.SizeCondition = ImGuiCond.Always;
		this.IsOpen = true;
		this.ShowCloseButton = false;
		this.RespectCloseHotkey = false;
	}

	public override bool DrawConditions() {
		if (!Svc.PlayerState.IsLoaded
			|| Svc.PlayerState.ContentId == 0
			|| Svc.Condition[ConditionFlag.WatchingCutscene]
			|| Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]) {
			return false;
		}

		return this.TryReadVisibleHuntBill();
	}

	protected override void PreDrawThemed() {
		base.PreDrawThemed();

		ImGuiViewportPtr viewport = ImGui.GetMainViewport();
		float centerX = viewport.Pos.X + ((this.cachedBounds.Pos1.X + this.cachedBounds.Pos2.X) * 0.5f);
		float belowY = viewport.Pos.Y + this.cachedBounds.Pos2.Y + 4f;
		float maxY = viewport.Pos.Y + viewport.Size.Y - 48f;
		float posY = belowY > maxY ? viewport.Pos.Y + this.cachedBounds.Pos1.Y - 44f : belowY;
		posY = Math.Clamp(posY, viewport.Pos.Y, Math.Max(viewport.Pos.Y, maxY));

		ImGui.SetNextWindowPos(new Vector2(centerX, posY), ImGuiCond.Always, new Vector2(0.5f, 0f));
	}

	public override void Draw() {
		if (this.markService.TryGetMarkByName(this.cachedMobName, out EliteMarkInfo? mark)) {
			MarkAchievementInfo achievement = mark.Achievement;
			bool isNeeded = !this.markService.IsMarkChecked(mark);

			string progressText = achievement.ProgressLoaded
				? $" ({achievement.ProgressCurrent}/{achievement.ProgressMax})"
				: string.Empty;

			if (isNeeded) {
				string label = $"Needed for {achievement.Name}{progressText}";
				string tooltip = $"{achievement.Description}\nClick to open Mark Achievements tracker.";
				if (M3Widgets.Pill("##hunt_bill_mark_pill", label, M3.Scheme.Warning, FontAwesomeIcon.Trophy, tooltip, true)) {
					Plugin.Instance.MainWindow.Open(MainWindow.MarksNeededPageId);
				}
			}
			else {
				string status = achievement.IsComplete ? "Complete" : "Tracked";
				string label = $"Not needed - {achievement.Name} ({status})";
				string tooltip = $"{achievement.Description}\nClick to open Mark Achievements tracker.";
				if (M3Widgets.Pill("##hunt_bill_mark_pill", label, M3.Scheme.Success, FontAwesomeIcon.CheckCircle, tooltip, true)) {
					Plugin.Instance.MainWindow.Open(MainWindow.MarksAllPageId);
				}
			}
			return;
		}

		M3Widgets.Pill(
			"##hunt_bill_mark_pill",
			"Not needed for any Mark achievement",
			M3.Scheme.OnSurfaceVariant,
			FontAwesomeIcon.MinusCircle,
			$"{this.cachedMobName} is not part of any Mark of the X achievement.",
			false);
	}

	private bool TryReadVisibleHuntBill() {
		AtkUnitBase* addon = GetVisibleBillAddon();
		if (addon == null) {
			return false;
		}

		AtkTextNode* nameNode = addon->GetTextNodeById(3);
		if (nameNode == null || !nameNode->IsVisible()) {
			return false;
		}

		ReadOnlySpan<byte> rawName = nameNode->NodeText.AsSpan();
		if (!rawName.SequenceEqual(this.cachedRawName)) {
			this.cachedRawName = rawName.ToArray();
			this.cachedMobName = SeString.Parse(rawName).TextValue.Trim();
		}

		if (this.cachedMobName.Length == 0) {
			return false;
		}

		Bounds bounds;
		AtkResNode* anchorNode = addon->GetNodeById(2);
		if (anchorNode != null) {
			anchorNode->GetBounds(&bounds);
		}
		else {
			addon->GetWindowBounds(&bounds);
		}

		this.cachedBounds = bounds;
		return true;
	}

	private static AtkUnitBase* GetVisibleBillAddon() {
		foreach (string name in billAddonNames) {
			AtkUnitBase* addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;
			if (addon != null && addon->IsVisible && addon->IsReady) {
				return addon;
			}
		}

		return null;
	}
}
