using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutBuild
{
    [Serializable]
    public sealed class BuildMove
    {
        public string name;
        public bool existed;
    }

    [Serializable]
    public sealed class BuildJournal
    {
        public string id, output, previousVersion, previousIosCode;
        public int previousAndroidCode;
        public bool previousBundle, previousDevelopment, committed;
        public WebBuildSettings previousWeb;
        public List<BuildMove> moves = new List<BuildMove>();
    }

    // Journal before settings changes and each file move. Only successful staged builds
    // are published, including all companion files of desktop players.
    public static class BuildRecovery
    {
        private static FileStream outputLock;
        private static void LockOutput(string output)
        {
            Directory.CreateDirectory(output);
            if (outputLock != null) return;
            try { outputLock = new FileStream(Path.Combine(output, ".buildautomator-build.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException e) { throw new IOException("Another Editor is using this build folder. Wait for that build to finish.", e); }
        }
        private static string JournalPath { get { return Path.Combine(BuildStorage.Root, "UserSettings", "OutBuildAutomator-build.json"); } }
        public static string Work(BuildJournal journal)
        {
            Guid id;
            if (journal == null || !Guid.TryParseExact(journal.id, "N", out id)) throw new IOException("Invalid build recovery identifier.");
            if (!Path.IsPathRooted(journal.output)) throw new IOException("Recovery output must be absolute.");
            return Path.Combine(BuildStorage.OutputRoot(journal.output), ".buildautomator", journal.id);
        }
        public static string Stage(BuildJournal journal) { return Path.Combine(Work(journal), "new"); }
        public static BuildJournal Begin(string output)
        {
            if (File.Exists(JournalPath)) throw new IOException("A previous build needs recovery. Reopen Build Automator before building.");
            var journal = new BuildJournal
            {
                id = Guid.NewGuid().ToString("N"), output = output,
                previousVersion = PlayerSettings.bundleVersion, previousAndroidCode = PlayerSettings.Android.bundleVersionCode,
                previousIosCode = PlayerSettings.iOS.buildNumber, previousBundle = EditorUserBuildSettings.buildAppBundle,
                previousDevelopment = EditorUserBuildSettings.development,
                previousWeb = BuildJobs.Target == BuildTarget.WebGL ? WebBuildSettings.Capture() : null
            };
            LockOutput(output);
            try { Directory.CreateDirectory(Stage(journal)); Save(journal); return journal; }
            catch { outputLock?.Dispose(); outputLock = null; throw; }
        }
        private static void Save(BuildJournal journal) { BuildStorage.Write(JournalPath, journal); }
        private static bool Exists(string path) { return File.Exists(path) || Directory.Exists(path); }
        private static void Move(string source, string target)
        {
            source = BuildStorage.FilePath(source); target = BuildStorage.FilePath(target);
            if (Directory.Exists(source)) Directory.Move(source, target);
            else File.Move(source, target);
        }
        private static string Child(string root, string name)
        {
            if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name == "." || name == "..") throw new IOException("Invalid recovery artifact name.");
            return Path.Combine(root, name);
        }
        public static void Publish(BuildJournal journal)
        {
            string previous = Path.Combine(Work(journal), "previous");
            Directory.CreateDirectory(previous);
            foreach (string source in Directory.GetFileSystemEntries(Stage(journal)))
            {
                string name = Path.GetFileName(source), target = Child(journal.output, name);
                var move = new BuildMove { name = name, existed = Exists(target) };
                journal.moves.Add(move);
                Save(journal);
                if (move.existed) Move(target, Child(previous, name));
                Move(source, target);
            }
        }
        public static void Commit(BuildJournal journal) { journal.committed = true; Save(journal); }
        public static void Finish(BuildJournal journal)
        {
            if (journal == null) return;
            Work(journal); // Validate before filesystem operations, including recovery from disk.
            LockOutput(journal.output);
            try
            {
            if (!journal.committed)
            {
                string previous = Path.Combine(Work(journal), "previous");
                string rejected = Path.Combine(Work(journal), "unpublished");
                Directory.CreateDirectory(rejected);
                foreach (var move in journal.moves.AsEnumerable().Reverse())
                {
                    string target = Child(journal.output, move.name), backup = Child(previous, move.name);
                    if (Exists(backup))
                    {
                        if (Exists(target)) Move(target, Child(rejected, move.name));
                        Move(backup, target);
                    }
                    else if (!move.existed && Exists(target) && !Exists(Child(Stage(journal), move.name)) && !Exists(Child(rejected, move.name)))
                        Move(target, Child(rejected, move.name));
                }
                PlayerSettings.bundleVersion = journal.previousVersion;
                PlayerSettings.Android.bundleVersionCode = journal.previousAndroidCode;
                PlayerSettings.iOS.buildNumber = journal.previousIosCode;
            }
            EditorUserBuildSettings.buildAppBundle = journal.previousBundle;
            EditorUserBuildSettings.development = journal.previousDevelopment;
            if (journal.previousWeb != null) journal.previousWeb.Apply();
            File.Delete(JournalPath);
            try { Directory.Delete(BuildStorage.FilePath(Work(journal)), true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            }
            finally { outputLock?.Dispose(); outputLock = null; }
        }
        public static string Recover()
        {
            if (!File.Exists(JournalPath)) return "";
            try
            {
                var journal = JsonUtility.FromJson<BuildJournal>(File.ReadAllText(JournalPath));
                Finish(journal);
                return "Recovered an interrupted build. Temporary settings were restored; incomplete artifact publication was rolled back.";
            }
            catch (Exception e) { return "Build recovery needs attention: " + e.Message + " Journal: " + JournalPath; }
        }
    }
}
