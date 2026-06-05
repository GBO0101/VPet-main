using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VPet_Simulator.Windows.AiAgent;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class ProgramShortcutStoreTests
{
    [Fact]
    public void FindLaunchableFilesReturnsSupportedFilesFromFolderAndSubfolders()
    {
        using var directory = new TestDirectory();
        var exePath = directory.AddFile("Alpha.exe");
        var lnkPath = directory.AddFile("Tool.lnk");
        var cmdPath = directory.AddFile("Tools", "Beta.cmd");
        directory.AddFile("readme.txt");

        var files = ProgramShortcutStore.FindLaunchableFiles(directory.Root);

        Assert.Equal(
            new[] { "Alpha", "Beta", "Tool" },
            files.Select(file => file.Name).ToArray());
        Assert.Equal(
            new[] { exePath, cmdPath, lnkPath },
            files.Select(file => file.Path).ToArray());
    }

    [Fact]
    public void FindLaunchableFilesReturnsEmptyListForMissingFolder()
    {
        var files = ProgramShortcutStore.FindLaunchableFiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Empty(files);
    }

    private sealed class TestDirectory : IDisposable
    {
        private readonly List<string> files = new();
        private readonly List<string> directories = new();

        public TestDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "VPetShortcutTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string AddFile(params string[] pathParts)
        {
            var path = Path.Combine(new[] { Root }.Concat(pathParts).ToArray());
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                directories.Add(directory);
            }

            File.WriteAllText(path, "");
            files.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var file in files)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            foreach (var directory in directories.OrderByDescending(path => path.Length))
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }

            if (Directory.Exists(Root) && !Directory.EnumerateFileSystemEntries(Root).Any())
                Directory.Delete(Root);
        }
    }
}
