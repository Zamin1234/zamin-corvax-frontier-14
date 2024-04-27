﻿using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Components.Debris;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Spawners;

namespace Content.Server.Worldgen.Systems;

/// <summary>
///     This handles loading in objects based on distance from player, using some metadata on chunks.
/// </summary>
public sealed class LocalityLoaderSystem : BaseWorldSystem
{
    [Dependency] private readonly TransformSystem _xformSys = default!;

    // Duration to reset the despawn timer to when a debris is loaded into a player's view.
    private const float DebrisActiveDuration = 300; // 5 минут Corvax.

    public override void Initialize()
    {
        SubscribeLocalEvent<SpaceDebrisComponent, TimedDespawnEvent>(OnDebrisDespawn);
    }

    /// <inheritdoc />
    public override void Update(float frameTime)
    {
        var e = EntityQueryEnumerator<LocalityLoaderComponent, TransformComponent>();
        var loadedQuery = GetEntityQuery<LoadedChunkComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();
        var controllerQuery = GetEntityQuery<WorldControllerComponent>();

        while (e.MoveNext(out var uid, out var loadable, out var xform))
        {
            if (!controllerQuery.TryGetComponent(xform.MapUid, out var controller))
            {
                RaiseLocalEvent(uid, new LocalStructureLoadedEvent());
                RemCompDeferred<LocalityLoaderComponent>(uid);
                continue;
            }

            var coords = GetChunkCoords(uid, xform);
            var done = false;
            for (var i = -1; i < 2 && !done; i++)
            {
                for (var j = -1; j < 2 && !done; j++)
                {
                    var chunk = GetOrCreateChunk(coords + (i, j), xform.MapUid!.Value, controller);
                    if (!loadedQuery.TryGetComponent(chunk, out var loaded) || loaded.Loaders is null)
                        continue;

                    foreach (var loader in loaded.Loaders)
                    {
                        if (!xformQuery.TryGetComponent(loader, out var loaderXform))
                            continue;

                        if ((_xformSys.GetWorldPosition(loaderXform) - _xformSys.GetWorldPosition(xform)).Length() > loadable.LoadingDistance)
                            continue;

                        // Reset the TimedDespawnComponent's lifetime when loaded
                        ResetTimedDespawn(uid);

                        RaiseLocalEvent(uid, new LocalStructureLoadedEvent());
                        RemCompDeferred<LocalityLoaderComponent>(uid);
                        done = true;
                        break;
                    }
                }
            }
        }
    }

    private void ResetTimedDespawn(EntityUid uid)
    {
        if (TryComp<TimedDespawnComponent>(uid, out var timedDespawn))
        {
            timedDespawn.Lifetime = DebrisActiveDuration;
        }
        else
        {
            // Add TimedDespawnComponent if it does not exist
            timedDespawn = AddComp<TimedDespawnComponent>(uid);
            timedDespawn.Lifetime = DebrisActiveDuration;
        }
    }

    private void OnDebrisDespawn(EntityUid entity, SpaceDebrisComponent component, TimedDespawnEvent e)
    {
        if (!EntityManager.TryGetComponent<TransformComponent>(entity, out var transform))
            return;

        var mobQuery = AllEntityQuery<HumanoidAppearanceComponent, MobStateComponent, TransformComponent>();

        while (mobQuery.MoveNext(out var mob, out _, out _, out var xform))
            if (xform.MapUid is not null && xform.GridUid == transform.GridUid)
                _xformSys.SetCoordinates(mob, new(xform.MapUid.Value, _xformSys.GetWorldPosition(xform)));
    }
}

/// <summary>
///     An event fired on a loadable entity when a local loader enters its vicinity.
/// </summary>
public record struct LocalStructureLoadedEvent;
