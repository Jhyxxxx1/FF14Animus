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

// Explicitly started execution only; progress is always read back from the game.
public sealed unsafe class AnimusBookAssistant : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "古武小黄书助手",
        Description = "读取古武书本进度，自动赶路、战斗与副本执行；支持连续任务及完成后换书。适配范围见使用说明。",
        Category = ModuleCategory.Automation,
        Author = "Local",
        SupportUrls = [],
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
    private uint lastActionStatus;
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
    private ushort FatePrecursor(int slot) => fateIds[slot] switch { 611 => 610, 571 => 570, _ => 0 };
    private sealed record RoutePoint(uint Territory, Vector3 Probe, int Slot);

    public override bool HasSettings => true;

    protected override void OnEnable()
    {
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
                { HelpMessage = "小黄书：open / nearby / monsters / dungeon / dungeons / leve / leves / stop / status / diagnose" });
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
        if (mode == RunMode.Idle) return false;
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

    public override bool DrawSettings()
    {
        var changed = false;
        if (ImGui.CollapsingHeader("赶路设置"))
        {
            var mount = config.UseMount;
            if (ImGui.Checkbox("长途优先使用坐骑", ref mount)) { config.UseMount = mount; changed = true; }
            var flight = config.UseFlight;
            if (ImGui.Checkbox("可飞行时优先飞行", ref flight)) { config.UseFlight = flight; changed = true; }
            ImGui.TextDisabled("无法起飞或通行受阻时自动回退地面路线。");
        }
        DrawContent();
        return changed;
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
            status = bookId == 1
                ? "读取成功；第 1 本的书本 ID 和怪物槽位已通过 MCP 对照游戏窗口核对。其余完成位仍待验证。"
                : "读取成功；当前书本映射尚需与游戏内核对";
            if (queuedAction != null)
            {
                var action = queuedAction;
                queuedAction = null;
                if (action == "stop") StopRun("已手动停止");
                else if (action == "diagnose") Diagnose();
                else if (action == "auto")
                {
                    if (mode == RunMode.Idle) { autoBooks = true; autoNextAt = 0; autoStatus = "准备连续执行当前及后续书本"; }
                }
                else
                {
                    singleDuty = action == "dungeon";
                    singleLeve = action == "leve";
                    StartRun(action == "nearby" ? RunMode.Nearby : action == "monsters" ? RunMode.WorldMonsters
                        : action == "fates" ? RunMode.Fates : action is "leve" or "leves" ? RunMode.Leves : RunMode.Dungeons);
                }
            }
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
        // The runtime sheet has been inspected through MCP. The relationship between
        // RelicNoteId and RowId still requires comparison against the open game book.
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
            // These are dungeon boss targets, not ContentFinderCondition IDs.
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

    private void StartRun(RunMode requested)
    {
        if (mode != RunMode.Idle) { executionStatus = "正在运行，请先停止"; return; }
        if (ownsDuty || ownsMovement || ownsOverrides || rotationPreset != null) { executionStatus = "上次执行的资源尚未清理，不能开始"; return; }
        var currentFate = FateManager.Instance();
        var resumeFate = requested == RunMode.Fates && bookId is 1 or 2 && currentFate != null
            && !condition[ConditionFlag.BoundByDuty]
            && Enumerable.Range(0, 3).Any(i => !fates[i] && (currentFate->GetCurrentFateId() == fateIds[i]
                || FatePrecursor(i) != 0 && currentFate->GetCurrentFateId() == FatePrecursor(i)));
        var resumeFateCleanup = requested == RunMode.Fates && !condition[ConditionFlag.BoundByDuty] && AttackingMe().Count > 0;
        var reason = StartBlockedReason(requested == RunMode.Leves && (CanResumeLeveDialog() || HasLeveReturnPrompt()), resumeFate || resumeFateCleanup || requested == RunMode.Leves && CanResumeActiveLeve());
        if (reason != null) { executionStatus = reason; return; }
        if (requested != RunMode.Dungeons && BaseAction() == 0)
        { executionStatus = "当前职业尚未实现基础攻击"; return; }
        if (requested == RunMode.Dungeons && !IpcBool("AutoDuty.IsStopped"))
        { executionStatus = "AutoDuty 已在运行，请先结束它的任务"; return; }
        if (requested != RunMode.Dungeons && (IpcBool("vnavmesh.Path.IsRunning") || IpcBool("vnavmesh.SimpleMove.PathfindInProgress")))
        { executionStatus = "vnavmesh 已有移动或寻路任务"; return; }
        if (requested == RunMode.Fates && (bookId is not (1 or 2) || objectTable.LocalPlayer.ClassJob.RowId != 24))
        { executionStatus = "FATE 验证目前仅支持第一、二卷白魔法师"; return; }
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
        executionStatus = requested == RunMode.Nearby ? "扫描附近未完成目标" : "准备运行未完成副本";
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
        if (slot < 0 || leveIds[slot] is not (644 or 849 or 868 or 645 or 853 or 875 or 646 or 860 or 863)) return false;
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
        // Paladin has separate sword/shield books; either equipped slot may carry the relic.
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
        if (!HasRequiredWeapon() || player.CurrentHp == 0 || condition[ConditionFlag.Unconscious])
        { StopRun("装备发生变化或角色死亡"); return; }
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
        var action = actions->GetAdjustedActionId(BaseAction());
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
        ActivateFateRotation();
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

    private uint BaseAction() => objectTable.LocalPlayer?.ClassJob.RowId switch
    {
        19 => 9, 20 => 53, 21 => 31, 22 => 75, 23 => 97,
        24 => 119, 25 => 141, 27 => 163, 28 => 178, 30 => 2240, _ => 0
    };

    private void DrivePath(Vector3 from, Vector3 to, bool allowMount = false)
    {
        if (rotationActive) RestoreFateRotation();
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
        var groundOnly = Environment.TickCount64 < flightFallbackUntil || HasOverheadObstacle(from) || HasOverheadObstacle(to);
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
            catch (Exception ex) when (pathFlying && !condition[ConditionFlag.InFlight])
            {
                flightFallbackUntil = Environment.TickCount64 + 60000;
                travelStatus = $"该段飞行寻路不可用，改用坐骑地面路线：{ex.GetBaseException().Message}";
                return;
            }
            if (points.Count == 0) throw new InvalidOperationException("未找到可用路线");
            if (IpcBool("vnavmesh.Path.IsRunning")) throw new InvalidOperationException("其他移动任务已占用寻路插件");
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
                flightFallbackUntil = now + 120000;
                if (!wasFlying && condition[ConditionFlag.Mounted])
                {
                    walkingRecoveryUntil = now + 60000;
                    mountFallbackUntil = walkingRecoveryUntil;
                }
                travelStatus = wasFlying ? "飞行移动停滞，停止旧路径并落地改走地面" : "地面移动停滞，重新寻路";
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
            && Environment.TickCount64 >= flightFallbackUntil && Vector3.Distance(from, to) >= 80);
        travelStatus = pathFlying ? "坐骑飞行寻路" : condition[ConditionFlag.Mounted] ? "坐骑地面寻路" : "步行寻路";
        pathTask = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable")
            .InvokeFunc(from, pathFlying ? to + new Vector3(0, 8, 0) : to, pathFlying, pathCancellation.Token);
    }

    private static bool HasOverheadObstacle(Vector3 point) =>
        BGCollisionModule.RaycastMaterialFilter(point + new Vector3(0, 2, 0), Vector3.UnitY, out _, 20f);

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
            if (HorizontalDistance(player.Position, destination) > 35)
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

    // Initially limited to the three execution types present in book 1. Every UI
    // action is guarded by the selected leve and live component labels.
    private void DriveLeves()
    {
        if (HasLeveReturnPrompt())
        {
            ConfirmLeveReturn();
            return;
        }
        if (leveSlot >= 0 && leves[leveSlot] && refreshLeveId == 0)
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
            leveSlot = Array.FindIndex(leves, complete => !complete);
            if (leveSlot < 0) { StopRun("全部理符目标已完成", true); return; }
            if (leveIds[leveSlot] is not (644 or 849 or 868 or 645 or 853 or 875 or 646 or 860 or 863))
            { StopRun($"该理符尚无执行策略：{leveLabels[leveSlot]}"); return; }
            leveAt = Environment.TickCount64;
            patrolPoint = 0; patrolArrivedAt = 0;
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
            if (quests->NumLeveAllowances == 0) { StopRun("没有理符受理权"); return; }
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
                && yesno->PromptText->NodeText.ToString().Contains("目标地点附近的集落", StringComparison.Ordinal))
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
            if ((id == 644 && obj.BaseId == 2001098 || id == 646 && obj.BaseId == 2001104
                || id == 645 && obj.ObjectKind == ObjectKind.EventObj) && (objective == null
                || Vector3.DistanceSquared(player.Position, obj.Position) < Vector3.DistanceSquared(player.Position, objective.Position))) objective = obj;
        }
        if (id == 645 && DriveLureLeve(leveActors, objective)) return;
        if (id == 849 && DriveImpostorLeve(leveActors)) return;
        if (enemy != null) { AttackLeveEnemy(enemy); return; }
        if (objective != null)
        {
            if (rotationActive) RestoreFateRotation();
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
        if (id == 646)
        {
            if (rotationActive) RestoreFateRotation();
            var probe = Patrol646Points[patrolPoint];
            var ground = DalamudServices.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable")
                .InvokeFunc(probe, 15f, 20f);
            if (!ground.HasValue) { StopRun("巡逻点未找到可达地面，已停止"); return; }
            levePhase = executionStatus = $"巡查任务地点 {patrolPoint + 1}/{Patrol646Points.Length}，寻找巡逻标记或敌人";
            if (HorizontalDistance(player.Position, ground.Value) > 5)
            { patrolArrivedAt = 0; DrivePath(player.Position, ground.Value); return; }
            StopPath();
            if (patrolArrivedAt == 0) patrolArrivedAt = Environment.TickCount64;
            if (Environment.TickCount64 - patrolArrivedAt > 5000)
            { patrolPoint = (patrolPoint + 1) % Patrol646Points.Length; patrolArrivedAt = 0; }
            return;
        }
        StopPath();
        executionStatus += "；等待属于本理符的目标出现";
    }

    private bool HasLeveReturnPrompt()
    {
        var confirm = (AddonSelectYesno*)FindAddon("SelectYesno");
        if (confirm == null || confirm->PromptText == null
            || !confirm->PromptText->NodeText.ToString().Contains("目标地点附近的集落")) return false;
        return true;
    }

    private void ConfirmLeveReturn()
    {
        StopPath();
        if (rotationActive) RestoreFateRotation();
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
        ActivateFateRotation();
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

    // Factual wait positions: public Skyfire I profile; see DATA-SOURCES.md.
    // The FATE table covers only the current territory. Do not infer remote spawns.
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

    private void ActivateFateRotation()
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

    private void RestoreFateRotation()
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
        // If the user changed presets during the run, keep that new choice.
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
            && ids.Contains(x.EntityId) && x.TargetObjectId == player.EntityId
            && ActionManager.CanUseActionOnTarget(BaseAction(), (NativeGameObject*)x.Address)).ToList();
    }

    private bool DriveFateAggressors(FateManager* manager)
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
            if (clearingFateAggressors)
            {
                StopPath();
                if (targetId != 0 && targetManager.Target?.GameObjectId == targetId) targetManager.Target = null;
                RestoreFateRotation();
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
            StopPath(); RestoreFateRotation();
            clearingFateAggressors = true; fateCleanupAt = now;
        }
        if (now - fateCleanupAt > 180000) { StopRun("清理攻击者超过 3 分钟，已停止"); return true; }
        var candidates = attackers.Where(x => !TargetTemporarilyBlocked(x)).ToArray();
        if (candidates.Length == 0)
        {
            StopPath(); RestoreFateRotation();
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
        ActivateFateRotation();
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
            RestoreFateRotation(); StopPath(); targetManager.Target = null; targetId = 0;
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
        if ((!fateEngaged || clearingFateAggressors || fateSlot >= 0 && fates[fateSlot]) && DriveFateAggressors(manager)) return;
        if (fates.All(x => x)) { StopRun("三项 FATE 的书本进度均已完成，攻击者已检查", true); return; }
        if (fateSlot >= 0 && fates[fateSlot])
        {
            if (targetId != 0 && targetManager.Target?.GameObjectId == targetId) targetManager.Target = null;
            RestoreFateRotation();
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
                RestoreFateRotation();
                if (DriveFateAggressors(manager)) return;
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
        fatePhase = executionStatus = $"FATE {activeFateId}：{context->State}，进度 {context->Progress}%";
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
                { StopPath(); talk->FireCallbackInt(0); nextFateUiAt = now + 1200; return; }
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
        { DrivePath(player.Position, location, true); return; }
        if (!PrepareDismount(location)) return;
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
                && ActionManager.CanUseActionOnTarget(BaseAction(), (NativeGameObject*)x.Address)
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
        ActivateFateRotation();
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
        RestoreFateRotation();
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
            StopPath(); RestoreFateRotation();
            executionStatus = "攻击普通敌人前退出 FATE 同步";
            if (now - fateSyncAt > 2000 && manager->CurrentFate != null && manager->IsSyncedToFate(manager->CurrentFate))
            { manager->LevelSync(); fateSyncAt = now; }
            return false;
        }
        if (manager->SyncedFateId != 0 && manager->SyncedFateId != id)
        {
            StopPath(); RestoreFateRotation();
            executionStatus = $"退出其他 FATE 同步，准备清理活动 {id} 的攻击者";
            if (now - fateSyncAt > 2000 && manager->CurrentFate != null && manager->IsSyncedToFate(manager->CurrentFate))
            { manager->LevelSync(); fateSyncAt = now; }
            return false;
        }
        if (player.Level <= context->MaxLevel || manager->IsSyncedToFate(context)) return true;
        RestoreFateRotation();
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
            if (manager != null && DriveFateAggressors(manager)) return;
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
        try { RestoreFateRotation(); } catch (Exception ex) { executionStatus += $"；恢复循环失败：{ex.Message}"; }
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
        if (manager != null && DriveFateAggressors(manager)) return;
        singleDuty = singleLeve = false;
        var next = !Enumerable.Range(0, 10).All(i => monsters[i] >= monsterRequired[i]) ? RunMode.WorldMonsters
            : dungeons.Any(x => !x) ? RunMode.Dungeons : leves.Any(x => !x) ? RunMode.Leves
            : fates.Any(x => !x) ? RunMode.Fates : RunMode.Exchange;
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
        if (manager != null && DriveFateAggressors(manager)) return;
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
        buildMvid = typeof(AnimusBookAssistant).Module.ModuleVersionId, enabled = IsEnabled,
        bookId, relicId, bookName, requiredWeapon, objectiveBits, dutyCompletionObserved = dutyCompletedAt != 0, dutyExitRequested,
        mode = mode.ToString(), executionStatus, diagnostics, spawnStatus, levePhase, leveDebug, travelStatus, autoBooks, autoStatus,
        refreshLeveId, leveRefreshAttempts, refreshLeveAccepted, refreshRewardRequested,
        useMount = config.UseMount, useFlight = config.UseFlight, pathFlying,
        landing, landingApproachGoal, landingSearchAt,
        lastRangeOrLos, lastActionStatus, fateIds, activeFateId, fateSlot, fatePhase, fateEngaged,
        rotationPreset, rotationActive, companionStatus, clearingFateAggressors, fateCleanupStatus,
        startBlockedReason = StartBlockedReason(),
        status, monsters = monsters.Select(x => (int)x).ToArray(), monsterRequired = monsterRequired.Select(x => (int)x).ToArray(), monsterLabels, dungeons, dungeonNames, dungeonTerritories,
        fates, fateLabels, leves, leveLabels, targetCount = targets.Count, lastRead
    });

    private void DrawWindow()
    {
        if (!windowOpen) return;
        ImGui.SetNextWindowSize(new Vector2(600, 560), ImGuiCond.FirstUseEver);
        var visible = ImGui.Begin("古武小黄书助手###AnimusBookAssistant", ref windowOpen);
        try
        {
            if (visible) DrawContent();
        }
        finally { ImGui.End(); }
    }

    private void DrawContent()
    {
        var running = autoBooks || mode != RunMode.Idle;
        var ready = lastRead != null && !failed && bookId != 0;
        ImGui.TextUnformatted(ready ? bookName : "等待读取当前书本");
        ImGui.TextColored(running ? new Vector4(0.35f, 0.8f, 0.55f, 1) : new Vector4(0.65f, 0.68f, 0.72f, 1),
            running ? "执行中" : "已停止 / 待机");
        ImGui.Spacing();
        ImGui.BeginDisabled(running || !ready);
        if (ImGui.Button("开始连续执行", new Vector2(160, 32))) queuedAction = "auto";
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!running);
        if (ImGui.Button("停止", new Vector2(80, 32))) queuedAction = "stop";
        ImGui.EndDisabled();
        ImGui.Spacing();
        ImGui.TextWrapped(executionStatus);
        if (running && travelStatus != "待机") ImGui.TextWrapped($"赶路 · {travelStatus}");
        if (!ready) ImGui.TextWrapped(status);
        var blocked = StartBlockedReason();
        if (!running && ready && blocked != null) ImGui.TextWrapped($"开始前请检查：{blocked}");
        ImGui.Spacing();
        ImGui.Separator();
        if (ready)
        {
            var doneMonsters = Enumerable.Range(0, 10).Count(i => monsterRequired[i] > 0 && monsters[i] >= monsterRequired[i]);
            var doneDungeons = dungeons.Count(x => x);
            var doneFates = fates.Count(x => x);
            var doneLeves = leves.Count(x => x);
            var done = doneMonsters + doneDungeons + doneFates + doneLeves;
            ImGui.Spacing();
            ImGui.ProgressBar(done / 19f, new Vector2(-1, 20), $"本卷完成 {done} / 19 项");
            ImGui.TextWrapped($"怪物 {doneMonsters}/10    副本 {doneDungeons}/3    理符 {doneLeves}/3    FATE {doneFates}/3");
            ImGui.Spacing();
            if (ImGui.BeginTabBar("BookProgress"))
            {
                if (ImGui.BeginTabItem("怪物"))
                {
                    for (var i = 0; i < monsters.Length; i++)
                        DrawObjective(monsterLabels[i], monsterRequired[i] > 0 && monsters[i] >= monsterRequired[i], $"{monsters[i]}/{monsterRequired[i]}");
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("副本")) { DrawFlags(dungeons, dungeonNames); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("理符")) { DrawFlags(leves, leveLabels); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("FATE")) { DrawFlags(fates, fateLabels); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
        }
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("使用说明"))
        {
            ImGui.TextWrapped("装备当前书本对应的古武后开始。连续模式依次执行怪物、副本、理符和 FATE；整卷完成后前往摩杜纳更换下一本书。停止会结束本模块的连续执行。");
            ImGui.TextWrapped("依赖：vnavmesh（赶路）、AutoDuty（副本）、BossMod Reborn（输出、治疗与自动移动）。FATE 前请备好基萨尔野菜。");
            ImGui.TextWrapped("适配范围：前两卷已完成实测，第二卷至第三卷换书已验证；后续卷的特殊理符与 FATE 尚未完整适配，遇到不支持任务会停止。当前 FATE 自动执行仅支持前两卷白魔法师。");
            ImGui.TextWrapped("BMR 仅能躲避其已识别的危险区域。FATE 一轮未刷新时等待 5 分钟，战后立即重新巡查。");
        }
        if (ImGui.CollapsingHeader("更多与排错"))
        {
            if (!windowOpen && ImGui.Button("打开独立窗口")) windowOpen = true;
            if (ImGui.Button("检查依赖")) queuedAction = "diagnose";
            ImGui.SameLine();
            if (ImGui.Button("刷新书本读取")) { failed = false; nextRead = 0; }
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
    }

    private static void DrawObjective(string label, bool complete, string progress)
    {
        ImGui.TextColored(complete ? new Vector4(0.35f, 0.8f, 0.55f, 1) : ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
            complete ? "已完成" : progress);
        ImGui.SameLine();
        ImGui.TextWrapped(label ?? "正在读取…");
    }

    private static void DrawFlags(bool[] flags, string[] labels)
    {
        for (var i = 0; i < flags.Length; i++)
            DrawObjective(labels[i], flags[i], "未完成");
    }

    private sealed record TargetSnapshot(string Name, uint EntityId, uint DataId,
        Vector3 Position, float Distance, bool Targetable);
}

[Serializable]
public sealed class AnimusBookAssistantConfig
{
    public bool UseMount { get; set; } = true;
    public bool UseFlight { get; set; } = true;
}

