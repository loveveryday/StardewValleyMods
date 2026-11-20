using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AutoDoor;

public class ModConfig
{
    public bool Enabled { get; set; } = true;
}

public partial class ModEntry : Mod
{
    private ModConfig config = new();

    // 地图门的 Action 名：这些本来要右键点，现在走过去顶住就触发
    private static readonly HashSet<string> DoorActions = new()
    {
        "Warp", "LockedDoorWarp", "WarpCommunityCenter", "WarpWomensLocker", "WarpMensLocker"
    };

    // 上次触发的位置：没开的门只提示一次，离开再回来才会再提示
    private string? lastTrigger;

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.GameLaunched += (_, _) => RegisterConfigMenu();
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        Farmer player = Game1.player;
        if (!config.Enabled || !Context.IsPlayerFree || !player.isMoving())
        {
            lastTrigger = null;
            return;
        }

        GameLocation location = player.currentLocation;
        Vector2 tile = player.GetGrabTile();
        string key = $"{location.NameOrUniqueName}:{tile}";
        if (key == lastTrigger || !IsDoor(location, tile))
        {
            if (key != lastTrigger)
                lastTrigger = null;
            return;
        }

        lastTrigger = key;
        // 和右键点门走同一条路径，锁门、营业时间、好感度判断都由游戏处理
        location.checkAction(new xTile.Dimensions.Location((int)tile.X, (int)tile.Y), Game1.viewport, player);
    }

    private static bool IsDoor(GameLocation location, Vector2 tile)
    {
        string? action = location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Action", "Buildings");
        if (action != null && DoorActions.Contains(action.Split(' ')[0]))
            return true;

        // 农场上的棚屋、谷仓等建筑的人用门
        var building = location.getBuildingAt(tile);
        return building?.GetIndoors() != null && building.getPointForHumanDoor() == Utility.Vector2ToPoint(tile);
    }
}
