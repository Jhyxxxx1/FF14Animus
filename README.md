# 古武小黄书助手

Omni Toolbox 本地模块 · **0.1.0-preview.4** · 作者 [Jhyxxxx1](https://github.com/Jhyxxxx1)

[下载 DLL](https://github.com/Jhyxxxx1/FF14Animus/raw/refs/heads/main/AnimusBookAssistant.dll) · [下载安装包](https://github.com/Jhyxxxx1/FF14Animus/raw/refs/heads/main/downloads/FF14Animus-0.1.0-preview.4.zip) · [问题反馈](https://github.com/Jhyxxxx1/FF14Animus/issues/new/choose)

## 安装

1. 安装 Omni Toolbox、vnavmesh、AutoDuty、BossMod Reborn。
2. 下载 DLL，在 Omni Toolbox 的「插件设置 → 本地模块」中添加并启用。
3. 准备对应职业的普通套装，将古武放在背包或兵装库，备好基萨尔野菜。
4. 打开模块，点击「开始连续执行」。

更新时覆盖原 DLL，在本地模块列表中重新加载。

## 使用说明

vnavmesh, AutoDuty, Bossmod.

插件会自动读取当前的小黄书，全自动完成，并且交付完成的小黄书，然后换新书。

理符限额、诗学不够会停止运行。理符受理权用尽时，先做完本书其他可执行任务再停止。

默认使用坐骑并优先飞行；FATE 一轮未刷新时等待 5 分钟，期间继续检查当前地图。战斗结束后立即巡查剩余目标。

## 使用范围

- 当前 FATE 自动战斗配置仅支持白魔法师；RSR、AE、WrathCombo 尚未接入。
- 九本书的全部特殊机制尚未逐项验证；没有副本路线或遇到不支持的交互时会停止并提示。
- 死亡后等待复活再继续；自动躲避取决于 BMR 对当前攻击的支持。
- 「小黄书」只保存实际读取到的历史记录，「未知」不代表未完成。

遇到问题，请附上模块版本、书名、任务名和界面状态截图。

[更新记录](CHANGELOG.md) · [数据来源与致谢](DATA-SOURCES.md) · [版权与使用范围](LICENSE)

本项目保留版权，暂不授予开源许可。
