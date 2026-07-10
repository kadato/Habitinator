#if WINDOWS
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute'
#pragma warning disable SYSLIB1096 // Use 'GeneratedComInterfaceAttribute' instead of 'ComImportAttribute'

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

using App.MAUI.Services;

namespace App.MAUI.Platforms.Windows;

[ComImport]
[Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITaskbarList3
{
    // ITaskbarList
    [PreserveSig] void HrInit();
    [PreserveSig] void AddTab(IntPtr hwnd);
    [PreserveSig] void DeleteTab(IntPtr hwnd);
    [PreserveSig] void ActivateTab(IntPtr hwnd);
    [PreserveSig] void SetActiveAlt(IntPtr hwnd);

    // ITaskbarList2
    [PreserveSig] void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

    // ITaskbarList3
    [PreserveSig] void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
    [PreserveSig] void SetProgressState(IntPtr hwnd, int tbpFlags);
    [PreserveSig] void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
    [PreserveSig] void UnregisterTab(IntPtr hwndTab);
    [PreserveSig] void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
    [PreserveSig] void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
    [PreserveSig] void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
    [PreserveSig] void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
    [PreserveSig] void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
    [PreserveSig] void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
}

[ComImport]
[Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
public class TaskbarList { }

public class WindowsAppWindowProgressService : MauiAppWindowProgressService
{
    private ITaskbarList3? _taskbarList;
    private bool _isInitialized;

    private void InitializeTaskbar()
    {
        if (_isInitialized)
        {
            return;
        }
        try
        {
            _taskbarList = (ITaskbarList3)new TaskbarList();
            _taskbarList.HrInit();
            _isInitialized = true;
        }
        catch
        {
            // Fail-silent on environments where taskbar APIs are unsupported
        }
    }

    private static IntPtr GetWindowHandle()
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.Count > 0 ? Microsoft.Maui.Controls.Application.Current.Windows[0] : null;
        var nativeWindow = window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (nativeWindow == null)
        {
            return IntPtr.Zero;
        }

        return WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
    }

    public override void SetTaskbarTimeBadge(int minutesRemaining)
    {
        InitializeTaskbar();
        if (_taskbarList == null)
        {
            return;
        }

        IntPtr hwnd = GetWindowHandle();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Draw the badge bitmap dynamically (16x16 size for taskbar overlay)
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        graphics.SmoothingMode = SmoothingMode.HighSpeed;

        // Draw background badge circle (colored blue matching MudBlazor Primary/Info #3b82f6)
        using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(220, 59, 130, 246)))
        {
            graphics.FillEllipse(brush, 0, 0, 16, 16);
        }

        // Draw concrete remaining minutes text
        string text = minutesRemaining > 99 ? "99+" : minutesRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
        float fontSize = text.Length > 1 ? 6.5f : 8f;
        using (var font = new System.Drawing.Font("Arial", fontSize, System.Drawing.FontStyle.Bold))
        using (var textBrush = new SolidBrush(System.Drawing.Color.White))
        {
            var stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(text, font, textBrush, new RectangleF(0, 0.5f, 16, 16), stringFormat);
        }

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            _taskbarList.SetOverlayIcon(hwnd, hIcon, $"{minutesRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture)} minutes remaining");
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public override void ClearTaskbarBadge()
    {
        InitializeTaskbar();
        if (_taskbarList == null)
        {
            return;
        }

        IntPtr hwnd = GetWindowHandle();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _taskbarList.SetOverlayIcon(hwnd, IntPtr.Zero, string.Empty);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);
}
#endif
