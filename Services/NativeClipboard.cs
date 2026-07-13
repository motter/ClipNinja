using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace ClipNinjaV2.Services;

/// <summary>
/// Low-level clipboard helper that bypasses WPF's slow SetDataObject path.
///
/// Why this exists: WPF's Clipboard.SetDataObject(data, copy:true) internally
/// retries for ~1 second when the clipboard is locked by another process
/// (Win+V history viewer, password managers, etc.), even on failure. That
/// turns every text write into a 1-second hang. Our trace logs caught it
/// red-handed — 960-1060ms per failed attempt.
///
/// We instead use Win32 directly with a tight 10ms retry loop, succeeding
/// fast when the clipboard becomes available. Total worst-case wait drops
/// from 8000ms (8 retries × 1000ms) to ~250ms (25 attempts × 10ms).
/// </summary>
public static class NativeClipboard
{
    private const uint CF_UNICODETEXT = 13;

    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    /// <summary>
    /// Set Unicode text on the clipboard via Win32. Retries OpenClipboard
    /// every 10ms for up to <paramref name="maxWaitMs"/> milliseconds.
    /// Returns true on success.
    /// </summary>
    public static bool SetText(string text, int maxWaitMs = 250)
    {
        if (text is null) return false;

        // Strip null chars — they break Win32 clipboard text APIs.
        if (text.IndexOf('\0') >= 0) text = text.Replace("\0", "");

        // Allocate global memory for the wide-char string (incl. null terminator)
        int byteCount = (text.Length + 1) * 2;   // UTF-16 + null
        IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
        if (hGlobal == IntPtr.Zero) return false;

        try
        {
            IntPtr ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }
            try
            {
                // Copy the wide-char string into the global buffer.
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                // Null terminator
                Marshal.WriteInt16(ptr, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            // Retry OpenClipboard with a tight loop — total budget = maxWaitMs
            int waited = 0;
            while (!OpenClipboard(IntPtr.Zero))
            {
                if (waited >= maxWaitMs)
                {
                    GlobalFree(hGlobal);
                    return false;
                }
                Thread.Sleep(10);
                waited += 10;
            }

            try
            {
                if (!EmptyClipboard())
                {
                    GlobalFree(hGlobal);
                    return false;
                }
                if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return false;
                }
                // After SetClipboardData succeeds, Windows OWNS the hGlobal.
                // We must NOT free it.
                hGlobal = IntPtr.Zero;
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            if (hGlobal != IntPtr.Zero) GlobalFree(hGlobal);
            return false;
        }
    }
}
