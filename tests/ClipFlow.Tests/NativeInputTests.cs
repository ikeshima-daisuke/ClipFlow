using System.Runtime.InteropServices;
using ClipFlow.Services;

namespace ClipFlow.Tests;

/// <summary>
/// SendInput に渡すネイティブ構造体のサイズを固定する。
/// サイズが Windows の期待値と異なると SendInput は無言で失敗し、
/// Ctrl+V が送られず「貼り付かない」症状になる。
/// </summary>
public class NativeInputTests
{
    [Fact]
    public void INPUT_size_matches_native_win32()
    {
        // x64: sizeof(INPUT)=40, x86: 28
        int expected = Environment.Is64BitProcess ? 40 : 28;
        Assert.Equal(expected, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    [Fact]
    public void KEYBDINPUT_size_matches_native_win32()
    {
        // x64: 24, x86: 16
        int expected = Environment.Is64BitProcess ? 24 : 16;
        Assert.Equal(expected, Marshal.SizeOf<NativeMethods.KEYBDINPUT>());
    }
}
