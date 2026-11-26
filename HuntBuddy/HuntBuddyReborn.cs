using System;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using HuntBuddy.Commands;
using HuntBuddy.IPC;
using HuntBuddy.Models;
using HuntBuddy.Services;
using HuntBuddy.UI;
using HuntBuddy.UI.Overlays;
using RebornMaterial;

namespace HuntBuddy;

public sealed class Plugin: IDalamudPlugin {
	public static string Name => "Hunt Buddy Reborn";

	public readonly Configuration Configuration;
	public HuntBillService HuntBillService { get; }
	public MarkAchievementService MarkAchievementService { get; }
	public static TeleportConsumer? TeleportConsumer { get; private set; }
	public static EspConsumer? EspConsumer { get; private set; }

	private WindowSystem WindowSystem {
		get;
	}

	internal MainWindow MainWindow {
		get;
	}

	public static Plugin Instance {
		get;
		internal set;
	} = null!;

	public Plugin(IDalamudPluginInterface pluginInterface) {
		Instance = this;

		ECommonsMain.Init(pluginInterface, this);

		this.Configuration = (Configuration)(Svc.PluginInterface.GetPluginConfig() ?? new Configuration());
		this.Configuration.CharacterMarkProgress ??= [];
		if (this.Configuration.Version < Configuration.CurrentVersion) {
			this.Configuration.UiAccentColor = Configuration.DefaultAccent;
			this.Configuration.Version = Configuration.CurrentVersion;
			this.Configuration.Save();
		}

		M3.ElementBaseline = 0.75f;
		M3.Initialize(pluginInterface, this.Configuration);
		M3Brand.Name = Name;
		M3Brand.Version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
		M3Brand.FallbackIcon = FontAwesomeIcon.Crosshairs;
		M3Brand.LogoCornerFraction = 0.04f;
		M3Brand.SetLogoResource(typeof(Plugin).Assembly, "HuntBuddy.Resources.HBR_Icon.png");

		this.HuntBillService = new HuntBillService();
		this.MarkAchievementService = new MarkAchievementService(this.HuntBillService);

		this.MainWindow = new MainWindow(this.HuntBillService, this.MarkAchievementService);

		this.WindowSystem = new WindowSystem("HuntBuddy");
		this.WindowSystem.AddWindow(this.MainWindow);
		this.WindowSystem.AddWindow(new HuntBillOverlayWindow(this.MarkAchievementService));
		this.WindowSystem.AddWindow(new LocalHuntsWindow(this.HuntBillService, this.MarkAchievementService));

		Plugin.TeleportConsumer = new TeleportConsumer();
		Plugin.EspConsumer = new EspConsumer();

		HuntBuddyCommands.Enable();
		Svc.PluginInterface.UiBuilder.Draw += this.DrawUi;
		Svc.PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
		Svc.PluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;
	}

	private void DrawUi() {
		M3Window.DefaultDensity = this.Configuration.UiCompactLayout ? M3Density.Tight : M3Density.Compact;
		M3.BeginFrame();
		this.WindowSystem.Draw();
	}

	public void OpenConfigUi() => this.MainWindow.Open(MainWindow.SettingsPageId);
	public void OpenMainUi() => this.MainWindow.Open(MainWindow.BillsPageId);
	public void OpenMarkAchievementsUi() => this.MainWindow.Open(MainWindow.MarksAllPageId);

	private void Dispose(bool disposing) {
		if (!disposing) {
			return;
		}

		Svc.PluginInterface.UiBuilder.Draw -= this.DrawUi;
		Svc.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
		Svc.PluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;

		HuntBuddyCommands.Disable();
		Plugin.TeleportConsumer = null;
		Plugin.EspConsumer = null;
		this.MarkAchievementService.Dispose();
		this.HuntBillService.Dispose();
		this.WindowSystem.RemoveAllWindows();
		M3.Dispose();
		ECommonsMain.Dispose();
	}

	public void Dispose() {
		this.Dispose(true);
		GC.SuppressFinalize(this);
	}
}
