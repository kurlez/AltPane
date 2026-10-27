using System.Runtime.InteropServices;
using System.Windows.Threading;
using AltPane.Core.Input;
using AltPane.Core.Models;

namespace AltPane;

internal sealed class HotkeyService : IDisposable
{
    readonly SwitcherPresenter _presenter;
    readonly Func<AppSettings> _settings;
    readonly Func<bool> _recording;
    readonly Dispatcher _dispatcher;
    readonly LowLevelKeyboardProc _proc;
    readonly object _gate = new();
    readonly Queue<UiOp> _ops = new();
    readonly HashSet<int> _pressed = new();
    GestureSnapshot _snap = GestureSnapshot.Idle;
    WinGuardState _win = WinGuardState.Idle;
    nint _hook;
    int _injectDepth;
    bool _drainQueued;
    bool _inDrain;

    public HotkeyService(SwitcherPresenter presenter, Func<AppSettings> settings, Func<bool> recording, Dispatcher dispatcher)
    {
        _presenter = presenter;
        _settings = settings;
        _recording = recording;
        _dispatcher = dispatcher;
        _proc = OnHook;
    }

    public bool IsInstalled => _hook != 0;

    public bool Install()
    {
        if (_hook != 0) return true;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == 0)
        {
            AppLog.Error("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
            return false;
        }

        AppLog.Info("Keyboard hook installed.");
        return true;
    }

    public void Uninstall()
    {
        if (_hook == 0) return;
        if (!NativeMethods.UnhookWindowsHookEx(_hook))
            AppLog.Error("UnhookWindowsHookEx failed: " + Marshal.GetLastWin32Error());
        else
            AppLog.Info("Keyboard hook removed.");
        _hook = 0;
        _pressed.Clear();
        _win = WinGuardState.Idle;
    }

    public void Dispose() => Uninstall();

    public void SyncQueryLength(int length)
    {
        lock (_gate)
            _snap = _snap with { QueryLength = Math.Max(0, length) };
    }

    public void RequestOpenFromTray() => Enqueue(GestureTransitions.OpenSticky);

    public void RequestSearchMode() => Enqueue(state =>
        GestureTransitions.Apply(state, HookResult.EnterSearch(), null, PhysicallyDown(state.LatchedTrigger), ModifierKeys.None));

    public void RequestCommit(int moveChoice = -1) => Enqueue(state =>
        GestureTransitions.Apply(state, HookResult.Commit(moveChoice), null, false, ModifierKeys.None));

    public void RequestCancel() => Enqueue(state =>
        GestureTransitions.Apply(state, HookResult.Cancel(), null, PhysicallyDown(state.LatchedTrigger), ModifierKeys.None));

    public void RequestClearQuery() => Enqueue(state =>
        GestureTransitions.Apply(state, HookResult.ClearQuery(), null, PhysicallyDown(state.LatchedTrigger), ModifierKeys.None));

    void Enqueue(Func<GestureSnapshot, TransitionResult> transition)
    {
        lock (_gate)
        {
            var result = transition(_snap);
            _snap = result.Next;
            if (result.Op is not null)
                _ops.Enqueue(result.Op);
        }
        Drain();
    }

    public void WhileInjecting(Action action)
    {
        _injectDepth++;
        try { action(); }
        finally { _injectDepth--; }
    }

    nint OnHook(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0 || _injectDepth > 0)
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if (data.dwExtraInfo == ModifierRelease.Magic)
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

        var vk = (int)data.vkCode;
        var isUp = (data.flags & 0x80) != 0;
        var isDown = !isUp;
        var repeat = false;
        if (isDown) repeat = !_pressed.Add(vk);
        else _pressed.Remove(vk);

        var settings = _settings();
        var hotkey = settings.Hotkey ?? HotkeyChord.AltTab();
        var mods = ReadModifiers();
        var visible = false;
        var queryLen = 0;
        var searchVisible = false;
        var ime = false;
        try
        {
            visible = _presenter.IsSwitcherVisible;
            if (vk == VirtualKeys.Escape)
            {
                queryLen = _presenter.VisibleQueryLength;
                searchVisible = _presenter.VisibleSearchMode;
                ime = _presenter.EscapeBelongsToIme();
            }
            else
            {
                ime = _presenter.IsImeComposing;
            }
        }
        catch (Exception ex)
        {
            if (vk == VirtualKeys.Escape)
                AppLog.Error(ex);
        }

        GestureSnapshot before;
        var snapshotWasOpen = false;
        var repaired = false;
        lock (_gate)
        {
            before = _snap;
            snapshotWasOpen = before.SwitcherOpen;
            if (vk == VirtualKeys.Escape && visible && !before.SwitcherOpen)
            {
                var repairedState = EscVisibility.Repair(false, true, searchVisible, queryLen);
                _snap = before with
                {
                    SwitcherOpen = repairedState.SwitcherOpen,
                    SearchMode = repairedState.SearchMode,
                    QueryLength = repairedState.QueryLength,
                    GestureLatched = false
                };
                before = _snap;
                repaired = true;
            }
        }

        var input = before.ToHookInput(
            hotkey,
            settings.CaptureWinTab,
            settings.OverridePaused,
            _recording(),
            vk,
            isDown,
            repeat,
            mods,
            settings.StickySwitcher,
            settings.QuickTapSwitches,
            visible) with
        {
            ImeComposing = ime
        };
        var result = HookInterpreter.Interpret(input);
        if (vk == VirtualKeys.Escape && (snapshotWasOpen || visible))
        {
            AppLog.Info(
                $"Esc {(isDown ? "down" : "up")} repeat={repeat} alt={mods.Alt} ctrl={mods.Ctrl} shift={mods.Shift} win={mods.Win} capture={settings.CaptureWinTab} snapshot={snapshotWasOpen} visible={visible} repaired={repaired} open={input.SwitcherOpen} search={input.SearchMode} query={input.HasSearchQuery} ime={input.ImeComposing} -> {result.Command}");
        }
        else if (AppLog.Verbose && vk is VirtualKeys.Tab or VirtualKeys.LWin or VirtualKeys.RWin)
        {
            AppLog.Info(
                $"hook vk=0x{vk:X2} {(isDown ? "down" : "up")} repeat={repeat} win={mods.Win} alt={mods.Alt} ctrl={mods.Ctrl} shift={mods.Shift} capture={settings.CaptureWinTab} open={input.SwitcherOpen} search={input.SearchMode} query={input.HasSearchQuery} ime={input.ImeComposing} -> {result.Command}");
        }
        var kind = HotkeyLogic.Match(hotkey, settings.CaptureWinTab, vk, mods);
        var consumedWinChord = isDown && !repeat && kind == ChordKind.WinTab && result.Command == HookCommand.Cycle;
        var win = WinChordGuard.OnKey(_win, vk, isDown, consumedWinChord);
        _win = win.Next;
        if (win.InjectDummy)
            WhileInjecting(ModifierRelease.MarkWinChordUsed);
        if (result.Command == HookCommand.Pass)
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

        string? typed = null;
        if (result.Command == HookCommand.Type)
        {
            typed = Translate(data.vkCode, data.scanCode);
            if (string.IsNullOrEmpty(typed))
                return 1;
        }

        var activation = HotkeyLogic.TriggerFor(kind, hotkey);
        var stillDown = PhysicallyDown(before.LatchedTrigger != ModifierKeys.None ? before.LatchedTrigger : activation);
        lock (_gate)
        {
            var transition = GestureTransitions.Apply(_snap, result, typed, stillDown, activation);
            _snap = transition.Next;
            if (transition.Op is not null)
                _ops.Enqueue(transition.Op);
        }

        RequestDrain();
        return 1;
    }

    void RequestDrain()
    {
        if (_drainQueued) return;
        _drainQueued = true;
        _dispatcher.BeginInvoke(DrainPosted, DispatcherPriority.Input);
    }

    void DrainPosted()
    {
        _drainQueued = false;
        Drain();
    }

    void Drain()
    {
        if (_inDrain) return;
        _inDrain = true;
        try
        {
            while (true)
            {
                List<UiOp> batch;
                lock (_gate)
                {
                    if (_ops.Count == 0) break;
                    batch = new List<UiOp>(_ops);
                    _ops.Clear();
                }

                foreach (var op in batch)
                {
                    ReleaseFor(op);
                    _presenter.Apply(op);
                }

                GestureSnapshot snap;
                bool more;
                lock (_gate)
                {
                    snap = _snap;
                    more = _ops.Count > 0;
                }

                var settings = _settings();
                if (GestureTransitions.AllowsInstantTap(settings.StickySwitcher, settings.QuickTapSwitches)
                    && GestureTransitions.ShouldQuickCommit(snap, TriggerIsDown(snap.LatchedTrigger), _presenter.PendingShow, more))
                {
                    var trigger = snap.LatchedTrigger;
                    var selected = _presenter.TakeForQuickCommit();
                    lock (_gate)
                        _snap = GestureSnapshot.Idle with { PendingTriggerRelease = trigger };
                    if (trigger != ModifierKeys.None)
                        WhileInjecting(() => ModifierRelease.Release(trigger));
                    if (selected is not null)
                        _presenter.ActivateEntry(selected);
                }
                else
                {
                    _presenter.FinishFrame(snap);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        finally
        {
            _inDrain = false;
        }
    }

    void ReleaseFor(UiOp op)
    {
        var trigger = op switch
        {
            UiOp.Commit commit => commit.Trigger,
            UiOp.Cancel cancel => cancel.Trigger,
            UiOp.EnterSearch search => search.Trigger,
            UiOp.ReleaseHold hold => hold.Trigger,
            _ => ModifierKeys.None
        };
        if (trigger != ModifierKeys.None)
            WhileInjecting(() => ModifierRelease.Release(trigger));
    }

    static string? Translate(uint vk, uint scan)
    {
        var state = new byte[256];
        if (!NativeMethods.GetKeyboardState(state)) return null;
        state[VirtualKeys.Menu] = 0;
        state[VirtualKeys.LMenu] = 0;
        state[VirtualKeys.RMenu] = 0;
        var buffer = new char[8];
        var count = NativeMethods.ToUnicode(vk, scan, state, buffer, buffer.Length, 0);
        if (count <= 0) return null;
        if (char.IsControl(buffer[0])) return null;
        return new string(buffer, 0, count);
    }

    static ModifierState ReadModifiers() => new(
        Down(VirtualKeys.Menu) || Down(VirtualKeys.LMenu) || Down(VirtualKeys.RMenu),
        Down(VirtualKeys.Control) || Down(VirtualKeys.LControl) || Down(VirtualKeys.RControl),
        Down(VirtualKeys.Shift) || Down(VirtualKeys.LShift) || Down(VirtualKeys.RShift),
        Down(VirtualKeys.LWin) || Down(VirtualKeys.RWin));

    static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    static bool PhysicallyDown(ModifierKeys trigger) => TriggerIsDown(trigger);

    static bool TriggerIsDown(ModifierKeys trigger) => trigger switch
    {
        ModifierKeys.Alt => Down(VirtualKeys.Menu) || Down(VirtualKeys.LMenu) || Down(VirtualKeys.RMenu),
        ModifierKeys.Control => Down(VirtualKeys.Control) || Down(VirtualKeys.LControl) || Down(VirtualKeys.RControl),
        ModifierKeys.Shift => Down(VirtualKeys.Shift) || Down(VirtualKeys.LShift) || Down(VirtualKeys.RShift),
        ModifierKeys.Win => Down(VirtualKeys.LWin) || Down(VirtualKeys.RWin),
        _ => false
    };
}
