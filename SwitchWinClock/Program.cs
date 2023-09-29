﻿using SwitchWinClock.utils;
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SwitchWinClock
{
    internal static class Program
    {
        private static Mutex MutexObj = null;
        private static SLog Log = null;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
#if DEBUG
            string mutexExt = "Debug";
            Log = new SLog(SMsgType.Debug);
#else
            string mutexExt = "Release";
            Log = new SLog(SMsgType.Information);
#endif
            bool createdNew = false;
            string appName = $"{About.AppTitle.Replace(" ", "")}{mutexExt}";
            Global.AppID = 1;   //default

            Log.WriteLine(SMsgType.Debug, $"appName: {appName}");

            //ignore args, just want to make sure an int was passed in.
            if (args.Length == 1 && int.TryParse(args[0], out int id))
            {
                Log.WriteLine(SMsgType.Debug, $"Arg.length == 1");
                for (; Global.AppID < 10; Global.AppID++)
                {
                    Log.WriteLine(SMsgType.Debug, $"Checking: {appName}{ Global.AppID}");
                    MutexObj = new Mutex(initiallyOwned: true, $"{appName}{Global.AppID}", out createdNew);
                    if (createdNew)
                    {
                        Log.WriteLine(SMsgType.Debug, $"createdNew (break): {appName}{Global.AppID}");
                        break;
                    }
                    Log.WriteLine(SMsgType.Debug, $"Was found, Check next.");
                }
            }
            else
            {
                Log.WriteLine(SMsgType.Debug, $"Arg.length != 1 or Arg wasn't Int");
                Log.WriteLine(SMsgType.Debug, $"Checking 2: {appName}{Global.AppID}");
                MutexObj = new Mutex(initiallyOwned: true, $"{appName}{Global.AppID}", out createdNew);
            }

            if (!createdNew)
            {
                Log.WriteLine(SMsgType.Debug, $"Exiting: createdNew: {createdNew}");
                return;
            }

            Log.WriteLine(SMsgType.Debug, $"Starting: {appName}{Global.AppID}");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new DisplayForm());
        }
    }
}
