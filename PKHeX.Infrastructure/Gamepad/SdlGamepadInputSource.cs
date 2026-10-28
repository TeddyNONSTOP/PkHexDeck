using System.Diagnostics;
using System.Runtime.InteropServices;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;

namespace PKHeX.Infrastructure.Gamepad;

/// <summary>
/// Reads game controllers through the system's SDL2 library (present on SteamOS and most Linux
/// desktops; not bundled). Polls on a background thread at about 60 Hz and merges every connected
/// controller into one state, so the same press arriving from two devices still counts once.
/// </summary>
/// <remarks>
/// SDL's own Steam Deck driver is disabled: it talks to the controller hardware directly and can turn
/// off the Deck's trackpad mouse. The Deck is read through Steam's virtual Xbox pad instead, which is
/// what Steam provides in Game Mode.
/// </remarks>
public sealed class SdlGamepadInputSource : IGamepadInputSource
{
    private static readonly string[] LibraryNames = ["libSDL2-2.0.so.0", "libSDL2.so", "libSDL2-2.0.0.dylib", "SDL2.dll"];

    private const uint InitGameController = 0x2000 | 0x200; // SDL_INIT_GAMECONTROLLER | SDL_INIT_JOYSTICK
    private const short StickThreshold = 16000;
    private const short TriggerThreshold = 12000;

    // SDL_GameControllerButton values
    private const int ButtonA = 0, ButtonB = 1, ButtonX = 2, ButtonY = 3, ButtonBack = 4, ButtonStart = 6;
    private const int ButtonLeftShoulder = 9, ButtonRightShoulder = 10;
    private const int DpadUp = 11, DpadDown = 12, DpadLeft = 13, DpadRight = 14;
    // SDL_GameControllerAxis values
    private const int AxisLeftX = 0, AxisLeftY = 1, AxisRightY = 3, AxisTriggerLeft = 4, AxisTriggerRight = 5;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IndexBoolFn(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr OpenFn(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseFn(IntPtr controller);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AttachedFn(IntPtr controller);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte ButtonFn(IntPtr controller, int button);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate short AxisFn(IntPtr controller, int axis);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InstanceIdFn(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetHintFn([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    private sealed record Api(
        InitFn Init, VoidFn Quit, IntFn NumJoysticks, IndexBoolFn IsGameController, OpenFn Open, CloseFn Close,
        AttachedFn GetAttached, VoidFn Update, ButtonFn GetButton, AxisFn GetAxis, InstanceIdFn GetInstanceId, SetHintFn SetHint);

    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _stopRequested;

    public event Action<GamepadButtons>? Pressed;

    public bool IsRunning => _thread is { IsAlive: true };

    public bool TryStart()
    {
        lock (_gate)
        {
            if (IsRunning)
                return true;
            var api = LoadApi();
            if (api is null)
                return false;

            _stopRequested = false;
            _thread = new Thread(() => Run(api)) { IsBackground = true, Name = "Gamepad input" };
            _thread.Start();
            return true;
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            _stopRequested = true;
            _thread = null;
        }
        thread?.Join(TimeSpan.FromSeconds(1));
    }

    public void Dispose() => Stop();

    private void Run(Api api)
    {
        api.SetHint("SDL_JOYSTICK_HIDAPI_STEAMDECK", "0");
        api.SetHint("SDL_GAMECONTROLLER_ALLOW_STEAM_VIRTUAL_GAMEPAD", "1");
        api.SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        if (api.Init(InitGameController) != 0)
            return;

        var controllers = new Dictionary<int, IntPtr>();
        var repeater = new GamepadRepeater();
        var clock = Stopwatch.StartNew();
        var nextScan = TimeSpan.Zero;
        try
        {
            while (!_stopRequested)
            {
                api.Update();
                if (clock.Elapsed >= nextScan)
                {
                    ScanControllers(api, controllers);
                    nextScan = clock.Elapsed + TimeSpan.FromSeconds(1);
                }

                var state = GamepadButtons.None;
                foreach (var controller in controllers.Values)
                    state |= ReadState(api, controller);

                foreach (var press in repeater.Update(state, clock.Elapsed))
                {
                    try { Pressed?.Invoke(press); }
                    catch (Exception ex) { Trace.TraceWarning($"Gamepad handler failed: {ex.Message}"); }
                }
                Thread.Sleep(16);
            }
        }
        finally
        {
            foreach (var controller in controllers.Values)
                api.Close(controller);
            api.Quit();
        }
    }

    private static void ScanControllers(Api api, Dictionary<int, IntPtr> controllers)
    {
        foreach (var (id, controller) in controllers.ToArray())
        {
            if (api.GetAttached(controller) == 0)
            {
                api.Close(controller);
                controllers.Remove(id);
            }
        }

        var count = api.NumJoysticks();
        for (var i = 0; i < count; i++)
        {
            if (api.IsGameController(i) == 0)
                continue;
            var id = api.GetInstanceId(i);
            if (controllers.ContainsKey(id))
                continue;
            var controller = api.Open(i);
            if (controller != IntPtr.Zero)
                controllers[id] = controller;
        }
    }

    private static GamepadButtons ReadState(Api api, IntPtr c)
    {
        var state = GamepadButtons.None;
        if (api.GetButton(c, DpadUp) != 0 || api.GetAxis(c, AxisLeftY) < -StickThreshold) state |= GamepadButtons.Up;
        if (api.GetButton(c, DpadDown) != 0 || api.GetAxis(c, AxisLeftY) > StickThreshold) state |= GamepadButtons.Down;
        if (api.GetButton(c, DpadLeft) != 0 || api.GetAxis(c, AxisLeftX) < -StickThreshold) state |= GamepadButtons.Left;
        if (api.GetButton(c, DpadRight) != 0 || api.GetAxis(c, AxisLeftX) > StickThreshold) state |= GamepadButtons.Right;
        if (api.GetButton(c, ButtonA) != 0) state |= GamepadButtons.Accept;
        if (api.GetButton(c, ButtonB) != 0) state |= GamepadButtons.Back;
        if (api.GetButton(c, ButtonX) != 0) state |= GamepadButtons.Menu;
        if (api.GetButton(c, ButtonY) != 0) state |= GamepadButtons.Toggle;
        if (api.GetButton(c, ButtonLeftShoulder) != 0) state |= GamepadButtons.PreviousTab;
        if (api.GetButton(c, ButtonRightShoulder) != 0) state |= GamepadButtons.NextTab;
        if (api.GetAxis(c, AxisTriggerLeft) > TriggerThreshold) state |= GamepadButtons.Decrease;
        if (api.GetAxis(c, AxisTriggerRight) > TriggerThreshold) state |= GamepadButtons.Increase;
        if (api.GetAxis(c, AxisRightY) < -StickThreshold) state |= GamepadButtons.ScrollUp;
        if (api.GetAxis(c, AxisRightY) > StickThreshold) state |= GamepadButtons.ScrollDown;
        if (api.GetButton(c, ButtonStart) != 0) state |= GamepadButtons.MainMenu;
        if (api.GetButton(c, ButtonBack) != 0) state |= GamepadButtons.NextGroup;
        return state;
    }

    private static Api? LoadApi()
    {
        IntPtr lib = IntPtr.Zero;
        foreach (var name in LibraryNames)
        {
            if (NativeLibrary.TryLoad(name, out lib))
                break;
        }
        if (lib == IntPtr.Zero)
            return null;

        try
        {
            T Get<T>(string export) where T : Delegate
                => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, export));

            return new Api(
                Get<InitFn>("SDL_Init"), Get<VoidFn>("SDL_Quit"), Get<IntFn>("SDL_NumJoysticks"),
                Get<IndexBoolFn>("SDL_IsGameController"), Get<OpenFn>("SDL_GameControllerOpen"),
                Get<CloseFn>("SDL_GameControllerClose"), Get<AttachedFn>("SDL_GameControllerGetAttached"),
                Get<VoidFn>("SDL_GameControllerUpdate"), Get<ButtonFn>("SDL_GameControllerGetButton"),
                Get<AxisFn>("SDL_GameControllerGetAxis"), Get<InstanceIdFn>("SDL_JoystickGetDeviceInstanceID"),
                Get<SetHintFn>("SDL_SetHint"));
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }
}
