using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor.U2D.Aseprite.Common;
using UnityEngine;

namespace UnityEditor.U2D.Aseprite
{
    [InitializeOnLoad]
    internal static class BackgroundImport
    {
        static readonly string k_AssetsPath = "Assets/";
        // Aseprite writes in place, so the watcher fires while the file is still growing.
        // A refresh during that write makes Unity hash a partial file and log
        // "Amount of processed bytes ... does not match file size".
        static readonly TimeSpan k_SaveSettleTime = TimeSpan.FromSeconds(0.5);
        static readonly List<string> s_AssetsFullPath = new List<string>();
        static long s_LastChangeTicks = 0;
        static bool s_HasChange = false;
        static bool s_LastSettingsValue = false;

        static FileSystemWatcher s_WatcherAse = null;
        static FileSystemWatcher s_WatcherAseprite = null;

        [InitializeOnLoadMethod]
        static void Setup()
        {

            Cleanup();

            EditorApplication.update += OnUpdate;
            if (ImportSettings.backgroundImport)
                SetupWatcher();

            s_LastSettingsValue = ImportSettings.backgroundImport;
        }

        static void Cleanup()
        {
            EditorApplication.update -= OnUpdate;
            StopWatchers();
        }


        static void OnUpdate()
        {
            if (EditorApplication.isCompiling)
                return;
            if (EditorApplication.isUpdating)
                return;

            CheckForSettingsUpdate();
            CheckForChange();
        }

        static void CheckForSettingsUpdate()
        {
            if (ImportSettings.backgroundImport == s_LastSettingsValue)
                return;

            if (ImportSettings.backgroundImport)
                SetupWatcher();
            else
                StopWatchers();

            s_LastSettingsValue = ImportSettings.backgroundImport;
        }

        static void SetupWatcher()
        {
            if (Application.isBatchMode)
                return;

            ThreadPool.QueueUserWorkItem(MonitorDirectory, k_AssetsPath);
        }

        static void MonitorDirectory(object obj)
        {
            var path = (string)obj;
            s_WatcherAse = SetupWatcher(path, "*.ase");
            s_WatcherAseprite = SetupWatcher(path, "*.aseprite");
        }

        static FileSystemWatcher SetupWatcher(string path, string filter)
        {
            var watcher = new FileSystemWatcher()
            {
                Filter = filter,
                Path = path,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnChangeDetected;
            watcher.Created += OnChangeDetected;
            watcher.Renamed += OnChangeDetected;
            return watcher;
        }

        static void OnChangeDetected(object sender, FileSystemEventArgs e)
        {
            var extension = Path.GetExtension(e.FullPath);
            if (extension != ".aseprite" && extension != ".ase")
                return;

            s_AssetsFullPath.Add(e.FullPath);
            Interlocked.Exchange(ref s_LastChangeTicks, DateTime.UtcNow.Ticks);
            s_HasChange = true;
        }

        static void StopWatchers()
        {
            if (s_WatcherAse != null)
            {
                s_WatcherAse.Dispose();
                s_WatcherAse = null;
            }
            if (s_WatcherAseprite != null)
            {
                s_WatcherAseprite.Dispose();
                s_WatcherAseprite = null;
            }
        }

        static void CheckForChange()
        {
            if (!s_HasChange)
                return;
            // If the editor is already focused, skip forced import.
            if (UnityEditorInternal.InternalEditorUtility.isApplicationActive)
            {
                s_HasChange = false;
                return;
            }
            if (Application.isPlaying)
                return;
            if (DateTime.UtcNow.Ticks - Interlocked.Read(ref s_LastChangeTicks) < k_SaveSettleTime.Ticks)
                return;

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport & ImportAssetOptions.ForceUpdate);

            var relativePaths = new List<string>(s_AssetsFullPath.Count);
            for (var i = 0; i < s_AssetsFullPath.Count; ++i)
                relativePaths.Add(FileUtil.GetProjectRelativePath(s_AssetsFullPath[i]));

            AssetDatabase.ForceReserializeAssets(relativePaths);
            InternalEditorBridge.RefreshInspectors();

            s_AssetsFullPath.Clear();
            s_HasChange = false;
        }
    }
}
