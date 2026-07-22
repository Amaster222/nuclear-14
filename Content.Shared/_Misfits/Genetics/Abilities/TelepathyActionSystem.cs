// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;

namespace Content.Shared._Misfits.Genetics.Abilities;

public sealed partial class TelepathyActionSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TelepathyActionComponent, TelepathyActionEvent>(OnTelepathyPrompt);

        Subs.BuiEvents<TelepathyActionComponent>(TelepathyUiKey.Key, subs =>
        {
            subs.Event<TelepathyChosenMessage>(OnTelepathyChosen);
        });
    }

    private void OnTelepathyPrompt(Entity<TelepathyActionComponent> ent, ref TelepathyActionEvent args)
    {
        // for this specifically, prediction is fucked
        // but other predicted opens are fine (e.g. debug effect stick)
        // incomprehensible shitcode
        if (_net.IsClient)
            return;

        var user = args.Performer;
        var target = args.Target;
        ent.Comp.Target = target; // so it can be used later

        if (!_ui.TryOpenUi(ent.Owner, TelepathyUiKey.Key, user))
            Log.Error($"Failed to open UI for {ToPrettyString(ent)} of {ToPrettyString(user)}");

        // intentionally not handled, only start the cooldown after a message is sent
    }

    private void OnTelepathyChosen(Entity<TelepathyActionComponent> ent, ref TelepathyChosenMessage args)
    {
        var user = args.Actor;
        if (ent.Comp.Target is not {} target)
            return;

        ent.Comp.Target = null;

        var msg = args.Message.Trim();
        if (msg.Length > ent.Comp.MaxLength) // no malf
            return;

        // TODO: close it if the target leaves range

        // no prediction beyond here since client doesn't know other entities' ActorComponent
        if (_net.IsClient)
            return;

        var ident = Identity.Entity(target, EntityManager);
        if (!HasComp<MetaDataComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("MutationTelepathy-popup-mindless", ("target", ident)), user, user);
            return;
        }

        // start the delay now that a message is being sent
        _actions.StartUseDelay(ent.Owner);

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"{user:user} sent a telepathic message to {target:target}: {msg}");

        // TODO: handle mind magic protection with -popup-blocked
        Tell(user, msg);
        // TODO: send message for ghosts too
    }

    private void Tell(EntityUid user, string message)
    {
        _popup.PopupEntity(
            Loc.GetString("MutationTelepathy-message-wrap", ("message", FormattedMessage.EscapeText(message))),
            user,
            user);
    }
}
