using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using Windows.ApplicationModel.Background;
using FMdotNet__DataAPI;
using System.Net;
using Windows.ApplicationModel.Resources;
using Windows.System.Threading;
using Windows.Foundation.Diagnostics;
using System.Threading;
using Windows.Storage;
using Windows.Media.Playback;
using Windows.Media.Core;
using System.IO;


// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace BillyBassPolly
{
    public sealed class StartupTask : IBackgroundTask
    {
        BackgroundTaskDeferral _deferral;
        FMS fmserver;
        string token;
        DateTime tokenRecieved;
        LoggingChannel lc;

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            // 
            // TODO: Insert code to perform background work
            //
            // If you start any asynchronous methods here, prevent the task
            // from closing prematurely by using BackgroundTaskDeferral as
            // described in http://aka.ms/backgroundtaskdeferral
            //

            _deferral = taskInstance.GetDeferral();

            // set the security for fms
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            // hook into FMS from settings in the config file
            fmserver = GetFMSinstance();
            token = string.Empty;

            // log into the Events Tracing for Windows ETW
            // on the Device Portal, ETW tab, pick "Microsoft-Windows-Diagnostics-LoggingChannel" from the registered providers
            // pick level 5 and enable

            lc = new LoggingChannel("BillyBass", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
            lc.LogMessage("Starting up.");

            // start the timer
            ThreadPoolTimer timer = ThreadPoolTimer.CreatePeriodicTimer(Timer_Tick, TimeSpan.FromSeconds(1));
        }

        private FMS GetFMSinstance()
        {
            var resources = new ResourceLoader("config");
            var fm_server_address = resources.GetString("fm_server_DNS_name");
            var fm_file = resources.GetString("fm_file");
            var fm_layout = resources.GetString("fm_layout");
            var fm_account = resources.GetString("fm_account");
            var fm_pw = resources.GetString("fm_pw");

            FMS17 fms = new FMS17(fm_server_address, fm_account, fm_pw);
            fms.SetFile(fm_file);
            fms.SetLayout(fm_layout);

            return fms;
        }

        private async void Timer_Tick(ThreadPoolTimer timer)
        {

            // record the start time
            DateTime start = DateTime.Now;

            // figure out if we need to authenticate to FMS or if we're good
            if (token == null || token == string.Empty)
            {
                lc.LogMessage("Logging into FMS.", LoggingLevel.Information);
                token = await fmserver.Authenticate();
                if (token.ToLower().Contains("error"))
                {
                    lc.LogMessage("Authentication error: " + token, LoggingLevel.Information);
                    token = string.Empty;

                    // and exit but without throwing an exception, we'll just try again on the next timer event
                    return;
                }
                else
                {
                    tokenRecieved = DateTime.Now;
                    lc.LogMessage("Token " + token + " Received at " + tokenRecieved.ToLongTimeString(), LoggingLevel.Information);
                }
            }
            else if (DateTime.Now >= tokenRecieved.AddMinutes(14))
            {
                // don't really need to log out after 14 minutes, we can just re-use the same token

                int logoutResponse = await fmserver.Logout();
                token = string.Empty;
                tokenRecieved = DateTime.Now;
                lc.LogMessage("Logging out of FMS.", LoggingLevel.Information);
                // we'll just wait for the next timer run
                return;
            }

            if (token != string.Empty)
            {
                // how old is the token?
                TimeSpan age = start - tokenRecieved;
                lc.LogMessage("Timed run; Token age = " + age, LoggingLevel.Information);

                // query FM to see if there are any outstanding tasks
                var find = fmserver.FindRequest();

                // we only need one at most
                find.SetHowManyRecords(1);

                var request1 = find.SearchCriterium();
                request1.AddFieldSearch("flag_ready", "1");

                var foundResponse = await find.Execute();
                if (foundResponse.errorCode > 0)
                {
                    lc.LogMessage("Search for tasks failed. " + fmserver.lastErrorCode.ToString() + " - " + fmserver.lastErrorMessage, LoggingLevel.Error);
                    return;
                }

                // ge the FM record
                var record = foundResponse.data.foundSet.records.First();

                // capture the record id, we'll need it to later update the record
                int taskId = Convert.ToInt32(record.recordId);

                // download the audio file
                string url = record.fieldsAndData.Where(pair => pair.Key == "audio_file").Select(pair => pair.Value).First();

                StorageFolder folder = KnownFolders.DocumentsLibrary;
                fmserver.SetDownloadFolder(folder.Path);

                FileInfo audioFile = await fmserver.DownloadFileFromContainerField(url);


                // play the audio
                StorageFile file = await StorageFile.GetFileFromPathAsync(audioFile.FullName);
                //MediaPlayer player = BackgroundMediaPlayer.Current;
                MediaPlayer billy = new MediaPlayer();
                billy.AutoPlay = false;
                //billy.SetFileSource(file);
                billy.Source = MediaSource.CreateFromStorageFile(file);
                billy.Play();

                // delete the file
                await file.DeleteAsync();


                // write an update to FMS
                var editRequest = fmserver.EditRequest(taskId);
                editRequest.AddField("flag_completed_when", DateTime.Now.ToString());
                editRequest.AddField("flag_ready", "");

                // execute the request
                var response = await editRequest.Execute();
                if (fmserver.lastErrorCode == 0)
                {
                    lc.LogMessage("Task finished.", LoggingLevel.Information);
                }
                else
                {
                    lc.LogMessage("Task failed. " + fmserver.lastErrorCode.ToString() + " - " + fmserver.lastErrorMessage, LoggingLevel.Error);
                }


                // don't log out, re-using the token for 14 minutes or so
                //await fmserver.Logout();
                //token = string.Empty;
            }


        }
    }
}
