# 游戏内控制台通过反射复用 SMAPI fork 的 internal 类型

**游戏内控制台** 的补全、`C-r` 历史搜索、命令历史文件和命令执行，都通过反射调用本地 SMAPI fork（`E:\project\SMAPI`）里的 `CommandManager`、`CommandQueue`、`HistorySearchState` 等 internal 类型，MOD 自己只写游戏内界面和按键处理。代价是这个 MOD 只能配合这个 fork 使用：在官方 SMAPI 上反射会失败，MOD 记一条日志后停用。

## 考虑过的方案

- **把代码写进 SMAPI fork，或给 `ICommandHelper` 加公开 API**：不需要反射，但每次改动都要同时改两个仓库并重新部署 SMAPI。
- **MOD 自己完整实现，不依赖 fork**：能在官方 SMAPI 上用，但要重写 fork 里已有测试覆盖的模糊补全、参数补全（351 个 debug 命令、本地化名称）和 Atuin 式历史搜索，而且两边的行为会逐渐不一致。
