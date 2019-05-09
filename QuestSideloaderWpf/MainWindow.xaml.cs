using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace QuestSideloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static string adbUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

        //Gets the wpf equivalent to winforms LocalUserAppDataPath
        private static string userprofilePath = $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\" + 
                                                $@"{Assembly.GetExecutingAssembly().GetName().Name}\" + 
                                                $@"{Assembly.GetExecutingAssembly().GetName().Name}\" + 
                                                $@"{Assembly.GetExecutingAssembly().GetName().Version.ToString()}";

        private DispatcherTimer devicesTimer = new DispatcherTimer();

        private const string STATUS_SEARCHING = "Searching for Quest/Go ...";
        private const string STATUS_SEARCHING_LONG = "Searching devices, please plug in your Quest/Go...";
        private const string STATUS_DEVICE_FOUND_UNAUTHORIZED = "Device found. Waiting for authorization...";
        private const string STATUS_DEVICE_FOUND_UNAUTHORIZED_LONG = "Device found. Leave your Quest/Go plugged in,\n put it on and authorize this computer when prompted.";
        private const string STATUS_DEVICE_FOUND_READY = "Device found. Ready to sideload Apps.";
        private const string STATUS_DEVICE_FOUND_DEVELOPERMODE_OFF = "Device found. Waiting for developer mode to be enabled.";
        private const string STATUS_DEVICE_FOUND_DEVELOPERMODE_OFF_LONG = "Quest/Go connected - Developer mode is disabled. \n Enable developer mode by clicking the above link.";
        private const string STATUS_APP_INSTALLATION_SUCCESS = "App installed successfully!";
        private const string STATUS_APP_INSTALLATION_FAILED = "App NOT installed!\nInfo: ";
        public MainWindow()
        {
            InitializeComponent();
            this.AllowDrop = true;
            this.devicesTimer.Interval = TimeSpan.FromSeconds(1);
            this.devicesTimer.Tick += DevicesTimer_Tick;

            Directory.CreateDirectory(userprofilePath);

            init();
        }

        private void DevicesTimer_Tick(object sender, EventArgs e)
        {
            devicesTimer.IsEnabled = false;
            getDevices();
        }

        private void init()
        {
            getAdb();
        }

        private void dragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                dragGrid.Visibility = Visibility.Visible;
            }
        }

        private void dragLeave(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            dragGrid.Visibility = Visibility.Collapsed;
        }

        private void dragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (string file in files)
            {
                if (file.EndsWith(".apk"))
                {
                    installApk(file);
                    break;
                }
            }
            dragGrid.Visibility = Visibility.Collapsed;
        }

        private void installApk(string path)
        {
            if (MessageBox.Show("Do you wish to install this app?\nWarning: You should only install apps from developers you trust!", "Install APK", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                statusLabel.Content = "Installing " + path + "...";
                var o = adbCmd("install -r " + path);
                checkInstallOutput(o);
            }
        }

        private void getAdb()
        {

            if (!System.IO.File.Exists(userprofilePath+ "/platform-tools/adb.exe"))
            {
                statusLabel.Content = "ADB not found.";
                if (MessageBox.Show("To sideload apps, a program called ADB must be downloaded from Google. Click OK to continue...", "Download ADB?", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                {
                    statusLabel.Content = "Please wait...\nThe program may look like it has frozen but be patient!";
                    dropLabel.Content = "Downloading: " + adbUrl;
                    WebClient Client = new WebClient();
                    try
                    {
                        Client.DownloadFile(adbUrl, userprofilePath + "/adb.zip");
                    }
                    catch (WebException we)
                    {
                        MessageBox.Show("An error occurred downloading ADB. Please try again or contact the developer: " + we.ToString());
                        Close();
                    }

                    try
                    {
                        ZipFile.ExtractToDirectory(userprofilePath + "/adb.zip", userprofilePath + "/");
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show("An error occurred extracting ADB. Please try again or contact the developer: " + e.ToString());
                        Close();
                    }

                    if (!System.IO.File.Exists(userprofilePath + "/platform-tools/adb.exe"))
                    {
                        MessageBox.Show("An error occurred extracting ADB. Please try again or contact the developer.");
                        Close();
                    }
                    else
                    {
                        statusLabel.Content = STATUS_SEARCHING;
                        dropLabel.Content = STATUS_SEARCHING_LONG;
                        getDevices();
                    }
                }
                else
                    Close();
            }
            else
            {
                statusLabel.Content = STATUS_SEARCHING;
                dropLabel.Content = STATUS_SEARCHING_LONG;
                getDevices();
            }
        }

        private async void getDevices()
        {
            statusBar.Visibility = Visibility.Visible;

            //adb devices
            var o = adbCmd("devices");
            if (o.ToLower().Contains("unauthorized"))
            {
                dropLabel.Content = STATUS_DEVICE_FOUND_UNAUTHORIZED_LONG;
                statusLabel.Content = STATUS_DEVICE_FOUND_UNAUTHORIZED;
                statusBar.Visibility = Visibility.Visible;
                devicesTimer.IsEnabled = true;
            }
            else if (o.Replace("List of devices", "").Contains("device"))
            {
                dropLabel.Content = STATUS_DEVICE_FOUND_READY;
                statusBar.Visibility = Visibility.Collapsed;
                checkAutoInstall();
            }
            else
            {
                var usbDevices = await UsbBrowser.GetUsbDevices();
                bool goOrQuestConnected = await IsGoOrQuestConnected(usbDevices);

                if (goOrQuestConnected)
                {
                    dropLabel.Content = STATUS_DEVICE_FOUND_DEVELOPERMODE_OFF_LONG;
                    statusLabel.Content = STATUS_DEVICE_FOUND_DEVELOPERMODE_OFF;
                }
                else
                {
                    statusLabel.Content = STATUS_SEARCHING;
                    dropLabel.Content = STATUS_SEARCHING_LONG;
                }

                statusBar.Visibility = Visibility.Visible;
                devicesTimer.IsEnabled = true;
            }
        }

        private async Task<bool> IsGoOrQuestConnected(IList<ManagementBaseObject> list)
        {
            foreach (var device in list)
            {
                foreach (var property in device.Properties)
                {
                    string val = property.Value != null ? property.Value.ToString() : string.Empty;
                    if (val.Contains("VID_2833&PID_0083") || val.Contains("VID_2833&PID_0086") || val.Contains("VID&0002045E_PID&065B"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string adbCmd(string options)
        {
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.WorkingDirectory = "./";
            p.StartInfo.FileName = userprofilePath + "/platform-tools/adb.exe";
            p.StartInfo.Arguments = options;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }

        private void checkAutoInstall()
        {
            //check for auto-install package apk
            if (System.IO.File.Exists("./autoinstall.apk"))
            {
                statusLabel.Content = STATUS_DEVICE_FOUND_READY;
                dropLabel.Content = "";
                if (MessageBox.Show("Auto-install APK detected. Do you wish to install this app?\nWarning: You should only install apps from developers you trust!", "Confirm Install", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                {
                    statusLabel.Content = "Auto installing, please wait.";
                    var o = adbCmd("install -r ./autoinstall.apk");
                    checkInstallOutput(o);
                    setupDragAndDrop();
                    statusLabel.Content = STATUS_APP_INSTALLATION_SUCCESS;
                }
                else
                {
                    setupDragAndDrop();
                }
            }
            else
            {
                setupDragAndDrop();
            }
        }

        private void checkInstallOutput(string output)
        {
            if (output.Contains("Success"))
            {
                statusLabel.Content = STATUS_APP_INSTALLATION_SUCCESS;
            }
            else
            {
                statusLabel.Content = STATUS_APP_INSTALLATION_FAILED + output;
            }
        }

        private void setupDragAndDrop()
        {
            statusLabel.Content = STATUS_DEVICE_FOUND_READY;
            dropLabel.Content = "Drag your app's APK file\nhere to install.";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}
