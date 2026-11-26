using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using ECommons.DalamudServices;
using HuntBuddy.Models;
namespace HuntBuddy.Utils;

/// <summary>
/// Interface utilities class.
/// </summary>
public static class InterfaceUtil {
	/// <summary>
	/// Draws hunt icons from game images.
	/// </summary>
	/// <param name="mobHuntEntry"><see cref="MobHuntEntry"/> containing relevant information.</param>
	public static void DrawHuntIcon(MobHuntEntry mobHuntEntry) {
		if (mobHuntEntry.Icon == 0) {
			return;
		}

		Vector2 cursorPos = ImGui.GetCursorScreenPos();
		Vector2 imageSize = mobHuntEntry.ExpansionId < 3 ? new Vector2(192f, 128f) : new Vector2(210f);
		imageSize *= ImGui.GetIO().FontGlobalScale * Plugin.Instance.Configuration.IconScale;
		uint backgroundColour = ImGui.ColorConvertFloat4ToU32(Plugin.Instance.Configuration.IconBackgroundColour);

		ImGui.InvisibleButton("##hunt_icon_canvas", imageSize);

		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		if (mobHuntEntry is { ExpansionId: 4, IsEliteMark: false }) // Endwalker uses circle for non elite mobs
		{
			drawList.AddCircleFilled(
				cursorPos + (imageSize / 2f),
				imageSize.X / 2f,
				backgroundColour);
		}
		else {
			drawList.AddRectFilled(
				cursorPos,
				cursorPos + imageSize,
				backgroundColour);
		}

		drawList.AddImage(LoadIcon(mobHuntEntry.Icon).Handle, cursorPos, cursorPos + imageSize);
	}

	/// <summary>
	/// Returns a IDalamudTextureWrap for an icon.
	/// Will request a Hi-Res version and fallback to normal resolution otherwise.
	/// </summary>
	/// <param name="id">Icon ID</param>
	/// <returns>IDalamudTextureWrap for the icon, or an empty wrap if the icon is not found.</returns>
	private static IDalamudTextureWrap LoadIcon(uint id) {
		return Svc.Texture.TryGetFromGameIcon(new GameIconLookup {
			IconId = id,
			HiRes = true,
		}, out ISharedImmediateTexture? texture)
			? texture.GetWrapOrEmpty()
			: Svc.Texture.GetFromGameIcon(new GameIconLookup {
				IconId = id,
				HiRes = false,
			}).GetWrapOrEmpty();
	}
}
