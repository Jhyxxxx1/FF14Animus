using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Game.DutyState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using OmenTools.Extensions;
using ModuleBase = OmniToolbox.Common.Module.Abstractions.ModuleBase;
using NativeEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmenTools.Interop.Game.Lumina;
using SheetRelicNote = Lumina.Excel.Sheets.RelicNote;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace OmniToolbox.TreePublic;

// 进度以客户端状态为准，执行由用户启动。
public sealed unsafe class AnimusBookAssistant : ModuleBase
{
    private const string ReleaseVersion = "0.1.0-preview.2";
    public override ModuleInfo Info { get; } = new()
    {
        Title = "古武小黄书助手",
        Description = "读取古武书本进度，自动赶路、战斗与副本执行；支持连续任务及完成后换书。适配范围见使用说明。",
        Category = ModuleCategory.Automation,
        Author = "Jhyxxxx1",
        SupportUrls = ["https://github.com/Jhyxxxx1/FF14Animus"],
        Commands = [new("打开小黄书助手", "/animusbook open")]
    };

    private readonly byte[] monsters = new byte[10];
    private readonly string[] monsterLabels = new string[10];
    private readonly byte[] monsterRequired = new byte[10];
    private readonly string[] dungeonLabels = new string[3];
    private readonly string[] fateLabels = new string[3];
    private readonly string[] leveLabels = new string[3];
    private string bookName = "未映射";
    private string leveDebug = "";
    private uint lastRangeOrLos;
    private int teleportRetries;
    private IDutyState dutyState;
    private long dutyCompletedAt;
    private bool dutyExitRequested;
    private readonly HashSet<ulong> pokedWisps = new();
    private readonly Dictionary<ulong, long> crystalAttempts = new();
    private ulong pokeTarget;
    private ulong crystalTarget;
    private ulong crystalPending;
    private int crystalCountBefore;
    private long crystalUsedAt;
    private readonly Dictionary<ulong, int> crystalFailures = new();
    private ulong settleTarget;
    private Vector3 settlePosition;
    private long settledAt;
    private long pokeAt;
    private readonly bool[] dungeons = new bool[3];
    private readonly bool[] fates = new bool[3];
    private readonly bool[] leves = new bool[3];
    private readonly List<TargetSnapshot> targets = new();
    private bool windowOpen;
    private byte relicId;
    private byte bookId;
    private int objectiveBits;
    private string status = "尚未读取";
    private long nextRead;
    private DateTime? lastRead;
    private bool failed;
    private IObjectTable objectTable = null!;
    private IClientState clientState = null!;
    private ICondition condition = null!;
    private ITargetManager targetManager = null!;
    private string executionStatus = "待机";
    private RunMode mode;
    private byte runBook;
    private byte runRelic;
    private uint runTerritory;
    private ulong runCharacter;
    private int lastProgress;
    private long progressAt;
    private long phaseAt;
    private ulong targetId;
    private CancellationTokenSource pathCancellation;
    private Task<List<Vector3>> pathTask;
    private bool ownsMovement;
    private bool ownsDuty;
    private bool ownsOverrides;
    private int dutySlot = -1;
    private uint dutyTerritory;
    private bool enteredDuty;
    private bool dutyWasBusy;
    private string queuedAction;
    private bool preparingEquipment;
    private bool weaponEquipRequested;
    private long equipmentRequestedAt;
    private long equipmentStableAt;
    private uint equipmentJob, equipmentWeapon;
    private byte equipmentBook, equipmentRelic;
    private ulong equipmentCharacter;
    private string equipmentStatus = "";
    private ulong historyCharacter;
    private Dictionary<string, BookHistoryEntry> bookHistory = new();
    private bool historyWritable, historyDirty;
    private long historyRetryAt;
    private string historyStatus = "";
    private long deathPausedAt;
    private bool equipmentContextPending;
    private readonly uint[] dungeonTerritories = new uint[3];
    private readonly string[] dungeonNames = new string[3];
    private readonly uint[] monsterNameIds = new uint[10];
    private uint requiredWeapon;
    private bool hasSlash;
    private long lastCastAt;
    private long playerMissingAt;
    private string diagnostics = "尚未检查";
    private bool singleDuty;
    private Task<List<MobSpawnPosition>> spawnLoad;
    private readonly uint[] monsterZones = new uint[10];
    private RoutePoint routePoint;
    private long routeAt;
    private long arrivedAt;
    private bool teleportRequested;
    private uint teleportDestination;
    private long teleportAt;
    private int routeAttempt;
    private string spawnStatus = "尚未加载";
    private readonly HashSet<(uint Territory, Vector3 Point)> rejectedSpawns = new();
    private IGameGui gameGui = null!;
    private readonly uint[] leveIds = new uint[3];
    private int leveSlot = -1;
    private bool singleLeve;
    private long leveAt;
    private long leveUiAt;
    private string levePhase = "未开始";
    private byte labelsForBook;
    private byte labelsForRelic;
    private bool labelsReady;
    private AnimusBookAssistantConfig config = new();
    private string settingsStatus = "";
    private bool settingsWritable = true;
    private bool pathFlying;
    private bool awaitingMount;
    private long mountRequestedAt;
    private long mountReadyAt;
    private long mountFallbackUntil;
    private long walkingRecoveryUntil;
    private long leveReturnAt;
    private int patrolPoint;
    private long patrolArrivedAt;
    private static readonly Vector3[] Patrol646Points =
    [new(-7.641754f, 3.0246f, 359.4961f), new(30.55565f, 6.980818f, 324.5174f), new(14.06814f, 6.060303f, 273.8956f)];
    private static readonly Vector3[] Search649Points =
    [new(-522.1238f, 206.3355f, -331.3058f), new(-607.6644f, 204.2211f, -412.5702f)];
    private static readonly Vector3[] Patrol652Points = [new(-412.2287f, 223.9546f, -157.087f)];
    private long dogBeckonAt;
    private bool dogGuideInitialized;
    private int dogGuidePhase;
    private long dogPhaseAt;
    private Vector3 dogStepStart;
    private Vector3 dogLastBeckonPosition;
    private int dogNoFollowAttempts;
    private bool dogProtecting;
    private int dogRetryAttempts;
    private long flightFallbackUntil;
    private bool landing;
    private long landingAt;
    private long dismountRequestedAt;
    private string travelStatus = "待机";
    private const string Command = "/animusbook";
    private enum RunMode { Idle, Nearby, Dungeons, WorldMonsters, Leves, Fates, Exchange }
    private bool autoBooks;
    private long autoNextAt, exchangeAt;
    private byte exchangeFrom;
    private string autoStatus = "未开启连续书本";
    private readonly uint[] fateTerritories = new uint[3];
    private readonly ushort[] fateIds = new ushort[3];
    private int fateSlot = -1;
    private ushort activeFateId;
    private long fateWaitAt, fateRunAt, fateEndedAt, fateSyncAt;
    private bool fateEngaged;
    private string fatePhase = "未开始";
    private readonly long[] fateVisited = new long[3];
    private uint fateScanTerritory;
    private long fateScanReadyAt;
    private long fateRoundResumeAt;
    private long fateInteractionAt, nextFateUiAt;
    private string rotationPreset, previousRotation;
    private bool rotationActive, previousRotationDisabled;
    private object combatAiConfig;
    private readonly Dictionary<System.Reflection.FieldInfo, object> combatAiSaved = new();
    private bool ownsCombatAi;
    private string borrowedRotation;
    private string previousAiPreset;
    private bool combatAiPresetOwned;
    private Vector3? fateEscapeGoal;
    private long fateEscapeAt;
    private long companionRequestedAt, companionSettleAt;
    private int companionAttempts;
    private string companionStatus = "未检查";
    private bool clearingFateAggressors;
    private long fateCleanupAt;
    private string fateCleanupStatus = "未检查";
    private readonly Dictionary<ulong, long> blockedTargets = new();
    private ulong blockedApproachTarget;
    private long blockedApproachAt;
    private Vector3 pathProgressPosition;
    private long pathProgressAt;
    private long pathDeadlineAt;
    private uint refreshLeveId;
    private int leveRefreshAttempts;
    private bool refreshLeveAccepted, refreshRewardRequested;
    private int stalledPathRetries;
    private bool preparingGroundLanding;
    private bool groundLandingInProgress;
    private Vector3? landingApproachGoal;
    private long landingSearchAt;
    private readonly List<Vector3> failedLandingPoints = new();
    private Vector3 landingDestination;
    private (uint Territory, Vector3 Point) FateWaitPoint(int slot) => (fateTerritories[slot], default);
    private ushort FatePrecursor(int slot) => fateIds[slot] switch { 611 => 610, 571 => 570, 569 => 568, _ => 0 };
    private sealed record RoutePoint(uint Territory, Vector3 Probe, int Slot);

    public override bool HasSettings => true;

    protected override void OnEnable()
    {
        LoadSettings();
        TraceLifecycle("enable.begin");
        objectTable = GetService<IObjectTable>();
        clientState = GetService<IClientState>();
        condition = GetService<ICondition>();
        targetManager = GetService<ITargetManager>();
        gameGui = GetService<IGameGui>();
        dutyState = GetService<IDutyState>();
        failed = false;
        spawnLoad = Task.Run(() => CsvLoader.LoadResource<MobSpawnPosition>(CsvLoader.MobSpawnResourceName, true, out _, out _));
        nextRead = 0;
        DalamudServices.Framework.Update += Update;
        try
        {
            dutyState.DutyCompleted += OnDutyCompleted;
            DalamudServices.PluginInterface.UiBuilder.Draw += DrawWindow;
            DalamudServices.PluginInterface.GetIpcProvider<string>("AnimusBookAssistant.Status").RegisterFunc(GetStatusJson);
            if (!DalamudServices.CommandManager.Commands.ContainsKey(Command))
            {
                DalamudServices.CommandManager.AddHandler(Command, new CommandInfo((_, args) => HandleAction(args))
                { HelpMessage = "小黄书：open 打开界面 / auto 连续执行 / stop 停止 / status 状态 / diagnose 检查依赖" });
                hasSlash = true;
            }
            TraceLifecycle("enable.ready");
        }
        catch
        {
            dutyState.DutyCompleted -= OnDutyCompleted;
            DalamudServices.Framework.Update -= Update;
            DalamudServices.PluginInterface.UiBuilder.Draw -= DrawWindow;
            DalamudServices.PluginInterface.GetIpcProvider<string>("AnimusBookAssistant.Status").UnregisterFunc();
            throw;
        }
    }

    protected override void OnDisable()
    {
        TraceLifecycle("disable.begin");
        StopRun("模块已停用");
        if (dutyState != null) dutyState.DutyCompleted -= OnDutyCompleted;
        TraceLifecycle("disable.stopped");
        queuedAction = null;
        if (hasSlash) DalamudServices.CommandManager.RemoveHandler(Command);
        hasSlash = false;
        DalamudServices.PluginInterface.GetIpcProvider<string>("AnimusBookAssistant.Status").UnregisterFunc();
        DalamudServices.Framework.Update -= Update;
        DalamudServices.PluginInterface.UiBuilder.Draw -= DrawWindow;
        Clear();
        windowOpen = false;
        status = "已停用";
        TraceLifecycle("disable.complete");
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        if (mode == RunMode.Dungeons && ownsDuty && clientState.TerritoryType == dutyTerritory)
        {
            dutyCompletedAt = Environment.TickCount64;
            TraceLifecycle("duty.completed.waiting-for-book");
        }
    }

    private static void TraceLifecycle(string stage)
    {
        try
        {
            var path = System.IO.Path.Combine(DalamudServices.PluginInterface.GetPluginConfigDirectory(), "AnimusBookAssistant.last-operation.txt");
            var bytes = System.Text.Encoding.UTF8.GetBytes($"{DateTime.UtcNow:O} {typeof(AnimusBookAssistant).Module.ModuleVersionId} {stage}\n");
            using var file = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write,
                System.IO.FileShare.ReadWrite, 4096, System.IO.FileOptions.WriteThrough);
            file.Write(bytes);
            file.Flush(true);
        }
        catch { /* Diagnostics must not interrupt the module. */ }
    }

    public override bool TryHandleCommand(string arguments)
    {
        return HandleAction(arguments);
    }

    protected override bool OnInterruptAutomation()
    {
        if (mode == RunMode.Idle && !preparingEquipment && !autoBooks) return false;
        queuedAction = "stop";
        return true;
    }

    private bool HandleAction(string arguments)
    {
        var action = arguments.Trim().ToLowerInvariant();
        if (action == "open") { windowOpen = true; return true; }
        if (action == "status") { DalamudServices.ChatGUI.Print(executionStatus); return true; }
        if (action is not ("auto" or "nearby" or "monsters" or "dungeon" or "dungeons" or "leve" or "leves" or "fates" or "stop" or "diagnose")) return false;
        queuedAction = action;
        return true;
    }

    private static T GetService<T>() where T : class =>
        DalamudServices.PluginInterface.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"缺少服务 {typeof(T).Name}");

    private string SettingsPath() => System.IO.Path.Combine(DalamudServices.PluginInterface.GetPluginConfigDirectory(), "AnimusBookAssistant.settings.json");

    private void LoadSettings()
    {
        settingsWritable = true;
        try
        {
            if (System.IO.File.Exists(SettingsPath()))
                config = JsonSerializer.Deserialize<AnimusBookAssistantConfig>(System.IO.File.ReadAllText(SettingsPath())) ?? new();
        }
        catch (Exception ex) { settingsWritable = false; settingsStatus = $"设置读取失败，使用默认值且不覆盖原文件：{ex.Message}"; }
    }

    private void SaveSettings()
    {
        if (!settingsWritable) return;
        try
        {
            var path = SettingsPath();
            System.IO.File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(config));
            System.IO.File.Move(path + ".tmp", path, true);
            settingsStatus = "";
        }
        catch (Exception ex) { settingsStatus = $"设置保存失败，本次更改仅在当前加载期间生效：{ex.Message}"; }
    }

    public override bool DrawSettings()
    {
        DrawContent();
        return false;
    }

    private void DrawTravelSettings()
    {
        var changed = false;
        if (ImGui.CollapsingHeader("赶路设置"))
        {
            var mount = config.UseMount;
            if (ImGui.Checkbox("长途优先使用坐骑", ref mount)) { config.UseMount = mount; changed = true; }
            var flight = config.UseFlight;
            if (ImGui.Checkbox("可飞行时优先飞行", ref flight)) { config.UseFlight = flight; changed = true; }
            ImGui.TextWrapped("无法起飞或通行受阻时自动回退地面路线。");
        }
        if (changed) SaveSettings();
    }

    private void Update(IFramework framework)
    {
        if (queuedAction == "stop")
        {
            queuedAction = null;
            StopRun("已手动停止");
            return;
        }
        if (failed || Environment.TickCount64 < nextRead) return;
        nextRead = Environment.TickCount64 + 500;
        var readingFinished = false;
        try
        {
            var player = objectTable.LocalPlayer;
            if (mode != RunMode.Idle && (condition[ConditionFlag.BetweenAreas] || player == null && clientState.IsLoggedIn))
            {
                playerMissingAt = playerMissingAt == 0 ? Environment.TickCount64 : playerMissingAt;
                if (Environment.TickCount64 - playerMissingAt > 120000) StopRun("切图等待超过两分钟");
                targets.Clear();
                status = "等待切图和玩家对象恢复";
                return;
            }
            playerMissingAt = 0;
            if (!clientState.IsLoggedIn || player is null)
            {
                StopRun("角色未登录");
                Clear();
                status = "等待角色登录";
                return;
            }

            if (HasLeveReturnPrompt())
            {
                ConfirmLeveReturn();
                return;
            }

            var note = RelicNote.Instance();
            if (note == null)
            {
                StopRun("书本数据不可用");
                Clear();
                status = "RelicNote 不可用";
                return;
            }

            relicId = note->RelicId;
            bookId = note->RelicNoteId;
            objectiveBits = note->ObjectiveProgress;
            ReadLabels();
            for (var i = 0; i < monsters.Length; i++) monsters[i] = note->GetMonsterProgress(i);
            for (var i = 0; i < 3; i++)
            {
                dungeons[i] = note->IsDungeonComplete(i);
                fates[i] = note->IsFateComplete(i);
                leves[i] = note->IsLeveComplete(i);
            }
            ObserveBookHistory();

            targets.Clear();
            foreach (var obj in objectTable)
            {
                if (obj.ObjectKind != ObjectKind.BattleNpc || obj.Address == IntPtr.Zero) continue;
                if (!note->IsMonsterNoteTarget((NativeCharacter*)obj.Address)) continue;
                targets.Add(new TargetSnapshot(obj.Name.ToString(), obj.EntityId, obj.BaseId,
                    obj.Position, Vector3.Distance(player.Position, obj.Position), obj.IsTargetable));
            }
            targets.Sort((left, right) => left.Distance.CompareTo(right.Distance));
            lastRead = DateTime.Now;
            readingFinished = true;
            status = bookId is >= 1 and <= 9 ? "书本进度已同步" : "当前没有有效的小黄书";
            if (queuedAction != null)
            {
                var action = queuedAction;
                queuedAction = null;
                if (action == "stop") StopRun("已手动停止");
                else if (action == "diagnose") Diagnose();
                else if (action == "auto")
                {
                    if (mode == RunMode.Idle && !preparingEquipment) PrepareAutoEquipment();
                }
                else
                {
                    singleDuty = action == "dungeon";
                    singleLeve = action == "leve";
                    StartRun(action == "nearby" ? RunMode.Nearby : action == "monsters" ? RunMode.WorldMonsters
                        : action == "fates" ? RunMode.Fates : action is "leve" or "leves" ? RunMode.Leves : RunMode.Dungeons);
                }
            }
            DriveEquipmentPreparation();
            DriveRun();
            DriveAutoBooks();
        }
        catch (Exception ex)
        {
            StopRun($"执行异常：{ex.Message}");
            if (readingFinished)
            {
                failed = false;
                status = $"执行已停止，继续读取书本：{ex.Message}";
            }
            else
            {
                Clear();
                failed = true;
                status = $"读取已暂停：{ex.GetType().Name}: {ex.Message}";
            }
        }
    }

    private void Clear()
    {
        targets.Clear();
        Array.Clear(monsters);
        Array.Clear(dungeons);
        Array.Clear(fates);
        Array.Clear(leves);
        relicId = bookId = 0;
        objectiveBits = 0;
        lastRead = null;
        bookName = "未映射";
        Array.Clear(monsterLabels);
        Array.Clear(monsterRequired);
        Array.Clear(dungeonLabels);
        Array.Clear(fateLabels);
        Array.Clear(leveLabels);
        requiredWeapon = 0;
        Array.Clear(monsterNameIds);
        Array.Clear(dungeonNames);
        Array.Clear(dungeonTerritories);
        labelsReady = false;
    }

    private void ReadLabels()
    {
        if (labelsReady && labelsForBook == bookId && labelsForRelic == relicId) return;
        if (!LuminaGetter.TryGetRow<SheetRelicNote>(bookId, out var row) || bookId == 0)
        {
            bookName = "未找到有效书本数据行";
            Array.Clear(monsterLabels);
            Array.Clear(monsterRequired);
            Array.Clear(dungeonLabels);
            Array.Clear(fateLabels);
            Array.Clear(leveLabels);
            return;
        }
        bookName = row.EventItem.Value.Singular.ToString();
        requiredWeapon = LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Relic>(relicId, out var relic)
            ? relic.ItemAtma.RowId : 0;
        for (var i = 0; i < 10; i++)
        {
            var target = row.MonsterNoteTargetCommon[i].Value;
            monsterLabels[i] = target.BNpcName.Value.Singular.ToString();
            monsterRequired[i] = row.MonsterCount[i];
            monsterNameIds[i] = target.BNpcName.RowId;
            monsterZones[i] = target.PlaceNameZone[0].RowId;
        }
        for (var i = 0; i < 3; i++)
        {
            // 这里存的是副本首领目标，副本区域需按地点名称另行关联。
            dungeonLabels[i] = row.MonsterNoteTargetNM[i].Value.BNpcName.Value.Singular.ToString();
            fateLabels[i] = row.Fate[i].Value.Name.ToString();
            fateIds[i] = (ushort)row.Fate[i].RowId;
            fateTerritories[i] = row.PlaceNameFate[i].RowId;
            leveLabels[i] = row.Leve[i].Value.Name.ToString();
            leveIds[i] = row.Leve[i].RowId;
            dungeonNames[i] = row.MonsterNoteTargetNM[i].Value.PlaceNameLocation[0].Value.Name.ToString();
            dungeonTerritories[i] = 0;
            foreach (var content in DalamudServices.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>())
            {
                if (content.Name.ToString() != dungeonNames[i] || content.TerritoryType.RowId == 0) continue;
                dungeonTerritories[i] = content.TerritoryType.RowId;
                break;
            }
        }
        labelsForBook = bookId;
        labelsForRelic = relicId;
        labelsReady = true;
    }

    private uint RequiredClassJob()
    {
        if (!LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Item>(requiredWeapon, out var item)) return 0;
        var c = item.ClassJobCategory.Value;
        return c.PLD ? 19u : c.MNK ? 20u : c.WAR ? 21u : c.DRG ? 22u : c.BRD ? 23u
            : c.WHM ? 24u : c.BLM ? 25u : c.SMN ? 27u : c.SCH ? 28u : c.NIN ? 30u : 0u;
    }

    private void PrepareAutoEquipment()
    {
        autoBooks = false;
        var job = RequiredClassJob();
        var player = objectTable.LocalPlayer;
        if (player == null || bookId == 0 || requiredWeapon == 0 || job == 0)
        { executionStatus = "无法识别当前书本对应的职业和古武"; return; }
        if (HasRequiredWeapon() && player.ClassJob.RowId == job)
        { autoBooks = true; autoNextAt = 0; equipmentStatus = "对应职业与古武已装备"; return; }
        if (condition[ConditionFlag.InCombat] || condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.Casting]
            || condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.Mounted] || condition[ConditionFlag.Diving]
            || condition[ConditionFlag.Gathering] || condition[ConditionFlag.Crafting] || player.CurrentHp == 0
            || condition[ConditionFlag.OccupiedInEvent] || condition[ConditionFlag.OccupiedInQuestEvent] || condition[ConditionFlag.WatchingCutscene])
        { executionStatus = "当前状态无法换装，请在副本外脱战、下坐骑并结束施法或交互后开始"; return; }
        var inventory = InventoryManager.Instance();
        if (inventory == null || inventory->GetInventoryItemCount(requiredWeapon, false, true, true) == 0)
        { executionStatus = $"未找到对应古武（物品 ID {requiredWeapon}），请放入背包或兵装库"; return; }
        var sets = RaptureGearsetModule.Instance();
        if (sets == null) { executionStatus = "装备套装服务尚未就绪"; return; }
        var chosen = -1;
        for (var i = 0; player.ClassJob.RowId != job && i < 100; i++)
        {
            if (!sets->IsValidGearset(i)) continue;
            var entry = sets->GetGearset(i);
            if (entry == null || (entry->Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0 || entry->ClassJob != job) continue;
            if (chosen < 0 || i == sets->CurrentGearsetIndex) chosen = i;
            if (i == sets->CurrentGearsetIndex) break;
        }
        if (chosen < 0 && player.ClassJob.RowId != job)
        { executionStatus = "没有对应职业的普通装备套装，请先保存该职业套装；不需要在套装中保存古武"; return; }
        equipmentJob = job; equipmentWeapon = requiredWeapon;
        equipmentBook = bookId; equipmentRelic = relicId;
        equipmentCharacter = DalamudServices.PlayerState.ContentId;
        equipmentRequestedAt = Environment.TickCount64;
        equipmentStableAt = 0;
        weaponEquipRequested = false; equipmentContextPending = false;
        equipmentStatus = executionStatus = chosen < 0 ? "职业已匹配，准备单独装备古武…" : $"正在切换对应职业（套装 {chosen + 1}），随后单独装备古武…";
        preparingEquipment = true;
        if (chosen >= 0 && sets->EquipGearset(chosen) != 0)
        { preparingEquipment = false; equipmentStatus = executionStatus = "游戏拒绝切换装备套装，请检查装备缺失或换装限制"; }
    }

    private void DriveEquipmentPreparation()
    {
        if (!preparingEquipment) return;
        var now = Environment.TickCount64;
        if (condition[ConditionFlag.Unconscious] || objectTable.LocalPlayer?.CurrentHp == 0)
        { equipmentRequestedAt = now; equipmentStatus = executionStatus = "等待复活，复活后继续换装"; return; }
        if (DalamudServices.PlayerState.ContentId != equipmentCharacter || bookId != equipmentBook || relicId != equipmentRelic
            || requiredWeapon != equipmentWeapon || condition[ConditionFlag.InCombat] || condition[ConditionFlag.BoundByDuty])
        { StopRun("换装期间角色、书本或战斗状态变化，已停止"); return; }
        if (now - equipmentRequestedAt < 1500) return;
        if (objectTable.LocalPlayer?.ClassJob.RowId == equipmentJob && HasRequiredWeapon())
        {
            if (equipmentStableAt == 0) equipmentStableAt = now;
            if (now - equipmentStableAt < 3000) return;
            preparingEquipment = false;
            equipmentStatus = "职业与古武核对成功";
            executionStatus = equipmentStatus;
            autoBooks = true; autoNextAt = now + 500;
            return;
        }
        equipmentStableAt = 0;
        if (equipmentContextPending)
        {
            var context = AgentInventoryContext.Instance();
            var menu = (AddonContextMenu*)FindAddon("ContextMenu");
            if (context != null && menu != null)
            {
                var count = Math.Clamp(context->ContextItemCount, 0, 32);
                var start = context->ContexItemStartIndex;
                for (var i = 0; i < count && start >= 0 && start + i < 98; i++)
                {
                    var value = context->EventParams[start + i];
                    if (value.Type is not (FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String or FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.ManagedString)) continue;
                    if (value.String.ToString() != "装备" || context->IsContextItemDisabled(i)) continue;
                    equipmentContextPending = false;
                    menu->OnMenuSelected(i, 0);
                    return;
                }
            }
            if (now - equipmentRequestedAt > 10000) StopRun("未找到可用的装备菜单项，已停止");
            return;
        }
        if (objectTable.LocalPlayer?.ClassJob.RowId == equipmentJob && !weaponEquipRequested)
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null || !LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Item>(equipmentWeapon, out var weapon))
            { StopRun("无法读取古武或背包数据，已停止"); return; }
            foreach (var type in new[] { InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand,
                InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
            {
                var container = inventory->GetInventoryContainer(type);
                if (container == null || !container->IsLoaded) continue;
                for (var slot = 0; slot < Math.Clamp(container->Size, 0, 200); slot++)
                {
                    var item = container->GetInventorySlot(slot);
                    if (item == null || item->ItemId != equipmentWeapon) continue;
                    weaponEquipRequested = true;
                    equipmentRequestedAt = now;
                    equipmentStatus = executionStatus = "职业切换完成，正在单独装备对应古武…";
                    // Use the same equipment operation as the inventory context menu.
                    var context = AgentInventoryContext.Instance();
                    if (context == null) { StopRun("装备菜单服务不可用"); return; }
                    equipmentContextPending = true;
                    context->OpenForItemSlot(type, slot, 0, 0);
                    return;
                }
            }
            StopRun("对应古武不在已加载的背包或兵装库中，已停止");
            return;
        }
        if (now - equipmentRequestedAt > 10000) StopRun("换装后职业或古武未匹配，已停止；请检查套装中的装备是否仍在身上");
    }

    private void StartRun(RunMode requested)
    {
        if (mode != RunMode.Idle) { executionStatus = "正在运行，请先停止"; return; }
        if (ownsDuty || ownsMovement || ownsOverrides || rotationPreset != null) { executionStatus = "上次执行的资源尚未清理，不能开始"; return; }
        var currentFate = FateManager.Instance();
        var resumeFate = requested == RunMode.Fates && currentFate != null
            && !condition[ConditionFlag.BoundByDuty]
            && Enumerable.Range(0, 3).Any(i => !fates[i] && (currentFate->GetCurrentFateId() == fateIds[i]
                || FatePrecursor(i) != 0 && currentFate->GetCurrentFateId() == FatePrecursor(i)));
        var resumeFateCleanup = requested == RunMode.Fates && !condition[ConditionFlag.BoundByDuty] && AttackingMe().Count > 0;
        var reason = StartBlockedReason(requested == RunMode.Leves && (CanResumeLeveDialog() || HasLeveReturnPrompt()), resumeFate || resumeFateCleanup || requested == RunMode.Leves && CanResumeActiveLeve());
        if (reason != null) { executionStatus = reason; return; }
        if (requested != RunMode.Dungeons && TargetCheckAction() == 0)
        { executionStatus = "当前职业尚未实现基础攻击"; return; }
        if (requested == RunMode.Dungeons && !IpcBool("AutoDuty.IsStopped"))
        { executionStatus = "AutoDuty 已在运行，请先结束它的任务"; return; }
        if (requested != RunMode.Dungeons && (IpcBool("vnavmesh.Path.IsRunning") || IpcBool("vnavmesh.SimpleMove.PathfindInProgress")))
        { executionStatus = "vnavmesh 已有移动或寻路任务"; return; }
        if (requested == RunMode.Fates && objectTable.LocalPlayer.ClassJob.RowId != 24)
        { executionStatus = "当前 FATE 自动战斗配置仅支持白魔法师"; return; }
        fateSlot = -1;
        activeFateId = 0;
        fateEngaged = false;
        fateRunAt = Environment.TickCount64;
        companionRequestedAt = companionSettleAt = 0;
        companionAttempts = 0;
        stalledPathRetries = 0;
        failedLandingPoints.Clear();
        clearingFateAggressors = false;
        fateCleanupStatus = "等待检查敌对列表";
        Array.Clear(fateVisited);
        fateScanTerritory = 0;
        fateScanReadyAt = 0;
        fateRoundResumeAt = 0;
        runBook = bookId;
        runRelic = relicId;
        runCharacter = DalamudServices.PlayerState.ContentId;
        runTerritory = clientState.TerritoryType;
        mode = requested;
        targetId = 0;
        routePoint = null;
        teleportRequested = false;
        teleportRetries = 0;
        routeAttempt = 0;
        arrivedAt = 0;
        rejectedSpawns.Clear();
        awaitingMount = landing = false;
        mountReadyAt = mountFallbackUntil = 0;
        leveSlot = -1;
        refreshLeveId = 0;
        leveRefreshAttempts = 0;
        refreshLeveAccepted = refreshRewardRequested = false;
        pokedWisps.Clear();
        crystalAttempts.Clear();
        pokeTarget = 0;
        crystalTarget = 0;
        crystalPending = settleTarget = 0;
        settledAt = 0;
        crystalFailures.Clear();
        lastProgress = monsters.Sum(x => (int)x);
        progressAt = phaseAt = Environment.TickCount64;
        executionStatus = requested switch
        {
            RunMode.Nearby => "扫描附近未完成目标", RunMode.WorldMonsters => "准备完成书本怪物",
            RunMode.Leves => "准备执行书本理符", RunMode.Fates => "准备巡查书本 FATE",
            RunMode.Exchange => "本卷已完成，准备前往摩杜纳换书", _ => "准备运行未完成副本"
        };
    }

    private bool CanResumeLeveDialog()
    {
        if (FindAddon("GuildLeve") == null && FindAddon("SelectString") == null)
        {
            var confirm = (AddonSelectYesno*)FindAddon("SelectYesno");
            if (confirm == null || confirm->PromptText == null
                || !confirm->PromptText->NodeText.ToString().Contains("领取报酬")) return false;
        }
        var player = objectTable.LocalPlayer;
        var slot = Array.FindIndex(leves, x => !x);
        if (player == null || slot < 0 || !LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Leve>(leveIds[slot], out var leve)
            || !LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Level>(leve.LevelLevemete.RowId, out var level)
            || level.Territory.RowId != clientState.TerritoryType) return false;
        return objectTable.Any(x => x.BaseId == level.Object.RowId && Vector3.Distance(player.Position, x.Position) < 4);
    }

    private bool CanResumeActiveLeve()
    {
        var slot = Array.FindIndex(leves, x => !x);
        if (slot < 0 || !SupportsLeve(leveIds[slot])) return false;
        var quests = QuestManager.Instance();
        return quests != null && quests->GetLeveQuestById((ushort)leveIds[slot]) != null
            && FindLeveDirector((ushort)leveIds[slot]) != null;
    }

    private string StartBlockedReason(bool allowLeveDialog = false, bool allowActiveLeve = false)
    {
        var player = objectTable.LocalPlayer;
        if (lastRead == null || bookId == 0 || requiredWeapon == 0 || player == null) return "没有可执行的小黄书";
        if (condition[ConditionFlag.Gathering] || condition[ConditionFlag.Crafting]) return "请先结束采集或制作";
        if (condition[ConditionFlag.WatchingCutscene] || !allowLeveDialog && (condition[ConditionFlag.OccupiedInEvent] || condition[ConditionFlag.OccupiedInQuestEvent])) return "请先结束当前对话或过场";
        if (!allowActiveLeve && (condition[ConditionFlag.InCombat] || condition[ConditionFlag.BoundByDuty])) return "请在脱战且副本外开始";
        if (condition[ConditionFlag.Unconscious] || player.CurrentHp == 0) return "角色已死亡";
        if (condition[ConditionFlag.Diving]) return "请先离开潜水状态";
        if (player.ClassJob.RowId is < 19 or > 30) return "请切换到古武对应的战斗职业";
        if (!HasRequiredWeapon()) return $"请装备本书对应的魂晶古武（物品 ID {requiredWeapon}）";
        return null;
    }

    private bool HasRequiredWeapon()
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return false;
        var equipment = manager->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipment == null) return false;
        // 骑士的剑、盾分属不同书本，主副手都要检查。
        for (var i = 0; i < 2; i++)
        {
            var item = equipment->GetInventorySlot(i);
            if (item != null && item->ItemId == requiredWeapon) return true;
        }
        return false;
    }

    private void DriveRun()
    {
        if (mode == RunMode.Idle) return;
        var player = objectTable.LocalPlayer;
        if (mode == RunMode.Exchange && (bookId == 0 || relicId == 0))
        {
            if (Environment.TickCount64 - exchangeAt > 300000) StopRun("换书数据更新超时");
            else executionStatus = "等待换书后的原生数据更新";
            return;
        }
        if (player == null || DalamudServices.PlayerState.ContentId != runCharacter || mode != RunMode.Exchange && bookId != runBook || relicId != runRelic)
        { StopRun("角色或书本已变化"); return; }
        if (player.CurrentHp == 0 || condition[ConditionFlag.Unconscious])
        {
            if (deathPausedAt == 0) { deathPausedAt = Environment.TickCount64; StopPath(); RestoreCombatRotation(); }
            executionStatus = "角色已死亡，暂停执行；复活后自动恢复";
            return;
        }
        if (deathPausedAt != 0)
        {
            var delay = Environment.TickCount64 - deathPausedAt;
            progressAt += delay; leveAt += delay; fateRunAt += delay; phaseAt += delay;
            deathPausedAt = 0;
            if (mode == RunMode.Fates)
            {
                fateEngaged = false; fateEndedAt = 0; activeFateId = 0;
                Array.Clear(fateVisited); fateRoundResumeAt = 0;
            }
        }
        if (preparingEquipment) return;
        if (!HasRequiredWeapon())
        {
            if (condition[ConditionFlag.InCombat] || condition[ConditionFlag.BoundByDuty])
            { executionStatus = "古武未装备，等待脱战或退出副本后自动恢复装备"; return; }
            PrepareAutoEquipment();
            if (!preparingEquipment && !autoBooks) StopRun(executionStatus);
            return;
        }
        if (condition[ConditionFlag.Gathering] || condition[ConditionFlag.Crafting])
        { StopRun("检测到采集或制作，已停止"); return; }
        if (mode == RunMode.Dungeons) { DriveDuty(); return; }
        if (mode == RunMode.Leves) { DriveLeves(); return; }
        if (mode == RunMode.Fates) { DriveFates(); return; }
        if (mode == RunMode.Exchange) { DriveExchange(); return; }
        if (mode == RunMode.WorldMonsters && teleportRequested) { DriveWorldRoute(); return; }
        if (mode == RunMode.Nearby && clientState.TerritoryType != runTerritory || condition[ConditionFlag.BoundByDuty])
        { StopRun("区域变化，已停止附近战斗"); return; }
        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.OccupiedInEvent] || condition[ConditionFlag.WatchingCutscene]) return;
        var progress = monsters.Sum(x => (int)x);
        if (progress != lastProgress) { lastProgress = progress; progressAt = Environment.TickCount64; }
        var lastEnemy = objectTable.OfType<IBattleNpc>().FirstOrDefault(x => x.GameObjectId == targetId);
        if ((fateEscapeGoal.HasValue || lastEnemy != null && Enumerable.Range(0, 10).Any(i => monsterNameIds[i] == lastEnemy.NameId && monsters[i] >= monsterRequired[i])
            && ((NativeGameObject*)lastEnemy.Address)->FateId != 0 && condition[ConditionFlag.InCombat])
            && DriveFateEscape(FateManager.Instance())) return;
        if (Enumerable.Range(0, 10).All(i => monsters[i] >= monsterRequired[i]))
        { StopRun("全部怪物目标已完成", true); return; }
        if (Environment.TickCount64 - progressAt > (mode == RunMode.WorldMonsters ? 600000 : 90000))
        { StopRun("长时间无书本进度变化，已停止"); return; }
        var target = objectTable.OfType<IBattleNpc>().FirstOrDefault(x => x.GameObjectId == targetId && PendingTarget(x));
        target ??= objectTable.OfType<IBattleNpc>().Where(PendingTarget)
            .Where(x => Vector3.Distance(player.Position, x.Position) < (mode == RunMode.WorldMonsters ? 250 : 100))
            .OrderBy(x => Vector3.Distance(player.Position, x.Position)).FirstOrDefault();
        if (target == null)
        {
            if (mode == RunMode.WorldMonsters) { DriveWorldRoute(); return; }
            StopPath();
            targetId = 0;
            executionStatus = "附近无未完成目标，等待刷新（仅扫描当前加载范围）";
            return;
        }
        targetId = target.GameObjectId;
        if (condition[ConditionFlag.Casting]) return;
        if (!PrepareEnemyFateSync(target)) return;
        var distance = Vector3.Distance(player.Position, target.Position) - target.HitboxRadius - player.HitboxRadius;
        if (condition[ConditionFlag.InFlight] && HorizontalDistance(player.Position, target.Position) < 35
            && !PrepareDismount(target.Position)) return;
        var ranged = player.ClassJob.RowId is 23 or 24 or 25 or 27 or 28;
        if (distance > (ranged ? 20 : 2.5f))
        {
            executionStatus = $"前往 {target.Name}，距离 {distance:F1}m";
            DrivePath(player.Position, target.Position, true);
            return;
        }
        if (!PrepareDismount(target.Position)) return;
        if (!EnsureTargetVisible(target)) return;
        var actions = ActionManager.Instance();
        if (actions == null) throw new InvalidOperationException("ActionManager 不可用");
        var action = actions->GetAdjustedActionId(TargetCheckAction());
        lastRangeOrLos = actions->GetActionStatus(ActionType.Action, action, target.GameObjectId);
        if (lastRangeOrLos is 566 or 562)
        {
            executionStatus = $"目标距离或视线受阻，沿地面接近 {target.Name}（{lastRangeOrLos}）";
            if (distance > 2.5f) DrivePath(player.Position, target.Position);
            else StopRun("贴近目标后仍无法满足攻击距离或视线，已停止");
            return;
        }
        StopPath();
        targetManager.Target = target;
        executionStatus = $"攻击 {target.Name}；等待书本进度更新";
        ActivateCombatRotation();
        return;
    }

    private bool PendingTarget(IBattleNpc target)
    {
        if (!target.IsTargetable || target.CurrentHp == 0 || TargetTemporarilyBlocked(target)) return false;
        var note = RelicNote.Instance();
        if (note == null || !note->IsMonsterNoteTarget((NativeCharacter*)target.Address)) return false;
        for (var i = 0; i < 10; i++)
            if (target.NameId == monsterNameIds[i] && monsterRequired[i] > 0 && monsters[i] < monsterRequired[i]) return true;
        return false;
    }

    private uint TargetCheckAction() => objectTable.LocalPlayer?.ClassJob.RowId switch
    {
        19 => 9, 20 => 53, 21 => 31, 22 => 75, 23 => 97,
        24 => 119, 25 => 141, 27 => 163, 28 => 178, 30 => 2240, _ => 0
    };

    private void DrivePath(Vector3 from, Vector3 to, bool allowMount = false, float maxSegment = 0)
    {
        if (rotationActive) RestoreCombatRotation();
        if (Environment.TickCount64 < walkingRecoveryUntil && condition[ConditionFlag.Mounted]
            && !condition[ConditionFlag.InFlight])
        {
            travelStatus = "坐骑地面受阻，先下坐骑再步行重算";
            if (!PrepareDismount(from)) return;
        }
        // A descent waypoint intentionally ends below the surface. Once landed,
        // it must be removed before any subsequent ground route can start.
        if (landing && !condition[ConditionFlag.InFlight]) { StopPath(); landing = false; }
        // Once a fallback landing point is chosen, finish landing before chasing actors.
        if (landingApproachGoal.HasValue && condition[ConditionFlag.InFlight] && !preparingGroundLanding)
        {
            preparingGroundLanding = true;
            try { if (!PrepareDismount(from)) return; }
            finally { preparingGroundLanding = false; }
        }
        // Try flight first. Roofs at either endpoint do not prove the whole route
        // is unflyable; only an actual flight failure starts a ground fallback.
        var groundOnly = Environment.TickCount64 < flightFallbackUntil;
        if (groundOnly && !preparingGroundLanding)
        {
            if (condition[ConditionFlag.InFlight])
            {
                if (!groundLandingInProgress) { StopPath(); groundLandingInProgress = true; }
                preparingGroundLanding = true;
                try { if (!PrepareDismount(from)) return; }
                finally { preparingGroundLanding = false; }
            }
            else
            {
                groundLandingInProgress = false;
                if (pathFlying && (ownsMovement || pathTask != null) && !landing) StopPath();
            }
        }
        if (allowMount && !PrepareMount(Vector3.Distance(from, to))) return;
        if (pathTask != null)
        {
            if (!pathTask.IsCompleted)
            {
                if (Environment.TickCount64 - phaseAt > 30000) StopRun("寻路超过 30 秒，已停止；书本继续读取");
                return;
            }
            var task = pathTask;
            pathTask = null;
            List<Vector3> points;
            try { points = task.GetAwaiter().GetResult(); }
            catch (Exception ex) when (pathFlying)
            {
                flightFallbackUntil = Environment.TickCount64 + 60000;
                travelStatus = $"该段飞行寻路不可用，改用坐骑地面路线：{ex.GetBaseException().Message}";
                return;
            }
            if (points.Count == 0)
            {
                if (pathFlying)
                {
                    flightFallbackUntil = Environment.TickCount64 + 60000;
                    pathFlying = false;
                    travelStatus = "未找到飞行路线，临时改走地面";
                    return;
                }
                throw new InvalidOperationException("未找到可用地面路线");
            }
            if (IpcBool("vnavmesh.Path.IsRunning")) throw new InvalidOperationException("其他移动任务已占用寻路插件");
            if (maxSegment > 0)
            {
                var segment = new List<Vector3>();
                var cursor = from;
                var remaining = maxSegment;
                foreach (var point in points)
                {
                    var length = Vector3.Distance(cursor, point);
                    if (length > remaining)
                    { segment.Add(Vector3.Lerp(cursor, point, remaining / length)); break; }
                    segment.Add(point); remaining -= length; cursor = point;
                    if (remaining <= 0) break;
                }
                points = segment;
            }
            ownsMovement = true;
            DalamudServices.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").InvokeAction(points, pathFlying);
            phaseAt = Environment.TickCount64;
            var routeLength = 0f;
            var previousPoint = from;
            foreach (var point in points) { routeLength += Vector3.Distance(previousPoint, point); previousPoint = point; }
            pathDeadlineAt = phaseAt + (long)Math.Clamp(routeLength / 3f * 1000f + 60000f, 120000f, 900000f);
            pathProgressPosition = from;
            pathProgressAt = phaseAt;
            return;
        }
        if (ownsMovement && IpcBool("vnavmesh.Path.IsRunning"))
        {
            var now = Environment.TickCount64;
            if (Vector3.DistanceSquared(from, pathProgressPosition) > 4)
            { pathProgressPosition = from; pathProgressAt = now; stalledPathRetries = 0; }
            if (now - pathProgressAt > 8000)
            {
                var wasFlying = pathFlying;
                StopPath();
                if (wasFlying) flightFallbackUntil = now + 60000;
                var retryFlight = !wasFlying && config.UseFlight && Control.CanFly;
                if (retryFlight)
                {
                    flightFallbackUntil = 0;
                    walkingRecoveryUntil = mountFallbackUntil = 0;
                }
                else if (!wasFlying && condition[ConditionFlag.Mounted])
                {
                    walkingRecoveryUntil = now + 60000;
                    mountFallbackUntil = walkingRecoveryUntil;
                }
                travelStatus = wasFlying ? "飞行移动停滞，落地后临时改走地面"
                    : retryFlight ? "地面路线受阻，重新尝试飞行" : "地面移动受阻，下坐骑重新寻路";
                if (++stalledPathRetries > 2) StopRun("连续三次移动停滞，已停止，避免反复卡住");
                return;
            }
            if (pathFlying && !condition[ConditionFlag.InFlight] && Environment.TickCount64 - phaseAt > 6000)
            {
                StopPath();
                flightFallbackUntil = Environment.TickCount64 + 60000;
                pathFlying = false;
                travelStatus = "附近无法起飞，暂用坐骑地面路线";
                return;
            }
            if (now > pathDeadlineAt) StopRun("移动超过按路径长度计算的时限，已停止；书本继续读取");
            return;
        }
        ownsMovement = false;
        if (!IpcBool("vnavmesh.Nav.IsReady")) throw new InvalidOperationException("vnavmesh 尚未就绪");
        pathCancellation?.Dispose();
        pathCancellation = new CancellationTokenSource();
        phaseAt = Environment.TickCount64;
        pathFlying = preparingGroundLanding || !groundOnly && (condition[ConditionFlag.InFlight] || config.UseFlight && condition[ConditionFlag.Mounted] && Control.CanFly
            && Environment.TickCount64 >= flightFallbackUntil && Vector3.Distance(from, to) >= 50);
        travelStatus = pathFlying ? "坐骑飞行寻路" : condition[ConditionFlag.Mounted] ? "坐骑地面寻路" : "步行寻路";
        pathTask = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable")
            .InvokeFunc(from, pathFlying ? to + new Vector3(0, 8, 0) : to, pathFlying, pathCancellation.Token);
    }


    private bool PrepareMount(float distance)
    {
        if (condition[ConditionFlag.Mounted]) { awaitingMount = false; mountReadyAt = 0; return true; }
        if (awaitingMount)
        {
            travelStatus = "正在召唤坐骑";
            if (Environment.TickCount64 - mountRequestedAt < 10000) return false;
            awaitingMount = false;
            mountFallbackUntil = Environment.TickCount64 + 30000;
            travelStatus = "召唤坐骑未成功，暂用地面步行";
            return true;
        }
        if (!config.UseMount || distance < 50 || condition[ConditionFlag.InCombat] || condition[ConditionFlag.Diving]
            || Environment.TickCount64 < mountFallbackUntil) return true;
        if (condition[ConditionFlag.Casting] || condition[ConditionFlag.Mounting] || condition[ConditionFlag.Mounting71]) return false;
        // Stop first and allow a frame for movement to settle before starting a mount cast.
        StopPath();
        if (mountReadyAt == 0) { mountReadyAt = Environment.TickCount64 + 700; travelStatus = "准备上坐骑"; return false; }
        if (Environment.TickCount64 < mountReadyAt) return false;
        mountReadyAt = 0;
        var actions = ActionManager.Instance();
        if (actions == null) return true;
        uint mountAction = config.UseFlight && actions->GetActionStatus(ActionType.GeneralAction, 24) == 0 ? 24u : 9u;
        if (actions->GetActionStatus(ActionType.GeneralAction, mountAction) != 0
            || !actions->UseAction(ActionType.GeneralAction, mountAction))
        {
            mountFallbackUntil = Environment.TickCount64 + 30000;
            travelStatus = "当前不能上坐骑，使用地面路线";
            return true;
        }
        awaitingMount = true;
        mountRequestedAt = Environment.TickCount64;
        travelStatus = "正在召唤坐骑";
        return false;
    }

    private bool PrepareDismount(Vector3 destination)
    {
        if (!condition[ConditionFlag.Mounted]) { landing = false; landingApproachGoal = null; landingSearchAt = 0; failedLandingPoints.Clear(); return true; }
        var player = objectTable.LocalPlayer;
        if (player == null) return false;
        if (condition[ConditionFlag.InFlight])
        {
            travelStatus = "正在降落";
            if (landing)
            {
                if (Environment.TickCount64 - landingAt > 15000)
                {
                    StopPath(); landing = false;
                    failedLandingPoints.Add(landingDestination);
                    landingApproachGoal = null;
                    travelStatus = "当前落点下降受阻，排除后寻找其他落点";
                    if (failedLandingPoints.Count >= 3) StopRun("三个不同落点均未能着陆，已停止");
                }
                return false;
            }
            if (landingSearchAt != 0 && Environment.TickCount64 - landingSearchAt > 90000)
            { StopRun("寻找并接近安全落点超过 90 秒，已停止"); return false; }
            if (landingApproachGoal is { } approach)
            {
                if (HorizontalDistance(player.Position, approach) > 5 || approach.Y > player.Position.Y + 1)
                {
                    var previous = preparingGroundLanding;
                    preparingGroundLanding = true;
                    try { DrivePath(player.Position, approach + new Vector3(0, 8, 0)); }
                    finally { preparingGroundLanding = previous; }
                    travelStatus = "飞往已找到的安全落地区域";
                    return false;
                }
                StopPath(); landingApproachGoal = null;
            }
            // Finish the flight approach before descending; a nearby point on
            // the wrong side of a cliff is not a useful destination landing.
            if (HorizontalDistance(player.Position, destination) > 8)
            { DrivePath(player.Position, destination); return false; }
            var floor = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor")
                .InvokeFunc(player.Position + new Vector3(0, 2, 0), false, 6f);
            floor = ValidateLandingSurface(floor);
            if (!floor.HasValue)
            {
                if (landingSearchAt == 0) landingSearchAt = Environment.TickCount64;
                var nearby = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor")
                    .InvokeFunc(player.Position + new Vector3(0, 2, 0), false, 40f);
                nearby = ValidateLandingSurface(nearby);
                if (!nearby.HasValue)
                {
                    foreach (var radius in new[] { 12f, 24f, 48f, 72f, 96f })
                    {
                        for (var i = 0; i < 8 && !nearby.HasValue; i++)
                        {
                            var angle = i * MathF.PI / 4;
                            var probe = player.Position + new Vector3(MathF.Cos(angle) * radius, 2, MathF.Sin(angle) * radius);
                            nearby = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor")
                                .InvokeFunc(probe, failedLandingPoints.Count > 0, 8f);
                            nearby = ValidateLandingSurface(nearby);
                        }
                        if (nearby.HasValue) break;
                    }
                }
                if (!nearby.HasValue)
                {
                    var candidate = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable")
                        .InvokeFunc(player.Position, 120f, 128f);
                    candidate ??= DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor")
                        .InvokeFunc(player.Position + new Vector3(0, 128, 0), true, 120f);
                    nearby = ValidateLandingSurface(candidate);
                }
                if (nearby.HasValue && (HorizontalDistance(player.Position, nearby.Value) > 6 || nearby.Value.Y > player.Position.Y + 1))
                {
                    StopPath(); landingApproachGoal = nearby.Value;
                    travelStatus = "先飞往附近安全落地区域";
                    return false;
                }
                if (nearby.HasValue) floor = nearby;
            }
            if (!floor.HasValue) { StopRun("目标附近没有安全落点"); return false; }
            StopPath();
            // The final short vertical segment presses the mount down onto the
            // already-resolved ground surface; stop it as soon as InFlight clears.
            ownsMovement = true;
            landing = true;
            landingAt = Environment.TickCount64;
            landingDestination = floor.Value;
            DalamudServices.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo")
                .InvokeAction(new List<Vector3> { floor.Value - new Vector3(0, 1, 0) }, true);
            return false;
        }
        StopPath();
        landing = false;
        landingApproachGoal = null; landingSearchAt = 0;
        failedLandingPoints.Clear();
        travelStatus = "正在下坐骑";
        if (Environment.TickCount64 - dismountRequestedAt < 1500) return false;
        dismountRequestedAt = Environment.TickCount64;
        var actions = ActionManager.Instance();
        if (actions != null && actions->GetActionStatus(ActionType.GeneralAction, 23) == 0)
            actions->UseAction(ActionType.GeneralAction, 23);
        return false;
    }

    private Vector3? ValidateLandingSurface(Vector3? point)
    {
        if (point is not { } p || failedLandingPoints.Any(x => HorizontalDistance(x, p) < 8)) return null;
        if (!BGCollisionModule.RaycastMaterialFilter(p + new Vector3(0, 4, 0), -Vector3.UnitY, out var hit, 8f)) return null;
        return hit.Point;
    }

    private bool PrepareTeleport()
    {
        mountReadyAt = 0;
        if (awaitingMount)
        {
            if (condition[ConditionFlag.Mounted] || Environment.TickCount64 - mountRequestedAt >= 10000) awaitingMount = false;
            else { travelStatus = "等待已提交的坐骑动作结束后传送"; return false; }
        }
        if (condition[ConditionFlag.Casting]) return false;
        if (!condition[ConditionFlag.Mounted]) return true;
        var player = objectTable.LocalPlayer;
        if (player == null) return false;
        awaitingMount = false;
        return PrepareDismount(player.Position);
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right) =>
        Vector2.Distance(new Vector2(left.X, left.Z), new Vector2(right.X, right.Z));

    private void DriveDuty()
    {
        if (ownsDuty)
        {
            var busy = !IpcBool("AutoDuty.IsStopped");
            dutyWasBusy |= busy;
            enteredDuty |= clientState.TerritoryType == dutyTerritory;
            if (condition[ConditionFlag.BoundByDuty] && clientState.TerritoryType != dutyTerritory && !condition[ConditionFlag.BetweenAreas])
            { StopRun("进入了非预期副本"); return; }
            if (Environment.TickCount64 - phaseAt > (enteredDuty ? 2400000 : 180000))
            { StopRun("等待入本或运行副本超时"); return; }
            executionStatus = $"AutoDuty：{dungeonNames[dutySlot]}；进入={enteredDuty}，书本完成={dungeons[dutySlot]}";
            if (dutyCompletedAt != 0 && clientState.TerritoryType == dutyTerritory
                && condition[ConditionFlag.BoundByDuty] && !condition[ConditionFlag.BetweenAreas])
            {
                if (busy) DalamudServices.PluginInterface.GetIpcSubscriber<object>("AutoDuty.Stop").InvokeAction();
                var elapsed = Environment.TickCount64 - dutyCompletedAt;
                executionStatus = $"副本已通关，留在副本等待书本更新：{elapsed / 1000} 秒；书本完成={dungeons[dutySlot]}";
                if (elapsed < 10000) return;
                if (!dungeons[dutySlot])
                {
                    if (elapsed >= 30000) StopRun("通关后等待 30 秒书本仍未更新，已停止并留在副本内供检查");
                    return;
                }
                if (!dutyExitRequested)
                {
                    if (!DalamudServices.CommandManager.ProcessCommand("/ad exitduty"))
                        throw new InvalidOperationException("AutoDuty 退本命令不可用，已留在副本内");
                    dutyExitRequested = true;
                    TraceLifecycle("duty.book-confirmed.exit-requested");
                }
                return;
            }
            if (!busy && dutyWasBusy && !condition[ConditionFlag.BoundByDuty] && !condition[ConditionFlag.BetweenAreas])
            {
                ownsDuty = false;
                RestoreDutyOverrides();
                if (!dungeons[dutySlot]) { StopRun("AutoDuty 已结束，但书本未完成，已停止"); return; }
                dutySlot = -1;
                if (singleDuty) { StopRun("单个副本验证完成，书本进度已更新"); return; }
            }
            else if (!busy && !dutyWasBusy && Environment.TickCount64 - phaseAt > 15000)
            { StopRun("AutoDuty 未接受执行请求"); return; }
            return;
        }
        for (var i = 0; i < 3; i++)
        {
            if (dungeons[i]) continue;
            var territory = dungeonTerritories[i];
            if (territory == 0 || !DalamudServices.PluginInterface.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath").InvokeFunc(territory))
            { StopRun($"缺少副本映射或 AutoDuty 路线：{dungeonNames[i]}"); return; }
            if (!IpcBool("AutoDuty.IsStopped")) { StopRun("AutoDuty 正被其他任务使用"); return; }
            var overrides = new Dictionary<string, string>
            {
                ["Meta.AutoDutyModeEnum"] = "Looping", ["Meta.LoopTimes"] = "1",
                ["Meta.DutyModeEnum"] = "Regular", ["Meta.Unsynced"] = "true",
                ["DutyConfig.AutoExitDuty"] = "false", ["DutyConfig.AutoManageBossModAISettings"] = "true"
            };
            if (!DalamudServices.PluginInterface.GetIpcSubscriber<object, bool>("AutoDuty.PushConfigOverrides").InvokeFunc(overrides))
                throw new InvalidOperationException("AutoDuty 不支持临时配置，未开始副本");
            ownsOverrides = true;
            dutySlot = i;
            dutyTerritory = territory;
            enteredDuty = dutyWasBusy = false;
            dutyCompletedAt = 0;
            dutyExitRequested = false;
            phaseAt = Environment.TickCount64;
            ownsDuty = true;
            DalamudServices.PluginInterface.GetIpcSubscriber<uint, int, bool, object>("AutoDuty.Run").InvokeAction(territory, 1, true);
            executionStatus = $"已请求单次解除限制副本：{dungeonNames[i]}";
            return;
        }
        StopRun("全部副本目标已完成", true);
    }

    private static bool IpcBool(string name) => DalamudServices.PluginInterface.GetIpcSubscriber<bool>(name).InvokeFunc();

    // 只操作当前理符对应的任务记录、对象和交互窗口。
    private void DriveLeves()
    {
        if (HasLeveReturnPrompt())
        {
            ConfirmLeveReturn();
            return;
        }
        if (leveSlot >= 0 && leves[leveSlot] && refreshLeveId == 0
            && QuestManager.Instance() != null && QuestManager.Instance()->GetLeveQuestById((ushort)leveIds[leveSlot]) == null)
        {
            StopPath();
            if (FindAddon("SelectString") != null)
            {
                if (Environment.TickCount64 >= leveUiAt) { ClickLabel("SelectString", "取消"); leveUiAt = Environment.TickCount64 + 1000; }
                return;
            }
            leveSlot = -1;
            if (singleLeve) { StopRun("单项理符完成，书本进度已更新"); return; }
        }
        if (leveSlot < 0)
        {
            var pendingQuests = QuestManager.Instance();
            leveSlot = Enumerable.Range(0, 3).FirstOrDefault(i => pendingQuests != null
                && pendingQuests->GetLeveQuestById((ushort)leveIds[i]) != null, -1);
            if (leveSlot < 0) leveSlot = Array.FindIndex(leves, complete => !complete);
            if (leveSlot < 0) { StopRun("全部理符目标已完成", true); return; }
            if (!SupportsLeve(leveIds[leveSlot]))
            { StopRun($"该理符尚无执行策略：{leveLabels[leveSlot]}"); return; }
            leveAt = Environment.TickCount64;
            patrolPoint = 0; patrolArrivedAt = 0;
            dogGuideInitialized = false; dogGuidePhase = 0;
            dogBeckonAt = 0; dogNoFollowAttempts = 0;
            levePhase = "前往发行人";
            teleportRequested = false;
            // Resume an already accepted supported backup at the same issuer after reload.
            if (LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Leve>(leveIds[leveSlot], out var targetLeve))
            {
                foreach (var backup in DalamudServices.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Leve>()
                    .Where(x => x.RowId != leveIds[leveSlot] && x.LevelLevemete.RowId == targetLeve.LevelLevemete.RowId
                        && (x.LeveVfx.RowId is 21 or 22 or 36 || x.RowId is 644 or 645 or 849)))
                {
                    if (QuestManager.Instance()->GetLeveQuestById((ushort)backup.RowId) == null) continue;
                    refreshLeveId = backup.RowId; refreshLeveAccepted = true;
                    leveRefreshAttempts = Math.Max(1, leveRefreshAttempts);
                    break;
                }
            }
        }
        if (Environment.TickCount64 - leveAt > 1500000) { StopRun("单项理符超过 25 分钟，已停止"); return; }
        var id = refreshLeveId != 0 ? refreshLeveId : leveIds[leveSlot];
        if (!LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Leve>(id, out var leve))
            throw new InvalidOperationException("理符数据不存在");
        var quests = QuestManager.Instance();
        if (quests == null) return;
        var work = quests->GetLeveQuestById((ushort)id);
        var leveName = leve.Name.ExtractText();
        if (id == 647 && work != null && work->Sequence == 3)
        {
            StopPath();
            if (rotationActive) RestoreCombatRotation();
            levePhase = executionStatus = "警犬引导失败，准备重新挑战";
            if (Environment.TickCount64 < leveUiAt) return;
            if (quests->NumLeveAllowances == 0)
            { StopRun("理符受理权用尽，暂缓失败理符，继续本书其他任务", autoBooks); return; }
            if (dogRetryAttempts >= 2 || quests->NumLeveAllowances < 2)
            { StopRun("引导重试达到上限或受理权不足，保留最后1次受理权"); return; }
            var retry = (AddonSelectYesno*)FindAddon("SelectYesno");
            if (retry != null && retry->PromptText != null
                && retry->PromptText->NodeText.ToString().Contains("重新挑战理符任务"))
            {
                dogRetryAttempts++;
                dogGuideInitialized = false; dogGuidePhase = 0; dogBeckonAt = 0; dogNoFollowAttempts = 0;
                retry->FireCallbackInt(0);
                leveUiAt = Environment.TickCount64 + 5000;
                return;
            }
            var journal = (AddonJournalDetail*)FindAddon("JournalDetail");
            if (journal != null && JournalMatches((AtkUnitBase*)journal, leveName))
                ClickButton((AtkUnitBase*)journal, journal->GetComponentButtonById(51));
            else AgentQuestJournal.Instance()->OpenForQuest(id, 2);
            leveUiAt = Environment.TickCount64 + 1500;
            return;
        }
        if (refreshLeveId != 0 && work != null) refreshLeveAccepted = true;
        if (refreshLeveId != 0 && refreshLeveAccepted && work == null)
        {
            if (!refreshRewardRequested) { StopRun("备用理符消失但未确认交付，已停止"); return; }
            if (FindAddon("Talk") != null)
            {
                if (Environment.TickCount64 >= leveUiAt) { AdvanceTalk(); leveUiAt = Environment.TickCount64 + 1200; }
                return;
            }
            refreshLeveId = 0; refreshLeveAccepted = refreshRewardRequested = false;
            leveAt = Environment.TickCount64; leveUiAt = leveAt + 1500;
            levePhase = $"备用理符已交付，重新检查书本目标（刷新 {leveRefreshAttempts}/3）";
            return;
        }
        leveDebug = work == null ? $"未受理 {id}" : $"已受理 {work->LeveId} Sequence={work->Sequence}";
        executionStatus = $"理符：{leveName}；{levePhase}";
        if (work == null)
        {
            if (quests->NumLeveAllowances == 0)
            { StopRun("理符受理权用尽，暂缓未受理理符，继续本书其他任务", autoBooks); return; }
            if (!TravelToLevel(leve.LevelLevemete.RowId)) return;
            if (Environment.TickCount64 < leveUiAt) return;
            leveUiAt = Environment.TickCount64 + 1000;
            var journal = (AddonJournalDetail*)FindAddon("JournalDetail");
            if (journal != null && JournalMatches((AtkUnitBase*)journal, leveName))
            {
                levePhase = "受理目标理符";
                ClickButton((AtkUnitBase*)journal, journal->GetComponentButtonById(50));
                return;
            }
            if (FindAddon("GuildLeve") != null)
            {
                var names = GuildLeveNames();
                var sheet = DalamudServices.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Leve>();
                var offered = sheet.Where(x => names.Contains(x.Name.ExtractText()) && x.LevelLevemete.RowId == leve.LevelLevemete.RowId).ToArray();
                if (leve.LeveAssignmentType.RowId is 13 or 14 or 15 && offered.Length > 0
                    && !offered.Any(x => x.LeveAssignmentType.RowId == leve.LeveAssignmentType.RowId))
                {
                    levePhase = $"当前类别不符，返回选择{leve.LeveAssignmentType.Value.Name.ExtractText()}";
                    FindAddon("GuildLeve")->FireCallbackInt(-1);
                    return;
                }
                levePhase = "选择目标理符";
                if (ClickLabel("GuildLeve", leveName)) return;
                if (refreshLeveId != 0) { StopRun("已选备用理符当前不再提供，已停止"); return; }
                if (offered.Length == 0) { levePhase = "等待理符列表加载"; return; }
                if (leveRefreshAttempts >= 3) { StopRun("已完成 3 次备用理符刷新，目标仍未提供，已停止"); return; }
                if (quests->NumLeveAllowances < 2) { StopRun("受理权不足，保留最后 1 次给书本目标"); return; }
                foreach (var backup in offered.Where(x => x.RowId != id && (x.LeveVfx.RowId is 21 or 22 or 36 || x.RowId is 644 or 645 or 849)
                    && x.ClassJobLevel <= objectTable.LocalPlayer.Level).OrderBy(x => x.TimeLimit).ThenBy(x => x.RowId))
                {
                    if (!ClickLabel("GuildLeve", backup.Name.ExtractText())) continue;
                    refreshLeveId = backup.RowId; leveRefreshAttempts++;
                    refreshLeveAccepted = refreshRewardRequested = false;
                    leveAt = Environment.TickCount64;
                    levePhase = $"刷新列表：完成备用 {backup.Name.ExtractText()}（{leveRefreshAttempts}/3）";
                    return;
                }
                StopRun("列表没有已适配的备用理符，已停止，未消耗受理权");
                return;
            }
            if (FindAddon("SelectString") != null)
            {
                levePhase = "选择理符类别";
                if (leve.LeveAssignmentType.RowId is 13 or 14 or 15)
                {
                    ClickLabel("SelectString", leve.LeveAssignmentType.Value.Name.ExtractText());
                    return;
                }
                if (ClickLabel("SelectString", "佣兵任务")) return;
                if (ClickLabel("SelectString", "战斗理符")) return;
                if (ClickLabel("SelectString", "军队理符")) return;
                if (ClickLabel("SelectString", "黑涡团")) return;
                ClickLabel("SelectString", "双蛇党");
                return;
            }
            levePhase = "与理符发行人交互";
            InteractLevemete(leve.LevelLevemete.RowId);
            return;
        }
        if (work->Sequence == byte.MaxValue)
        {
            levePhase = "返回发行人交付";
            var yesno = (AddonSelectYesno*)FindAddon("SelectYesno");
            if (yesno != null && yesno->PromptText != null)
            {
                var prompt = yesno->PromptText->NodeText.ToString();
                if (prompt.Contains("领取报酬") && prompt.Contains("军票数量已达到上限"))
                {
                    levePhase = "军票已满，确认领取可获得的报酬并完成交付";
                    if (Environment.TickCount64 >= leveUiAt)
                    {
                        if (refreshLeveId != 0) refreshRewardRequested = true;
                        ((AtkUnitBase*)yesno)->FireCallbackInt(0);
                        leveUiAt = Environment.TickCount64 + 1500;
                    }
                    return;
                }
            }
            if (yesno != null && yesno->PromptText != null
                && IsLeveReturnText(yesno->PromptText->NodeText.ToString()))
            {
                StopPath();
                levePhase = "确认免费返回交付集落";
                if (Environment.TickCount64 >= leveUiAt)
                {
                    ((AtkUnitBase*)yesno)->FireCallbackInt(0);
                    leveUiAt = Environment.TickCount64 + 3000;
                }
                return;
            }
            if (Environment.TickCount64 < leveUiAt) return;
            if (!TravelToLevel(leve.LevelLevemete.RowId)) return;
            if (Environment.TickCount64 < leveUiAt) return;
            leveUiAt = Environment.TickCount64 + 1000;
            var result = (AddonJournalResult*)FindAddon("JournalResult");
            if (result != null)
            {
                if (result->AtkTextNode250 != null && result->AtkTextNode250->NodeText.ToString().Contains(leveName, StringComparison.Ordinal))
                {
                    if (refreshLeveId != 0 && result->CompleteButton != null && result->CompleteButton->IsEnabled)
                        refreshRewardRequested = true;
                    ClickButton((AtkUnitBase*)result, result->CompleteButton);
                }
                return;
            }
            if (FindAddon("SelectString") != null)
            {
                // Selecting the completed quest can deliver its reward synchronously.
                // Record the request before entering native UI event handling.
                var rewardWasRequested = refreshRewardRequested;
                if (refreshLeveId != 0) refreshRewardRequested = true;
                if (!ClickLabel("SelectString", leveName) && !ClickLabel("SelectString", "领取报酬")
                    && refreshLeveId != 0) refreshRewardRequested = rewardWasRequested;
                return;
            }
            InteractLevemete(leve.LevelLevemete.RowId);
            return;
        }
        var director = FindLeveDirector((ushort)id);
        if (director == null)
        {
            var confirm = (AddonSelectYesno*)FindAddon("SelectYesno");
            if (confirm != null && confirm->PromptText != null
                && confirm->PromptText->NodeText.ToString().Contains("要开始理符任务吗", StringComparison.Ordinal))
            {
                levePhase = "确认启动目标理符";
                if (Environment.TickCount64 >= leveUiAt)
                {
                    leveUiAt = Environment.TickCount64 + 1500;
                    ClickButton((AtkUnitBase*)confirm, confirm->GetComponentButtonById(8));
                }
                return;
            }
            if (Environment.TickCount64 >= leveUiAt && FindAddon("GuildLeve") is var vendor && vendor != null)
            {
                levePhase = "退出发行人列表";
                vendor->FireCallbackInt(-1);
                leveUiAt = Environment.TickCount64 + 1000;
                return;
            }
            if (FindAddon("SelectString") != null)
            {
                if (Environment.TickCount64 >= leveUiAt) { ClickLabel("SelectString", "取消"); leveUiAt = Environment.TickCount64 + 1000; }
                return;
            }
            levePhase = "前往理符启动点";
            if (!TravelToLevel(leve.LevelStart.RowId)) return;
            if (Environment.TickCount64 < leveUiAt) return;
            leveUiAt = Environment.TickCount64 + 1000;
            var difficulty = (AddonGuildLeveDifficulty*)FindAddon("GuildLeveDifficulty");
            if (difficulty != null)
            {
                levePhase = "选择最低难度并启动";
                if (difficulty->DifficultySlider != null) difficulty->DifficultySlider->SetValue(difficulty->DifficultySlider->MinValue);
                if (!ClickLabel("GuildLeveDifficulty", "开始")) ClickLabel("GuildLeveDifficulty", "确定");
                return;
            }
            var journal = (AddonJournalDetail*)FindAddon("JournalDetail");
            if (journal != null && JournalMatches((AtkUnitBase*)journal, leveName))
            {
                levePhase = "启动目标理符";
                leveUiAt = Environment.TickCount64 + 3000;
                var button = journal->GetComponentButtonById(51);
                leveDebug += $" 启动按钮={button != null} 点击={ClickButton((AtkUnitBase*)journal, button)}";
                return;
            }
            var questJournal = AgentQuestJournal.Instance();
            if (questJournal == null) throw new InvalidOperationException("任务日志不可用");
            questJournal->OpenForQuest(id, 2);
            return;
        }
        levePhase = "执行场地目标";
        var player = objectTable.LocalPlayer;
        if (player == null) return;
        IBattleNpc enemy = null;
        IGameObject objective = null;
        var leveActors = new List<IBattleNpc>();
        foreach (var obj in objectTable)
        {
            if (!obj.IsTargetable || obj.Address == IntPtr.Zero || Vector3.Distance(player.Position, obj.Position) > 250) continue;
            if (obj is IBattleNpc && TargetTemporarilyBlocked(obj)) continue;
            var native = (NativeGameObject*)obj.Address;
            if (native->EventHandler != (NativeEventHandler*)director && !native->EventId.Equals(director->Info.EventId)) continue;
            if (obj is IBattleNpc actor && actor.CurrentHp > 0) leveActors.Add(actor);
            if (obj is IBattleNpc battle && battle.CurrentHp > 0 && battle.StatusFlags.HasFlag(StatusFlags.Hostile))
            {
                if (enemy == null || Vector3.Distance(player.Position, battle.Position) < Vector3.Distance(player.Position, enemy.Position)) enemy = battle;
            }
            if ((id == 644 && obj.BaseId == 2001098 || id is 646 or 652 && obj.BaseId == 2001104
                || id == 645 && obj.ObjectKind == ObjectKind.EventObj) && (objective == null
                || Vector3.DistanceSquared(player.Position, obj.Position) < Vector3.DistanceSquared(player.Position, objective.Position))) objective = obj;
        }
        if (id == 645 && DriveLureLeve(leveActors, objective)) return;
        if (id == 849 && DriveImpostorLeve(leveActors)) return;
        if (id == 647) { DriveDogLeve(leveActors, leve.LevelLevemete.RowId); return; }
        if (enemy != null) { AttackLeveEnemy(enemy); return; }
        if (objective != null)
        {
            if (rotationActive) RestoreCombatRotation();
            var distance = Vector3.Distance(player.Position, objective.Position);
            if (condition[ConditionFlag.InFlight] && HorizontalDistance(player.Position, objective.Position) < 35
                && !PrepareDismount(objective.Position)) return;
            if (distance > 3) { DrivePath(player.Position, objective.Position); return; }
            if (!PrepareDismount(objective.Position)) return;
            StopPath();
            if (Environment.TickCount64 < leveUiAt) return;
            leveUiAt = Environment.TickCount64 + 1500;
            TargetSystem.Instance()->InteractWithObject((NativeGameObject*)objective.Address);
            return;
        }
        if (id is 646 or 649 or 652)
        {
            if (rotationActive) RestoreCombatRotation();
            var searchPoints = id == 646 ? Patrol646Points : id == 649 ? Search649Points : Patrol652Points;
            patrolPoint %= searchPoints.Length;
            var probe = searchPoints[patrolPoint];
            var ground = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable")
                .InvokeFunc(probe, 15f, 20f);
            if (!ground.HasValue) { StopRun("巡逻点未找到可达地面，已停止"); return; }
            levePhase = executionStatus = $"搜索任务地点 {patrolPoint + 1}/{searchPoints.Length}，寻找本理符目标";
            if (HorizontalDistance(player.Position, ground.Value) > 5)
            { patrolArrivedAt = 0; DrivePath(player.Position, ground.Value); return; }
            StopPath();
            if (patrolArrivedAt == 0) patrolArrivedAt = Environment.TickCount64;
            if (Environment.TickCount64 - patrolArrivedAt > 5000)
            { patrolPoint = (patrolPoint + 1) % searchPoints.Length; patrolArrivedAt = 0; }
            return;
        }
        StopPath();
        executionStatus += "；等待属于本理符的目标出现";
    }

    private void DriveDogLeve(List<IBattleNpc> actors, uint issuerLevel)
    {
        var player = objectTable.LocalPlayer;
        if (player == null) return;
        var dog = actors.FirstOrDefault(x => x.NameId == 1014 && x.CurrentHp > 0);
        if (dog == null)
        { StopPath(); levePhase = executionStatus = "等待本理符的艾玛吉娜看门犬加载"; return; }
        if (!LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Level>(issuerLevel, out var level))
        { StopRun("无法读取警犬返回地点"); return; }
        var home = new Vector3(level.X, level.Y, level.Z);
        if (condition[ConditionFlag.Casting]) return;
        if (dog.CurrentHp < dog.MaxHp * 0.85f && player.ClassJob.RowId == 24
            && ActionManager.CanUseActionOnTarget(135, (NativeGameObject*)dog.Address))
        {
            var actions = ActionManager.Instance();
            if (actions != null && actions->GetActionStatus(ActionType.Action, 135, dog.GameObjectId) == 0)
            {
                StopPath();
                if (rotationActive) RestoreCombatRotation();
                actions->UseAction(ActionType.Action, 135, dog.GameObjectId);
                levePhase = executionStatus = "优先治疗受伤的警犬";
                return;
            }
        }
        var ahead = home - player.Position; ahead.Y = 0;
        if (ahead.LengthSquared() > 1) ahead = Vector3.Normalize(ahead);
        var threat = objectTable.OfType<IBattleNpc>().Where(x => x.CurrentHp > 0 && x.IsTargetable
            && x.GameObjectId != dog.GameObjectId && Vector3.Distance(x.Position, player.Position) < 45
            && !TargetTemporarilyBlocked(x) && ActionManager.CanUseActionOnTarget(TargetCheckAction(), (NativeGameObject*)x.Address))
            .Where(x =>
            {
                if (x.TargetObjectId == dog.EntityId || x.TargetObjectId == player.EntityId) return true;
                if (((NativeGameObject*)x.Address)->FateId != 0) return false;
                var relative = x.Position - player.Position; relative.Y = 0;
                var forward = Vector3.Dot(relative, ahead);
                return HorizontalDistance(x.Position, dog.Position) < 20
                    || forward >= 0 && forward <= 30 && (relative - ahead * forward).Length() < 15;
            })
            .OrderBy(x => x.TargetObjectId == dog.EntityId ? 0 : x.TargetObjectId == player.EntityId ? 1 : 2)
            .ThenBy(x => Vector3.DistanceSquared(x.Position, dog.Position)).FirstOrDefault();
        if (threat != null)
        {
            dogProtecting = true;
            levePhase = executionStatus = $"保护警犬，先清理沿途敌人：{threat.Name}";
            if (PrepareEnemyFateSync(threat)) AttackLeveEnemy(threat);
            return;
        }
        if (dogProtecting)
        { StopPath(); dogGuideInitialized = false; dogGuidePhase = 0; dogProtecting = false; }
        if (rotationActive) RestoreCombatRotation();
        if (!PrepareDismount(dog.Position)) return;
        var distance = Vector3.Distance(player.Position, dog.Position);
        var now = Environment.TickCount64;
        if (!dogGuideInitialized)
        {
            if (distance > 12)
            { levePhase = executionStatus = "首次接近警犬，准备分段引导"; DrivePath(player.Position, dog.Position); return; }
            dogGuideInitialized = true;
            dogGuidePhase = 0;
        }
        if (dogGuidePhase == 0)
        {
            dogStepStart = player.Position;
            dogPhaseAt = now;
            dogGuidePhase = 1;
        }
        if (dogGuidePhase == 1)
        {
            var moved = HorizontalDistance(player.Position, dogStepStart);
            if (moved >= 13 || HorizontalDistance(player.Position, new(level.X, level.Y, level.Z)) < 3
                || moved > 2 && now - dogPhaseAt > 1500 && pathTask == null && !IpcBool("vnavmesh.Path.IsRunning"))
            { StopPath(); dogGuidePhase = 2; dogPhaseAt = now; }
            else
            {
                levePhase = executionStatus = "先向营地方向步行一段，再停下招手";
                DrivePath(player.Position, new Vector3(level.X, level.Y, level.Z), false, 14);
                return;
            }
        }
        if (dogGuidePhase == 2)
        {
            StopPath();
            if (now - dogPhaseAt < 750) return;
            if (distance > 25)
            { dogGuideInitialized = false; levePhase = executionStatus = "警犬掉队过远，重新接近后引导"; return; }
            targetManager.Target = dog; targetId = dog.GameObjectId;
            var emotes = EmoteManager.Instance();
            var option = new EmoteController.PlayEmoteOption { TargetId = dog.GameObjectId, DisableLogMessage = true };
            if (emotes != null && emotes->CanExecuteEmote(8) && emotes->ExecuteEmote(8, &option))
            {
                dogBeckonAt = now; dogLastBeckonPosition = dog.Position;
                dogGuidePhase = 3;
            }
            levePhase = executionStatus = "已走到前方，向警犬招手";
            return;
        }
        StopPath();
        levePhase = executionStatus = "原地等待警犬跟上，再走下一段";
        if (now - dogBeckonAt >= 1500 && distance <= 5)
        { dogNoFollowAttempts = 0; dogGuidePhase = 0; return; }
        if (now - dogBeckonAt > 12000)
        {
            if (HorizontalDistance(dog.Position, dogLastBeckonPosition) < 1 && ++dogNoFollowAttempts >= 3)
            { StopRun("警犬三次未确认跟随，已停止并保留任务"); return; }
            dogGuidePhase = 2; dogPhaseAt = now;
        }
    }
    private static bool SupportsLeve(uint id) =>
        id is 644 or 645 or 646 or 647 or 649 or 652 or 849
        || LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Leve>(id, out var row) && row.LeveVfx.RowId is 21 or 22 or 36;

    private static bool SupportsFate(ushort id) =>
        id is 611 or 571 or 424 or 633
        || LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Fate>(id, out var row) && row.Rule == 1 && row.Icon is 60721 or 60722;

    private bool HasLeveReturnPrompt()
    {
        var confirm = (AddonSelectYesno*)FindAddon("SelectYesno");
        if (confirm == null || confirm->PromptText == null
            || !IsLeveReturnText(confirm->PromptText->NodeText.ToString())) return false;
        return true;
    }

    private static bool IsLeveReturnText(string text) =>
        text.Contains("目标地点附近的集落", StringComparison.Ordinal)
        || (text.Contains("要回到", StringComparison.Ordinal) || text.Contains("要返回", StringComparison.Ordinal))
            && text.Contains("理符发行人", StringComparison.Ordinal)
            && (text.Contains("身边", StringComparison.Ordinal) || text.Contains("附近", StringComparison.Ordinal));

    private void ConfirmLeveReturn()
    {
        StopPath();
        if (rotationActive) RestoreCombatRotation();
        levePhase = executionStatus = "理符已完成，确认返回交付集落";
        if (Environment.TickCount64 < leveReturnAt) return;
        var prompt = FindAddon("SelectYesno");
        if (prompt == null) return;
        leveReturnAt = Environment.TickCount64 + 1000;
        prompt->FireCallbackInt(0);
        leveUiAt = Math.Max(leveUiAt, Environment.TickCount64 + 3000);
    }

    private HashSet<string> GuildLeveNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var addon = (AddonGuildLeve*)FindAddon("GuildLeve");
        if (addon == null || addon->AtkComponentTreeList228 == null) return names;
        var tree = addon->AtkComponentTreeList228;
        if (tree->Items.Count > 256) return names;
        for (var i = 0; i < tree->Items.Count; i++)
        {
            var item = tree->GetItem(i);
            if (item == null || item->IsHidden || item->StringValues.Count == 0 || item->StringValues.Count > 32) continue;
            names.Add(item->StringValues[0].ToString());
        }
        return names;
    }

    private bool DriveLureLeve(List<IBattleNpc> actors, IGameObject point)
    {
        var boss = actors.FirstOrDefault(x => x.NameId == 1739);
        if (boss != null) { levePhase = "击杀被引出的恶神之眼巴罗尔"; AttackLeveEnemy(boss); return true; }
        var inventory = InventoryManager.Instance();
        var count = inventory == null ? 0 : inventory->GetInventoryItemCount(2000872);
        var now = Environment.TickCount64;
        if (crystalPending != 0)
        {
            StopPath();
            if (condition[ConditionFlag.Casting]) return true;
            if (count < crystalCountBefore)
            { crystalPending = 0; crystalUsedAt = now; leveUiAt = now + 6000; return true; }
            if (now - crystalUsedAt < 6000) return true;
            crystalPending = 0;
            if (++companionAttempts >= 3) { StopRun("忘忧泪三次未确认使用成功，已停止"); return true; }
        }
        if (now < leveUiAt) return true;
        if (count == 0)
        {
            var source = actors.FirstOrDefault(x => x.NameId == 1738);
            if (source != null) { levePhase = "击杀巴罗尔之铃取得忘忧泪"; AttackLeveEnemy(source); }
            else { StopPath(); levePhase = "等待本理符的巴罗尔之铃"; }
            return true;
        }
        levePhase = "向本理符指定地点使用忘忧泪";
        if (point == null) { StopPath(); levePhase += "（等待目标地点加载）"; return true; }
        if (!ApproachLeveActor(point, 3)) return true;
        var actions = ActionManager.Instance();
        if (actions != null && actions->GetActionStatus(ActionType.EventItem, 2000872, point.GameObjectId) == 0
            && actions->UseAction(ActionType.EventItem, 2000872, point.GameObjectId))
        { crystalPending = point.GameObjectId; crystalCountBefore = count; crystalUsedAt = now; }
        return true;
    }

    private bool DriveImpostorLeve(List<IBattleNpc> actors)
    {
        var player = objectTable.LocalPlayer;
        if (player == null) return true;
        var revealed = actors.Where(x => x.NameId == 1788).OrderBy(x => Vector3.DistanceSquared(player.Position, x.Position)).FirstOrDefault();
        if (revealed != null) { levePhase = "击杀已现形通缉犯"; AttackLeveEnemy(revealed); return true; }
        var inventory = InventoryManager.Instance();
        var crystals = inventory == null ? 0 : inventory->GetInventoryItemCount(2000558);
        leveDebug += $" 流光水晶={crystals}";
        var now = Environment.TickCount64;
        if (crystalPending != 0)
        {
            StopPath();
            levePhase = "等待水晶施放完成并核对结果";
            if (condition[ConditionFlag.Casting]) return true;
            if (crystals < crystalCountBefore)
            {
                crystalAttempts[crystalPending] = now;
                crystalPending = crystalTarget = 0;
                settledAt = 0;
                return true;
            }
            if (now - crystalUsedAt < 6000) return true;
            var retryTarget = crystalPending;
            crystalPending = 0;
            crystalFailures.TryGetValue(retryTarget, out var failures);
            crystalFailures[retryTarget] = ++failures;
            if (failures >= 3) { StopRun("同一目标水晶施放三次未确认成功，已停止；请检查距离和施放中断"); return true; }
            crystalTarget = retryTarget;
            settledAt = 0;
            leveUiAt = now + 1500;
            return true;
        }
        if (condition[ConditionFlag.Casting]) { StopPath(); return true; }
        if (crystals > 0)
        {
            if (now < leveUiAt) return true;
            var candidates = actors.Where(x => x.NameId is 1638 or 1789)
                .Where(x => !crystalAttempts.ContainsKey(x.GameObjectId)).ToList();
            var disguise = candidates.FirstOrDefault(x => x.GameObjectId == crystalTarget)
                ?? candidates.OrderBy(x => Vector3.DistanceSquared(player.Position, x.Position)).FirstOrDefault();
            if (disguise == null) { levePhase = "等待伪装目标或水晶效果"; return true; }
            crystalTarget = disguise.GameObjectId;
            levePhase = "对伪装目标使用流光水晶";
            if (!ApproachLeveActor(disguise, 3)) return true;
            if (now < leveUiAt) return true;
            var actions = ActionManager.Instance();
            if (actions != null && actions->GetActionStatus(ActionType.EventItem, 2000558, disguise.GameObjectId) == 0)
            {
                targetId = disguise.GameObjectId;
                targetManager.Target = disguise;
                if (actions->UseAction(ActionType.EventItem, 2000558, disguise.GameObjectId))
                { crystalPending = disguise.GameObjectId; crystalCountBefore = crystals; crystalUsedAt = now; leveUiAt = now + 6000; }
            }
            return true;
        }
        var wisp = actors.Where(x => x.NameId == 491)
            .OrderBy(x => Vector3.DistanceSquared(player.Position, x.Position)).FirstOrDefault();
        if (wisp == null) { levePhase = "等待本理符的徘徊妖火"; return true; }
        if (!pokedWisps.Contains(wisp.GameObjectId))
        {
            levePhase = "对徘徊妖火使用戳指";
            if (!ApproachLeveActor(wisp, 4) || now < leveUiAt) return true;
            targetId = wisp.GameObjectId;
            targetManager.Target = wisp;
            var emotes = EmoteManager.Instance();
            if (emotes != null && emotes->CanExecuteEmote(28) && emotes->ExecuteEmote(28))
            { pokedWisps.Add(wisp.GameObjectId); pokeTarget = wisp.GameObjectId; pokeAt = now; leveUiAt = now + 2500; }
            return true;
        }
        if (pokeTarget == wisp.GameObjectId && now - pokeAt < 2500) return true;
        levePhase = "击杀徘徊妖火取得流光水晶";
        AttackLeveEnemy(wisp);
        return true;
    }

    private bool ApproachLeveActor(IGameObject actor, float range)
    {
        var player = objectTable.LocalPlayer;
        if (player == null) return false;
        if (landing && !condition[ConditionFlag.InFlight]) { StopPath(); landing = false; }
        if (condition[ConditionFlag.InFlight] && HorizontalDistance(player.Position, actor.Position) < 35
            && !PrepareDismount(actor.Position)) return false;
        if (condition[ConditionFlag.Mounted] && !condition[ConditionFlag.InFlight]
            && HorizontalDistance(player.Position, actor.Position) <= 40 && !PrepareDismount(actor.Position)) return false;
        if (Vector3.Distance(player.Position, actor.Position) > range)
        { settledAt = 0; DrivePath(player.Position, actor.Position, true); return false; }
        if (!PrepareDismount(actor.Position)) return false;
        StopPath();
        var now = Environment.TickCount64;
        if (settledAt == 0 || settleTarget != actor.GameObjectId || Vector3.DistanceSquared(player.Position, settlePosition) > 0.0025f)
        { settleTarget = actor.GameObjectId; settlePosition = player.Position; settledAt = now; return false; }
        return now - settledAt >= 1000 && !IpcBool("vnavmesh.Path.IsRunning") && !IpcBool("vnavmesh.SimpleMove.PathfindInProgress");
    }

    private bool TravelToLevel(uint levelId)
    {
        if (!LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Level>(levelId, out var level))
            throw new InvalidOperationException($"地点数据缺失：{levelId}");
        var player = objectTable.LocalPlayer;
        if (player == null) return false;
        if (clientState.TerritoryType != level.Territory.RowId)
        {
            if (teleportRequested)
            {
                if (Environment.TickCount64 - teleportAt > 90000) StopRun("理符传送超时");
                return false;
            }
            if (condition[ConditionFlag.InCombat] || condition[ConditionFlag.Casting]) return false;
            if (!PrepareTeleport()) return false;
            StopPath();
            var telepo = Telepo.Instance();
            if (telepo == null) return false;
            telepo->UpdateAetheryteList();
            foreach (var crystal in telepo->TeleportList)
            {
                if (crystal.TerritoryId != level.Territory.RowId || crystal.SubIndex != 0 || crystal.IsSharedHouse) continue;
                if (!telepo->Teleport(crystal.AetheryteId, 0)) throw new InvalidOperationException("理符传送请求失败");
                teleportRequested = true;
                teleportAt = Environment.TickCount64;
                teleportDestination = level.Territory.RowId;
                return false;
            }
            throw new InvalidOperationException("目标理符区域没有已解锁水晶");
        }
        teleportRequested = false;
        if (!IpcBool("vnavmesh.Nav.IsReady")) return false;
        var destination = new Vector3(level.X, level.Y, level.Z);
        var surface = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable")
            .InvokeFunc(destination, 5f, 16f);
        if (surface.HasValue) destination = surface.Value;
        if (landing && !condition[ConditionFlag.InFlight]) { StopPath(); landing = false; }
        if (condition[ConditionFlag.InFlight] && HorizontalDistance(player.Position, destination) < 35)
        {
            PrepareDismount(destination);
            return false;
        }
        if (Vector3.Distance(player.Position, destination) <= 3)
        {
            if (!PrepareDismount(destination)) return false;
            StopPath();
            return true;
        }
        DrivePath(player.Position, destination, true);
        return false;
    }

    private void InteractLevemete(uint levelId)
    {
        if (!LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Level>(levelId, out var level)) return;
        if (FindAddon("Talk") != null) { levePhase = "等待对话结束（可使用已安装的对话跳过）"; return; }
        var npc = objectTable.FirstOrDefault(x => x.BaseId == level.Object.RowId && x.IsTargetable);
        if (npc != null) TargetSystem.Instance()->InteractWithObject((NativeGameObject*)npc.Address);
    }

    private static LeveDirector* FindLeveDirector(ushort id)
    {
        var framework = EventFramework.Instance();
        if (framework == null) return null;
        foreach (Director* director in framework->DirectorModule.DirectorList)
        {
            if (director == null || director->Info.EventId.ContentId is not (EventHandlerContent.BattleLeveDirector or EventHandlerContent.CompanyLeveDirector)) continue;
            var leve = (LeveDirector*)director;
            if (leve->LeveId == id) return leve;
        }
        return null;
    }

    private void AttackLeveEnemy(IBattleNpc target)
    {
        var player = objectTable.LocalPlayer;
        if (player == null) return;
        if (condition[ConditionFlag.Casting]) return;
        var ranged = player.ClassJob.RowId is 23 or 24 or 25 or 27 or 28;
        if (condition[ConditionFlag.InFlight] && HorizontalDistance(player.Position, target.Position) < 35
            && !PrepareDismount(target.Position)) return;
        if (Vector3.Distance(player.Position, target.Position) - target.HitboxRadius - player.HitboxRadius > (ranged ? 20 : 2.5f))
        { DrivePath(player.Position, target.Position, true); return; }
        if (!PrepareDismount(target.Position)) return;
        if (!EnsureTargetVisible(target)) return;
        StopPath();
        targetId = target.GameObjectId;
        targetManager.Target = target;
        ActivateCombatRotation();
    }

    private static bool JournalMatches(AtkUnitBase* addon, string title)
    {
        var text = addon->GetTextNodeById(38);
        return text != null && text->NodeText.ToString().Contains(title, StringComparison.Ordinal);
    }

    private AtkUnitBase* FindAddon(string name)
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
    }

    private static bool ClickButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (button == null || !button->IsEnabled) return false;
        return ClickRegistered(addon, button->OwnerNode);
    }

    private static bool ClickRegistered(AtkUnitBase* addon, AtkComponentNode* node)
    {
        if (node == null || !node->IsVisible()) return false;
        var count = 0;
        for (var entry = node->AtkEventManager.Event; entry != null && count++ < 64; entry = entry->NextEvent)
        {
            if (entry->State.EventType != AtkEventType.ButtonClick || entry->Listener != &addon->AtkEventListener) continue;
            // Keep the native event's target and listener; some addons inspect
            // these fields and cannot handle a synthetic component event.
            TraceLifecycle($"button.before node={node->NodeId} param={entry->Param}");
            addon->ReceiveEvent(entry->State.EventType, (int)entry->Param, entry);
            TraceLifecycle("button.returned");
            return true;
        }
        return false;
    }

    private bool ClickLabel(string addonName, string label)
    {
        var addon = FindAddon(addonName);
        if (addon == null) return false;
        if (addonName == "GuildLeve")
        {
            var tree = ((AddonGuildLeve*)addon)->AtkComponentTreeList228;
            if (tree == null || tree->Items.Count > 256) return false;
            for (var i = 0; i < tree->Items.Count; i++)
            {
                var item = tree->GetItem(i);
                if (item == null || item->StringValues.Count == 0 || item->StringValues.Count > 32
                    || item->StringValues[0].ToString() != label || item->IsHidden
                    || (item->State & TreeListItemState.Disabled) != 0 || item->Renderer == null) continue;
                var index = item->Renderer->ListItemIndex;
                if (index < 0 || index >= tree->ListLength) return false;
                tree->SelectItem(index, true);
                tree->DispatchItemEvent(index, AtkEventType.ListItemClick);
                return true;
            }
            return false;
        }
        if (addonName is "SelectString" or "SelectIconString")
        {
            var menu = addonName == "SelectString" ? (PopupMenu*)&((AddonSelectString*)addon)->PopupMenu
                : (PopupMenu*)&((AddonSelectIconString*)addon)->PopupMenu;
            if (menu->EntryNames == null || menu->List == null || menu->EntryCount < 0 || menu->EntryCount > 32) return false;
            for (var i = 0; i < menu->EntryCount; i++)
            {
                if (i >= menu->List->ListLength) break;
                if (!menu->EntryNames[i].ToString().Contains(label, StringComparison.Ordinal) || menu->List->GetItemDisabledState(i)) continue;
                menu->List->DispatchItemEvent(i, AtkEventType.ListItemClick);
                return true;
            }
            return false;
        }
        var count = 0;
        return FindAndClickLabel(addon, &addon->UldManager, null, label, 0, ref count);
    }

    private static bool FindAndClickLabel(AtkUnitBase* addon, AtkUldManager* manager, AtkComponentNode* owner, string label, int depth, ref int count)
    {
        if (depth > 12 || manager == null) return false;
        for (var i = 0; i < manager->NodeListCount && count++ < 1000; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !node->IsVisible()) continue;
            if (node->Type == NodeType.Text)
            {
                var text = ((AtkTextNode*)node)->NodeText.ToString();
                if (text.Contains(label, StringComparison.Ordinal) && ClickRegistered(addon, owner)) return true;
            }
            if ((ushort)node->Type < 1000) continue;
            var component = (AtkComponentNode*)node;
            if (component->Component != null && FindAndClickLabel(addon, &component->Component->UldManager, component, label, depth + 1, ref count)) return true;
        }
        return false;
    }

    private bool PrepareFateCompanion()
    {
        var ui = UIState.Instance();
        if (ui != null && ui->Buddy.CompanionInfo.TimeLeft > 60
            && ui->Buddy.CompanionInfo.Companion != null && ui->Buddy.CompanionInfo.Companion->CurrentHealth > 0)
        { companionStatus = "陆行鸟已召唤"; companionAttempts = 0; companionRequestedAt = 0; return true; }
        companionStatus = executionStatus = "参战前召唤陆行鸟";
        var now = Environment.TickCount64;
        var inventory = InventoryManager.Instance();
        if (inventory == null || inventory->GetInventoryItemCount(4868) == 0)
        { StopRun("背包没有基萨尔野菜，无法先召唤陆行鸟"); return false; }
        if (!PrepareDismount(objectTable.LocalPlayer.Position)) return false;
        StopPath();
        if (companionRequestedAt != 0 && now - companionRequestedAt < 10000) return false;
        if (companionAttempts >= 3) { StopRun("陆行鸟召唤三次未确认成功，已停止"); return false; }
        if (companionSettleAt == 0) { companionSettleAt = now; return false; }
        if (now - companionSettleAt < 1000 || condition[ConditionFlag.Casting]) return false;
        var context = AgentInventoryContext.Instance();
        if (context == null) throw new InvalidOperationException("道具使用服务不可用");
        companionRequestedAt = now;
        companionAttempts++;
        context->UseItem(4868);
        return false;
    }

    private void ActivateCombatRotation()
    {
        if (rotationActive) return;
        // Snapshot only fields this module changes; never clear another plugin's overrides.
        var aiType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("BossMod.AI.AIManager", false)).FirstOrDefault(t => t != null)
            ?? throw new InvalidOperationException("未找到 BMR 自动移动接口，已停止以免无躲避战斗");
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var ai = aiType.GetField("Instance", flags)?.GetValue(null)
            ?? throw new InvalidOperationException("BMR 自动移动尚未就绪");
        combatAiConfig = aiType.GetField("_config", flags)?.GetValue(null)
            ?? throw new InvalidOperationException("无法读取 BMR 自动移动配置");
        var active = DalamudServices.PluginInterface.GetIpcSubscriber<string>("BossMod.Presets.GetActive").InvokeFunc();
        previousAiPreset = DalamudServices.PluginInterface.GetIpcSubscriber<string>("BossMod.AI.GetPreset").InvokeFunc();
        if (string.IsNullOrWhiteSpace(active)) active = previousAiPreset;
        foreach (var setting in new[] { ("ForbidMovement", false), ("ForbidActions", false), ("ManualTarget", true), ("FollowOutOfCombat", false), ("FollowTarget", false), ("AutoAFK", false) })
        {
            var field = combatAiConfig.GetType().GetField(setting.Item1)
                ?? throw new InvalidOperationException($"BMR 缺少配置 {setting.Item1}");
            combatAiSaved.TryAdd(field, field.GetValue(combatAiConfig));
            field.SetValue(combatAiConfig, setting.Item2);
        }
        if (aiType.GetField("Beh")?.GetValue(ai) == null)
        {
            if (!DalamudServices.CommandManager.ProcessCommand("/bmrai on")) throw new InvalidOperationException("无法开启 BMR 自动躲避");
            ownsCombatAi = true;
        }
        if (!string.IsNullOrWhiteSpace(active) && active != rotationPreset)
        {
            borrowedRotation = active;
            DalamudServices.PluginInterface.GetIpcSubscriber<string, object>("BossMod.AI.SetPreset").InvokeAction(active);
            combatAiPresetOwned = true;
            DalamudServices.PluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive").InvokeFunc(active);
            rotationActive = true;
            return;
        }
        if (rotationPreset == null)
        {
            previousRotation = DalamudServices.PluginInterface.GetIpcSubscriber<string>("BossMod.Presets.GetActive").InvokeFunc();
            previousRotationDisabled = IpcBool("BossMod.Presets.GetForceDisabled");
            var name = $"AnimusBookAssistant.FATE.{Guid.NewGuid():N}";
            var preset = JsonSerializer.Serialize(new
            {
                Name = name,
                Modules = new Dictionary<string, object>
                {
                    ["BossMod.Autorotation.xan.WHM"] = new[]
                    { new { Track = "Targeting", Option = "Manual" }, new { Track = "AOE", Option = "ForceST" } },
                    ["BossMod.Autorotation.xan.HealerAI"] = new[]
                    { new { Track = "Heal", Option = "Enabled" }, new { Track = "Stay near party", Option = "Disabled" } }
                }
            });
            if (!DalamudServices.PluginInterface.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create").InvokeFunc(preset, false))
                throw new InvalidOperationException("BossMod 未能创建 FATE 输出与治疗循环");
            rotationPreset = name;
            var actual = DalamudServices.PluginInterface.GetIpcSubscriber<string, string>("BossMod.Presets.Get").InvokeFunc(name);
            if (actual == null || !actual.Contains("BossMod.Autorotation.xan.WHM") || !actual.Contains("BossMod.Autorotation.xan.HealerAI"))
                throw new InvalidOperationException("BossMod 缺少白魔输出或自动治疗模块");
        }
        if (rotationActive) return;
        if (!DalamudServices.PluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive").InvokeFunc(rotationPreset))
            throw new InvalidOperationException("BossMod 未能启用 FATE 循环");
        DalamudServices.PluginInterface.GetIpcSubscriber<string, object>("BossMod.AI.SetPreset").InvokeAction(rotationPreset);
        combatAiPresetOwned = true;
        rotationActive = true;
    }

    private void RestoreCombatRotation()
    {
        var restoreTemporary = rotationPreset != null &&
            DalamudServices.PluginInterface.GetIpcSubscriber<string>("BossMod.Presets.GetActive").InvokeFunc() == rotationPreset;
        if (combatAiPresetOwned)
        {
            DalamudServices.PluginInterface.GetIpcSubscriber<string, object>("BossMod.AI.SetPreset").InvokeAction(previousAiPreset ?? "");
            combatAiPresetOwned = false;
        }
        if (ownsCombatAi) { DalamudServices.CommandManager.ProcessCommand("/bmrai off"); ownsCombatAi = false; }
        foreach (var saved in combatAiSaved) saved.Key.SetValue(combatAiConfig, saved.Value);
        combatAiSaved.Clear(); combatAiConfig = null;
        if (borrowedRotation != null)
        {
            DalamudServices.PluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive").InvokeFunc(borrowedRotation);
            borrowedRotation = null;
        }
        rotationActive = false;
        if (rotationPreset == null) return;
        var current = DalamudServices.PluginInterface.GetIpcSubscriber<string>("BossMod.Presets.GetActive").InvokeFunc();
        // 用户中途改过预设时，保留用户的选择。
        if (current == rotationPreset || restoreTemporary)
        {
            if (previousRotationDisabled) IpcBool("BossMod.Presets.SetForceDisabled");
            else if (previousRotation != null)
            {
                if (!DalamudServices.PluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive").InvokeFunc(previousRotation))
                    throw new InvalidOperationException("原 BossMod 循环未能恢复");
            }
            else IpcBool("BossMod.Presets.ClearActive");
        }
        rotationActive = false;
        if (!DalamudServices.PluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.Delete").InvokeFunc(rotationPreset))
            throw new InvalidOperationException("临时 BossMod 循环未能清理");
        rotationPreset = previousRotation = null;
    }

    private List<IBattleNpc> AttackingMe()
    {
        var player = objectTable.LocalPlayer;
        var ui = UIState.Instance();
        if (player == null || ui == null) return new();
        var ids = new HashSet<uint>();
        for (var i = 0; i < Math.Clamp(ui->Hater.HaterCount, 0, 32); i++)
            ids.Add(ui->Hater.Haters[i].EntityId);
        return objectTable.OfType<IBattleNpc>().Where(x => x.CurrentHp > 0 && x.IsTargetable
            && (ids.Contains(x.EntityId) || x.TargetObjectId == player.EntityId
                && ActionManager.CanUseActionOnTarget(TargetCheckAction(), (NativeGameObject*)x.Address))).ToList();
    }

    private bool HandleCombatCleanup(FateManager* manager)
    {
        var player = objectTable.LocalPlayer;
        var now = Environment.TickCount64;
        if (fateEscapeGoal.HasValue && DriveFateEscape(manager)) return true;
        var attackers = AttackingMe().Where(x =>
        {
            var id = ((NativeGameObject*)x.Address)->FateId;
            // Leave unfinished book enemies to the normal, synchronized FATE combat path.
            return !(mode == RunMode.Fates && Enumerable.Range(0, 3).Any(i => !fates[i] && FateWaitPoint(i).Territory == clientState.TerritoryType
                && (id == fateIds[i] || FatePrecursor(i) != 0 && id == FatePrecursor(i)) && FateAvailable(manager, id)));
        }).ToArray();
        if (attackers.Any(x => ((NativeGameObject*)x.Address)->FateId != 0) && DriveFateEscape(manager)) return true;
        if (attackers.Length == 0)
        {
            if (condition[ConditionFlag.InCombat])
            {
                if (manager->CurrentFate != null && DriveFateEscape(manager)) return true;
                fateCleanupStatus = executionStatus = "战斗状态尚未解除，等待敌对列表刷新或敌人返回后继续";
                return true;
            }
            if (clearingFateAggressors)
            {
                StopPath();
                if (targetId != 0 && targetManager.Target?.GameObjectId == targetId) targetManager.Target = null;
                RestoreCombatRotation();
                if (manager->SyncedFateId != 0)
                {
                    fateCleanupStatus = executionStatus = "攻击者已清理，退出临时 FATE 同步后继续原任务";
                    if (now - fateSyncAt > 2000 && manager->CurrentFate != null && manager->IsSyncedToFate(manager->CurrentFate))
                    { manager->LevelSync(); fateSyncAt = now; }
                    return true;
                }
                clearingFateAggressors = false;
            }
            fateCleanupStatus = "敌对列表中没有正在攻击角色的非目标敌人";
            return false;
        }
        if (!clearingFateAggressors)
        {
            StopPath(); RestoreCombatRotation();
            clearingFateAggressors = true; fateCleanupAt = now;
        }
        if (now - fateCleanupAt > 180000) { StopRun("清理攻击者超过 3 分钟，已停止"); return true; }
        var candidates = attackers.Where(x => !TargetTemporarilyBlocked(x)).ToArray();
        if (candidates.Length == 0)
        {
            StopPath(); RestoreCombatRotation();
            fateCleanupStatus = executionStatus = "攻击者暂时均不可达，等待跳过时限后重试";
            return true;
        }
        var enemy = candidates.OrderBy(x => ((NativeGameObject*)x.Address)->FateId == 0 ? 0 : 1)
            .ThenBy(x => x.GameObjectId == targetId ? 0 : 1)
            .ThenBy(x => Vector3.DistanceSquared(player.Position, x.Position)).First();
        if (!PrepareEnemyFateSync(enemy)) { fateCleanupStatus = executionStatus; return true; }
        fateCleanupStatus = executionStatus = $"按攻击者所属活动处理，清理 {enemy.Name}";
        if (condition[ConditionFlag.InFlight] && !PrepareDismount(enemy.Position)) return true;
        if (Vector3.Distance(player.Position, enemy.Position) - enemy.HitboxRadius > 20)
        { DrivePath(player.Position, enemy.Position); return true; }
        if (!PrepareDismount(enemy.Position)) return true;
        if (!EnsureTargetVisible(enemy)) { fateCleanupStatus = executionStatus; return true; }
        StopPath(); targetId = enemy.GameObjectId; targetManager.Target = enemy;
        ActivateCombatRotation();
        return true;
    }

    private bool DriveFateEscape(FateManager* manager)
    {
        var player = objectTable.LocalPlayer;
        if (manager == null || player == null) return false;
        var now = Environment.TickCount64;
        if (!fateEscapeGoal.HasValue)
        {
            var enemy = AttackingMe().FirstOrDefault(x => ((NativeGameObject*)x.Address)->FateId != 0)
                ?? objectTable.OfType<IBattleNpc>().FirstOrDefault(x => x.GameObjectId == targetId);
            var id = enemy == null ? (ushort)0 : ((NativeGameObject*)enemy.Address)->FateId;
            var fate = id == 0 ? manager->CurrentFate : manager->GetFateById(id);
            if (fate == null) return false;
            var away = player.Position - fate->Location; away.Y = 0;
            if (away.LengthSquared() < 1) away = Vector3.UnitX;
            away = Vector3.Normalize(away);
            Vector3? goal = null;
            for (var i = 0; i < 8; i++)
            {
                var angle = i * MathF.PI / 4;
                var direction = new Vector3(away.X * MathF.Cos(angle) - away.Z * MathF.Sin(angle), 0,
                    away.X * MathF.Sin(angle) + away.Z * MathF.Cos(angle));
                var probe = fate->Location + direction * (MathF.Max(fate->Radius, 40) + 65);
                probe.Y = player.Position.Y;
                var candidate = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable")
                    .InvokeFunc(probe, 30, 60);
                if (candidate.HasValue && HorizontalDistance(candidate.Value, fate->Location) > fate->Radius + 25)
                { goal = candidate; break; }
            }
            if (!goal.HasValue) { StopRun("未找到 FATE 范围外可达撤离点，已停止"); return true; }
            RestoreCombatRotation(); StopPath(); targetManager.Target = null; targetId = 0;
            clearingFateAggressors = false;
            fateEscapeGoal = goal; fateEscapeAt = now;
        }
        if (now - fateEscapeAt > 90000) { StopRun("撤离 FATE 脱战超过 90 秒，已停止"); return true; }
        if (manager->SyncedFateId != 0 && manager->CurrentFate != null && manager->IsSyncedToFate(manager->CurrentFate)
            && now - fateSyncAt > 2000) { manager->LevelSync(); fateSyncAt = now; }
        var pos = player.Position;
        if (!condition[ConditionFlag.InCombat] && !manager->IsInFateRadius(&pos))
        { StopPath(); fateEscapeGoal = null; executionStatus = "已离开 FATE 并脱战，继续书本任务"; return false; }
        executionStatus = "停止攻击，离开 FATE 范围脱战";
        if (HorizontalDistance(player.Position, fateEscapeGoal.Value) > 4)
            DrivePath(player.Position, fateEscapeGoal.Value);
        else { StopPath(); executionStatus = "已到 FATE 范围外，等待敌人返回并脱战"; }
        return true;
    }

    private void DriveFates()
    {
        var now = Environment.TickCount64;
        var player = objectTable.LocalPlayer;
        if (player == null) return;
        if (now - fateRunAt > 3600000) { StopRun("FATE 执行已达 60 分钟，已停止"); return; }
        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.Casting]) return;
        if (condition[ConditionFlag.BoundByDuty]) { StopRun("进入副本，已停止 FATE"); return; }
        var manager = FateManager.Instance();
        if (manager == null) throw new InvalidOperationException("FATE 服务不可用");
        if ((!fateEngaged || clearingFateAggressors || fateSlot >= 0 && fates[fateSlot]) && HandleCombatCleanup(manager)) return;
        if (fates.All(x => x)) { StopRun("三项 FATE 的书本进度均已完成，攻击者已检查", true); return; }
        if (fateSlot >= 0 && fates[fateSlot])
        {
            if (targetId != 0 && targetManager.Target?.GameObjectId == targetId) targetManager.Target = null;
            RestoreCombatRotation();
            StopPath();
            // Combat makes earlier remote scans stale. Begin a fresh round immediately.
            Array.Clear(fateVisited);
            fateRoundResumeAt = 0;
            fateSlot = -1;
            activeFateId = 0; fateEngaged = false;
        }
        if (fateSlot < 0)
        {
            // Five minutes limits cross-territory polling, not local spawn detection.
            var localTargetAvailable = Enumerable.Range(0, 3).Any(i => !fates[i]
                && FateWaitPoint(i).Territory == clientState.TerritoryType
                && (FateAvailable(manager, fateIds[i]) || FatePrecursor(i) != 0 && FateAvailable(manager, FatePrecursor(i))));
            if (localTargetAvailable) fateRoundResumeAt = 0;
            if (fateRoundResumeAt != 0 && now >= fateRoundResumeAt)
            {
                Array.Clear(fateVisited);
                fateRoundResumeAt = 0;
            }
            if (!localTargetAvailable && fateRoundResumeAt == 0 && Enumerable.Range(0, 3).Where(i => !fates[i]).All(i => fateVisited[i] != 0))
                fateRoundResumeAt = now + 300000;
            if (fateRoundResumeAt > now)
            {
                StopPath();
                fatePhase = executionStatus = $"本轮地图已查完，{(fateRoundResumeAt - now) / 1000 + 1} 秒后开始下一轮（等待 5 分钟）";
                return;
            }
            fateSlot = Enumerable.Range(0, 3).Where(i => !fates[i])
                .OrderBy(i => FateWaitPoint(i).Territory == clientState.TerritoryType
                    && (FateAvailable(manager, fateIds[i]) || FatePrecursor(i) != 0 && FateAvailable(manager, FatePrecursor(i))) ? 0 : 1)
                .ThenBy(i => fateVisited[i]).ThenBy(i => FateWaitPoint(i).Territory == clientState.TerritoryType ? 0 : 1).First();
            fateWaitAt = now; fateEngaged = false; fateEndedAt = 0; activeFateId = 0;
        }
        if (!SupportsFate(fateIds[fateSlot]))
        {
            StopRun($"该 FATE 的机制尚未支持：{fateLabels[fateSlot]}（{fateIds[fateSlot]}），需要补充交互策略");
            return;
        }
        var place = FateWaitPoint(fateSlot);
        if (!TravelFateTerritory(place.Territory)) return;
        // Scan after loading at the aetheryte; do not travel to a dormant spawn point.
        if (fateScanTerritory != clientState.TerritoryType)
        {
            fateScanTerritory = clientState.TerritoryType;
            fateScanReadyAt = now + 3000;
        }
        if (now < fateScanReadyAt) { executionStatus = "地图已到达，等待 FATE 列表加载"; return; }
        var context = manager->GetFateById(fateIds[fateSlot]);
        if (context == null && FatePrecursor(fateSlot) != 0) context = manager->GetFateById(FatePrecursor(fateSlot));
        if (context == null || context->State is FateState.Ending or FateState.Ended or FateState.Failed)
        {
            if (fateEngaged)
            {
                StopPath();
                if (targetId != 0 && targetManager.Target?.GameObjectId == targetId) targetManager.Target = null;
                RestoreCombatRotation();
                if (HandleCombatCleanup(manager)) return;
                if (fateEndedAt == 0) fateEndedAt = now;
                fatePhase = executionStatus = "活动已结束，等待书本完成标志（最多 30 秒）";
                if (now - fateEndedAt < 30000) return;
                if (activeFateId != FatePrecursor(fateSlot)) { StopRun("FATE 结束但书本未完成，需检查评价或活动结果"); return; }
                fateEngaged = false; activeFateId = 0; fateWaitAt = now;
            }
            StopPath();
            fatePhase = executionStatus = $"本地图未刷新 {fateLabels[fateSlot]}，转查下一项书本 FATE";
            if (!condition[ConditionFlag.InCombat])
            { fateVisited[fateSlot] = now; fateSlot = -1; activeFateId = 0; }
            return;
        }
        if (!PrepareFateCompanion()) return;
        if (activeFateId != context->FateId) { fateEngaged = false; fateEndedAt = 0; }
        activeFateId = context->FateId;
        var location = context->Location;
        var activeName = LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Fate>(activeFateId, out var fateRow)
            ? fateRow.Name.ToString() : fateLabels[fateSlot];
        var phaseName = context->State switch
        {
            FateState.Preparing => "准备中", FateState.Running => "进行中",
            FateState.Ending or FateState.Ended => "已结束", FateState.Failed => "未完成", _ => "等待状态更新"
        };
        fatePhase = executionStatus = $"{activeName} · {phaseName} · {context->Progress}%";
        if (context->State == FateState.Preparing)
        {
            var initiator = objectTable.FirstOrDefault(x => x.EntityId == context->MotivationNpc && x.IsTargetable);
            if (initiator == null)
            {
                if (HorizontalDistance(player.Position, location) > 15) DrivePath(player.Position, location, true);
                else { PrepareDismount(location); executionStatus += "；等待启动 NPC 加载"; }
                return;
            }
            if (fateInteractionAt != 0 && now - fateInteractionAt < 15000)
            {
                var talk = FindAddon("Talk");
                if (talk != null && now >= nextFateUiAt)
                { StopPath(); AdvanceTalk(); nextFateUiAt = now + 1200; return; }
                var confirm = (AddonSelectYesno*)FindAddon("SelectYesno");
                if (confirm != null && confirm->PromptText != null)
                {
                    var prompt = confirm->PromptText->NodeText.ToString();
                    executionStatus = $"FATE 启动确认：{prompt}";
                    if (prompt.Contains("要帮忙吗") && prompt.Contains("危命任务") && now >= nextFateUiAt)
                    { ((AtkUnitBase*)confirm)->FireCallbackInt(0); nextFateUiAt = now + 1500; }
                    return;
                }
            }
            if (!ApproachLeveActor(initiator, 3)) return;
            executionStatus = $"与 FATE 启动 NPC {initiator.Name} 交互";
            if (now < nextFateUiAt) return;
            targetId = initiator.GameObjectId;
            targetManager.Target = initiator;
            TargetSystem.Instance()->InteractWithObject((NativeGameObject*)initiator.Address);
            fateInteractionAt = now; nextFateUiAt = now + 3000;
            return;
        }
        var pos = player.Position;
        if (manager->GetCurrentFateId() != activeFateId || !manager->IsInFateRadius(&pos))
        { executionStatus = $"前往 {fateLabels[fateSlot]}，距离 {HorizontalDistance(player.Position, location):F0} 米"; DrivePath(player.Position, location, true); return; }
        if (!PrepareDismount(location)) { executionStatus = $"接近 {fateLabels[fateSlot]}，准备降落"; return; }
        if (player.Level > context->MaxLevel && !manager->IsSyncedToFate(context))
        {
            StopPath();
            if (now - fateSyncAt > 3000) { fateSyncAt = now; manager->LevelSync(); }
            executionStatus = "等待目标 FATE 等级同步";
            return;
        }
        if (context->State != FateState.Running) { StopPath(); executionStatus = "等待 FATE 正式开始"; return; }
        // BossMod's party healer does not include the friendly defense objective.
        // Protect only this known book objective; output/self healing remain with BossMod.
        var defenseNpc = activeFateId == 611 ? objectTable.OfType<IBattleNpc>()
            .FirstOrDefault(x => x.BaseId == 1188 && x.NameId == 1729 && x.CurrentHp > 0) : null;
        if (defenseNpc != null && defenseNpc.CurrentHp < defenseNpc.MaxHp * 0.75f)
        {
            executionStatus = $"治疗持久战防卫 NPC {defenseNpc.Name}（{defenseNpc.CurrentHp}/{defenseNpc.MaxHp}）";
            if (Vector3.Distance(player.Position, defenseNpc.Position) - defenseNpc.HitboxRadius > 25)
            { DrivePath(player.Position, defenseNpc.Position); return; }
            StopPath();
            var actions = ActionManager.Instance();
            if (actions != null && now - lastCastAt >= 1500
                && actions->GetActionStatus(ActionType.Action, 135, defenseNpc.GameObjectId) == 0)
            { actions->UseAction(ActionType.Action, 135, defenseNpc.GameObjectId); lastCastAt = now; }
            return;
        }
        var enemy = objectTable.OfType<IBattleNpc>()
            .Where(x => x.IsTargetable && x.CurrentHp > 0 && !TargetTemporarilyBlocked(x)
                && ActionManager.CanUseActionOnTarget(TargetCheckAction(), (NativeGameObject*)x.Address)
                && ((NativeGameObject*)x.Address)->FateId == activeFateId)
            .OrderBy(x => defenseNpc != null && x.TargetObjectId == defenseNpc.EntityId ? 0 : 1)
            .ThenBy(x => x.GameObjectId == targetId ? 0 : 1)
            .ThenBy(x => Vector3.DistanceSquared(player.Position, x.Position)).FirstOrDefault();
        if (enemy == null)
        {
            StopPath();
            if (targetId != 0 && targetManager.Target?.GameObjectId == targetId) targetManager.Target = null;
            executionStatus += "；等待本活动敌人"; return;
        }
        var distance = Vector3.Distance(player.Position, enemy.Position) - enemy.HitboxRadius - player.HitboxRadius;
        if (distance > 20) { DrivePath(player.Position, enemy.Position); return; }
        if (!EnsureTargetVisible(enemy)) return;
        StopPath(); targetId = enemy.GameObjectId; targetManager.Target = enemy;
        ActivateCombatRotation();
        fateEngaged = true;
        executionStatus += $"；BossMod 自动战斗：{enemy.Name}";
    }
    private bool TargetTemporarilyBlocked(IGameObject target) => blockedTargets.TryGetValue(target.GameObjectId, out var until)
        && Environment.TickCount64 < until;

    private bool EnsureTargetVisible(IBattleNpc target)
    {
        var player = objectTable.LocalPlayer;
        if (player == null || TargetTemporarilyBlocked(target)) return false;
        var from = player.Position + new Vector3(0, 2, 0);
        var delta = target.Position + new Vector3(0, 2, 0) - from;
        var length = delta.Length();
        if (length < 0.1f || !BGCollisionModule.RaycastMaterialFilter(from, delta / length, out _, length))
        {
            if (blockedApproachTarget == target.GameObjectId) blockedApproachTarget = 0;
            return true;
        }
        if (targetManager.Target?.GameObjectId == target.GameObjectId) targetManager.Target = null;
        RestoreCombatRotation();
        var now = Environment.TickCount64;
        if (blockedApproachTarget != target.GameObjectId)
        { StopPath(); blockedApproachTarget = target.GameObjectId; blockedApproachAt = now; }
        if (now - blockedApproachAt > 12000 || Vector3.Distance(player.Position, target.Position) - target.HitboxRadius < 2.5f)
        {
            blockedTargets[target.GameObjectId] = now + 60000;
            blockedApproachTarget = 0;
            if (targetId == target.GameObjectId) targetId = 0;
            StopPath(); executionStatus = $"{target.Name} 视线仍受阻，暂时跳过 60 秒并换目标";
            return false;
        }
        executionStatus = $"{target.Name} 不可见，沿地面移动到目标脚下";
        DrivePath(player.Position, target.Position);
        return false;
    }

    private bool PrepareEnemyFateSync(IBattleNpc enemy)
    {
        var manager = FateManager.Instance();
        var player = objectTable.LocalPlayer;
        if (manager == null || player == null) return false;
        var now = Environment.TickCount64;
        var id = ((NativeGameObject*)enemy.Address)->FateId;
        var context = id != 0 && FateAvailable(manager, id) ? manager->GetFateById(id) : null;
        if (context == null)
        {
            if (manager->SyncedFateId == 0) return true;
            StopPath(); RestoreCombatRotation();
            executionStatus = "攻击普通敌人前退出 FATE 同步";
            if (now - fateSyncAt > 2000 && manager->CurrentFate != null && manager->IsSyncedToFate(manager->CurrentFate))
            { manager->LevelSync(); fateSyncAt = now; }
            return false;
        }
        if (manager->SyncedFateId != 0 && manager->SyncedFateId != id)
        {
            StopPath(); RestoreCombatRotation();
            executionStatus = $"退出其他 FATE 同步，准备清理活动 {id} 的攻击者";
            if (now - fateSyncAt > 2000 && manager->CurrentFate != null && manager->IsSyncedToFate(manager->CurrentFate))
            { manager->LevelSync(); fateSyncAt = now; }
            return false;
        }
        if (player.Level <= context->MaxLevel || manager->IsSyncedToFate(context)) return true;
        RestoreCombatRotation();
        executionStatus = $"{enemy.Name} 属于 FATE {id}，临时同步后清理";
        var pos = player.Position;
        if (manager->GetCurrentFateId() != id || !manager->IsInFateRadius(&pos))
        { DrivePath(player.Position, context->Location); return false; }
        StopPath();
        if (now - fateSyncAt > 2000) { manager->LevelSync(); fateSyncAt = now; }
        return false;
    }

    private static bool FateAvailable(FateManager* manager, ushort id)
    {
        var fate = manager->GetFateById(id);
        return fate != null && fate->State is FateState.Preparing or FateState.Running;
    }

    private bool TravelFateTerritory(uint territory)
    {
        var now = Environment.TickCount64;
        if (clientState.TerritoryType == territory)
        {
            teleportRequested = false; teleportRetries = 0;
            if (!IpcBool("vnavmesh.Nav.IsReady")) { executionStatus = "等待 FATE 区域导航网格"; return false; }
            return true;
        }
        if (teleportRequested)
        {
            executionStatus = "等待前往 FATE 区域的传送";
            if (now - teleportAt > 15000)
            {
                teleportRequested = false;
                if (++teleportRetries > 2) StopRun("FATE 传送连续未实际开始，已停止");
            }
            return false;
        }
        if (condition[ConditionFlag.InCombat]) { executionStatus = "等待脱战后前往 FATE 区域"; return false; }
        if (!PrepareTeleport()) return false;
        var telepo = Telepo.Instance();
        if (telepo == null) return false;
        telepo->UpdateAetheryteList();
        uint aetheryte = 0, cheapest = uint.MaxValue;
        foreach (var crystal in telepo->TeleportList)
        {
            if (crystal.TerritoryId != territory || crystal.SubIndex != 0 || crystal.IsSharedHouse || crystal.GilCost >= cheapest) continue;
            aetheryte = crystal.AetheryteId; cheapest = crystal.GilCost;
        }
        if (aetheryte == 0) { StopRun($"FATE 区域 {territory} 无已解锁水晶"); return false; }
        StopPath();
        if (!telepo->Teleport(aetheryte, 0)) { StopRun("FATE 传送请求被拒绝"); return false; }
        teleportRequested = true; teleportAt = now;
        executionStatus = "传送前往未完成 FATE 区域";
        return false;
    }

    private void DriveWorldRoute()
    {
        var player = objectTable.LocalPlayer;
        if (player == null) return;
        if (condition[ConditionFlag.InCombat] || clearingFateAggressors)
        {
            var manager = FateManager.Instance();
            if (manager != null && HandleCombatCleanup(manager)) return;
        }
        if (teleportRequested)
        {
            if (clientState.TerritoryType == teleportDestination && !condition[ConditionFlag.Casting] && !condition[ConditionFlag.BetweenAreas])
            {
                teleportRequested = false;
                teleportRetries = 0;
                routeAt = Environment.TickCount64;
            }
            else
            {
                if (Environment.TickCount64 - teleportAt > 15000 && !condition[ConditionFlag.Casting]
                    && !condition[ConditionFlag.BetweenAreas])
                {
                    teleportRequested = false;
                    if (++teleportRetries > 2) { StopRun("传送连续未实际开始，已停止"); return; }
                    executionStatus = "传送未实际开始，重新检查坐骑后重试";
                    return;
                }
                if (Environment.TickCount64 - teleportAt > 90000) StopRun("传送未到达预期区域");
                executionStatus = $"等待传送至区域 {teleportDestination}";
                return;
            }
        }
        if (routePoint != null && monsters[routePoint.Slot] >= monsterRequired[routePoint.Slot])
        { StopPath(); routePoint = null; routeAttempt = 0; }
        if (routePoint == null)
        {
            StopPath();
            if (!spawnLoad.IsCompleted) { executionStatus = "等待出生点数据加载"; return; }
            var spawns = spawnLoad.GetAwaiter().GetResult();
            var points = new List<RoutePoint>();
            for (var slot = 0; slot < 10; slot++)
            {
                if (monsters[slot] >= monsterRequired[slot] || monsterRequired[slot] == 0) continue;
                foreach (var spawn in spawns.Where(x => x.BNpcNameId == monsterNameIds[slot]))
                {
                    if (!LuminaGetter.TryGetRow<Lumina.Excel.Sheets.TerritoryType>(spawn.TerritoryTypeId, out var territory)) continue;
                    if (territory.PlaceName.RowId != monsterZones[slot] || territory.Map.RowId == 0) continue;
                    if (!float.IsFinite(spawn.Position.X) || !float.IsFinite(spawn.Position.Y)
                        || spawn.Position.X is < 1 or > 100 || spawn.Position.Y is < 1 or > 100) continue;
                    // Some source records contain a map-centre placeholder, not a spawn.
                    if (MathF.Abs(spawn.Position.X - spawn.Position.Y) < 0.001f && MathF.Abs(spawn.Position.Z) <= 2) continue;
                    var mapLink = new MapLinkPayload(spawn.TerritoryTypeId, territory.Map.RowId, spawn.Position.X, spawn.Position.Y, 0f);
                    points.Add(new RoutePoint(spawn.TerritoryTypeId, new Vector3(mapLink.RawX / 1000f, 1024, mapLink.RawY / 1000f), slot));
                }
                if ((bookId == 1 ? BookOneFallback(slot) : bookId == 2 ? BookTwoFallback(slot) : null) is { } fallback) points.Add(fallback);
            }
            spawnStatus = $"本书待完成目标的候选出生点：{points.Count}";
            if (points.Count == 0) { StopRun("配套出生点数据中没有可靠目标位置"); return; }
            var ordered = points.Where(x => !rejectedSpawns.Contains((x.Territory, x.Probe)))
                .OrderBy(x => x.Territory == clientState.TerritoryType ? 0 : 1)
                .ThenBy(x => x.Territory == clientState.TerritoryType ? Vector2.Distance(new Vector2(x.Probe.X, x.Probe.Z), new Vector2(player.Position.X, player.Position.Z)) : x.Slot)
                .ToArray();
            if (ordered.Length == 0) { StopRun("所有出生点候选都不可达，请检查当前区域导航网格"); return; }
            routePoint = ordered[routeAttempt % ordered.Length];
            routeAt = Environment.TickCount64;
            arrivedAt = 0;
        }
        if (clientState.TerritoryType != routePoint.Territory)
        {
            if (condition[ConditionFlag.InCombat] || condition[ConditionFlag.Casting])
            { executionStatus = "等待脱战后传送"; return; }
            if (!PrepareTeleport()) return;
            var telepo = Telepo.Instance();
            if (telepo == null) throw new InvalidOperationException("传送服务不可用");
            telepo->UpdateAetheryteList();
            uint aetheryte = 0;
            uint cheapest = uint.MaxValue;
            foreach (var crystal in telepo->TeleportList)
            {
                if (crystal.TerritoryId != routePoint.Territory || crystal.SubIndex != 0 || crystal.IsSharedHouse || crystal.GilCost >= cheapest) continue;
                aetheryte = crystal.AetheryteId;
                cheapest = crystal.GilCost;
            }
            if (aetheryte == 0) { StopRun($"目标区域 {routePoint.Territory} 无已解锁水晶"); return; }
            StopPath();
            if (!telepo->Teleport(aetheryte, 0)) { StopRun("传送请求被拒绝"); return; }
            teleportRequested = true;
            teleportDestination = routePoint.Territory;
            teleportAt = Environment.TickCount64;
            executionStatus = $"传送前往 {monsterLabels[routePoint.Slot]} 所在区域";
            return;
        }
        if (!IpcBool("vnavmesh.Nav.IsReady"))
        {
            if (Environment.TickCount64 - routeAt > 90000) StopRun("区域导航网格加载超时");
            executionStatus = "等待当前区域导航网格";
            return;
        }
        var ground = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable")
            .InvokeFunc(routePoint.Probe, 20f, routePoint.Probe.Y == 1024 ? 2048f : 30f);
        if (!ground.HasValue)
        {
            rejectedSpawns.Add((routePoint.Territory, routePoint.Probe));
            StopPath();
            routePoint = null;
            executionStatus = "已跳过无法投影到可达地面的出生点";
            return;
        }
        var horizontalDistance = Vector2.Distance(new Vector2(player.Position.X, player.Position.Z), new Vector2(ground.Value.X, ground.Value.Z));
        executionStatus = $"前往 {monsterLabels[routePoint.Slot]} 出生点，距离 {horizontalDistance:F0}m";
        if (horizontalDistance > 20)
        {
            DrivePath(player.Position, ground.Value, true);
            return;
        }
        if (!PrepareDismount(ground.Value)) return;
        StopPath();
        if (arrivedAt == 0) arrivedAt = Environment.TickCount64;
        if (Environment.TickCount64 - arrivedAt > 60000)
        {
            routeAttempt++;
            routePoint = null;
        }
        executionStatus = "已到出生点，等待目标加载或刷新";
    }

    // World-coordinate observations from the public Skyfire I profile, used as
    // factual navigation fallbacks. Provenance is recorded in DATA-SOURCES.md.
    private static RoutePoint BookOneFallback(int slot) => slot switch
    {
        0 => new(156, new(-227.1997f, -4.063313f, -301.7137f), slot),
        1 => new(155, new(-473.0542f, 276.0642f, 233.3405f), slot),
        2 => new(138, new(-206.0953f, -42.0848f, -239.8467f), slot),
        3 => new(156, new(-559.3f, -3.146109f, -351.7f), slot),
        4 => new(180, new(109.4875f, 23.91594f, -715.1216f), slot),
        5 => new(140, new(-498.4164f, 50.98945f, -746.5322f), slot),
        6 => new(146, new(-178.9906f, 40f, 153.9457f), slot),
        7 => new(152, new(119.6483f, -28.93971f, -233.3251f), slot),
        8 => new(147, new(-22.92793f, 12.49097f, 251.8611f), slot),
        9 => new(137, new(210.5027f, 73.61314f, -15.33077f), slot),
        _ => null
    };

    private void StopPath()
    {
        pathCancellation?.Cancel();
        pathCancellation?.Dispose();
        pathCancellation = null;
        if (pathTask != null)
        {
            _ = pathTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            pathTask = null;
        }
        if (!ownsMovement) return;
        DalamudServices.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction();
        ownsMovement = false;
    }

    private void RestoreDutyOverrides()
    {
        if (!ownsOverrides) return;
        DalamudServices.PluginInterface.GetIpcSubscriber<bool>("AutoDuty.PopConfigOverrides").InvokeFunc();
        ownsOverrides = false;
    }

    private static RoutePoint BookTwoFallback(int slot) => slot switch
    {
        0 => new(156, new(-227.0259f, -4.048429f, -301.5592f), slot),
        1 => new(152, new(367.3162f, -26.90956f, -399.1715f), slot),
        2 => new(155, new(-381.1835f, 239.0962f, 370.6928f), slot),
        3 => new(138, new(-182.4838f, -42.42295f, -269.1765f), slot),
        4 => new(146, new(-4.16819f, 8.360384f, -92.10616f), slot),
        5 => new(154, new(66.31128f, 6.925154f, -86.75264f), slot),
        6 => new(138, new(-377.0648f, -39.90769f, -218.3152f), slot),
        7 => new(146, new(472.7139f, 9.887727f, -140.1137f), slot),
        8 => new(180, new(290.6886f, 21.53372f, -713.575f), slot),
        9 => new(155, new(492.5438f, 303.7798f, -207.8961f), slot),
        _ => null
    };

    private void StopRun(string reason, bool completed = false)
    {
        preparingEquipment = false;
        equipmentContextPending = false;
        deathPausedAt = 0;
        if (!completed) { autoBooks = false; autoStatus = reason; }
        else autoNextAt = Environment.TickCount64 + 2000;
        mode = RunMode.Idle;
        executionStatus = reason;
        try { StopPath(); } catch (Exception ex) { executionStatus += $"；停止移动失败：{ex.Message}"; }
        if (ownsDuty)
        {
            try
            {
                DalamudServices.PluginInterface.GetIpcSubscriber<object>("AutoDuty.Stop").InvokeAction();
                ownsDuty = false;
            }
            catch (Exception ex) { executionStatus += $"；停止 AutoDuty 失败：{ex.Message}"; }
        }
        try { RestoreDutyOverrides(); } catch (Exception ex) { executionStatus += $"；恢复配置失败：{ex.Message}"; }
        if (targetId != 0 && targetManager?.Target?.GameObjectId == targetId) targetManager.Target = null;
        try { RestoreCombatRotation(); } catch (Exception ex) { executionStatus += $"；恢复循环失败：{ex.Message}"; }
        dutySlot = -1;
        targetId = 0;
        routePoint = null;
        teleportRequested = false;
        awaitingMount = landing = false;
        groundLandingInProgress = false;
        mountReadyAt = 0;
        landingApproachGoal = null;
        landingSearchAt = 0;
        fateEscapeGoal = null;
    }

    private bool BookComplete() => Enumerable.Range(0, 10).All(i => monsterRequired[i] > 0 && monsters[i] >= monsterRequired[i])
        && dungeons.All(x => x) && leves.All(x => x) && fates.All(x => x);

    private void DriveAutoBooks()
    {
        if (objectTable.LocalPlayer?.CurrentHp == 0 || condition[ConditionFlag.Unconscious]) return;
        if (!autoBooks || mode != RunMode.Idle || Environment.TickCount64 < autoNextAt) return;
        if (condition[ConditionFlag.Casting] || condition[ConditionFlag.BetweenAreas]) return;
        if ((condition[ConditionFlag.OccupiedInEvent] || condition[ConditionFlag.OccupiedInQuestEvent]) && !CanResumeLeveDialog() && !HasLeveReturnPrompt())
        {
            if (targetManager.Target?.BaseId == 1006976 && !BookComplete() && Environment.TickCount64 >= nextFateUiAt)
            {
                if (FindAddon("Talk") != null) AdvanceTalk();
                else if (FindAddon("SelectIconString") != null) ClickLabel("SelectIconString", "取消");
                else if (FindAddon("SelectString") != null) ClickLabel("SelectString", "取消");
                nextFateUiAt = Environment.TickCount64 + 1200;
            }
            return;
        }
        var manager = FateManager.Instance();
        if (manager != null && HandleCombatCleanup(manager)) return;
        if (condition[ConditionFlag.InCombat])
        { executionStatus = "等待战后清理或脱战完成，再继续下一阶段"; return; }
        singleDuty = singleLeve = false;
        var quests = QuestManager.Instance();
        var hasAllowance = quests != null && quests->NumLeveAllowances > 0;
        var runnableAcceptedLeve = false;
        if (quests != null)
        {
            foreach (var id in leveIds.Concat(new[] { refreshLeveId }).Where(id => id != 0))
            {
                var work = quests->GetLeveQuestById((ushort)id);
                if (work != null && (work->Sequence != 3 || hasAllowance)) { runnableAcceptedLeve = true; break; }
            }
        }
        var canRunLeves = runnableAcceptedLeve || hasAllowance && leves.Any(x => !x);
        var next = !Enumerable.Range(0, 10).All(i => monsters[i] >= monsterRequired[i]) ? RunMode.WorldMonsters
            : dungeons.Any(x => !x) ? RunMode.Dungeons : canRunLeves ? RunMode.Leves
            : fates.Any(x => !x) ? RunMode.Fates : RunMode.Exchange;
        if (next == RunMode.Exchange && (!BookComplete() || HasPendingBookLeve()))
        {
            StopRun("本书其他任务已完成；剩余理符因受理权不足暂缓，已停止，恢复受理权后可继续");
            return;
        }
        if (next == RunMode.Exchange && bookId >= 9)
        { autoBooks = false; autoStatus = executionStatus = "最后一卷已完成，连续执行结束"; return; }
        autoStatus = $"连续书本：{bookName} → {next}";
        if (next == RunMode.Exchange) { exchangeFrom = bookId; exchangeAt = Environment.TickCount64; }
        StartRun(next);
        if (mode == RunMode.Idle) { autoBooks = false; autoStatus = executionStatus; }
    }

    private bool AdvanceTalk()
    {
        var talk = FindAddon("Talk");
        if (talk == null) return false;
        var component = talk->GetComponentByNodeId(21);
        if (component == null) return false;
        var node = component->UldManager.SearchNodeById(2);
        if (node == null) return false;
        var count = 0;
        for (var e = node->AtkEventManager.Event; e != null && count++ < 32; e = e->NextEvent)
        {
            if (e->State.EventType != AtkEventType.MouseClick || e->Listener == null) continue;
            e->Listener->ReceiveEvent(e->State.EventType, (int)e->Param, e);
            return true;
        }
        return false;
    }

    private string BookHistoryPath() => System.IO.Path.Combine(
        DalamudServices.PluginInterface.GetPluginConfigDirectory(), $"AnimusBookAssistant.books.{historyCharacter}.json");

    private void ObserveBookHistory()
    {
        var character = DalamudServices.PlayerState.ContentId;
        if (character == 0 || relicId == 0 || bookId is < 1 or > 9 || !labelsReady) return;
        if (historyCharacter != character)
        {
            historyCharacter = character;
            bookHistory = new(); historyWritable = true; historyDirty = false; historyRetryAt = 0;
            try
            {
                var path = BookHistoryPath();
                if (System.IO.File.Exists(path)) bookHistory = JsonSerializer.Deserialize<Dictionary<string, BookHistoryEntry>>(
                    System.IO.File.ReadAllText(path)) ?? new();
                historyStatus = "记录按角色与古武分别保存；未知不代表未完成。";
            }
            catch (Exception ex)
            {
                historyWritable = false;
                historyStatus = $"历史记录读取失败，暂不覆盖原文件：{ex.Message}";
            }
        }
        var key = $"{relicId}:{bookId}";
        if (!bookHistory.TryGetValue(key, out var entry) || entry == null)
        {
            entry = new BookHistoryEntry { FirstSeen = DateTimeOffset.UtcNow };
            bookHistory[key] = entry;
            historyDirty = true;
        }
        if (!entry.Completed && BookComplete())
        {
            entry.Completed = true; entry.CompletedAt = DateTimeOffset.UtcNow;
            historyDirty = true;
        }
        if (!historyDirty || !historyWritable || Environment.TickCount64 < historyRetryAt) return;
        try
        {
            var path = BookHistoryPath();
            System.IO.File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(bookHistory, new JsonSerializerOptions { WriteIndented = true }));
            System.IO.File.Move(path + ".tmp", path, true);
            historyDirty = false;
            historyStatus = "记录按角色与古武分别保存；未知不代表未完成。";
        }
        catch (Exception ex)
        {
            historyRetryAt = Environment.TickCount64 + 30000;
            historyStatus = $"历史记录保存失败，当前仅保留在内存：{ex.Message}";
        }
    }

    private void DrawBookOverview()
    {
        if (!ImGui.CollapsingHeader("九本书总览")) return;
        if (relicId == 0 || historyCharacter != DalamudServices.PlayerState.ContentId)
        { ImGui.TextWrapped("请先读取当前角色的古武书本。"); return; }
        ImGui.TextWrapped($"当前古武：{(LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Item>(requiredWeapon, out var weapon) ? weapon.Name.ExtractText() : requiredWeapon.ToString())}");
        var complete = 0;
        var columns = ImGui.GetContentRegionAvail().X >= 480 ? 3 : 2;
        if (!ImGui.BeginTable("BookOverview", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings)) return;
        for (uint i = 1; i <= 9; i++)
        {
            ImGui.TableNextColumn();
            bookHistory.TryGetValue($"{relicId}:{i}", out var entry);
            if (entry?.Completed == true) complete++;
            var label = LuminaGetter.TryGetRow<SheetRelicNote>(i, out var row) ? row.EventItem.Value.Singular.ToString() : $"书本 {i}";
            var state = entry?.Completed == true ? "已完成" : i == bookId ? "进行中" : entry != null ? "曾读取" : "未知";
            var color = entry?.Completed == true ? CompleteColor
                : i == bookId ? ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark] : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
            ImGui.TextWrapped(label);
            ImGui.TextColored(color, state);
            if (i == bookId) { ImGui.SameLine(); ImGui.TextUnformatted("· 当前"); }
            ImGui.Spacing();
        }
        ImGui.EndTable();
        ImGui.TextUnformatted($"已确认完成 {complete} / 9 本");
        ImGui.TextWrapped("未知：没有可靠历史记录。曾读取：插件见过这本书，但未确认全项完成。不会按当前卷数推断之前书本已完成。");
        ImGui.TextWrapped(historyStatus);
    }

    private bool HasPendingBookLeve()
    {
        var quests = QuestManager.Instance();
        if (quests == null) return false;
        for (var i = 0; i < leveIds.Length; i++)
            if (leveIds[i] != 0 && quests->GetLeveQuestById((ushort)leveIds[i]) != null) return true;
        return false;
    }

    private void DriveExchange()
    {
        var now = Environment.TickCount64;
        if (bookId == exchangeFrom + 1 && relicId == runRelic)
        {
            if (FindAddon("Talk") != null) { if (now >= nextFateUiAt) { AdvanceTalk(); nextFateUiAt = now + 1200; } return; }
            StopRun($"已换得 {bookName}，继续执行", true); return;
        }
        if (bookId != exchangeFrom || !BookComplete()) { StopRun("书本尚未全部完成或编号异常，停止换书"); return; }
        if (now - exchangeAt > 300000) { StopRun("换书超过 5 分钟，已停止；请检查 NPC 菜单或诗学数量"); return; }
        var manager = FateManager.Instance();
        if (manager != null && HandleCombatCleanup(manager)) return;
        if (!TravelFateTerritory(156)) return;
        var npc = objectTable.FirstOrDefault(x => x.BaseId == 1006976 && x.IsTargetable);
        if (npc == null)
        { DrivePath(objectTable.LocalPlayer.Position, new(71.95f, 33.07f, -706.29f), true); return; }
        if (now < nextFateUiAt) return;
        if (FindAddon("Talk") != null) { AdvanceTalk(); nextFateUiAt = now + 1200; return; }
        if (!LuminaGetter.TryGetRow<SheetRelicNote>((uint)(exchangeFrom + 1), out var next))
        { StopRun("未找到下一卷数据"); return; }
        var nextName = next.EventItem.Value.Singular.ExtractText();
        var confirm = (AddonSelectYesno*)FindAddon("SelectYesno");
        if (confirm != null && confirm->PromptText != null)
        {
            var text = confirm->PromptText->NodeText.ToString();
            if (!text.Contains(nextName, StringComparison.Ordinal))
            { StopRun($"换书确认未匹配下一卷 {nextName}，保留窗口供检查"); return; }
            ((AtkUnitBase*)confirm)->FireCallbackInt(0); nextFateUiAt = now + 2000; return;
        }
        if (FindAddon("SelectIconString") != null)
        { ClickLabel("SelectIconString", "交换«黄道文书»"); nextFateUiAt = now + 1200; return; }
        if (FindAddon("SelectString") != null)
        {
            if (ClickLabel("SelectString", nextName)) { nextFateUiAt = now + 1500; return; }
            var element = nextName.Contains('火') ? "火" : nextName.Contains('水') ? "水" : nextName.Contains('风') ? "风" : "土";
            if (!ClickLabel("SelectString", element))
            { StopRun($"菜单未提供下一卷 {nextName}，已停止"); return; }
            nextFateUiAt = now + 1200; return;
        }
        if (!ApproachLeveActor(npc, 3)) return;
        targetId = npc.GameObjectId; targetManager.Target = npc;
        TargetSystem.Instance()->InteractWithObject((NativeGameObject*)npc.Address);
        nextFateUiAt = now + 1500;
    }

    private void Diagnose()
    {
        var results = new List<string>();
        foreach (var name in new[] { "vnavmesh.Nav.IsReady", "vnavmesh.Path.IsRunning", "AutoDuty.IsStopped" })
        {
            try { results.Add($"{name}={IpcBool(name)}"); }
            catch (Exception ex) { results.Add($"{name}: {ex.Message}"); }
        }
        for (var i = 0; i < 3; i++)
        {
            try
            {
                var hasPath = DalamudServices.PluginInterface.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath").InvokeFunc(dungeonTerritories[i]);
                results.Add($"{dungeonNames[i]}({dungeonTerritories[i]}) 路线={hasPath}");
            }
            catch (Exception ex) { results.Add(ex.Message); }
        }
        foreach (var name in new[] { "Meta.DutyModeEnum", "Meta.Unsynced", "DutyConfig.AutoExitDuty", "DutyConfig.AutoManageBossModAISettings" })
        {
            try { results.Add($"{name}={DalamudServices.PluginInterface.GetIpcSubscriber<string, string>("AutoDuty.GetConfig").InvokeFunc(name)}"); }
            catch (Exception ex) { results.Add(ex.Message); }
        }
        diagnostics = string.Join("\n", results);
    }

    private string GetStatusJson() => JsonSerializer.Serialize(new
    {
        version = ReleaseVersion, buildMvid = typeof(AnimusBookAssistant).Module.ModuleVersionId, enabled = IsEnabled,
        bookId, relicId, bookName, requiredWeapon, objectiveBits, dutyCompletionObserved = dutyCompletedAt != 0, dutyExitRequested,
        mode = mode.ToString(), executionStatus, diagnostics, spawnStatus, levePhase, leveDebug, travelStatus, autoBooks, autoStatus,
        preparingEquipment, equipmentStatus, equipmentJob, equipmentWeapon, equipmentContextPending, deathPausedAt,
        historyStatus,
        bookOverview = Enumerable.Range(1, 9).Select(i => new
        {
            book = i, current = i == bookId,
            completed = bookHistory.TryGetValue($"{relicId}:{i}", out var entry) && entry?.Completed == true,
            observed = bookHistory.ContainsKey($"{relicId}:{i}")
        }).ToArray(),
        refreshLeveId, leveRefreshAttempts, refreshLeveAccepted, refreshRewardRequested,
        useMount = config.UseMount, useFlight = config.UseFlight, pathFlying,
        landing, landingApproachGoal, landingSearchAt,
        lastRangeOrLos, fateIds, activeFateId, fateSlot, fatePhase, fateEngaged,
        rotationPreset, rotationActive, companionStatus, clearingFateAggressors, fateCleanupStatus,
        startBlockedReason = StartBlockedReason(),
        status, monsters = monsters.Select(x => (int)x).ToArray(), monsterRequired = monsterRequired.Select(x => (int)x).ToArray(), monsterLabels, dungeons, dungeonNames, dungeonTerritories,
        fates, fateLabels, leves, leveLabels, targetCount = targets.Count, lastRead
    });

    private void DrawWindow()
    {
        if (!windowOpen) return;
        ImGui.SetNextWindowSize(new Vector2(600, 520), ImGuiCond.FirstUseEver);
        var visible = ImGui.Begin("古武小黄书助手###AnimusBookAssistant", ref windowOpen);
        try
        {
            if (visible) DrawContent();
        }
        finally { ImGui.End(); }
    }

    private enum ObjectiveGroup { Monsters, Dungeons, Leves, Fates }

    private ObjectiveGroup visibleGroup;
    private RunMode displayedMode;
    private byte displayedBook;
    private static readonly Vector4 CompleteColor = new(0.35f, 0.8f, 0.55f, 1);

    private void DrawContent()
    {
        var running = preparingEquipment || autoBooks || mode != RunMode.Idle;
        var ready = lastRead != null && !failed && bookId != 0;
        FollowExecutionGroup(running);
        DrawBookHeader(ready, running);
        DrawCurrentTask(ready, running);
        if (ready)
        {
            DrawProgressGroups();
            DrawObjectiveList();
        }
        ImGui.Spacing();
        DrawBookOverview();
        DrawTravelSettings();
        DrawHelp();
        DrawDiagnostics();
    }

    private void FollowExecutionGroup(bool running)
    {
        if (displayedBook == bookId && (!running || displayedMode == mode)) return;
        if (mode is RunMode.WorldMonsters or RunMode.Nearby) visibleGroup = ObjectiveGroup.Monsters;
        else if (mode == RunMode.Dungeons) visibleGroup = ObjectiveGroup.Dungeons;
        else if (mode == RunMode.Leves) visibleGroup = ObjectiveGroup.Leves;
        else if (mode == RunMode.Fates) visibleGroup = ObjectiveGroup.Fates;
        else if (displayedBook != bookId)
            visibleGroup = monsters.Where((_, i) => monsters[i] < monsterRequired[i]).Any() ? ObjectiveGroup.Monsters
                : dungeons.Any(x => !x) ? ObjectiveGroup.Dungeons : leves.Any(x => !x) ? ObjectiveGroup.Leves : ObjectiveGroup.Fates;
        displayedBook = bookId;
        displayedMode = mode;
    }

    private string ExecutionStage()
    {
        if (preparingEquipment) return "准备装备";
        if (deathPausedAt != 0) return "等待复活";
        if (mode == RunMode.Fates && fateRoundResumeAt > Environment.TickCount64) return "等待刷新";
        if (rotationActive) return "战斗中";
        if (ownsMovement || pathTask != null) return "赶路中";
        return mode switch
        {
            RunMode.WorldMonsters or RunMode.Nearby => "怪物任务",
            RunMode.Dungeons => "副本任务",
            RunMode.Leves => "理符任务",
            RunMode.Fates => "FATE 巡查",
            RunMode.Exchange => "自动换书",
            _ => "准备下一阶段"
        };
    }

    private void DrawBookHeader(bool ready, bool running)
    {
        ImGui.TextUnformatted(ready ? bookName : "等待读取书本");
        if (ready && LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Item>(requiredWeapon, out var weapon))
            ImGui.TextColored(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], weapon.Name.ExtractText());
        ImGui.TextColored(running ? ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark]
            : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], running ? ExecutionStage() : "待机");
        ImGui.Spacing();
        var width = Math.Max(120, ImGui.GetContentRegionAvail().X);
        ImGui.BeginDisabled(running || !ready);
        if (ImGui.Button("开始连续执行", new Vector2(Math.Min(160, width * 0.6f), 32))) queuedAction = "auto";
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!running);
        if (ImGui.Button("停止", new Vector2(Math.Min(80, width * 0.3f), 32))) queuedAction = "stop";
        ImGui.EndDisabled();
    }

    private void DrawCurrentTask(bool ready, bool running)
    {
        ImGui.Spacing();
        DrawSectionTitle("当前任务");
        var waiting = running && mode == RunMode.Fates && fateRoundResumeAt > Environment.TickCount64;
        ImGui.TextWrapped(waiting ? "本轮暂未发现目标 FATE" : executionStatus);
        if (!ready) ImGui.TextWrapped(status);
        if (running && mode != RunMode.Dungeons && travelStatus != "待机" && (ownsMovement || pathTask != null || landing || awaitingMount))
            ImGui.TextWrapped(travelStatus);
        var nextStep = NextStepText(running, waiting);
        if (nextStep != null) ImGui.TextWrapped(nextStep);
        if (!running && ready)
        {
            if (!HasRequiredWeapon()) ImGui.TextWrapped("开始后先切换职业，再单独装备古武。");
            else if (StartBlockedReason() is { } reason) ImGui.TextWrapped(reason);
        }
        ImGui.Spacing();
    }

    private string NextStepText(bool running, bool waiting)
    {
        if (!running) return null;
        if (preparingEquipment) return "职业和古武核对完成后开始任务。";
        if (deathPausedAt != 0) return "复活后继续，已完成进度保留。";
        if (waiting)
        {
            var seconds = Math.Max(0, (fateRoundResumeAt - Environment.TickCount64) / 1000);
            return $"下一轮巡查 {seconds / 60:00}:{seconds % 60:00} · 等待期间继续检查当前地图";
        }
        var quests = QuestManager.Instance();
        if (leves.Any(x => !x) && quests != null && quests->NumLeveAllowances == 0)
            return "理符受理权已用尽，先完成其余任务。";
        return mode == RunMode.Exchange ? "换书后继续下一本。" : null;
    }

    private (int Done, int Total) GroupProgress(ObjectiveGroup group) => group switch
    {
        ObjectiveGroup.Monsters => (Enumerable.Range(0, 10).Count(i => monsterRequired[i] > 0 && monsters[i] >= monsterRequired[i]), 10),
        ObjectiveGroup.Dungeons => (dungeons.Count(x => x), 3),
        ObjectiveGroup.Leves => (leves.Count(x => x), 3),
        _ => (fates.Count(x => x), 3)
    };

    private static string GroupName(ObjectiveGroup group) => group switch
    {
        ObjectiveGroup.Monsters => "怪物", ObjectiveGroup.Dungeons => "副本", ObjectiveGroup.Leves => "理符", _ => "FATE"
    };

    private void DrawProgressGroups()
    {
        var done = Enum.GetValues<ObjectiveGroup>().Sum(group => GroupProgress(group).Done);
        ImGui.ProgressBar(done / 19f, new Vector2(-1, 18), $"本卷 {done} / 19");
        ImGui.Spacing();
        var columns = ImGui.GetContentRegionAvail().X >= 480 ? 4 : 2;
        if (!ImGui.BeginTable("TaskGroups", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings)) return;
        foreach (var group in Enum.GetValues<ObjectiveGroup>())
        {
            ImGui.TableNextColumn();
            var progress = GroupProgress(group);
            if (ImGui.Selectable($"{GroupName(group)}  {progress.Done}/{progress.Total}##group{group}", visibleGroup == group,
                ImGuiSelectableFlags.None, new Vector2(0, 30))) visibleGroup = group;
            ImGui.ProgressBar((float)progress.Done / progress.Total, new Vector2(-1, 4), "");
        }
        ImGui.EndTable();
        ImGui.Spacing();
    }

    private IEnumerable<(string Name, bool Complete, string Progress)> ObjectiveRows()
    {
        if (visibleGroup == ObjectiveGroup.Monsters)
            return Enumerable.Range(0, monsters.Length).Select(i => (monsterLabels[i], monsterRequired[i] > 0 && monsters[i] >= monsterRequired[i], $"{monsters[i]}/{monsterRequired[i]}"));
        var flags = visibleGroup == ObjectiveGroup.Dungeons ? dungeons : visibleGroup == ObjectiveGroup.Leves ? leves : fates;
        var labels = visibleGroup == ObjectiveGroup.Dungeons ? dungeonNames : visibleGroup == ObjectiveGroup.Leves ? leveLabels : fateLabels;
        return Enumerable.Range(0, flags.Length).Select(i => (labels[i], flags[i], flags[i] ? "已完成" : "未完成"));
    }

    private void DrawObjectiveList()
    {
        DrawSectionTitle(GroupName(visibleGroup));
        var showCompleted = config.ShowCompletedObjectives;
        if (ImGui.Checkbox("显示已完成", ref showCompleted))
        { config.ShowCompletedObjectives = showCompleted; SaveSettings(); }
        var rows = ObjectiveRows().OrderBy(x => x.Complete).Where(x => showCompleted || !x.Complete).ToArray();
        if (rows.Length == 0) { ImGui.TextWrapped("这类任务已全部完成。"); return; }
        if (!ImGui.BeginTable("Objectives", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings)) return;
        ImGui.TableSetupColumn("任务", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("进度", ImGuiTableColumnFlags.WidthFixed, 80);
        foreach (var row in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextWrapped(row.Name ?? "正在读取…");
            ImGui.TableNextColumn();
            ImGui.TextColored(row.Complete ? CompleteColor : ImGui.GetStyle().Colors[(int)ImGuiCol.Text], row.Progress);
        }
        ImGui.EndTable();
    }

    private static void DrawSectionTitle(string title)
    {
        ImGui.Separator();
        ImGui.TextColored(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], title);
    }

    private void DrawHelp()
    {
        if (!ImGui.CollapsingHeader("使用说明")) return;
        ImGui.TextWrapped("准备普通职业套装，将古武放在背包或兵装库。开始后先换职业，再单独装备古武，不会改写套装。");
        ImGui.TextWrapped("依赖 Omni Toolbox、vnavmesh、AutoDuty 和 BossMod Reborn。FATE 前准备基萨尔野菜；当前 FATE 战斗配置只支持白魔。");
        ImGui.TextWrapped("任务按实际机制识别，未适配的特殊交互会提示停止。BMR 只会躲避它能识别的危险区域。详细支持范围见仓库说明。");
    }

    private void DrawDiagnostics()
    {
        if (!ImGui.CollapsingHeader("更多与排错")) return;
        ImGui.TextUnformatted($"版本 {ReleaseVersion}");
        ImGui.TextUnformatted($"构建 {typeof(AnimusBookAssistant).Module.ModuleVersionId}");
        if (!string.IsNullOrEmpty(settingsStatus)) ImGui.TextWrapped(settingsStatus);
        if (!windowOpen && ImGui.Button("打开独立窗口")) windowOpen = true;
        if (ImGui.Button("检查依赖")) queuedAction = "diagnose";
        ImGui.SameLine();
        if (ImGui.Button("刷新读取")) { failed = false; nextRead = 0; }
        ImGui.TextWrapped(diagnostics);
        ImGui.TextWrapped(status);
        ImGui.TextWrapped(spawnStatus);
        ImGui.TextWrapped(autoStatus);
        ImGui.TextWrapped($"理符：{levePhase}    FATE：{fatePhase}");
        ImGui.TextUnformatted($"Relic={relicId}  Book={bookId}  Progress=0x{objectiveBits:X8}");
        if (lastRead != null) ImGui.TextUnformatted($"上次读取 {lastRead:HH:mm:ss}");
        if (ImGui.TreeNode($"附近目标 ({targets.Count})"))
        {
            foreach (var target in targets)
                ImGui.TextWrapped($"{target.Name} · {target.Distance:F1}m · Entity={target.EntityId:X8} · Data={target.DataId}");
            ImGui.TreePop();
        }
    }
    public sealed class BookHistoryEntry
    {
        public DateTimeOffset FirstSeen { get; set; }
        public bool Completed { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    private sealed record TargetSnapshot(string Name, uint EntityId, uint DataId,
        Vector3 Position, float Distance, bool Targetable);
}

[Serializable]
public sealed class AnimusBookAssistantConfig
{
    public bool UseMount { get; set; } = true;
    public bool UseFlight { get; set; } = true;
    public bool ShowCompletedObjectives { get; set; }
}

