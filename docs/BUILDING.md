# 构建与打包

## 环境

Windows、PowerShell 7，以及已安装的 Dalamud API 15 和 Omni Toolbox。编译器使用Omni目录中提供的Roslyn程序集，不需要下载第三方插件源码。不要将宿主或依赖DLL提交进本仓库。

默认从 `%APPDATA%/XIVLauncherCN` 查找最近更新且含宿主DLL的目录。存在多个渠道/版本时建议显式指定实际游戏所用目录。

```powershell
pwsh -NoProfile -File ./build.ps1
# 自定义安装位置：
./build.ps1 -OmniDirectory 'D:/Plugins/OmniToolbox/1.1.8.0' -DalamudDirectory 'D:/Dalamud/Hooks/26-10-21-01'
./package.ps1
```

产物：`dist/AnimusBookAssistant.dll`、`artifacts/FF14Animus-<版本>.zip`及SHA-256校验文件。只有编译成功才替换dist DLL。每次构建使用唯一程序集名称，避免游戏热重载复用旧程序集；因此重复构建的二进制哈希不保证相同。

`package.ps1`检查源码ReleaseVersion与VERSION一致，只收录指定公开文件；不会打包个人配置、MCP数据、战斗日志、截图或依赖库。发布前应核对压缩包清单和SHA-256。

## 版本更新

1. 同时更新源码ReleaseVersion、VERSION及CHANGELOG。
2. 编译、打包并在游戏本地模块中重载核验。
3. 将对应源码及编译产物一起提交，确保DLL与源码来自同一次构建。
4. 上传版本包及SHA-256文件作为GitHub Release资产；预览版标记Pre-release。

仓库根目录DLL供直接下载，package.ps1会用最新构建产物更新它。不要只更新源码而遗留旧DLL。
