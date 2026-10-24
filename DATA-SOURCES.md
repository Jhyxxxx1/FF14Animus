# 数据来源

模块运行时从当前客户端的 Relic、RelicNote、MonsterNoteTarget、ContentFinderCondition、Leve、Level 等游戏表读取名称、ID、要求和地点。

怪物候选出生点使用 Omni 自带 LuminaSupplemental.Excel 的 MobSpawn 资源，经公开 API 读取，未将该库或其源代码打包进模块。项目：https://github.com/Critical-Impact/LuminaSupplemental 。数据存在占位坐标和缺失情况，因此运行时还会检查区域、可达性和目标实际加载状态。

《火天文书》第一卷的 10 个备用世界坐标参考公开的 ZodiacWeapons/Skyfire I 敌人配置中的地点事实。原配置标注作者 Zamphire，Code Credit: nishap；这里只记录地图位置，没有使用 RebornBuddy 实现或行为代码。来源：https://github.com/Angles24/ZodiacWeapons/blob/master/Animus/Skyfire%20I/Skyfire%20I%20-%20Enemies.xml 。备用位置仍须通过当前 vnavmesh 投影检查。

客户端结构参考：https://github.com/aers/FFXIVClientStructs 。寻路和副本通过原插件公开 IPC 调用，不包含其程序实现。

第一卷 FATE 的三个等待位置和 610→611 的前置关系参考同仓库 Skyfire I - Fates.xml 中的地点事实（作者 Zamphire，标注 nishap）。活动 480、589、610、611 的名称、等级和目标类型另与运行中客户端 Fate 表核对。只使用坐标和活动关联，不复制配置行为或执行代码。实际活动位置、范围、状态和进度以当前区域 FateManager 为准。

ZodiacBuddyReborn 作为概念和数据关联的研究参考保存在 references 中，未将其程序或源代码复制到模块构建。

## 第一卷理符交互数据

索敌理符 849 的“戳指”、流光水晶和伪装目标规则来自运行中客户端 Leve 描述、Emote、EventItem 和 BNpcName 表：Emote 28；EventItem 2000558；徘徊妖火 491、追猎蜻蜓飞蜥 1638、鲜血短尾雕 1789、浮尘小恶魔 1788。低版本公开路线只用于比对事实，不复制其执行实现。

军队类别按客户端 LeveAssignmentType 表读取；第三项 868 为双蛇党。地点由 Level 表读取，额外投影到当前导航网格地面，修正表内高度与可走地面不一致。

第二卷的十个备用怪物位置取自同仓库 Animus/Skyfire II/Skyfire II - Enemies.xml 地点事实，仍标注作者Zamphire/nishap；571前置570的关联用Skyfire II - Fates.xml作事实对照。忘忧泪2000872、巴罗尔之铃1738、恶神之眼1739另与客户端EventItem/BNpcName表核对。换书NPC通过运行中对象表核对BaseId1006976、摩杜纳坐标(71.95,33.07,-706.29)；不复制外部插件执行代码。

第三卷巡逻理符646的三个搜索坐标与交互对象2001104，参考ZodiacWeapons/GuildLeves/Camp Bluefog/Circling the Ceruleum.xml中的地点与对象事实；原文件署名Zamphire，Code Credit TuckMeIntoBread/Kayla/Sodimm/y2krazy/Ken_512。仅提取事实，未复制其执行代码。646、860、863的名称、类型及地点已与运行中客户端Leve表核对；2001104另核对EObj表。新增第三卷理符执行仍待实测。
