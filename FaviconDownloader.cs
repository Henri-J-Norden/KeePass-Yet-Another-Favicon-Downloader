﻿using KeePass.Plugins;
using KeePass.UI;
using KeePassLib;
using KeePassLib.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace YetAnotherFaviconDownloader
{
    class FaviconDownloader
    {
        private readonly IPluginHost pluginHost;
        private readonly BackgroundWorker bgWorker;
        private readonly IStatusLogger logger;

        private class ProgressInfo
        {
            public int Success { get; set; }
            public int NotFound { get; set; }
            public int Error { get; set; }
            public int Total { get; set; }
            public float Percent => ((Success + NotFound + Error) * 100f) / Total;
        }
        private readonly ProgressInfo progress;

        public FaviconDownloader(IPluginHost host)
        {
            // KeePass plugin host
            pluginHost = host;

            // Set up BackgroundWorker
            bgWorker = new BackgroundWorker()
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            // BackgroundWorker Events
            bgWorker.DoWork += BgWorker_DoWork;
            bgWorker.ProgressChanged += BgWorker_ProgressChanged;
            bgWorker.RunWorkerCompleted += BgWorker_RunWorkerCompleted;

            // Status Progress Form
            Form fStatusDialog;
            logger = StatusUtil.CreateStatusDialog(pluginHost.MainWindow, out fStatusDialog, "Yet Another Favicon Downloader", "Downloading favicons...", true, false);

            // Progress information
            progress = new ProgressInfo();
        }

        public void Run(PwEntry[] entries)
        {
            bgWorker.RunWorkerAsync(entries);
        }

        private void BgWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;
            var entries = e.Argument as PwEntry[];

            // Start process
            progress.Total = entries.Length;

            // Custom icons that will be added to the database
            var icons = new List<PwCustomIcon>(entries.Length);

            foreach (var entry in entries)
            {
                // Checks whether the user pressed the cancel button or the close button
                if (worker.CancellationPending || !logger.ContinueWork())
                {
                    e.Cancel = true;
                    break;
                }

                // Fields
                var title = entry.Strings.ReadSafe("Title");
                var url = entry.Strings.ReadSafe("URL");

                Util.Log("Downloading: {0}", url);

                WebClient wc = new WebClient();
                try
                {
                    // Download
                    var data = wc.DownloadData(url + "favicon.ico");
                    Util.Log("Icon downloaded with success");

                    // Create icon
                    var uuid = new PwUuid(true);
                    var icon = new PwCustomIcon(uuid, data);
                    icons.Add(icon);

                    // Associate with this entry
                    entry.CustomIconUuid = uuid;

                    // Save it
                    entry.Touch(true);

                    // Icon downloaded with success
                    progress.Success++;
                }
                catch (WebException ex)
                {
                    Util.Log("Failed to download favicon");

                    var response = ex.Response as HttpWebResponse;
                    if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Can't find an icon
                        progress.NotFound++;
                    }
                    else
                    {
                        // Some other error (network, etc)
                        progress.Error++;
                    }
                }

                // Progress
                worker.ReportProgress((int)progress.Percent, progress);
            }

            // Add all icons to the database
            pluginHost.Database.CustomIcons.AddRange(icons);

            // Refresh icons
            pluginHost.Database.UINeedsIconUpdate = true;

            // Waits long enough until we can see the output
            Thread.Sleep(3000);
        }

        private void BgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            Util.Log("Progress: {0}%", e.ProgressPercentage);
            logger.SetProgress((uint)e.ProgressPercentage);

            var state = e.UserState as ProgressInfo;
            var status = String.Format("Downloading favicons... Success: {0} / Not Found: {1} / Error: {2}", state.Success, state.NotFound, state.Error);
            logger.SetText(status, LogStatusType.Info);
        }

        private void BgWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                Util.Log("Cancelled");
            }
            else if (e.Error != null)
            {
                Util.Log("Error: {0}", e.Error.Message);
            }
            else
            {
                Util.Log("Done");
            }

            // Refresh icons
            pluginHost.MainWindow.UpdateUI(false, null, false, null, true, null, true);

            logger.EndLogging();
        }
    }
}