using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Weather;
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
    private const string AllowedTile = "FloorAsteroidSandUnvariantized";
    private const int ClearRadius = 8;
    private const int RockMinDistance = 9;
    private const int RockMaxDistance = 16;
    private const int SpawnAttempts = 10000;

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

        // Never sample a hand-maintained world AABB. Wendover can be translated or
        // resized, while the grid tile list is the authoritative set of candidates.
        var candidates = new List<Vector2i>();
        var allTiles = _map.GetAllTilesEnumerator(gridUid, grid);
        while (allTiles.MoveNext(out var tileRef))
        {
            if (_tileDefs[tileRef.Tile.TypeId].ID == AllowedTile)
                candidates.Add(tileRef.Value);
        }

        var physicsQuery = GetEntityQuery<PhysicsComponent>();
        var weatherBlockQuery = GetEntityQuery<BlockWeatherComponent>();
        var attempts = Math.Min(SpawnAttempts, candidates.Count);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            // Partial Fisher-Yates selection: every candidate has equal odds, with
            // no repeated failures against the same tile.
            var index = _random.Next(candidates.Count);
            var tile = candidates[index];
            candidates[index] = candidates[^1];
            candidates.RemoveAt(candidates.Count - 1);

            if (!IsValidSite(gridUid, grid, tile, physicsQuery, weatherBlockQuery))
                continue;

            Spawn(ExtractorPrototype, _map.GridTileToLocal(gridUid, grid, tile));
            _log.Info($"Spawned the round's material extractor at {tile} on Wendover map {mapId}.");
            return;
        }

        _log.Warning($"No valid material extractor site found after checking {attempts} of {attempts + candidates.Count} Wendover sand tiles on map {mapId}.");
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

    private bool IsValidSite(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i center,
        EntityQuery<PhysicsComponent> physicsQuery,
        EntityQuery<BlockWeatherComponent> weatherBlockQuery)
    {
        if (!IsAllowedTile(gridUid, grid, center))
            return false;

        for (var x = -ClearRadius; x <= ClearRadius; x++)
        {
            for (var y = -ClearRadius; y <= ClearRadius; y++)
            {
                if (x * x + y * y > ClearRadius * ClearRadius)
                    continue;

                var tile = center + new Vector2i(x, y);
                // Only the extractor itself must be on sand/grass. The defensive ring may
                // cross ordinary walkable terrain; requiring 197 more exact sand tiles made
                // legitimate Wendover sites effectively impossible to find.
                if (!IsWalkableTile(gridUid, grid, tile)
                    || HasHardAnchoredEntity(gridUid, grid, tile, physicsQuery)
                    || HasWeatherBlocker(gridUid, grid, tile, weatherBlockQuery))
                    return false;
            }
        }

        return HasNearbySolidRock(gridUid, grid, center);
    }

    private bool HasWeatherBlocker(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        EntityQuery<BlockWeatherComponent> weatherBlockQuery)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var entity))
        {
            if (entity is { } anchoredUid && weatherBlockQuery.HasComponent(anchoredUid))
                return true;
        }

        return false;
    }

    private bool IsAllowedTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        return _tileDefs[tileRef.Tile.TypeId].ID == AllowedTile;
    }

    private bool IsWalkableTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        var id = _tileDefs[tileRef.Tile.TypeId].ID;
        return id is not "FloorWater"
            and not "FloorWaterEntity"
            and not "FloorSwamp"
            and not "FloorLava"
            and not "FloorLavaEntity";
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
            if (physicsQuery.TryGetComponent(entity, out var physics) && physics.CanCollide && physics.Hard)
                return true;
        }

        return false;
    }

    private bool HasNearbySolidRock(EntityUid gridUid, MapGridComponent grid, Vector2i center)
    {
        for (var x = -RockMaxDistance; x <= RockMaxDistance; x++)
        {
            for (var y = -RockMaxDistance; y <= RockMaxDistance; y++)
            {
                var distanceSquared = x * x + y * y;
                if (distanceSquared < RockMinDistance * RockMinDistance || distanceSquared > RockMaxDistance * RockMaxDistance)
                    continue;

                var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, center + new Vector2i(x, y));
                while (anchored.MoveNext(out var entity))
                {
                    if (entity is not { } anchoredUid)
                        continue;

                    var prototype = MetaData(anchoredUid).EntityPrototype;
                    if (prototype?.ID is "FloraRockSolid01" or "FloraRockSolid02" or "FloraRockSolid03")
                        return true;
                }
            }
        }

        return false;
    }
}
