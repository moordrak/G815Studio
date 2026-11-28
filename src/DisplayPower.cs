using System.Runtime.InteropServices;

namespace Svitac;

/// <summary>
/// Prati jesu li monitori upaljeni (GUID_CONSOLE_DISPLAY_STATE). Windows šalje poruku kad se zasloni ugase zbog
/// štednje energije, kad se zatamne i kad se ponovno upale. Živi na UI niti (treba skriveni prozor za poruke).
/// </summary>
public sealed class DisplayPowerWatcher : NativeWindow, IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    // 0 = ugašeni, 1 = upaljeni, 2 = zatamnjeni
    private static Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private IntPtr _registration;

    /// <summary>true kad su monitori ugašeni (štednja energije). Zatamnjeni zasloni se smatraju upaljenima.</summary>
    public bool DisplaysOff { get; private set; }

    /// <summary>Javlja se na UI niti kad se monitori ugase (true) ili upale (false).</summary>
    public event Action<bool> Changed;

    public DisplayPowerWatcher()
    {
        try
        {
            CreateHandle(new CreateParams { Caption = "SVITAC.DisplayPower" });
            _registration = RegisterPowerSettingNotification(Handle, ref GUID_CONSOLE_DISPLAY_STATE, DEVICE_NOTIFY_WINDOW_HANDLE);
            if (_registration == IntPtr.Zero)
                Log.Write("Praćenje monitora nije uspjelo (greška " + Marshal.GetLastWin32Error() + ").");
        }
        catch (Exception ex)
        {
            Log.Write("Praćenje monitora nije uspjelo: " + ex.Message);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_POWERBROADCAST && (int)m.WParam == PBT_POWERSETTINGCHANGE && m.LParam != IntPtr.Zero)
        {
            try
            {
                var s = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(m.LParam);
                if (s.PowerSetting == GUID_CONSOLE_DISPLAY_STATE && s.DataLength >= 1)
                {
                    bool off = s.Data == 0;
                    if (off != DisplaysOff)
                    {
                        DisplaysOff = off;
                        Log.Write(off ? "Monitori su se ugasili." : "Monitori su se upalili.");
                        Changed?.Invoke(off);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Greška kod poruke o monitorima: " + ex.Message);
            }
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_registration != IntPtr.Zero)
        {
            try { UnregisterPowerSettingNotification(_registration); } catch { }
            _registration = IntPtr.Zero;
        }
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
