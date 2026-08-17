using System.Linq;
using System.Numerics;
using Content.Client.Examine;
using Content.Client.Hands.Systems;
using Content.Client.Interaction;
using Content.Client.Items.Systems;
using Content.Client.Storage;
using Content.Client.Storage.Systems;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Client.UserInterface.Systems.Info;
using Content.Client.UserInterface.Systems.Storage.Controls;
using Content.Client.Verbs.UI;
using Content.Shared.CCVar;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Robust.Client.Graphics.Clyde;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
// using Content.Shared.Crafting.Events; // #Misfits Remove: Stalker14 crafting system

namespace Content.Client.UserInterface.Systems.Storage;

public sealed class StorageUIController : UIController, IOnSystemChanged<StorageSystem>
{
    /*
     * Things are a bit over the shop but essentially
     * - Clicking into storagewindow is handled via storagewindow
     * - Clicking out of it is via ItemGridPiece
     * - Dragging around is handled here
     * - Drawing is handled via ItemGridPiece
     * - StorageSystem handles any sim stuff around open windows.
     */

    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly CloseRecentWindowUIController _closeRecentWindowUIController = default!;
    [UISystemDependency] private readonly StorageSystem _storage = default!;
    /// <summary>
    /// Cached positions for opening nested storage.
    /// </summary>
    private readonly Dictionary<EntityUid, Vector2> _reservedStorage = new();

    private readonly DragDropHelper<ItemGridPiece> _menuDragHelper;

    public ItemGridPiece? DraggingGhost => _menuDragHelper.Dragged;
    public Angle DraggingRotation = Angle.Zero;
    public bool StaticStorageUIEnabled;
    public bool OpaqueStorageWindow;
    private int _openStorageLimit = -1;

    public bool IsDragging => _menuDragHelper.IsDragging;
    public ItemGridPiece? CurrentlyDragging => _menuDragHelper.Dragged;

    public bool WindowTitle { get; private set; } = false;

    public StorageUIController()
    {
        _menuDragHelper = new DragDropHelper<ItemGridPiece>(OnMenuBeginDrag, OnMenuContinueDrag, OnMenuEndDrag);
    }

    public override void Initialize()
    {
        base.Initialize();

        UIManager.OnScreenChanged += OnScreenChange;
        _configuration.OnValueChanged(CCVars.StaticStorageUI, OnStaticStorageChanged, true);
        _configuration.OnValueChanged(CCVars.OpaqueStorageWindow, OnOpaqueWindowChanged, true);
        _configuration.OnValueChanged(CCVars.StorageWindowTitle, OnStorageWindowTitle, true);
        _configuration.OnValueChanged(CCVars.StorageLimit, OnStorageLimitChanged, true);
    }

    private void OnStorageLimitChanged(int obj)
    {
        _openStorageLimit = obj;
    }

    private void OnScreenChange((UIScreen? Old, UIScreen? New) obj)
    {
        // Handle reconnects with hotbargui.

        // Essentially HotbarGui / the screen gets loaded AFTER gamestates at the moment (because clientgameticker manually changes it via event)
        // and changing this may be a massive change.
        // So instead we'll just manually reload it for now.
        if (!StaticStorageUIEnabled ||
            obj.New == null ||
            !EntityManager.TryGetComponent(_player.LocalEntity, out UserInterfaceUserComponent? userComp))
        {
            return;
        }
        // TODO: fix this
        // UISystemDependency not injected at this point so do it the old fashion way, I love ordering issues.
        var uiSystem = EntityManager.System<SharedUserInterfaceSystem>();

        foreach (var bui in uiSystem.GetActorUis((_player.LocalEntity.Value, userComp)))
        {
            if (!uiSystem.TryGetOpenUi<StorageBoundUserInterface>(bui.Entity, StorageComponent.StorageUiKey.Key, out var storageBui))
                continue;

            storageBui.ReOpen();
        }
    }

    private void OnStorageWindowTitle(bool obj)
    {
        WindowTitle = obj;
    }

    private void OnOpaqueWindowChanged(bool obj)
    {
        OpaqueStorageWindow = obj;
    }

    private void OnStaticStorageChanged(bool obj)
    {
        StaticStorageUIEnabled = obj;
    }

    public StorageWindow CreateStorageWindow(EntityUid uid)
    {
        var window = new StorageWindow();
        window.MouseFilter = Control.MouseFilterMode.Pass;

        window.OnPiecePressed += (args, piece) =>
        {
            OnPiecePressed(args, window, piece);
        };
        window.OnPieceUnpressed += (args, piece) =>
        {
            OnPieceUnpressed(args, window, piece);
        };
        // #Misfits Remove: Stalker14 crafting system — craft button handler disabled
        // window.OnCraftButtonPressed += () =>
        // {
        //     if (window.StorageEntity is not { } storageEnt)
        //         return;
        //     EntityManager.RaisePredictiveEvent(new CraftStartedEvent(EntityManager.GetNetEntity(storageEnt)));
        // };
        if (StaticStorageUIEnabled)
        {
            var hotbar = UIManager.GetActiveUIWidgetOrNull<HotbarGui>();
            // this lambda handles the nested storage case
            // during nested storage, a parent window hides and a child window is
            // immediately inserted to the end of the list
            // we can reorder the newly inserted to the same index as the invisible
            // window in order to prevent an invisible window from being replaced
            // with a visible one in a different position
            Action<Control?, Control> reorder = (parent, child) =>
            {
                if (parent is null)
                    return;

                var parentChildren = parent.Children.ToList();
                var invisibleIndex = parentChildren.FindIndex(c => c.Visible == false);
                if (invisibleIndex == -1)
                    return;
                child.SetPositionInParent(invisibleIndex);
            };

            if (_openStorageLimit == 2)
            {
                if (hotbar?.LeftStorageContainer.Children.Count(c => c.Visible) == 0)
                {
                    hotbar?.LeftStorageContainer.AddChild(window);
                    reorder(hotbar?.LeftStorageContainer, window);
                }
                else
                {
                    hotbar?.RightStorageContainer.AddChild(window);
                    reorder(hotbar?.RightStorageContainer, window);
                }
            }
            else
            {
                hotbar?.SingleStorageContainer.AddChild(window);
                reorder(hotbar?.SingleStorageContainer, window);
            }

            _closeRecentWindowUIController.SetMostRecentlyInteractedWindow(window);
        }
        else
        {
            window.OpenCenteredLeft();

            if (_reservedStorage.Remove(uid, out var pos))
            {
                LayoutContainer.SetPosition(window, pos);
            }
        }

        return window;
    }

    public void OnSystemLoaded(StorageSystem system)
    {
        _input.FirstChanceOnKeyEvent += OnMiddleMouse;
    }

    public void OnSystemUnloaded(StorageSystem system)
    {
        _input.FirstChanceOnKeyEvent -= OnMiddleMouse;
    }
    // TODO: talk to emo and clean
    /// One might ask, Hey Emo, why are you parsing raw keyboard input just to rotate a rectangle?
    /// The answer is, that input bindings regarding mouse inputs are always intercepted by the UI,
    /// thus, if i want to be able to rotate my damn piece anywhere on the screen,
    /// I have to side-step all of the input handling. Cheers.
    private void OnMiddleMouse(KeyEventArgs keyEvent, KeyEventType type)
    {
        if (keyEvent.Handled)
            return;

        if (type != KeyEventType.Down)
            return;

        //todo there's gotta be a method for this in InputManager just expose it to content I BEG.
        if (!_input.TryGetKeyBinding(ContentKeyFunctions.RotateStoredItem, out var binding))
            return;
        if (binding.BaseKey != keyEvent.Key)
            return;

        if (keyEvent.Shift &&
            !(binding.Mod1 == Keyboard.Key.Shift ||
              binding.Mod2 == Keyboard.Key.Shift ||
              binding.Mod3 == Keyboard.Key.Shift))
            return;

        if (keyEvent.Alt &&
            !(binding.Mod1 == Keyboard.Key.Alt ||
              binding.Mod2 == Keyboard.Key.Alt ||
              binding.Mod3 == Keyboard.Key.Alt))
            return;

        if (keyEvent.Control &&
            !(binding.Mod1 == Keyboard.Key.Control ||
              binding.Mod2 == Keyboard.Key.Control ||
              binding.Mod3 == Keyboard.Key.Control))
            return;

        if (!IsDragging && EntityManager.System<HandsSystem>().GetActiveHandEntity() == null)
            return;

        //clamp it to a cardinal.
        DraggingRotation = (DraggingRotation + Math.PI / 2f).GetCardinalDir().ToAngle();
        if (DraggingGhost != null)
            DraggingGhost.InsertLoc.Rotation = DraggingRotation;

        if (IsDragging || UIManager.CurrentlyHovered is StorageWindow)
            keyEvent.Handle();
    }
    // TODO: clean
    private void OnPiecePressed(GUIBoundKeyEventArgs args, StorageWindow window, ItemGridPiece control)
    {
        if (IsDragging || !window.IsOpen)
            return;

        if (args.Function == ContentKeyFunctions.MoveStoredItem)
        {
            DraggingRotation = control.InsertLoc.Rotation;
            _menuDragHelper.MouseDown(control);
            _menuDragHelper.Update(0f);

            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.SaveItemLocation)
        {
            if (window.StorageEntity is not { } storage)
                return;

            EntityManager.RaisePredictiveEvent(new StorageSaveItemLocationEvent(
                EntityManager.GetNetEntity(control.Entity),
                EntityManager.GetNetEntity(storage)));
            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.ExamineEntity)
        {
            EntityManager.System<ExamineSystem>().DoExamine(control.Entity);
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UseSecondary)
        {
            UIManager.GetUIController<VerbMenuUIController>().OpenVerbMenu(control.Entity);
            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.ActivateItemInWorld)
        {
            EntityManager.RaisePredictiveEvent(
                new InteractInventorySlotEvent(EntityManager.GetNetEntity(control.Entity), altInteract: false));
            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.AltActivateItemInWorld)
        {
            EntityManager.RaisePredictiveEvent(new InteractInventorySlotEvent(EntityManager.GetNetEntity(control.Entity), altInteract: true));
            args.Handle();
        }

        window.FlagDirty();
    }


    private void OnPieceUnpressed(GUIBoundKeyEventArgs args, StorageWindow window, ItemGridPiece control)
    {
        if (args.Function != ContentKeyFunctions.MoveStoredItem)
            return;

        // Want to get the control under the dragged control.
        // This means we can drag the original control around (and not hide the original).
        control.MouseFilter = Control.MouseFilterMode.Ignore;
        var targetControl = UIManager.MouseGetControl(args.PointerLocation);
        var targetStorage = targetControl as StorageWindow;
        control.MouseFilter = Control.MouseFilterMode.Pass;

        var localPlayer = _player.LocalEntity;
        window.RemoveGrid(control);
        window.FlagDirty();

        // If we tried to drag it on top of another grid piece then cancel out.
        if (targetControl is ItemGridPiece || window.StorageEntity is not { } sourceStorage
        || localPlayer == null)
        {
            window.Reclaim(control.InsertLoc, control);
            args.Handle();
            _menuDragHelper.EndDrag();
            return;
        }

        if (_menuDragHelper.IsDragging && DraggingGhost is { } draggingGhost)
        {
            // Misfit Fix and Change: reworked UI dragging a little to not throw execptions
            //                        by checking if dragged item grid coords doesnt result
            //                        in calc'd storage grid to be out of index
            //
            //                        Items dragged outside of any storage now
            //                        drop items by default(redid StorageInteractWithItemEvent a little)
            DragStuff(targetStorage, window, draggingGhost, control);
        }
        // If we just clicked, then take it out of the bag.
        else
        {
            EntityManager.RaisePredictiveEvent(new StorageInteractWithItemEvent(
                EntityManager.GetNetEntity(control.Entity),
                EntityManager.GetNetEntity(sourceStorage)));
        }
        DraggingRotation = Angle.Zero;
        _menuDragHelper.EndDrag();
        args.Handle();
    }

    /// Misfit: mostly follows what orignal did but with less if nesting
    /// I dont plan on touching UI code any more than this.
    /// modifications still couple with already existing system
    /// so there SHOULD not be much volatility
    private void DragStuff(StorageWindow? targetStorage, StorageWindow window,
                            ItemGridPiece draggingGhost, ItemGridPiece control)
    {

        var dragEnt = draggingGhost.Entity;

        if (targetStorage?.StorageEntity == null)
        {
            EntityManager.RaisePredictiveEvent(new StorageTransferItemEvent(
            EntityManager.GetNetEntity(dragEnt),
            EntityManager.GetNetEntity(window.StorageEntity!.Value),
            new ItemStorageLocation(Angle.Zero, new Vector2i(-100, -100))));
            return;
        }
        //TODO: finish up implementing this math. I am leaving this math in lol.
        // if someone can suggest be a FREE app to do this stuff
        // on PC or even mobile pls

        // bottomLeft = (b1,b2) as new origin
        // (bxn,byn) being n points done wrt to b1,b2(we pretending b1,b2 is origin)
        //[x0...xn]   [cos,sin,b1] [bx0,...bxn]
        //[y0...yn] = [-sin,cos,b2][by0,...byn]
        //[1.....1]   [0,0,1]      [1........1]
        // tho need ez way to turn x,y points to bx,by points first
        // [bx0..]   [1,0,xb][xb0..]   map origin x,y wrt to b to translate
        // [by0..] = [0,1,yb][yb0..]
        // [1....]   [0,0,1] [1....]
        // so by sub
        // [x0..]   [cos,sin,b1]  [1,0,xb][xb0..]
        // [y0..] = [-sin,cos,b2] [0,1,yb][yb0..]
        // [1....]  [0,0,1]       [0,0,1] [1....]
        //
        // (final x,y)[x0..]   [cos,sin,B1]   [xb0..] (Unaltered x,y)
        //            [y0..] = [-sin,cos,B2]  [yb0..]
        //            [1...]   [0,0,1]        [1....]
        //
        // [cos()xb+sin()yb+b1] = B1
        // [-sin()xb+cos()yb+b2]= B2
        //
        var posFloat = targetStorage.MouseToGridFloat();
        posFloat -= draggingGhost.BoundingBox.Center.Floored();

        var posGrid = posFloat.Floored();
        var newLocation = new ItemStorageLocation(DraggingRotation, posGrid);

        var gridMax = targetStorage.ControlGridCount() - 1;
        var columns = targetStorage.GridColumnsNum();

        var shapes = EntityManager.System<ItemSystem>().GetAdjustedItemShape(dragEnt, DraggingRotation, posGrid);
        var bawx = shapes.GetBoundingBox();
        var guh = bawx.Center;

        if (InBounds(bawx, gridMax, columns) && !NoItemOverlap(shapes, columns, targetStorage).Any(x => x == false))
        {
            targetStorage.Reclaim(newLocation, control);
            targetStorage.FlagDirty();
            EntityManager.RaisePredictiveEvent(new StorageTransferItemEvent(
            EntityManager.GetNetEntity(dragEnt),
            EntityManager.GetNetEntity(targetStorage.StorageEntity.Value),
            newLocation));
            return;
        }
        window.Reclaim(control.InsertLoc, control);
        window.FlagDirty();
    }
    private static bool InBounds(Box2i box, int gridMax, int gridCol)
    {
        var isNeg = ((box.Left | box.Bottom | box.Right | box.Top) & 0x80_00_00_0) != 0x0;

        var vMin = box.BottomLeft.X + box.BottomLeft.Y * gridCol;
        var vMax = box.TopRight.X + box.TopRight.Y * gridCol;
        return !isNeg && vMin >= 0 && vMax <= gridMax;
    }
    // TODO: due for a rewrite
    /// <summary>
    ///
    /// </summary>

    private static IEnumerable<bool> NoItemOverlap(IReadOnlyList<Box2i> shapes, int gridCol, StorageWindow win)
    {

        foreach (Box2i box in shapes)
        {
            var min = box.Left;
            var max = box.Right;
            var row = box.Top;
            var rowMin = box.Bottom;
            while (row >= rowMin)
            {
                var spaces = win.GetControlSlice(min + gridCol * row, max + gridCol * row);
                if (spaces.Any(space => space.ChildCount > 0))
                {
                    yield return false;
                }
                row--;
            }
        }
        yield return true;
    }
    private bool OnMenuBeginDrag()
    {
        if (_menuDragHelper.Dragged is not { } dragged)
            return false;

        DraggingGhost!.Orphan();
        DraggingRotation = dragged.InsertLoc.Rotation;

        UIManager.PopupRoot.AddChild(DraggingGhost);
        SetDraggingRotation();
        return true;
    }

    private bool OnMenuContinueDrag(float frameTime)
    {
        if (DraggingGhost == null)
            return false;

        SetDraggingRotation();
        return true;
    }

    private void SetDraggingRotation()
    {
        if (DraggingGhost == null)
            return;

        var offset = ItemGridPiece.GetCenterOffset(
            (DraggingGhost.Entity, null),
            new ItemStorageLocation(DraggingRotation, Vector2i.Zero),
            EntityManager);

        // I don't know why it divides the position by 2. Hope this helps! -emo
        LayoutContainer.SetPosition(DraggingGhost, UIManager.MousePositionScaled.Position / 2 - offset);
    }

    private void OnMenuEndDrag()
    {
        if (DraggingGhost == null)
            return;

        DraggingRotation = Angle.Zero;
    }


    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _menuDragHelper.Update(args.DeltaSeconds);
    }
}
