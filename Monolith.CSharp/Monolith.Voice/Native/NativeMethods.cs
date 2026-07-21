using System.Runtime.InteropServices;

namespace Monolith.Voice.Native;

[StructLayout(LayoutKind.Sequential)]
public struct TtsConfig
{
    public IntPtr ModelPath;
    public int SampleRate;
    public int Channels;
    public int OpusBitrate;
    public int MaxConcurrency;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void OnOpusPacketDelegate(IntPtr data, int len, IntPtr user);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void OnLogDelegate(IntPtr msg, IntPtr user);

internal static class NativeMethods
{
    private const string DllName = "tts_native";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tts_create(
        ref TtsConfig cfg,
        OnOpusPacketDelegate packetCb,
        OnLogDelegate logCb,
        IntPtr user);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tts_speak_async(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string text,
        [MarshalAs(UnmanagedType.LPStr)] string? style,
        int utteranceId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tts_stop(IntPtr handle, int utteranceId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tts_get_metrics(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void tts_free_string(IntPtr s);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void tts_destroy(IntPtr handle);
}
