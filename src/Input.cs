using System.Diagnostics;
using System.Runtime.InteropServices;

namespace G815Studio;

public readonly record struct KeyHookEvent(int Vk, int Scan, bool Extended, bool Down, bool Injected, bool OwnInjected);

/// <summary>Globalni low-level hook tipkovnice (za reaktivne efekte i snimanje makroa). Mora živjeti na UI niti.</summary>
public sealed class KeyboardHook : IDisposable
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;

    public event Action<KeyHookEvent> KeyEvent;
    public bool Installed => _hook != IntPtr.Zero;

    public KeyboardHook()
    {
        _proc = HookProc; // drži referencu da je GC ne pokupi
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Log.Write("Hook tipkovnice nije postavljen: " + Marshal.GetLastWin32Error());
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            bool down = msg == 0x100 || msg == 0x104;
            bool up = msg == 0x101 || msg == 0x105;
            if (down || up)
            {
                var d = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var e = new KeyHookEvent((int)d.vkCode, (int)d.scanCode, (d.flags & 0x01) != 0, down,
                    (d.flags & 0x10) != 0, d.dwExtraInfo == NativeInput.Magic);
                try { KeyEvent?.Invoke(e); } catch (Exception ex) { Log.Write("Hook: " + ex.Message); }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}

internal static class NativeInput
{
    public static readonly IntPtr Magic = new(0x0815_5715);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x01;
    private const uint KEYEVENTF_KEYUP = 0x02;
    private const uint KEYEVENTF_UNICODE = 0x04;
    private const uint KEYEVENTF_SCANCODE = 0x08;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    private static readonly HashSet<int> ExtendedVks = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2C, 0x2D, 0x2E, // PgUp..Down, PrtSc, Ins, Del
        0x5B, 0x5C, 0x5D, 0x6F, 0x90, 0xA3, 0xA5                          // Win, Apps, Num/, NumLock, RCtrl, RAlt
    };

    public static bool IsExtendedVk(int vk) => ExtendedVks.Contains(vk);

    public static int ScanFromVk(int vk) => (int)MapVirtualKey((uint)vk, 0 /* MAPVK_VK_TO_VSC */);

    private static void Send(KEYBDINPUT ki)
    {
        ki.dwExtraInfo = Magic;
        var input = new[] { new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = ki } } };
        SendInput(1, input, Marshal.SizeOf<INPUT>());
    }

    public static void Key(int vk, int scan, bool extended, bool up)
    {
        if (scan == 0) scan = ScanFromVk(vk);
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (scan != 0) flags |= KEYEVENTF_SCANCODE;
        Send(new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)scan, dwFlags = flags });
    }

    public static void Text(string text)
    {
        foreach (char ch in text ?? "")
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                Key(0x0D, 0, false, false);
                Key(0x0D, 0, false, true);
                continue;
            }
            Send(new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE });
            Send(new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP });
        }
    }
}

/// <summary>Izvodi makroe na pozadinskim nitima; svaka G tipka ima svoj "utor".</summary>
public sealed class MacroPlayer
{
    private readonly Dictionary<int, CancellationTokenSource> _running = new();
    private readonly object _lock = new();

    public event Action<string> Error;
    public event Action<string> Info;

    /// <summary>Izvršava funkciju na UI niti i vraća rezultat (postavlja AppController).</summary>
    public Func<Func<string>, string> Ui { get; set; }

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public bool IsRunning(int slot)
    {
        lock (_lock) return _running.ContainsKey(slot);
    }

    public void OnKeyDown(int slot, Macro m)
    {
        if (m == null || m.Steps == null || m.Steps.Count == 0) return;
        lock (_lock)
        {
            if (_running.TryGetValue(slot, out var existing))
            {
                if (m.Mode == PlayMode.Toggle)
                {
                    existing.Cancel();
                    _running.Remove(slot);
                }
                return;
            }
            var cts = new CancellationTokenSource();
            _running[slot] = cts;
            var steps = m.Steps.ToList();
            bool loop = m.Mode != PlayMode.Once;
            var thread = new Thread(() => Run(slot, steps, loop, cts)) { IsBackground = true, Name = "Makro G" + slot };
            thread.Start();
        }
    }

    public void OnKeyUp(int slot, Macro m)
    {
        if (m == null || m.Mode != PlayMode.RepeatWhileHeld) return;
        lock (_lock)
        {
            if (_running.TryGetValue(slot, out var cts))
            {
                cts.Cancel();
                _running.Remove(slot);
            }
        }
    }

    public void Play(Macro m) => OnKeyDown(-1 - Environment.TickCount % 1000, new Macro { Steps = m.Steps, Mode = PlayMode.Once });

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var c in _running.Values) c.Cancel();
            _running.Clear();
        }
    }

    private void Run(int slot, List<MacroStep> steps, bool loop, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        var held = new List<MacroStep>();
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            do
            {
                var sw = Stopwatch.StartNew();
                foreach (var s in steps)
                {
                    if (ct.IsCancellationRequested) return;
                    Execute(s, held, vars, ct);
                }
                // zaštita od beskonačne petlje bez kašnjenja
                if (loop && sw.ElapsedMilliseconds < 15 && ct.WaitHandle.WaitOne(15)) return;
            } while (loop && !ct.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
        }
        finally
        {
            for (int i = held.Count - 1; i >= 0; i--)
                NativeInput.Key(held[i].Vk, held[i].Scan, held[i].Extended, true);
            lock (_lock)
            {
                if (_running.TryGetValue(slot, out var c) && c == cts) _running.Remove(slot);
            }
        }
    }

    private void Execute(MacroStep s, List<MacroStep> held, Dictionary<string, string> vars, CancellationToken ct)
    {
        switch (s.Type)
        {
            case StepType.KeyPress:
                NativeInput.Key(s.Vk, s.Scan, s.Extended, false);
                Thread.Sleep(10);
                NativeInput.Key(s.Vk, s.Scan, s.Extended, true);
                break;
            case StepType.KeyDown:
                NativeInput.Key(s.Vk, s.Scan, s.Extended, false);
                held.Add(s);
                break;
            case StepType.KeyUp:
                NativeInput.Key(s.Vk, s.Scan, s.Extended, true);
                held.RemoveAll(h => h.Vk == s.Vk);
                break;
            case StepType.Delay:
                if (s.DelayMs > 0 && ct.WaitHandle.WaitOne(s.DelayMs)) throw new OperationCanceledException();
                break;
            case StepType.Text:
                NativeInput.Text(Substitute(s.Text, vars));
                break;
            case StepType.Run:
                if (!string.IsNullOrWhiteSpace(s.Path))
                    Process.Start(new ProcessStartInfo(Substitute(s.Path, vars), Substitute(s.Args ?? "", vars)) { UseShellExecute = true });
                break;
            case StepType.Input:
            {
                string name = string.IsNullOrWhiteSpace(s.Variable) ? "vrijednost" : s.Variable.Trim();
                string def = Substitute(s.DefaultValue ?? "", vars);
                string prompt = string.IsNullOrWhiteSpace(s.Prompt) ? $"Unesi vrijednost za {{{name}}}:" : Substitute(s.Prompt, vars);
                string value = Ui?.Invoke(() => PromptDialog.ShowTop("G815 Studio – makro", prompt, def));
                if (value == null) throw new OperationCanceledException(); // korisnik je odustao → prekini makro
                vars[name] = value;
                break;
            }
            case StepType.Http:
                DoHttp(s, vars, ct);
                break;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex VarRx =
        new(@"\{([\p{L}\p{N}_]+)(:url)?\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Zamjenjuje {naziv} vrijednostima: varijable iz koraka "Unos vrijednosti", {odgovor} (tijelo zadnjeg HTTP odgovora),
    /// {status}, {datum}, {vrijeme}, {clipboard}. Dodatak ":url" kodira vrijednost za URL, npr. {grad:url}.
    /// </summary>
    private string Substitute(string text, Dictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
        return VarRx.Replace(text, m =>
        {
            string name = m.Groups[1].Value;
            string value;
            if (vars.TryGetValue(name, out var v)) value = v;
            else if (name.Equals("datum", StringComparison.OrdinalIgnoreCase)) value = DateTime.Now.ToString("dd.MM.yyyy");
            else if (name.Equals("vrijeme", StringComparison.OrdinalIgnoreCase)) value = DateTime.Now.ToString("HH:mm:ss");
            else if (name.Equals("clipboard", StringComparison.OrdinalIgnoreCase))
                value = Ui?.Invoke(() => Clipboard.ContainsText() ? Clipboard.GetText() : "") ?? "";
            else return m.Value;
            return m.Groups[2].Success ? Uri.EscapeDataString(value ?? "") : value;
        });
    }

    private void DoHttp(MacroStep s, Dictionary<string, string> vars, CancellationToken ct)
    {
        string url = Substitute(s.Url ?? "", vars).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new InvalidOperationException("Neispravan URL: " + url);

        var req = new System.Net.Http.HttpRequestMessage(new System.Net.Http.HttpMethod((s.Method ?? "GET").ToUpperInvariant()), uri);
        string body = Substitute(s.Body ?? "", vars);
        string method = req.Method.Method;
        if (body.Length > 0 && method != "GET" && method != "HEAD")
        {
            req.Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8);
            req.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                string.IsNullOrWhiteSpace(s.ContentType) ? "application/json" : s.ContentType.Trim());
        }
        foreach (var raw in (Substitute(s.Headers ?? "", vars)).Split('\n'))
        {
            string line = raw.Trim();
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string hn = line[..colon].Trim(), hv = line[(colon + 1)..].Trim();
            if (!req.Headers.TryAddWithoutValidation(hn, hv)) req.Content?.Headers.TryAddWithoutValidation(hn, hv);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Math.Clamp(s.TimeoutMs, 1000, 120000));
        try
        {
            using var resp = Http.SendAsync(req, timeout.Token).GetAwaiter().GetResult();
            string text = resp.Content.ReadAsStringAsync(timeout.Token).GetAwaiter().GetResult();
            vars["odgovor"] = text;
            vars["status"] = ((int)resp.StatusCode).ToString();
            Log.Write($"HTTP {method} {uri} → {(int)resp.StatusCode}");
            if (s.ShowResult)
            {
                string preview = text.Length > 120 ? text[..120] + "…" : text;
                Info?.Invoke($"{method} {uri.Host}: {(int)resp.StatusCode} {resp.ReasonPhrase}" + (preview.Length > 0 ? "\n" + preview : ""));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"HTTP poziv nije odgovorio na vrijeme ({uri.Host}).");
        }
        finally
        {
            req.Dispose();
        }
    }
}

/// <summary>Snima stvarne pritiske tipki (ignorira one koje generira sam program).</summary>
public sealed class MacroRecorder
{
    private readonly KeyboardHook _hook;
    private readonly Stopwatch _sw = new();
    private readonly HashSet<int> _down = new();
    private List<MacroStep> _steps = new();
    private long _last;
    private bool _recordDelays;

    public bool IsRecording { get; private set; }
    public int Count => _steps.Count;
    public event Action StepsChanged;

    public MacroRecorder(KeyboardHook hook)
    {
        _hook = hook;
    }

    public void Start(bool recordDelays)
    {
        if (IsRecording) return;
        _recordDelays = recordDelays;
        _steps = new List<MacroStep>();
        _down.Clear();
        _last = 0;
        _sw.Restart();
        IsRecording = true;
        _hook.KeyEvent += OnKey;
    }

    public List<MacroStep> Stop()
    {
        if (!IsRecording) return new List<MacroStep>();
        _hook.KeyEvent -= OnKey;
        IsRecording = false;
        _sw.Stop();
        // otpusti sve što je ostalo pritisnuto
        foreach (int vk in _down.ToList())
        {
            var downStep = _steps.LastOrDefault(s => s.Type == StepType.KeyDown && s.Vk == vk);
            _steps.Add(new MacroStep { Type = StepType.KeyUp, Vk = vk, Scan = downStep?.Scan ?? 0, Extended = downStep?.Extended ?? false });
        }
        _down.Clear();
        return Simplify(_steps);
    }

    private void OnKey(KeyHookEvent e)
    {
        if (e.Injected || e.OwnInjected) return;
        if (e.Down)
        {
            if (!_down.Add(e.Vk)) return; // autorepeat
        }
        else if (!_down.Remove(e.Vk))
        {
            return; // otpuštena tipka koja je pritisnuta prije početka snimanja
        }

        long now = _sw.ElapsedMilliseconds;
        if (_recordDelays && _steps.Count > 0)
        {
            int d = (int)(now - _last);
            if (d >= 10) _steps.Add(new MacroStep { Type = StepType.Delay, DelayMs = d });
        }
        _last = now;
        _steps.Add(new MacroStep
        {
            Type = e.Down ? StepType.KeyDown : StepType.KeyUp,
            Vk = e.Vk,
            Scan = e.Scan,
            Extended = e.Extended
        });
        StepsChanged?.Invoke();
    }

    /// <summary>Spaja "spusti X" + "otpusti X" (bez ičega između) u jedan "pritisni X".</summary>
    private static List<MacroStep> Simplify(List<MacroStep> steps)
    {
        var r = new List<MacroStep>();
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.Type == StepType.KeyDown && i + 1 < steps.Count &&
                steps[i + 1].Type == StepType.KeyUp && steps[i + 1].Vk == s.Vk)
            {
                r.Add(new MacroStep { Type = StepType.KeyPress, Vk = s.Vk, Scan = s.Scan, Extended = s.Extended });
                i++;
                continue;
            }
            r.Add(s);
        }
        return r;
    }
}
