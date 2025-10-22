using System.Diagnostics;
using System.Runtime.InteropServices;

namespace dotVFile.Test;

public static class ConsoleUtil
{
	private const int DefaultWidth = 1300;
	private const int DefaultHeight = 1400;
	private static ConsoleColor DefaultTextColor = ConsoleColor.Gray;

	public static void InitializeConsole(
		int width = DefaultWidth,
		int height = DefaultHeight,
		ConsoleColor textColor = ConsoleColor.Gray)
	{
		SetWindow(width, height);
		DefaultTextColor = textColor;
		SetTextColor(textColor);
	}

	private static void SetWindow(
		int width = DefaultWidth,
		int height = DefaultHeight)
	{
		var screenSize = WindowUtil.GetScreenSize();

		WindowUtil.MoveWindow(
			WindowUtil.FindConsoleWindow(),
			screenSize.Width / 2 - width / 2, // x
			0, // y
			width,
			height);
	}

	public static void WriteLine(string? value, ConsoleColor? textColor = null)
	{
		if (textColor.HasValue)
			SetTextColor(textColor.Value);

		Console.WriteLine(value);

		if (textColor.HasValue)
			ResetTextColor();
	}

	public static void SetTextColor(ConsoleColor color)
	{
		Console.ForegroundColor = color;
	}

	public static void ResetTextColor()
	{
		SetTextColor(DefaultTextColor);
	}
}

public static partial class WindowUtil
{
	[LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial IntPtr FindWindow(IntPtr zero, string lpWindowName);

	[LibraryImport("user32.dll", EntryPoint = "MoveWindow")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

	[LibraryImport("user32.dll")]
	private static partial int GetSystemMetrics(int nIndex);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SetForegroundWindow(IntPtr hWnd);

	public record Size(int Width, int Height);

	public static Size GetScreenSize() => new(GetSystemMetrics(0), GetSystemMetrics(1));

	public static void MoveWindow(IntPtr handle, int x, int y, int width, int height)
	{
		MoveWindow(handle, x, y, width, height, true);

		SetForegroundWindow(handle);
	}

	public static IntPtr FindConsoleWindow()
	{
		return FindConsoleWindowInternal(false, 0);
	}

	private static IntPtr FindConsoleWindowInternal(
		bool waitForStartup,
		int retryCount)
	{
#pragma warning disable CA1416 // Validate platform compatibility
		if (waitForStartup)
			Thread.Sleep(100);

		IntPtr ptr = FindWindow(IntPtr.Zero, Console.Title);

		if (ptr == 0)
			ptr = Process.GetCurrentProcess().MainWindowHandle;

		return ptr != 0 || retryCount > 10
			? ptr
			: FindConsoleWindowInternal(true, retryCount + 1);
#pragma warning restore CA1416 // Validate platform compatibility
	}
}