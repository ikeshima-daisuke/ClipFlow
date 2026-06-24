using System;
using System.IO;

namespace ClipFlow.Services;

/// <summary>データ保存先（%APPDATA%\ClipFlow）。</summary>
internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipFlow");

    public static string ImagesDir { get; } = Path.Combine(Root, "images");

    public static string DbPath { get; } = Path.Combine(Root, "clipflow.db");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ImagesDir);
    }
}
