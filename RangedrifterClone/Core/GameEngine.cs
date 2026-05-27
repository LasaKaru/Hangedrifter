using RangedrifterClone.Components;
using RangedrifterClone.Data;
using RangedrifterClone.MapSystem;
using RangedrifterClone.Systems;

namespace RangedrifterClone.Core;

public class GameEngine
{
    private static GameEngine? _instance;
    public static GameEngine Instance => _instance ??= new GameEngine();

    public EntityManager EntityManager { get; } = new();
    public GameMap? CurrentMap { get; private set; }
    public Entity PlayerEntity { get; private set; }
    public MessageLog MessageLog { get; } = new();
    public DataLoader DataLoader { get; } = new();
    public GameState State { get; set; } = GameState.MainMenu;

    private MovementSystem? _movementSystem;
    private CombatSystem? _combatSystem;
    private AISystem? _aiSystem;
    private FovSystem? _fovSystem;
    private InventorySystem? _inventorySystem;

    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }

    private GameEngine() { }

    public void Initialize(int screenWidth, int screenHeight)
    {
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        DataLoader.Load();
        InitializeSystems();
    }

    private void InitializeSystems()
    {
        _movementSystem = new MovementSystem(EntityManager);
        _combatSystem = new CombatSystem(EntityManager, MessageLog);
        _aiSystem = new AISystem(EntityManager, MessageLog);
        _fovSystem = new FovSystem(EntityManager);
        _inventorySystem = new InventorySystem(EntityManager, MessageLog);
    }

    public void StartNewGame()
    {
        foreach (var e in EntityManager.AllEntities.ToList())
            EntityManager.DestroyEntity(e);

        var generator = new DungeonGenerator(200, 200);
        CurrentMap = generator.Generate();

        PlayerEntity = CreatePlayer(CurrentMap.StartPosition);
        SpawnEntities(CurrentMap);

        _fovSystem!.ComputeFov(CurrentMap, PlayerEntity, 8);

        State = GameState.Playing;
        MessageLog.Add("Welcome to RangedrifterClone! Use arrow keys or numpad to move.",
            SadRogue.Primitives.Color.Yellow);
    }

    private Entity CreatePlayer(SadRogue.Primitives.Point pos)
    {
        var player = EntityManager.CreateEntity();
        EntityManager.AddComponent(player, new PositionComponent { X = pos.X, Y = pos.Y });
        EntityManager.AddComponent(player, new RenderComponent
        {
            Glyph = '@',
            Foreground = SadRogue.Primitives.Color.Yellow,
            Background = SadRogue.Primitives.Color.Transparent,
            RenderLayer = 10
        });
        EntityManager.AddComponent(player, new FighterComponent
        {
            Hp = 12, MaxHp = 12,
            Defense = 1, Strength = 3,
            DamageDice = 1, DamageSides = 6, DamageBonus = 2
        });
        EntityManager.AddComponent(player, new PlayerInputComponent());
        EntityManager.AddComponent(player, new InventoryComponent());
        EntityManager.AddComponent(player, new NameComponent { Name = "Player" });
        EntityManager.AddComponent(player, new ExperienceComponent
            { Level = 1, Experience = 0, NextLevelExp = 25 });
        EntityManager.AddComponent(player, new StatusComponent());
        return player;
    }

    private void SpawnEntities(GameMap map)
    {
        var rng = new Random();
        var enemyDefs = DataLoader.EnemyDefinitions;
        foreach (var spawnPoint in map.EnemySpawnPoints)
        {
            if (enemyDefs.Count == 0) break;
            var def = enemyDefs[rng.Next(enemyDefs.Count)];
            var enemy = EntityManager.CreateEntity();
            EntityManager.AddComponent(enemy, new PositionComponent
                { X = spawnPoint.X, Y = spawnPoint.Y });
            EntityManager.AddComponent(enemy, new RenderComponent
            {
                Glyph = def.Glyph,
                Foreground = def.Color,
                Background = SadRogue.Primitives.Color.Transparent,
                RenderLayer = 5
            });
            EntityManager.AddComponent(enemy, new FighterComponent
            {
                Hp = def.MaxHp, MaxHp = def.MaxHp,
                Defense = def.Defense, Strength = def.Strength,
                DamageDice = def.DamageDice, DamageSides = def.DamageSides,
                DamageBonus = def.DamageBonus
            });
            EntityManager.AddComponent(enemy, new AIComponent { Behavior = def.AiBehavior });
            EntityManager.AddComponent(enemy, new NameComponent { Name = def.Name });
        }

        var itemDefs = DataLoader.ItemDefinitions;
        foreach (var itemSpawn in map.ItemSpawnPoints)
        {
            if (itemDefs.Count == 0) break;
            var itemDef = itemDefs[rng.Next(itemDefs.Count)];
            var item = EntityManager.CreateEntity();
            EntityManager.AddComponent(item, new PositionComponent
                { X = itemSpawn.X, Y = itemSpawn.Y });
            EntityManager.AddComponent(item, new RenderComponent
            {
                Glyph = itemDef.Glyph,
                Foreground = itemDef.Color,
                Background = SadRogue.Primitives.Color.Transparent,
                RenderLayer = 2
            });
            EntityManager.AddComponent(item, new ItemComponent
            {
                ItemId = itemDef.Id,
                Name = itemDef.Name,
                Category = itemDef.Category,
                Value = itemDef.Value
            });
            EntityManager.AddComponent(item, new NameComponent { Name = itemDef.Name });
        }
    }

    public void ProcessPlayerTurn(int dx, int dy)
    {
        if (State != GameState.Playing || CurrentMap == null) return;

        bool actionTaken = _movementSystem!.TryMovePlayer(
            PlayerEntity, dx, dy, CurrentMap, _combatSystem!, _inventorySystem!);

        if (actionTaken)
        {
            var fighter = EntityManager.GetComponent<FighterComponent>(PlayerEntity);
            if (fighter != null && fighter.Hp <= 0)
            {
                State = GameState.GameOver;
                MessageLog.Add("You have died! Press R to restart.", SadRogue.Primitives.Color.Red);
                return;
            }

            _fovSystem!.ComputeFov(CurrentMap, PlayerEntity, 8);
            _aiSystem!.ProcessTurns(CurrentMap, PlayerEntity, _combatSystem!, _movementSystem);

            var status = EntityManager.GetComponent<StatusComponent>(PlayerEntity);
            if (status != null) status.Turn++;
        }
    }

    public void ProcessAction(PlayerAction action)
    {
        if (State != GameState.Playing) return;
        switch (action)
        {
            case PlayerAction.PickUp:
                _inventorySystem!.TryPickUp(PlayerEntity, CurrentMap!);
                _fovSystem!.ComputeFov(CurrentMap!, PlayerEntity, 8);
                _aiSystem!.ProcessTurns(CurrentMap!, PlayerEntity, _combatSystem!, _movementSystem!);
                var s = EntityManager.GetComponent<StatusComponent>(PlayerEntity);
                if (s != null) s.Turn++;
                break;
            case PlayerAction.Wait:
                ProcessPlayerTurn(0, 0);
                break;
        }
    }
}

public enum GameState { MainMenu, Playing, Inventory, GameOver }
public enum PlayerAction { MoveNorth, MoveSouth, MoveEast, MoveWest,
    MoveNE, MoveNW, MoveSE, MoveSW, PickUp, Wait, OpenInventory, CloseInventory }
