using System;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace HuntBuddy.IPC;

public class EspConsumer: ConsumerBase {
	public override string RequiredPlugin { get; } = "XivEsp";
	protected override bool Validate() {
		if (this.getUnifiedSearch == null || this.setSubstringSearch == null) {
			return false;
		}

		this.getUnifiedSearch.InvokeFunc();
		return true;
	}

	private ICallGateSubscriber<string>? getUnifiedSearch;
	private ICallGateSubscriber<string, object>? setSubstringSearch;

	private void Subscribe() {
		try {
			this.getUnifiedSearch = Svc.PluginInterface.GetIpcSubscriber<string>("XivEsp.GetSearch");
			this.setSubstringSearch = Svc.PluginInterface.GetIpcSubscriber<string, object>("XivEsp.SetSubstring");
		}
		catch (Exception ex) {
			Svc.Log.Debug($"Failed to subscribe to XivEsp\nReason: {ex}");
		}
	}

	public EspConsumer() => this.Subscribe();

	public bool CanSetSearch {
		get {
			if (this.getUnifiedSearch == null) {
				return false;
			}

			try {
				string current = this.getUnifiedSearch.InvokeFunc();
				return string.IsNullOrEmpty(current) || current[0] is 'N' or 'S';
			}
			catch {
				return false;
			}
		}
	}
	public bool SearchFor(string target) {
		if (this.setSubstringSearch == null) {
			return false;
		}

		try {
			if (this.CanSetSearch) {
				this.setSubstringSearch.InvokeAction(target);
				return true;
			}
			else {
				Svc.Chat.Print("Cannot override complex (glob/regex) XivEsp search");
				return false;
			}
		}
		catch (Exception ex) {
			Svc.Log.Error($"XivEsp is not responding to IPC: {ex}");
			Svc.Chat.PrintError("XivEsp plugin is not responding");
			return false;
		}
	}
}
