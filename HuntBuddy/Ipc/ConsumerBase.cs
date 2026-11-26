using System;
using Dalamud.Plugin;
using ECommons.DalamudServices;

namespace HuntBuddy.IPC;

public abstract class ConsumerBase {
	public const int MsPerAvailabilityRecheck = 5000;

	public abstract string RequiredPlugin { get; }

	private long timeSinceLastCheck = -MsPerAvailabilityRecheck;

	public bool IsAvailable {
		get {
			if (Environment.TickCount64 > this.timeSinceLastCheck + MsPerAvailabilityRecheck) {
				bool loaded = false;
				foreach (IExposedPlugin plugin in Svc.PluginInterface.InstalledPlugins) {
					if (plugin.IsLoaded && plugin.InternalName == this.RequiredPlugin) {
						loaded = true;
						break;
					}
				}

				if (loaded) {
					try {
						field = this.Validate();
					}
					catch {
						field = false;
					}
				}
				else {
					field = false;
				}
				this.timeSinceLastCheck = Environment.TickCount64;
			}

			return field;
		}
	}

	protected abstract bool Validate();
}
