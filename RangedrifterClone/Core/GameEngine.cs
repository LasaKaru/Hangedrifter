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
    public GameState     State          { get; set; } = GameState.MainMenu;
    public int           CurrentFloor   { get; private set; } = 1;

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
    public void StartNewGame(string classId = "warrior")
    {
        // Clear world
        foreach (var e in EntityManager.AllEntities.ToList())
            EntityManager.DestroyEntity(e);
        _floorCache.Clear();
        CurrentFloor = 1;

        // Generate first floor
        var theme = DungeonGenerator.ThemeForFloor(1);
        var gen   = new DungeonGenerator(200, 200, 1, theme);
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

    public void UseAbility(int index, int dx = 0, int dy = 0)
    {
        if (State != GameState.Playing || CurrentMap == null) return;
        bool acted = _ability!.UseAbility(PlayerEntity, index, CurrentMap, _combat!, dx, dy);
        if (acted) EndTurn();
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

        // Status effect tick
        _statusFx!.ProcessAll();

        // Mana regen
        EntityManager.GetComponent<ManaComponent>(PlayerEntity)?.TickRegen();

        // Check player death
        var fighter = EntityManager.GetComponent<FighterComponent>(PlayerEntity);
        if (fighter != null && fighter.Hp <= 0)
        {
            State = GameState.GameOver;
            MessageLog.Add("You have died! Press R to restart.", SadRogue.Primitives.Color.Red);
            return;
        }

        // Ability cooldown tick
        EntityManager.GetComponent<AbilityComponent>(PlayerEntity)?.TickCooldowns();

        // Update FOV
        _fov!.ComputeFov(CurrentMap, PlayerEntity, 9);

        // AI turns
        _ai!.ProcessTurns(CurrentMap, PlayerEntity, _combat!, _movement!);

        // Check player death again after AI
        fighter = EntityManager.GetComponent<FighterComponent>(PlayerEntity);
        if (fighter != null && fighter.Hp <= 0)
        {
            State = GameState.GameOver;
            MessageLog.Add("You have died! Press R to restart.", SadRogue.Primitives.Color.Red);
            return;
        }

        // Turn counter
        var status = EntityManager.GetComponent<StatusComponent>(PlayerEntity);
        if (status != null) status.Turn++;
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
        // Remove all non-player entities from current state
        foreach (var e in EntityManager.AllEntities.Where(e => e != PlayerEntity).ToList())
            EntityManager.DestroyEntity(e);

        if (_floorCache.TryGetValue(floor, out var cached))
        {
            CurrentMap = cached;
        }
        else
        {
            var theme = DungeonGenerator.ThemeForFloor(floor);
            var gen   = new DungeonGenerator(200, 200, floor, theme);
            CurrentMap = gen.Generate();
            _floorCache[floor] = CurrentMap;
            SpawnEntities(CurrentMap);
        }

        // Place player at appropriate stairs
        var pos = EntityManager.GetComponent<PositionComponent>(PlayerEntity)!;
        var dest = fromAbove ? CurrentMap.StartPosition : CurrentMap.StairsDownPos;
        pos.X = dest.X; pos.Y = dest.Y;

        _fov!.ComputeFov(CurrentMap, PlayerEntity, 9);
        MessageLog.Add($"Floor {floor} — Theme: {CurrentMap.Theme}", SadRogue.Primitives.Color.Cyan);
    }
}

public enum GameState   { MainMenu, CharacterCreation, Playing, Inventory, GameOver, Settings }
public enum PlayerAction { MoveNorth, MoveSouth, MoveEast, MoveWest,
    MoveNE, MoveNW, MoveSE, MoveSW, PickUp, Wait, UseItem, OpenInventory, CloseInventory }
