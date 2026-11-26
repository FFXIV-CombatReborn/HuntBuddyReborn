using System;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace HuntBuddy.IPC;

public class TeleportConsumer: ConsumerBase {
	public override string RequiredPlugin { get; } = "TeleporterPlugin";
	protected override bool Validate() {
		if (this.consumerMessageSetting == null || this.consumerTeleport == null) {
			return false;
		}

		this.consumerMessageSetting.InvokeFunc();
		return true;
	}

	private ICallGateSubscriber<bool>? consumerMessageSetting;
	private ICallGateSubscriber<uint, byte, bool>? consumerTeleport;

	private void Subscribe() {
		try {
			this.consumerTeleport = Svc.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Teleport");
			this.consumerMessageSetting = Svc.PluginInterface.GetIpcSubscriber<bool>("Teleport.ChatMessage");
		}
		catch (Exception ex) {
			Svc.Log.Debug($"Failed to subscribe to Teleporter\nReason: {ex}");
		}
	}

	public TeleportConsumer() => this.Subscribe();

	public bool Teleport(uint aetheryteId) {
		if (this.consumerTeleport == null) {
			return false;
		}

		try {
			return this.consumerTeleport.InvokeFunc(aetheryteId, 0);
		}
		catch {
			Svc.Chat.PrintError("Teleporter plugin is not responding");
			return false;
		}
	}
}
