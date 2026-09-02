using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Misfits.MaterialExtractor;

/// <summary>
/// Creates the single round-scoped Seismic Material Extractor on Wendover.
/// Placement is deliberately data-independent: it examines the loaded grid rather
/// than relying on a serialized map marker.
/// </summary>
public sealed partial class MaterialExtractorSpawnerSystem : EntitySystem
{
    private const string WendoverGameMap = "Wendover";
    private const string ExtractorPrototype = "N14SeismicMaterialExtractor";
    // Wendover's visual salt flats are FloorDesert. The asteroid-sand tile only
    // appears in a few tiny decorative patches and cannot host an open worksite.
    private static readonly HashSet<string> AllowedTiles =
    [
        "FloorDesert",
        "ForgeFloorWastelandDesert",
        "ForgeFloorWastelandDesertVariative",
        "FloorAsteroidSandUnvariantized",
        "FloorAsteroidIronsand",
        "FloorAsteroidIronsandUnvariantized",
    ];
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    private readonly HashSet<MapId> _wendoverMaps = [];
    private ISawmill _log = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _log = Logger.GetSawmill("material_extractor");
    }

    private void OnPostGameMapLoad(PostGameMapLoad ev)
    {
        if (ev.GameMap.ID == WendoverGameMap)
            _wendoverMaps.Add(ev.Map);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _wendoverMaps.Clear();
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        foreach (var mapId in _wendoverMaps)
            TrySpawnForMap(mapId);
    }

    private void TrySpawnForMap(MapId mapId)
    {
        if (!TryGetWendoverGrid(mapId, out var gridUid, out var grid))
        {
            _log.Warning($"Unable to find a grid on Wendover map {mapId}; skipping material extractor.");
            return;
        }

        // The geology is the landmark. Pick an actual solid boulder, then place
        // the machine on a free adjacent salt-flat tile. Do not impose a fake
        // eight-tile arena requirement on the hand-authored wasteland.
        var rockCandidates = new List<Vector2i>();
        var transforms = EntityQueryEnumerator<TransformComponent>();
        while (transforms.MoveNext(out var uid, out var xform))
        {
            if (xform.GridUid != gridUid || MetaData(uid).EntityPrototype?.ID is not ("FloraRockSolid01" or "FloraRockSolid02" or "FloraRockSolid03"))
                continue;

            rockCandidates.Add(_map.CoordinatesToTile(gridUid, grid, xform.Coordinates));
        }

        var physicsQuery = GetEntityQuery<PhysicsComponent>();
        var attempts = rockCandidates.Count;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var index = _random.Next(rockCandidates.Count);
            var rockTile = rockCandidates[index];
            rockCandidates[index] = rockCandidates[^1];
            rockCandidates.RemoveAt(rockCandidates.Count - 1);

            if (!TryFindSpawnTileByRock(gridUid, grid, rockTile, physicsQuery, out var tile))
                continue;

            Spawn(ExtractorPrototype, _map.GridTileToLocal(gridUid, grid, tile));
            _log.Info($"Spawned the round's material extractor at {tile} beside boulder {rockTile} on Wendover map {mapId}.");
            return;
        }

        _log.Warning($"No free salt-flat tile was found beside any of the {attempts} solid boulders on Wendover map {mapId}.");
    }

    private bool TryFindSpawnTileByRock(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i rockTile,
        EntityQuery<PhysicsComponent> physicsQuery,
        out Vector2i spawnTile)
    {
        // Randomize the nearest eight tiles so the same boulder is not always used
        // from the same side, while remaining visibly tied to the rock deposit.
        var offsets = new List<Vector2i>
        {
            new(-1, -1), new(0, -1), new(1, -1), new(-1, 0),
            new(1, 0), new(-1, 1), new(0, 1), new(1, 1),
        };

        while (offsets.Count > 0)
        {
            var index = _random.Next(offsets.Count);
            var offset = offsets[index];
            offsets[index] = offsets[^1];
            offsets.RemoveAt(offsets.Count - 1);

            var candidate = rockTile + offset;
            if (!IsAllowedTile(gridUid, grid, candidate) || HasHardAnchoredEntity(gridUid, grid, candidate, physicsQuery))
                continue;

            spawnTile = candidate;
            return true;
        }

        spawnTile = default;
        return false;
    }

    private bool TryGetWendoverGrid(MapId mapId, out EntityUid gridUid, out MapGridComponent grid)
    {
        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var candidate, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            gridUid = uid;
            grid = candidate;
            return true;
        }

        gridUid = default;
        grid = default!;
        return false;
    }

    private bool IsAllowedTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        return AllowedTiles.Contains(_tileDefs[tileRef.Tile.TypeId].ID);
    }

    private bool HasHardAnchoredEntity(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        EntityQuery<PhysicsComponent> physicsQuery)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var entity))
        {
            if (entity is { } anchoredUid
                && physicsQuery.TryGetComponent(anchoredUid, out var physics)
                && physics.CanCollide
                && physics.Hard)
                return true;
        }

        return false;
    }

}
