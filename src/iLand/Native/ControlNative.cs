using System.Runtime.InteropServices;

namespace iLand.Native;

/// <summary>P/Invoke kecil untuk tab Control (kunci layar dan pemberitahuan perubahan tema).</summary>
internal static class ControlNative
{
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    // ---- Pengaturan daya (powrprof.dll), dipakai untuk ambang Battery saver ----

    [DllImport("powrprof.dll")]
    internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerReadDCValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, out uint dcValueIndex);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerWriteDCValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, uint dcValueIndex);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr memory);

    /// <summary>Mengunci sesi Windows (sama seperti Win+L). Fungsi resmi user32.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LockWorkStation();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeoutMs, out UIntPtr result);

    /// <summary>
    /// Memberi tahu semua window bahwa pengaturan tema berubah ("ImmersiveColorSet"), supaya Explorer dan aplikasi lain
    /// langsung ikut berganti. Memakai batas waktu 100 ms per window agar window yang macet tidak menahan kita.
    /// Panggil dari thread latar karena bisa memakan waktu.
    /// </summary>
    internal static void BroadcastThemeChange()
    {
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "ImmersiveColorSet",
            SMTO_ABORTIFHUNG, 100, out _);
    }
}
