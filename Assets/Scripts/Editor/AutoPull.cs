// AutoPull.cs - lives in Assets/Scripts/Editor/
//
// Pulls the git repo every 60 seconds while the Unity editor is open (never
// during Play mode), so changes pushed to GitHub show up on their own.
// Delete this file to turn it off.
//
// Uses `git pull --ff-only`, which never merges and never touches your local
// changes: if you have uncommitted work, the pull is skipped and you'll see a
// note in the Console telling you to pull manually in GitHub Desktop.

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;

[InitializeOnLoad]
public static class AutoPull
{
    const double IntervalSeconds = 60;

    static string gitPath;
    static bool gitResolved;
    static double nextPull;

    static AutoPull()
    {
        nextPull = EditorApplication.timeSinceStartup + 15;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextPull) return;
        nextPull = EditorApplication.timeSinceStartup + IntervalSeconds;
        if (EditorApplication.isPlaying) return; // never yank files mid-game
        TryPull();
    }

    static void TryPull()
    {
        if (!gitResolved)
        {
            gitPath = FindGit();
            gitResolved = true;
            if (gitPath == null)
                UnityEngine.Debug.LogWarning("[AutoPull] git not found (checked PATH and GitHub Desktop's bundled git). Auto-pull disabled.");
        }
        if (gitPath == null) return;

        string repoRoot = FindRepoRoot();
        if (repoRoot == null) return;

        try
        {
            var psi = new ProcessStartInfo(gitPath, "pull --ff-only")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(20000);
                if (p.ExitCode == 0)
                {
                    if (stdout.IndexOf("Already up to date", StringComparison.OrdinalIgnoreCase) < 0)
                        UnityEngine.Debug.Log("[AutoPull] Pulled latest changes:\n" + stdout.Trim());
                }
                else
                {
                    UnityEngine.Debug.Log("[AutoPull] Pull skipped (" + stderr.Trim() +
                        "). Pull manually in GitHub Desktop if you want the latest.");
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("[AutoPull] " + e.Message);
        }
    }

    static string FindRepoRoot()
    {
        // Walk up from Assets/ looking for the .git folder.
        string dir = Path.GetDirectoryName(UnityEngine.Application.dataPath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    static string FindGit()
    {
        // 1. git on PATH (Git for Windows, if installed).
        try
        {
            var psi = new ProcessStartInfo("git", "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit(5000);
                if (p.ExitCode == 0) return "git";
            }
        }
        catch { /* fall through */ }

        // 2. GitHub Desktop's bundled git.
        try
        {
            string ghd = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GitHubDesktop");
            if (Directory.Exists(ghd))
            {
                foreach (var appDir in Directory.GetDirectories(ghd, "app-*"))
                {
                    string candidate = Path.Combine(appDir, "resources", "app", "git", "cmd", "git.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch { /* fall through */ }

        return null;
    }
}
