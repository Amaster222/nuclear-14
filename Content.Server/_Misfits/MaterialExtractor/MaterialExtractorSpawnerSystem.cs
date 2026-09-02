using System.Numerics;
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
    private const int SpawnAttempts = 2000;

    // Current rendered Wendover surface bounds. These are a sampling window only;
    // every candidate is subsequently validated against the actual loaded grid.
    private static readonly Box2 WendoverBounds = new(-517, -328, 484, 311);

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

        var physicsQuery = GetEntityQuery<PhysicsComponent>();
        var weatherBlockQuery = GetEntityQuery<BlockWeatherComponent>();
        for (var attempt = 0; attempt < SpawnAttempts; attempt++)
        {
            var worldPosition = new Vector2(
                _random.NextFloat(WendoverBounds.Left, WendoverBounds.Right),
                _random.NextFloat(WendoverBounds.Bottom, WendoverBounds.Top));
            var tile = _map.WorldToTile(gridUid, grid, worldPosition);

            if (!IsValidSite(gridUid, grid, tile, physicsQuery, weatherBlockQuery))
                continue;

            Spawn(ExtractorPrototype, _map.GridTileToLocal(gridUid, grid, tile));
            _log.Info($"Spawned the round's material extractor at {tile} on Wendover map {mapId}.");
            return;
        }

        _log.Warning($"No valid material extractor site found after {SpawnAttempts} attempts on Wendover map {mapId}.");
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
                if (!IsAllowedTile(gridUid, grid, tile)
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
