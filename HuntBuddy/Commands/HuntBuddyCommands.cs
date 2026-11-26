using System;
using Dalamud.Game.Command;
using ECommons.DalamudServices;

namespace HuntBuddy.Commands;

public static class HuntBuddyCommands {
	public const string Command = "/hbr";

	private const string HelpMessage =
		"Toggles UI\nArguments:\nreload - Reloads data\nlocal - Toggles the local hunt marks window\nnext - Flags the next hunt target to find\nnext tp - Flags the next hunt target and teleports if in another zone\nroute - Opens the optimal hunt route page\nlist - list all hunt targets by expansion\nmarks - Opens the Mark of the X achievements page\nmilestones - Opens the hunt kill milestones page\nmarks log - Refreshes and logs all Mark achievement progress and data entries\nsettings - Opens the General settings page";

	public static void Enable() {
		Svc.Commands.AddHandler(Command, new CommandInfo(OnCommand) {
			HelpMessage = HelpMessage,
			ShowInHelp = true,
		});
	}

	public static void Disable() => Svc.Commands.RemoveHandler(Command);

	public static void OnCommand(string command, string args) {
		try {
			switch (args.Trim().ToLowerInvariant()) {
				case "reload":
					Plugin.Instance.HuntBillService.RequestReload();
					break;
				case "local":
					Plugin.Instance.Configuration.ShowLocalHunts = !Plugin.Instance.Configuration.ShowLocalHunts;
					Plugin.Instance.Configuration.Save();
					break;
				case "next":
					Plugin.Instance.HuntBillService.FlagNextTarget();
					break;
				case "next tp":
				case "next teleport":
					Plugin.Instance.HuntBillService.FlagNextTarget(teleport: true);
					break;
				case "route":
					Plugin.Instance.MainWindow.Open(HuntBuddy.UI.MainWindow.RoutePageId);
					break;
				case "ls":
				case "list":
					Plugin.Instance.HuntBillService.PrintList();
					break;
				case "marks":
				case "achievements":
					Plugin.Instance.OpenMarkAchievementsUi();
					break;
				case "milestones":
					Plugin.Instance.MainWindow.Open(HuntBuddy.UI.MainWindow.MilestonesPageId);
					break;
				case "settings":
				case "config":
					Plugin.Instance.OpenConfigUi();
					break;
				case "marks log":
				case "marks refresh":
				case "achievements log":
					Svc.Chat.Print(Plugin.Instance.MarkAchievementService.RefreshAll(true)
						? "Refreshing and logging all Mark of the X achievement progress and data entries."
						: "Achievement data has not loaded yet. Please try again in a moment.");
					break;
				default:
					Plugin.Instance.MainWindow.ToggleWindow();
					break;
			}
		}
		catch (Exception e) {
			Svc.Log.Error($"Error in command handler: {e}");
		}
	}
}
