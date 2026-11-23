# StardewValleyMods

几个自用的 SMAPI MOD。本文件只记录术语。

## Language

### 快捷键

**快捷键 (Keybind)**:
某个 **归属方** 的一项按键设置，可以有多个可选组合，任意一个按下都会触发。
_Avoid_: 热键、按键（"按键"只指单个键）

**组合 (Key combo)**:
必须同时按住的一组键，例如 `LeftControl + OemPlus`。单个键也算只有一个键的组合。

**归属方 (Owner)**:
快捷键属于谁：某个已加载的 MOD，或原版游戏。

**冲突 (Conflict)**:
两个不同的 **归属方** 的快捷键里出现完全相同的 **组合**。`Ctrl + F5` 和 `F5` 不算冲突。

**来源 (Source)**:
快捷键是从哪里读出来的：MOD 在 GMCM 里注册的配置项、MOD 的 `config.json`，或原版游戏的按键设置。

### 控制台

**游戏内控制台 (In-game console)**:
在游戏画面里打开的命令输入界面，和 **SMAPI 窗口** 执行同一套命令、共用同一份 **命令历史**。
_Avoid_: 控制台（不加限定时有歧义）、终端

**SMAPI 窗口 (SMAPI console window)**:
游戏启动时附带的那个命令行窗口。
_Avoid_: 终端、控制台

**命令 (Command)**:
SMAPI 或某个 MOD 注册的一条控制台命令，例如 `player_add`。原版调试命令不单独算命令，要经由 `debug` 这条命令执行，例如 `debug warp Farm`。
_Avoid_: 指令

**命令历史 (Command history)**:
执行过的命令行，跨游戏启动保留，**游戏内控制台** 和 **SMAPI 窗口** 共用一份。

**补全 (Completion)**:
按当前光标所在位置给出的候选：命令名或参数，按模糊匹配排序。
