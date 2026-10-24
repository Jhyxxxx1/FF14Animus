# 数据来源与致谢

本仓库只发布本模块源码与编译产物，不重新分发下列第三方插件或库。相关项目的代码、数据与名称归其各自权利人所有，适用各项目自身的许可及使用条件。

## 客户端与运行时数据

书名、古武、职业、怪物、任务类型、地点、动作、交互对象及进度，来自当前客户端的Relic、RelicNote、MonsterNoteTarget、Item、ClassJobCategory、Fate、Leve、Level、BNpcName、EObj、Emote、TextCommand等游戏表及原生状态。

- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs)：客户端结构、方法签名及公开字段参考。
- [LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental)：通过Omni提供的公开API读取MobSpawn资源。没有把该库或其源代码打包进模块。位置数据可能缺失或不精确，运行时仍需检查区域和可达性。

## 宿主与集成

- [OmniToolbox.TreePublic](https://github.com/YouShux/OmniToolbox.TreePublic)
- [OmniToolbox.Common](https://github.com/YouShux/OmniToolbox.Common)
- [OmniToolbox.TreeHouseOnline](https://github.com/YouShux/OmniToolbox.TreeHouseOnline)
- [vnavmesh](https://github.com/awgil/ffxiv_navmesh)：通过IPC使用导航查询与路径执行。
- [BossMod Reborn](https://github.com/FFXIV-CombatReborn/BossModReborn)：通过IPC与经过形状检查的内部接口使用预设及自动移动；不包含其实现。
- AutoDuty：使用本机安装版本的公开IPC执行已有副本路线，不包含其程序、路线文件或依赖库。

## 地点事实与任务关联研究

[Angles24/ZodiacWeapons](https://github.com/Angles24/ZodiacWeapons)中的公开配置用于参考地点、目标编号、前置活动等事实，未复制其行为执行代码。

- Skyfire I / II敌人配置：第一、二卷备用位置；作者Zamphire，标注Code Credit nishap。
- Skyfire I / II、Netherfire I、Skyfall I / II FATE配置：目标ID及610→611、570→571、568→569等前置关联；作者Zamphire，标注nishap。活动信息另与当前客户端数据核对。
- Camp Bluefog/Circling the Ceruleum：646巡逻位置与2001104交互对象。
- Whitebrim/Necrologos Whispers of the Gem：649场地搜索位置与目标关联。
- Whitebrim/The Area’s a Bit Sketchy：652巡逻位置与对象。
- Camp Bluefog/Someone’s in the Doghouse：647与警犬关联的研究参考。该旧配置本身未完成引导流程，未采用其被注释的执行方案；本模块的分段导航、定向招手与保护逻辑单独实现。

相关GuildLeves原配置署名Zamphire，部分文件列出的Code Credit包括TuckMeIntoBread、Kayla、Sodimm、y2krazy、Ken_512；具体以各原文件署名为准。

ZodiacBuddyReborn曾作为概念与数据关联的研究参考，未将其源码或程序纳入本模块构建或发布包。

## 本项目声明

本模块使用的直接游戏表数据、坐标及任务关联不构成对第三方代码的许可声明。作者仅就拥有权利的本模块内容作出LICENSE中的使用范围说明；第三方权利不因本仓库发布而改变。
