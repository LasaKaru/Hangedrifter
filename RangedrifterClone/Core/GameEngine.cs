using RangedrifterClone.Components;
using RangedrifterClone.Data;
using RangedrifterClone.MapSystem;
using RangedrifterClone.Systems;

namespace RangedrifterClone.Core;

public class GameEngine
{
    private static GameEngine? _instance;
    public static GameEngine Instance => _instance ??= new GameEngine();

    public EntityManager EntityManager  { get; } = new();
    public GameMap?      CurrentMap     { get; private set; }
    public Entity        PlayerEntity   { get; private set; }
    public MessageLog    MessageLog     { get; } = new();
    public DataLoader    DataLoader     { get; } = new();
    public GameState     State          { get; set; } = GameState.StudioIntro;
    public int           CurrentFloor   { get; private set; } = 1;

    // ── Run statistics (serialised into save / score record) ──────────────
    public int  RunSeed          { get; private set; }
    public bool IsDailyChallenge { get; private set; }
    public int  KillCount        { get; private set; }
    public int  AbilityUseCount  { get; private set; }
    public int  TotalRangedShots { get; private set; }
    public int  MinHpReached     { get; private set; } = int.MaxValue;
    public bool BossKilled       { get; private set; }

    // Achievement system — lives for the lifetime of the process
    public AchievementSystem Achievements { get; } = new();

    // Set by MainMenuScreen when the user picks Daily Challenge before char creation
    public bool IsDailyPending { get; set; }

    // Set by EndTurn when a ranged enemy just fired; cleared by GameScreen after pickup
    public bool HasEnemyShot { get; private set; }
    public int  EnemyShotX   { get; private set; }
    public int  EnemyShotY   { get; private set; }
    public void ClearEnemyShot() => HasEnemyShot = false;

    // Enemy count snapshot used for kill-counting inside EndTurn
    private int _enemiesBeforeTurn;

    // Systems
    private MovementSystem?     _movement;
    private CombatSystem?       _combat;
    private AISystem?           _ai;
    private FovSystem?          _fov;
    private InventorySystem?    _inventory;
    private AbilitySystem?      _ability;
    private EquipmentSystem?    _equipment;
    private StatusEffectSystem? _statusFx;
    private FeatureSystem?      _feature;

    // Floor cache: revisiting a floor restores its state
    private readonly Dictionary<int, GameMap> _floorCache = new();

    public int ScreenWidth  { get; private set; }
    public int ScreenHeight { get; private set; }

    private GameEngine() { }

    public void Initialize(int w, int h)
    {
        ScreenWidth = w; ScreenHeight = h;
        DataLoader.Load();
        InitSystems();
    }

    private void InitSystems()
    {
        _movement  = new MovementSystem(EntityManager);
        _combat    = new CombatSystem(EntityManager, MessageLog, DataLoader);
        _ai        = new AISystem(EntityManager, MessageLog);
        _fov       = new FovSystem(EntityManager);
        _inventory = new InventorySystem(EntityManager, MessageLog);
        _ability   = new AbilitySystem(EntityManager, MessageLog);
        _equipment = new EquipmentSystem(EntityManager, MessageLog);
        _statusFx  = new StatusEffectSystem(EntityManager, MessageLog);
        _feature   = new FeatureSystem(EntityManager, MessageLog);
        _feature.StairsDescended += OnStairsDescended;
        _feature.StairsAscended  += OnStairsAscended;
    }

    // ── New Game / Class selection ────────────────────────────────────────
    public void StartNewGame(string classId = "warrior", int? forceSeed = null, bool daily = false)
    {
        ResetRunStats();
        IsDailyChallenge = daily;
        RunSeed = forceSeed ?? new Random().Next(1, int.MaxValue);

        // Clear world
        foreach (var e in EntityManager.AllEntities.ToList())
            EntityManager.DestroyEntity(e);
        _floorCache.Clear();
        CurrentFloor = 1;

        // Generate first floor with deterministic seed
        var theme = DungeonGenerator.ThemeForFloor(1);
        var gen   = new DungeonGenerator(200, 200, 1, theme, FloorSeed(1));
        CurrentMap = gen.Generate();
        _floorCache[1] = CurrentMap;

        // Create player with chosen class
        var classDef = DataLoader.ClassDefinitions
            .FirstOrDefault(c => c.Id == classId) ?? DataLoader.ClassDefinitions[0];
        PlayerEntity = CreatePlayer(CurrentMap.StartPosition, classDef);

        // Spawn entities
        SpawnEntities(CurrentMap);

        _fov!.ComputeFov(CurrentMap, PlayerEntity, 9);
        State = GameState.Playing;

        MessageLog.Add($"You enter the dungeon as a {classDef.Name}.", SadRogue.Primitives.Color.Yellow);
        MessageLog.Add("Numpad/Arrows=move  G=pickup  1-4=ability  E=equip  .=wait",
            new SadRogue.Primitives.Color(100, 100, 100));
    }

    // ── Player creation ───────────────────────────────────────────────────
    private Entity CreatePlayer(SadRogue.Primitives.Point pos,
        DataLoader.ClassDefinition cls)
    {
        var p = EntityManager.CreateEntity();

        // Robot character — colour reflects chosen class
        var robotColor = cls.Id switch
        {
            "warrior" => new SadRogue.Primitives.Color(255, 165,  60),  // amber/gold
            "rogue"   => new SadRogue.Primitives.Color(210,  90, 230),  // violet
            "mage"    => new SadRogue.Primitives.Color( 80, 185, 255),  // electric blue
            _         => new SadRogue.Primitives.Color(230, 115,  70),  // robot orange
        };
        // Warm glow halo behind the robot so it pops against dark floors
        var robotBg = new SadRogue.Primitives.Color(50, 25, 10);

        EntityManager.AddComponent(p, new PositionComponent { X = pos.X, Y = pos.Y });
        EntityManager.AddComponent(p, new RenderComponent
        {
            // '\x02' = CP437 glyph index 2 = ☻ (filled smiley / robot face).
            // Must use the byte-value form — '☻' (U+263B = 9275) is outside
            // the 0-255 glyph-index range and renders as blank in SadConsole.
            Glyph = '\x02', Foreground = robotColor,
            Background = robotBg, RenderLayer = 10
        });
        EntityManager.AddComponent(p, new FighterComponent
        {
            Hp = cls.StartHp, MaxHp = cls.StartHp,
            Defense = cls.StartDefense, Strength = cls.StartStrength,
            DamageDice = cls.StartDamageDice, DamageSides = cls.StartDamageSides,
            DamageBonus = cls.StartDamageBonus
        });
        EntityManager.AddComponent(p, new ManaComponent
        {
            Mana = cls.StartMana, MaxMana = cls.StartMana,
            BaseMana = cls.StartMana, RegenPerTurn = 1
        });
        EntityManager.AddComponent(p, new ClassComponent
        {
            Class = Enum.TryParse<PlayerClass>(cls.Id, true, out var pc) ? pc : PlayerClass.Warrior,
            ClassName = cls.Name, ClassDescription = cls.Description,
            BonusDamage = cls.BonusDamage, BonusDefense = cls.BonusDefense,
            BonusMaxHp = cls.BonusMaxHp, BonusMaxMana = cls.BonusMaxMana,
            CritChance = cls.CritChance, DodgeChance = cls.DodgeChance
        });

        var abilities = new AbilityComponent();
        foreach (var ad in cls.Abilities)
        {
            abilities.Abilities.Add(new Ability
            {
                Id = ad.Id, Name = ad.Name, Description = ad.Description,
                Glyph = ad.Glyph, Color = ad.Color,
                ManaCost = ad.ManaCost, MaxCooldown = ad.MaxCooldown,
                Type = ad.AbilType, Power = ad.Power,
                Range = ad.Range, Radius = ad.Radius,
                DurationTurns = ad.DurationTurns, StatusEffect = ad.StatusEffect
            });
        }
        EntityManager.AddComponent(p, abilities);

        EntityManager.AddComponent(p, new InventoryComponent());
        EntityManager.AddComponent(p, new EquipmentSlotComponent());
        EntityManager.AddComponent(p, new StatusEffectComponent());
        EntityManager.AddComponent(p, new NameComponent { Name = "Player" });
        EntityManager.AddComponent(p, new ExperienceComponent
            { Level = 1, Experience = 0, NextLevelExp = 30 });
        EntityManager.AddComponent(p, new StatusComponent
            { WeaponQuality = "Bare fists" });

        // Give starting items and auto-equip first weapon
        var inv = EntityManager.GetComponent<InventoryComponent>(p)!;
        foreach (var itemId in cls.StartItems)
        {
            var def = DataLoader.ItemDefinitions.FirstOrDefault(i => i.Id == itemId);
            if (def == null) continue;
            inv.Items.Add(new InventoryEntry
            {
                ItemId = def.Id, Name = def.Name, Category = def.Category,
                Value = def.Value, Glyph = def.Glyph, Color = def.Color,
                BonusDamage = def.BonusDamage, BonusDefense = def.BonusDefense,
                BonusMaxHp = def.BonusMaxHp, BonusMaxMana = def.BonusMaxMana,
                UseEffect = def.UseEffect
            });
        }

        // Auto-equip first weapon and armor
        var equip = EntityManager.GetComponent<EquipmentSlotComponent>(p)!;
        for (int i = inv.Items.Count - 1; i >= 0; i--)
        {
            if (inv.Items[i].Category == "Weapon" && equip.Weapon == null)
                _equipment!.TryEquip(p, i);
            else if (inv.Items[i].Category == "Armor" && equip.Armor == null)
                _equipment!.TryEquip(p, i);
        }

        return p;
    }

    // ── Enemy / Item spawning ─────────────────────────────────────────────
    private void SpawnEntities(GameMap map)
    {
        var rng = new Random();

        // Enemies — weight selection scales with floor
        var enemyPool = DataLoader.EnemyDefinitions
            .Where(e => e.Behavior != "Boss" || CurrentFloor % 5 == 0)
            .ToList();

        foreach (var sp in map.EnemySpawnPoints)
        {
            if (enemyPool.Count == 0) break;
            var def = enemyPool[rng.Next(enemyPool.Count)];
            SpawnEnemy(def, sp.X, sp.Y);
        }

        // Boss at last spawn if floor is boss floor
        if (CurrentFloor % 5 == 0)
        {
            var boss = DataLoader.EnemyDefinitions.FirstOrDefault(e => e.Behavior == "Boss");
            if (boss != null && map.EnemySpawnPoints.Count > 0)
                SpawnEnemy(boss, map.StairsDownPos.X - 2, map.StairsDownPos.Y - 2);
        }

        // Items
        foreach (var ip in map.ItemSpawnPoints)
        {
            if (DataLoader.ItemDefinitions.Count == 0) break;
            // Weight toward floor-appropriate items
            var def = DataLoader.ItemDefinitions[rng.Next(DataLoader.ItemDefinitions.Count)];
            SpawnItem(def, ip.X, ip.Y);
        }

        // Chests → each chest position gets 2-3 random items
        foreach (var cp in map.ChestPositions)
        {
            var chest = EntityManager.CreateEntity();
            EntityManager.AddComponent(chest, new PositionComponent { X = cp.X, Y = cp.Y });
            EntityManager.AddComponent(chest, new RenderComponent
            {
                Glyph = 'C', Foreground = new SadRogue.Primitives.Color(255, 200, 50),
                Background = SadRogue.Primitives.Color.Transparent, RenderLayer = 3
            });
            var feat = new FeatureComponent
            {
                Type = FeatureType.Chest,
                IsTrapped = rng.NextDouble() < 0.2
            };
            int lootCount = rng.Next(2, 4);
            for (int i = 0; i < lootCount; i++)
            {
                var idef = DataLoader.ItemDefinitions[rng.Next(DataLoader.ItemDefinitions.Count)];
                feat.ChestContents.Add(idef.Name);
            }
            EntityManager.AddComponent(chest, feat);
            EntityManager.AddComponent(chest, new NameComponent { Name = "Chest" });
        }

        // Traps
        foreach (var tp in map.TrapPositions)
        {
            var trap = EntityManager.CreateEntity();
            EntityManager.AddComponent(trap, new PositionComponent { X = tp.X, Y = tp.Y });
            EntityManager.AddComponent(trap, new RenderComponent
            {
                Glyph = '^', Foreground = new SadRogue.Primitives.Color(200, 50, 50),
                Background = SadRogue.Primitives.Color.Transparent, RenderLayer = 1, IsVisible = false
            });
            var trapTypes = Enum.GetValues<TrapType>();
            EntityManager.AddComponent(trap, new FeatureComponent
            {
                Type = FeatureType.Trap,
                TrapKind = trapTypes[rng.Next(trapTypes.Length)],
                TrapDamage = 3 + CurrentFloor
            });
        }

        // Doors
        foreach (var dp in map.DoorPositions)
        {
            var door = EntityManager.CreateEntity();
            EntityManager.AddComponent(door, new PositionComponent { X = dp.X, Y = dp.Y });
            EntityManager.AddComponent(door, new RenderComponent
            {
                Glyph = '+', Foreground = new SadRogue.Primitives.Color(200, 160, 80),
                Background = SadRogue.Primitives.Color.Transparent, RenderLayer = 4
            });
            EntityManager.AddComponent(door, new FeatureComponent
            {
                Type = FeatureType.Door, IsLocked = rng.NextDouble() < 0.08
            });
        }

        // Hidden doors (invisible wall — revealed by pressing X)
        foreach (var hdp in map.HiddenDoorPositions)
        {
            var hd = EntityManager.CreateEntity();
            EntityManager.AddComponent(hd, new PositionComponent { X = hdp.X, Y = hdp.Y });
            EntityManager.AddComponent(hd, new FeatureComponent  { Type = FeatureType.HiddenDoor });
        }

        // Stairs entities
        if (map.InBounds(map.StairsDownPos.X, map.StairsDownPos.Y))
        {
            var stDown = EntityManager.CreateEntity();
            EntityManager.AddComponent(stDown, new PositionComponent
                { X = map.StairsDownPos.X, Y = map.StairsDownPos.Y });
            EntityManager.AddComponent(stDown, new RenderComponent
            {
                Glyph = '>', Foreground = new SadRogue.Primitives.Color(200, 200, 255),
                Background = SadRogue.Primitives.Color.Transparent, RenderLayer = 3
            });
            EntityManager.AddComponent(stDown, new FeatureComponent
            {
                Type = FeatureType.StairsDown, GoesDown = true,
                TargetFloor = CurrentFloor + 1
            });
            EntityManager.AddComponent(stDown, new NameComponent { Name = "Stairs Down" });
        }

        if (CurrentFloor > 1 && map.InBounds(map.StairsUpPos.X, map.StairsUpPos.Y))
        {
            var stUp = EntityManager.CreateEntity();
            EntityManager.AddComponent(stUp, new PositionComponent
                { X = map.StairsUpPos.X, Y = map.StairsUpPos.Y });
            EntityManager.AddComponent(stUp, new RenderComponent
            {
                Glyph = '<', Foreground = new SadRogue.Primitives.Color(200, 200, 255),
                Background = SadRogue.Primitives.Color.Transparent, RenderLayer = 3
            });
            EntityManager.AddComponent(stUp, new FeatureComponent
            {
                Type = FeatureType.StairsUp, GoesDown = false,
                TargetFloor = CurrentFloor - 1
            });
            EntityManager.AddComponent(stUp, new NameComponent { Name = "Stairs Up" });
        }
    }

    private void SpawnEnemy(DataLoader.EnemyDefinition def, int x, int y)
    {
        if (!CurrentMap!.IsWalkable(x, y)) return;
        var e = EntityManager.CreateEntity();
        EntityManager.AddComponent(e, new PositionComponent { X = x, Y = y });
        EntityManager.AddComponent(e, new RenderComponent
        {
            Glyph = def.Glyph, Foreground = def.Color,
            Background = SadRogue.Primitives.Color.Transparent, RenderLayer = 5
        });
        EntityManager.AddComponent(e, new FighterComponent
        {
            Hp = def.MaxHp, MaxHp = def.MaxHp,
            Defense = def.Defense, Strength = def.Strength,
            DamageDice = def.DamageDice, DamageSides = def.DamageSides,
            DamageBonus = def.DamageBonus
        });
        EntityManager.AddComponent(e, new AIComponent
        {
            Behavior = def.AiBehavior, IsRanged = def.IsRanged,
            AttackRange = def.AttackRange, AlertRadius = def.AlertRadius,
            PackTag = def.PackTag, CanFlee = def.CanFlee,
            FleeThreshold = def.FleeThreshold
        });
        EntityManager.AddComponent(e, new NameComponent { Name = def.Name });
        EntityManager.AddComponent(e, new StatusEffectComponent());

        if (def.LootDrops != null && def.LootDrops.Count > 0)
        {
            EntityManager.AddComponent(e, new LootDropComponent
            {
                DropChance = def.DropChance,
                PossibleDrops = def.LootDrops.Select(l =>
                    new Components.LootEntry { ItemId = l.ItemId, Weight = l.Weight }).ToList()
            });
        }
    }

    private void SpawnItem(DataLoader.ItemDefinition def, int x, int y)
    {
        var item = EntityManager.CreateEntity();
        EntityManager.AddComponent(item, new PositionComponent { X = x, Y = y });
        EntityManager.AddComponent(item, new RenderComponent
        {
            Glyph = def.Glyph, Foreground = def.Color,
            Background = SadRogue.Primitives.Color.Transparent, RenderLayer = 2
        });
        EntityManager.AddComponent(item, new ItemComponent
        {
            ItemId = def.Id, Name = def.Name, Category = def.Category, Value = def.Value
        });
        EntityManager.AddComponent(item, new NameComponent { Name = def.Name });
    }

    // ── Turn processing ───────────────────────────────────────────────────
    public void ProcessPlayerTurn(int dx, int dy)
    {
        if (State != GameState.Playing || CurrentMap == null) return;
        _enemiesBeforeTurn = EntityManager.GetEntitiesWith<AIComponent>().Count();

        var fx = EntityManager.GetComponent<StatusEffectComponent>(PlayerEntity);
        if (fx != null && fx.IsCrowdControlled)
        {
            _statusFx!.ProcessAll();
            EndTurn();
            return;
        }

        // Check feature interaction (doors, stairs) before moving
        var pos   = EntityManager.GetComponent<PositionComponent>(PlayerEntity)!;
        int nx = pos.X + dx, ny = pos.Y + dy;
        var result = _feature!.TryInteract(PlayerEntity, nx, ny, CurrentMap);

        if (result == InteractResult.StairsUsed) return; // floor changed
        if (result == InteractResult.Blocked)    return;

        // Normal movement / combat
        bool acted = _movement!.TryMovePlayer(PlayerEntity, dx, dy, CurrentMap, _combat!, _inventory!);
        if (!acted) return;

        // Check trap at new position
        var newPos = EntityManager.GetComponent<PositionComponent>(PlayerEntity)!;
        _feature.CheckTrapAtPosition(PlayerEntity, newPos.X, newPos.Y);

        EndTurn();
    }

    public void StartDailyChallenge(string classId = "warrior")
    {
        int seed = DateOnly.FromDateTime(DateTime.Today).DayNumber;
        StartNewGame(classId, forceSeed: seed, daily: true);
    }

    public void RestoreFromSave(SaveData save)
    {
        ResetRunStats();
        RunSeed          = save.RunSeed;
        IsDailyChallenge = save.IsDailyChallenge;
        KillCount        = save.KillCount;
        AbilityUseCount  = save.AbilityUseCount;
        TotalRangedShots = save.TotalRangedShots;

        foreach (var e in EntityManager.AllEntities.ToList())
            EntityManager.DestroyEntity(e);
        _floorCache.Clear();
        CurrentFloor = save.CurrentFloor;

        var theme = DungeonGenerator.ThemeForFloor(CurrentFloor);
        var gen   = new DungeonGenerator(200, 200, CurrentFloor, theme, FloorSeed(CurrentFloor));
        CurrentMap = gen.Generate();
        _floorCache[CurrentFloor] = CurrentMap;

        var classDef = DataLoader.ClassDefinitions
            .FirstOrDefault(c => c.Id == save.ClassId) ?? DataLoader.ClassDefinitions[0];
        PlayerEntity = CreatePlayer(
            new SadRogue.Primitives.Point(save.Player.X, save.Player.Y), classDef);

        // Override class-default stats with saved values
        var fighter = EntityManager.GetComponent<FighterComponent>(PlayerEntity)!;
        fighter.Hp = save.Player.Hp; fighter.MaxHp = save.Player.MaxHp;
        fighter.Strength = save.Player.Strength; fighter.Defense = save.Player.Defense;

        var mana = EntityManager.GetComponent<ManaComponent>(PlayerEntity);
        if (mana != null) { mana.Mana = save.Player.Mana; mana.MaxMana = save.Player.MaxMana; }

        var xp = EntityManager.GetComponent<ExperienceComponent>(PlayerEntity);
        if (xp != null)
        {
            xp.Level = save.Player.Level;
            xp.Experience   = save.Player.Experience;
            xp.NextLevelExp = save.Player.NextLevelExp;
        }

        var status = EntityManager.GetComponent<StatusComponent>(PlayerEntity);
        if (status != null) status.Turn = save.TurnCount;

        // Restore inventory from item IDs
        var inv = EntityManager.GetComponent<InventoryComponent>(PlayerEntity)!;
        inv.Items.Clear();
        foreach (var is_ in save.Player.Inventory)
        {
            var def = DataLoader.ItemDefinitions.FirstOrDefault(d => d.Id == is_.ItemId);
            if (def == null) continue;
            inv.Items.Add(new InventoryEntry
            {
                ItemId = def.Id, Name = def.Name, Category = def.Category,
                Count = is_.Count, Value = def.Value,
                Glyph = def.Glyph, Color = def.Color,
                BonusDamage = def.BonusDamage, BonusDefense = def.BonusDefense,
                BonusMaxHp = def.BonusMaxHp, BonusMaxMana = def.BonusMaxMana,
                UseEffect = def.UseEffect
            });
        }

        // Restore equipment
        var equip = EntityManager.GetComponent<EquipmentSlotComponent>(PlayerEntity)!;
        equip.Weapon = MakeEquipEntry(save.Player.Weapon, EquipSlot.Weapon);
        equip.Armor  = MakeEquipEntry(save.Player.Armor,  EquipSlot.Armor);
        equip.Shield = MakeEquipEntry(save.Player.Shield, EquipSlot.Shield);
        equip.Ring   = MakeEquipEntry(save.Player.Ring,   EquipSlot.Ring);
        equip.Amulet = MakeEquipEntry(save.Player.Amulet, EquipSlot.Amulet);
        _equipment!.RecalculateStats(PlayerEntity);

        SpawnEntities(CurrentMap);
        _fov!.ComputeFov(CurrentMap, PlayerEntity, 9);
        State = GameState.Playing;

        MessageLog.Add($"Run restored — Floor {CurrentFloor}  Seed #{RunSeed}",
            SadRogue.Primitives.Color.Cyan);
    }

    private EquipmentEntry? MakeEquipEntry(string? itemId, EquipSlot slot)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        var def = DataLoader.ItemDefinitions.FirstOrDefault(d => d.Id == itemId);
        if (def == null) return null;
        return new EquipmentEntry
        {
            ItemId = def.Id, Name = def.Name, Slot = slot,
            BonusDamage = def.BonusDamage, BonusDefense = def.BonusDefense,
            BonusMaxHp = def.BonusMaxHp, BonusMaxMana = def.BonusMaxMana,
            Glyph = def.Glyph, Color = def.Color
        };
    }

    private void ResetRunStats()
    {
        KillCount = AbilityUseCount = TotalRangedShots = 0;
        MinHpReached = int.MaxValue;
        BossKilled = false;
    }

    private int FloorSeed(int floor) => RunSeed ^ (floor * 7919);

    public void UseAbility(int index, int dx = 0, int dy = 0)
    {
        if (State != GameState.Playing || CurrentMap == null) return;
        bool acted = _ability!.UseAbility(PlayerEntity, index, CurrentMap, _combat!, dx, dy);
        if (acted) { AbilityUseCount++; EndTurn(); }
    }

    public void ProcessAction(PlayerAction action)
    {
        if (State != GameState.Playing) return;
        switch (action)
        {
            case PlayerAction.PickUp:
                if (_inventory!.TryPickUp(PlayerEntity, CurrentMap!)) EndTurn();
                break;
            case PlayerAction.Wait:
                ProcessPlayerTurn(0, 0);
                break;
            case PlayerAction.UseItem:
                UseSelectedItem();
                break;
        }
    }

    public void TryEquipItem(int inventoryIndex)
    {
        _equipment!.TryEquip(PlayerEntity, inventoryIndex);
    }

    public void UseInventoryItem(int index)
    {
        if (State != GameState.Playing) return;
        var inv = EntityManager.GetComponent<InventoryComponent>(PlayerEntity);
        if (inv == null || index < 0 || index >= inv.Items.Count) return;
        var item = inv.Items[index];
        if (string.IsNullOrEmpty(item.UseEffect))
        { MessageLog.Add("That item can't be used.", SadRogue.Primitives.Color.Gray); return; }
        ApplyItemEffect(item);
        if (item.Count > 1) item.Count--;
        else inv.Items.RemoveAt(index);
        MessageLog.Add($"Used {item.Name}.", SadRogue.Primitives.Color.LightGreen);
        EndTurn();
    }

    public void DropItem(int index)
    {
        if (State != GameState.Playing) return;
        var inv   = EntityManager.GetComponent<InventoryComponent>(PlayerEntity);
        var pos   = EntityManager.GetComponent<PositionComponent>(PlayerEntity);
        if (inv == null || pos == null || index < 0 || index >= inv.Items.Count) return;
        var item  = inv.Items[index];
        // Auto-unequip if this item was equipped
        var equip = EntityManager.GetComponent<EquipmentSlotComponent>(PlayerEntity);
        if (equip != null)
        {
            bool wasEquipped = false;
            if (equip.Weapon?.ItemId  == item.ItemId) { equip.Weapon  = null; wasEquipped = true; }
            if (equip.Armor?.ItemId   == item.ItemId) { equip.Armor   = null; wasEquipped = true; }
            if (equip.Shield?.ItemId  == item.ItemId) { equip.Shield  = null; wasEquipped = true; }
            if (equip.Ring?.ItemId    == item.ItemId) { equip.Ring    = null; wasEquipped = true; }
            if (equip.Amulet?.ItemId  == item.ItemId) { equip.Amulet  = null; wasEquipped = true; }
            if (wasEquipped) _equipment!.RecalculateStats(PlayerEntity);
        }
        inv.Items.RemoveAt(index);
        if (CurrentMap != null)
        {
            var def = DataLoader.ItemDefinitions.FirstOrDefault(d => d.Id == item.ItemId);
            if (def != null) SpawnItem(def, pos.X, pos.Y);
        }
        MessageLog.Add($"Dropped {item.Name}.", SadRogue.Primitives.Color.Gray);
    }

    private void UseSelectedItem()
    {
        var inv = EntityManager.GetComponent<InventoryComponent>(PlayerEntity);
        if (inv == null || inv.Items.Count == 0) return;
        // Use first consumable found
        for (int i = 0; i < inv.Items.Count; i++)
        {
            var item = inv.Items[i];
            if (!string.IsNullOrEmpty(item.UseEffect))
            {
                ApplyItemEffect(item);
                if (item.Count > 1) item.Count--;
                else inv.Items.RemoveAt(i);
                MessageLog.Add($"Used {item.Name}.", SadRogue.Primitives.Color.LightGreen);
                EndTurn();
                return;
            }
        }
        MessageLog.Add("Nothing to use.", SadRogue.Primitives.Color.Gray);
    }

    private void ApplyItemEffect(InventoryEntry item)
    {
        var fighter = EntityManager.GetComponent<FighterComponent>(PlayerEntity);
        var mana    = EntityManager.GetComponent<ManaComponent>(PlayerEntity);
        var fx      = EntityManager.GetComponent<StatusEffectComponent>(PlayerEntity);

        foreach (var part in item.UseEffect.Split(','))
        {
            var tokens = part.Trim().Split(':');
            switch (tokens[0].ToLower())
            {
                case "heal":
                    if (fighter != null && int.TryParse(tokens.ElementAtOrDefault(1), out int hp))
                    {
                        int healed = Math.Min(hp, fighter.MaxHp - fighter.Hp);
                        fighter.Hp += healed;
                        MessageLog.Add($"Healed {healed} HP.", SadRogue.Primitives.Color.LightGreen);
                    }
                    break;
                case "mana":
                    if (mana != null && int.TryParse(tokens.ElementAtOrDefault(1), out int mp))
                    {
                        mana.Restore(mp);
                        MessageLog.Add($"Restored {mp} mana.", SadRogue.Primitives.Color.Cyan);
                    }
                    break;
                case "curepoison":
                    fx?.Remove(EffectType.Poisoned);
                    fx?.Remove(EffectType.Poisoning);
                    MessageLog.Add("Poison cured!", SadRogue.Primitives.Color.Green);
                    break;
                case "regen":
                    if (int.TryParse(tokens.ElementAtOrDefault(1), out int mag) &&
                        int.TryParse(tokens.ElementAtOrDefault(2), out int dur))
                        fx?.Apply(EffectType.Regenerating, mag, dur);
                    break;
                case "burn":
                    // fire essence: deal self-damage or use as weapon
                    break;
            }
        }
    }

    private void EndTurn()
    {
        if (CurrentMap == null) return;

        // ── Kill accounting ───────────────────────────────────────────────
        int enemiesNow = EntityManager.GetEntitiesWith<AIComponent>().Count();
        int newKills   = Math.Max(0, _enemiesBeforeTurn - enemiesNow);
        KillCount += newKills;
        _enemiesBeforeTurn = enemiesNow;

        // ── Status effect tick ────────────────────────────────────────────
        _statusFx!.ProcessAll();

        // ── Mana regen ────────────────────────────────────────────────────
        EntityManager.GetComponent<ManaComponent>(PlayerEntity)?.TickRegen();

        // ── Check player death ────────────────────────────────────────────
        var fighter = EntityManager.GetComponent<FighterComponent>(PlayerEntity);
        if (fighter != null && fighter.Hp <= 0)
        {
            RecordGameOver("Slain in combat");
            return;
        }

        // Track min HP
        if (fighter != null)
            MinHpReached = Math.Min(MinHpReached, fighter.Hp);

        // ── Ability cooldown tick ─────────────────────────────────────────
        EntityManager.GetComponent<AbilityComponent>(PlayerEntity)?.TickCooldowns();

        // ── FOV ───────────────────────────────────────────────────────────
        _fov!.ComputeFov(CurrentMap, PlayerEntity, 9);

        // ── AI turns ──────────────────────────────────────────────────────
        _ai!.ProcessTurns(CurrentMap, PlayerEntity, _combat!, _movement!);
        if (_ai.RangedShotsFired.Count > 0)
        {
            var shot = _ai.RangedShotsFired[^1];
            EnemyShotX = shot.X; EnemyShotY = shot.Y;
            HasEnemyShot = true;
        }

        // ── Check player death after AI ───────────────────────────────────
        fighter = EntityManager.GetComponent<FighterComponent>(PlayerEntity);
        if (fighter != null && fighter.Hp <= 0)
        {
            RecordGameOver("Slain in combat");
            return;
        }

        // ── Turn counter ──────────────────────────────────────────────────
        var status = EntityManager.GetComponent<StatusComponent>(PlayerEntity);
        if (status != null) status.Turn++;

        // ── Achievement checks ────────────────────────────────────────────
        var xp    = EntityManager.GetComponent<ExperienceComponent>(PlayerEntity);
        var inv   = EntityManager.GetComponent<InventoryComponent>(PlayerEntity);
        var equip = EntityManager.GetComponent<EquipmentSlotComponent>(PlayerEntity);
        int equippedSlots = (equip?.Weapon  != null ? 1 : 0)
                          + (equip?.Armor   != null ? 1 : 0)
                          + (equip?.Shield  != null ? 1 : 0)
                          + (equip?.Ring    != null ? 1 : 0)
                          + (equip?.Amulet  != null ? 1 : 0);
        Achievements.CheckAll(
            kills:          KillCount,
            floor:          CurrentFloor,
            level:          xp?.Level ?? 1,
            abilities:      AbilityUseCount,
            turns:          status?.Turn ?? 0,
            invCount:       inv?.Items.Count ?? 0,
            equippedSlots:  equippedSlots,
            minHp:          MinHpReached == int.MaxValue ? (fighter?.Hp ?? 1) : MinHpReached,
            rangedShots:    TotalRangedShots,
            bossKilled:     BossKilled,
            dailyComplete:  false   // set to true by feature system when boss dies on daily
        );
    }

    private void RecordGameOver(string cause)
    {
        State = GameState.GameOver;
        MessageLog.Add("You have died! Press R to restart.", SadRogue.Primitives.Color.Red);
        var status = EntityManager.GetComponent<StatusComponent>(PlayerEntity);
        var cls    = EntityManager.GetComponent<ClassComponent>(PlayerEntity);
        int score  = KillCount * 15 + CurrentFloor * 200 + (status?.Turn ?? 0) / 5;
        SaveSystem.AddScore(new ScoreRecord
        {
            PlayerClass  = cls?.ClassName ?? "Unknown",
            Score        = score,
            Floor        = CurrentFloor,
            KillCount    = KillCount,
            TurnCount    = status?.Turn ?? 0,
            IsDaily      = IsDailyChallenge,
            DailySeed    = IsDailyChallenge ? RunSeed : 0,
            Date         = DateOnly.FromDateTime(DateTime.Today).ToString(),
            Cause        = cause,
        });
        SaveSystem.DeleteSave();
    }

    // ── Ranged shot ───────────────────────────────────────────────────────
    /// <summary>
    /// Fires a ranged shot in the direction given by <paramref name="angleRad"/>.
    /// Marches a ray up to <c>MaxRange</c> tiles, hits the first enemy found,
    /// applies damage through the combat system, and ends the player's turn.
    /// Returns the world-cell that was struck (or the first wall) so the caller
    /// can show a visual flash.
    /// </summary>
    public (bool hitEnemy, int cellX, int cellY) FireRangedShot(double angleRad)
    {
        if (State != GameState.Playing || CurrentMap == null)
            return (false, 0, 0);
        TotalRangedShots++;
        _enemiesBeforeTurn = EntityManager.GetEntitiesWith<AIComponent>().Count();

        var pos = EntityManager.GetComponent<PositionComponent>(PlayerEntity);
        if (pos == null) return (false, 0, 0);

        double dirX = Math.Cos(angleRad), dirY = Math.Sin(angleRad);
        double px   = pos.X + 0.5,        py   = pos.Y + 0.5;

        const int MaxRange = 22;
        for (int step = 1; step <= MaxRange; step++)
        {
            int cx = (int)(px + dirX * step), cy = (int)(py + dirY * step);

            // Stop at solid wall
            var tile = CurrentMap.GetTile(cx, cy);
            if (!tile.IsWalkable || tile.Type == TileType.Empty)
                return (false, cx, cy);

            // Check every entity at this cell for an AI (= enemy)
            foreach (var e in EntityManager
                .GetEntitiesWith<AIComponent, PositionComponent>()
                .ToList())
            {
                var ep = EntityManager.GetComponent<PositionComponent>(e)!;
                if (ep.X != cx || ep.Y != cy) continue;

                // Hit — deal damage with a small ranged bonus
                _combat!.AttackWithBonus(PlayerEntity, e, 3);

                // Alert all nearby pack-mates
                var ai = EntityManager.GetComponent<AIComponent>(e);
                if (ai != null) ai.State = AIState.Hunting;

                EndTurn();
                return (true, cx, cy);
            }
        }

        MessageLog.Add("Your shot disappears into the dark.", new SadRogue.Primitives.Color(100, 100, 70));
        EndTurn();
        return (false, 0, 0);
    }

    // ── Floor transitions ─────────────────────────────────────────────────
    private void OnStairsDescended(int targetFloor)
    {
        _floorCache[CurrentFloor] = CurrentMap!;
        CurrentFloor = targetFloor;
        SaveSystem.SaveGame(this);        // auto-save on descent
        LoadOrGenerateFloor(targetFloor, fromAbove: true);
    }

    private void OnStairsAscended(int targetFloor)
    {
        _floorCache[CurrentFloor] = CurrentMap!;
        CurrentFloor = targetFloor;
        LoadOrGenerateFloor(targetFloor, fromAbove: false);
    }

    private void LoadOrGenerateFloor(int floor, bool fromAbove)
    {
        foreach (var e in EntityManager.AllEntities.Where(e => e != PlayerEntity).ToList())
            EntityManager.DestroyEntity(e);

        if (_floorCache.TryGetValue(floor, out var cached))
        {
            CurrentMap = cached;
        }
        else
        {
            var theme = DungeonGenerator.ThemeForFloor(floor);
            var gen   = new DungeonGenerator(200, 200, floor, theme, FloorSeed(floor));
            CurrentMap = gen.Generate();
            _floorCache[floor] = CurrentMap;
            SpawnEntities(CurrentMap);
        }

        var pos  = EntityManager.GetComponent<PositionComponent>(PlayerEntity)!;
        var dest = fromAbove ? CurrentMap.StartPosition : CurrentMap.StairsDownPos;
        pos.X = dest.X; pos.Y = dest.Y;

        _fov!.ComputeFov(CurrentMap, PlayerEntity, 9);
        MessageLog.Add($"Floor {floor} — Theme: {CurrentMap.Theme}", SadRogue.Primitives.Color.Cyan);
    }

    public bool TryRevealHiddenDoor(int x, int y)
    {
        if (CurrentMap == null) return false;
        foreach (var e in EntityManager.GetEntitiesWith<FeatureComponent, PositionComponent>())
        {
            var p    = EntityManager.GetComponent<PositionComponent>(e)!;
            var feat = EntityManager.GetComponent<FeatureComponent>(e)!;
            if (p.X != x || p.Y != y || feat.Type != FeatureType.HiddenDoor) continue;

            EntityManager.DestroyEntity(e);
            CurrentMap.SetTile(x, y, MapSystem.Tile.CreateFloor(CurrentMap.Theme));
            _fov!.ComputeFov(CurrentMap, PlayerEntity, 9);
            MessageLog.Add("*** You found a SECRET PASSAGE! ***",
                new SadRogue.Primitives.Color(255, 230, 50));
            MusicPlayer.PlayReveal();
            return true;
        }
        return false;
    }
}

public enum GameState   { StudioIntro, Loading, MainMenu, CharacterCreation, Playing, Inventory, GameOver, Settings, Leaderboard }
public enum PlayerAction { MoveNorth, MoveSouth, MoveEast, MoveWest,
    MoveNE, MoveNW, MoveSE, MoveSW, PickUp, Wait, UseItem, OpenInventory, CloseInventory }
