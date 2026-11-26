using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using RebornMaterial;

namespace HuntBuddy.UI;

public partial class MainWindow {
	private const float HeroLogoSize = 56f;

	private void DrawAboutPage() {
		M3Scheme s = M3.Scheme;
		string version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

		using (M3Card.Begin("##about_hero", null, style: M3CardStyle.Elevated)) {
			DrawHeroTitle();
			ImGui.Dummy(new Vector2(0f, M3.Space2));
			M3Text.Draw(
				"Hunt Buddy Reborn tracks your active daily and weekly hunt bills, flags map coordinates, integrates with teleport and XivEsp, " +
				"and tracks ARR, Heavensward, and Stormblood \"Mark of the X\" elite hunt achievements with S-rank spawn requirements and Hunt Bill indicators.",
				M3TextStyle.Muted,
				true);

			ImGui.Dummy(new Vector2(0f, M3.Space2));
			_ = M3Widgets.Pill("##about_version", $"v{version}", s.Primary, FontAwesomeIcon.CodeBranch);
			ImGui.SameLine(0f, M3.Space1);
			_ = M3Widgets.Pill("##about_marks", $"{this.markService.Achievements.Count} Mark achievements", s.Tertiary, FontAwesomeIcon.Trophy);

			ImGui.Dummy(new Vector2(0f, M3.Space2));
			if (M3Widgets.Button("##about_discord", "Discord", M3ButtonStyle.Outlined, FontAwesomeIcon.Comments)) {
				OpenDiscord();
			}
		}

		using (M3Card.Begin("##about_commands", "Commands", FontAwesomeIcon.Terminal, s.Tertiary)) {
			CommandRow("/hbr", "Toggles this window.");
			CommandRow("/hbr reload", "Reloads active daily and weekly hunt bill data.");
			CommandRow("/hbr local", "Toggles the local hunt marks HUD overlay.");
			CommandRow("/hbr next", "Flags the next incomplete hunt target on your map.");
			CommandRow("/hbr next tp", "Flags the next hunt target and teleports if in another zone.");
			CommandRow("/hbr route", "Opens the optimal multi-zone Hunt Route page.");
			CommandRow("/hbr list", "Prints active hunt bill targets to chat by expansion.");
			CommandRow("/hbr marks", "Opens the Mark of the X achievements view.");
			CommandRow("/hbr milestones", "Opens the hunt kill milestones achievements view.");
			CommandRow("/hbr marks log", "Refreshes and logs all hunt achievement progress and Data IDs.");
			CommandRow("/hbr settings", "Opens the General settings page.");
		}
	}

	private static void DrawHeroTitle() {
		float logoSize = HeroLogoSize * M3.Scale;
		Vector2 origin = ImGui.GetCursorScreenPos();
		M3Brand.DrawLogo(ImGui.GetWindowDrawList(), origin, logoSize);

		using (ImRaii.PushFont(M3.TitleLarge))
		using (ImRaii.PushColor(ImGuiCol.Text, M3.Scheme.Primary)) {
			float titleTop = origin.Y + ((logoSize - ImGui.GetTextLineHeight()) * 0.5f);
			ImGui.SetCursorScreenPos(new Vector2(origin.X + logoSize + M3.Space3, titleTop));
			ImGui.TextUnformatted("Hunt Buddy Reborn");
		}

		ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + logoSize));
		ImGui.Dummy(Vector2.Zero);
	}

	private static void CommandRow(string command, string description) {
		M3RowInfo row = M3SettingRow.Begin(command, description, Vector2.Zero);
		M3SettingRow.End(row);
	}
}
