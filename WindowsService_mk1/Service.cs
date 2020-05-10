using System.Diagnostics;
using System.ServiceProcess;
using System.Timers;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System;
using Microsoft.Win32;
using System.Windows;
using System.Security.AccessControl;
using WindowsService_AlianceRacorder_sazonov.rtsp;

namespace WindowsService_AlianceRacorder_sazonov
{
    public partial class AlianceRacorder_sazonov : ServiceBase
    {
        private SimpleHTTPServer myServer;
        //private int eventId = 1;
        private camsInfo recorder_data;
        private EventLog EVENT_LOG;

        private recorderInfo RECORDER_DATA;
        private recorder_CAMS[] RECORDER_CAMS;
        private string CURRENT_DIR = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        private string CURRENT_DATA_DIR = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "data");
        private string CURRENT_INT_DIR = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "interface");

        public AlianceRacorder_sazonov()
        {
            InitializeComponent();

            PrepairLog();
            GetData();
            SetRegestryKey();
        }

        protected override void OnStart(string[] args)
        {
            // Update the service state to Start Pending after 10 sec.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
            serviceStatus.dwWaitHint = 10000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            //create server with auto assigned port
            myServer = new SimpleHTTPServer(RECORDER_DATA, RECORDER_CAMS, EVENT_LOG, CURRENT_DIR, CURRENT_INT_DIR);
            EVENT_LOG.WriteEntry("Recorder command server is running on port: " + myServer.Port.ToString());

            // Update the service state to Running.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            EVENT_LOG.WriteEntry("In OnStart.");
        }

        protected override void OnStop()
        {
            // Update the service state to Stop Pending.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOP_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            myServer.Stop();

            // Update the service state to Stopped.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        protected override void OnContinue()
        {
            eventLog.WriteEntry("In OnContinue.");
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(System.IntPtr handle, ref ServiceStatus serviceStatus);

        public enum ServiceState
        {
            SERVICE_STOPPED = 0x00000001,
            SERVICE_START_PENDING = 0x00000002,
            SERVICE_STOP_PENDING = 0x00000003,
            SERVICE_RUNNING = 0x00000004,
            SERVICE_CONTINUE_PENDING = 0x00000005,
            SERVICE_PAUSE_PENDING = 0x00000006,
            SERVICE_PAUSED = 0x00000007,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ServiceStatus
        {
            public int dwServiceType;
            public ServiceState dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
        };

        private void SetRegestryKey() 
        {
            string APP_NAME = "Aliance Recorder";

            RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            RegistryKey APP_key = key.OpenSubKey(APP_NAME, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.FullControl);


            /*RegistryKey key = Registry.LocalMachine.OpenSubKey("SOFTWARE", true);
            RegistryKey APP_key  = key.OpenSubKey(APP_NAME, true);*/

            if (APP_key != null)
            {
                key.CreateSubKey(APP_NAME);
                key = key.OpenSubKey(APP_NAME, true);

                key.SetValue("APP_DIR", CURRENT_DIR);
            } 
        }

        private void GetData() {
            XmlConverter.Serializer ser = new XmlConverter.Serializer();
            string xmlpath = string.Empty;
            string xmlInputData = string.Empty;
            string xmlOutputData = string.Empty;

            try
            {
                xmlpath = Path.Combine(CURRENT_DATA_DIR, "cam.info");
                xmlInputData = File.ReadAllText(xmlpath);

                recorder_data = ser.Deserialize<camsInfo>(xmlInputData);

                foreach (camsInfoCam cam in recorder_data.cam)
                {
                    EVENT_LOG.WriteEntry("Есть камера - " + cam.CamName + ", адрес: " + cam.CamIP + "\nОписание: " + cam.CamDescription);
                }

                RECORDER_CAMS = new recorder_CAMS[recorder_data.cam.Length];
                for (int i = 0; i < recorder_data.cam.Length; i++)
                {
                    recorder_CAMS _cam = new recorder_CAMS();
                    _cam.camName = recorder_data.cam[i].CamName;
                    _cam.camIP = recorder_data.cam[i].CamIP;
                    _cam.camDescription = recorder_data.cam[i].CamDescription;
                    _cam.camLogin = recorder_data.cam[i].CamLogin;
                    _cam.camPassword = recorder_data.cam[i].CamPassword;
                    Uri URL_data = new Uri(recorder_data.cam[i].CamIP);
                    string camIPwithAuth = URL_data.Scheme + "://" + _cam.camLogin + ":" + _cam.camPassword + "@" + URL_data.Host + URL_data.PathAndQuery;
                    _cam.CamClient = new rtsp_client(camIPwithAuth, 0, recorder_data.recorder.recorderArchiveDir, EVENT_LOG, recorder_data.cam[i].CamName, recorder_data.cam[i].camAutoRecconect);
                    RECORDER_CAMS[i] = _cam;
                }

                RECORDER_DATA = recorder_data.recorder;
                EVENT_LOG.WriteEntry("Архив - " + RECORDER_DATA.recorderArchiveDir);
                EVENT_LOG.WriteEntry("Адрес веб сервера - " + RECORDER_DATA.recorderURL);
                EVENT_LOG.WriteEntry("Порт веб сервера - " + RECORDER_DATA.recorderURLPort);
            }
            catch (Exception ex)
            {
                EVENT_LOG.WriteEntry("Ошибка чтения настроек - " + ex.ToString());
            }
        }

        private void PrepairLog() {

            EVENT_LOG = new System.Diagnostics.EventLog();
            if (!System.Diagnostics.EventLog.SourceExists("AlianceRecord"))
            {
                System.Diagnostics.EventLog.CreateEventSource(
                    "AlianceRecord", "AlianceRecorderLog");
            } 
            EVENT_LOG.Source = "AlianceRacorder_sazonov";
            EVENT_LOG.Log = "AlianceRecorderLog";
        }
    }
}

#region Описание классов данных регистратора

public class recorder_CAMS
{
    public string camName;
    public string camIP;
    public string camDescription;
    public string camLogin;
    public string camPassword;
    public rtsp_client CamClient;
    public string nowRec;
    public bool camAutoRecconect;
}

// Примечание. Для запуска созданного кода может потребоваться NET Framework версии 4.5 или более поздней версии и .NET Core или Standard версии 2.0 или более поздней.
/// <remarks/>
[System.SerializableAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
[System.Xml.Serialization.XmlRootAttribute(Namespace = "", IsNullable = false)]
public partial class camsInfo
{
    private recorderInfo recorderField;
    private camsInfoCam[] camField;

    /// <remarks/>
    [System.Xml.Serialization.XmlElementAttribute("cam")]
    public camsInfoCam[] cam
    {
        get
        {
            return this.camField;
        }
        set
        {
            this.camField = value;
        }
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlElementAttribute("recorder")]
    public recorderInfo recorder
    {
        get
        {
            return this.recorderField;
        }
        set
        {
            this.recorderField = value;
        }
    }
}

/// <remarks/>
[System.SerializableAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
public partial class camsInfoCam
{
    private string camNameField;
    private string camIPField;
    private string camDescriptionField;
    private string camLoginField;
    private string camPasswordField;
    private bool camAutoRecconectField;

    public string CamName
    {
        get
        {
            return this.camNameField;
        }
        set
        {
            this.camNameField = value;
        }
    }
    public string CamIP
    {
        get
        {
            return this.camIPField;
        }
        set
        {
            this.camIPField = value;
        }
    }
    public string CamDescription
    {
        get
        {
            return this.camDescriptionField;
        }
        set
        {
            this.camDescriptionField = value;
        }
    }
    public string CamLogin
    {
        get
        {
            return this.camLoginField;
        }
        set
        {
            this.camLoginField = value;
        }
    }
    public string CamPassword
    {
        get
        {
            return this.camPasswordField;
        }
        set
        {
            this.camPasswordField = value;
        }
    }
    public bool camAutoRecconect
    {
        get
        {
            return this.camAutoRecconectField;
        }
        set
        {
            this.camAutoRecconectField = value;
        }
    }
}

public partial class recorderInfo 
{
    private string recorderURLField;
    private string recorderURLPortField;
    private string recorderLoginField;
    private string recorderPasswordField;
    private string recorderArchiveDirField;

    public string recorderURL 
    {
        get
        {
            return this.recorderURLField;
        }
        set
        {
            this.recorderURLField = value;
        }
    }
    public string recorderURLPort 
    {
        get
        {
            return this.recorderURLPortField;
        }
        set
        {
            this.recorderURLPortField = value;
        }
    }
    public string recorderLogin
    {
        get
        {
            return this.recorderLoginField;
        }
        set
        {
            this.recorderLoginField = value;
        }
    }
    public string recorderPassword
    {
        get
        {
            return this.recorderPasswordField;
        }
        set
        {
            this.recorderPasswordField = value;
        }
    }
    public string recorderArchiveDir
    {
        get
        {
            return this.recorderArchiveDirField;
        }
        set
        {
            this.recorderArchiveDirField = value;
        }
    }
}

#endregion