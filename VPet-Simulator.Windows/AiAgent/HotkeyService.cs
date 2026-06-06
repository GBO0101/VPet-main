using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class HotkeyService : IDisposable
{
    private readonly Window window;
    private HwndSource? source;
    private readonly Dictionary<int, (ModifierKeys Modifiers, Keys Key, Action Callback)> registrations = new();
    private int nextId = 1;

    [Flags]
    internal enum ModifierKeys : uint
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8
    }

    internal enum Keys : int
    {
        None = 0,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        A = 0x41,
        B = 0x42,
        C = 0x43,
        D = 0x44,
        E = 0x45,
        F = 0x46,
        G = 0x47,
        H = 0x48,
        I = 0x49,
        J = 0x4A,
        K = 0x4B,
        L = 0x4C,
        M = 0x4D,
        N = 0x4E,
        O = 0x4F,
        P = 0x50,
        Q = 0x51,
        R = 0x52,
        S = 0x53,
        T = 0x54,
        U = 0x55,
        V = 0x56,
        W = 0x57,
        X = 0x58,
        Y = 0x59,
        Z = 0x5A,
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    public HotkeyService(Window window)
    {
        this.window = window;
        source = PresentationSource.FromVisual(window) as HwndSource;
        if (source == null)
        {
            window.SourceInitialized += OnSourceInitialized;
        }
        else
        {
            source.AddHook(HwndHook);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        source = PresentationSource.FromVisual(window) as HwndSource;
        source?.AddHook(HwndHook);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (registrations.TryGetValue(id, out var reg))
            {
                reg.Callback();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public int Register(ModifierKeys modifiers, Keys key, Action callback)
    {
        var hwnd = source?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Window handle not available");

        var id = nextId++;
        if (!RegisterHotKey(hwnd, id, (uint)modifiers, (uint)key))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterHotKey failed");

        registrations[id] = (modifiers, key, callback);
        HotkeyLogger.Log($"[Register] id={id}, modifiers={modifiers}, key={key}");
        return id;
    }

    public void Unregister(int id)
    {
        var hwnd = source?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
            UnregisterHotKey(hwnd, id);
        registrations.Remove(id);
        HotkeyLogger.Log($"[Unregister] id={id}");
    }

    public void UnregisterAll()
    {
        foreach (var id in new List<int>(registrations.Keys))
            Unregister(id);
    }

    public static bool TryParse(string text, out ModifierKeys modifiers, out Keys key)
    {
        modifiers = ModifierKeys.None;
        key = Keys.None;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": modifiers |= ModifierKeys.Control; break;
                case "ALT": modifiers |= ModifierKeys.Alt; break;
                case "SHIFT": modifiers |= ModifierKeys.Shift; break;
                case "WIN": case "WINDOWS": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        var last = parts[^1].ToUpperInvariant();
        if (last.Length == 1 && last[0] >= 'A' && last[0] <= 'Z')
        {
            key = (Keys)((int)last[0]);
            return true;
        }
        if (last.StartsWith("F") && int.TryParse(last[1..], out var fNum) && fNum >= 1 && fNum <= 12)
        {
            key = (Keys)(0x70 + fNum - 1);
            return true;
        }

        return false;
    }

    public static string Format(ModifierKeys modifiers, Keys key)
    {
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

        if (key >= Keys.F1 && key <= Keys.F12)
            parts.Add($"F{(int)key - 0x70 + 1}");
        else if (key >= Keys.A && key <= Keys.Z)
            parts.Add(((char)(int)key).ToString());

        return string.Join(" + ", parts);
    }

    public void Dispose()
    {
        UnregisterAll();
        if (source != null)
        {
            source.RemoveHook(HwndHook);
            source = null;
        }
    }
}
