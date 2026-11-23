# StardewValleyMods

几个自用的《星露谷物语》SMAPI MOD。

## MOD 列表

| MOD | 功能 |
|---|---|
| [AutoDoor](AutoDoor/) | 走到房子门口时自动开门进屋，不用再点门。 |
| [MapTweaks](MapTweaks/) | 按 M 打开地图后，点空白处不再跳到背包；从菜单其他标签页切到地图时，按 ESC、菜单键或关闭按钮回到原来的标签页，而不是关掉整个菜单。每项都能单独开关。 |
| [CheatPanel](CheatPanel/) | 修改器：无敌、无限体力、移动速度、游戏速度等功能，用数字小键盘热键开关，按 F8 打开面板。 |
| [WindowScaler](WindowScaler/) | 窗口模式下按 Ctrl + `=` / `-` 按倍数缩放游戏窗口，并记住上次的大小。 |
| [HotkeyFinder](HotkeyFinder/) | 列出所有已加载 MOD 和原版游戏的快捷键，并标出冲突。游戏里按 F9 打开列表，也可以在 SMAPI 控制台输入 `hotkeys`。 |

每个 MOD 都支持 [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098)（可选），装了它就能在游戏里改配置；没装的话改 MOD 目录下的 `config.json`。

## 环境要求

- 星露谷物语 1.6
- [SMAPI](https://smapi.io/) 4.0 或更高版本

## 构建与安装

需要 [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)。在 MOD 目录里运行：

```sh
dotnet build -c Release
```

项目使用 [Pathoschild.Stardew.ModBuildConfig](https://www.nuget.org/packages/Pathoschild.Stardew.ModBuildConfig)，它会自动找到游戏目录，并把编译好的 MOD 部署到游戏的 `Mods` 文件夹。

## 许可证

[MIT](LICENSE)
