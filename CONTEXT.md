# StardewValleyMod

几个自用的 SMAPI MOD。本文件只记录术语。

## Language

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
