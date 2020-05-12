using System.Diagnostics;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System;
using Microsoft.Win32;
using System.Security.AccessControl;
using WindowsService_AlianceRacorder_sazonov.rtsp;
using WindowsService_AlianceRacorder_sazonov.DB;

namespace WindowsService_AlianceRacorder_sazonov
{
    public partial class AlianceRacorder_sazonov : ServiceBase
    {
        private SimpleHTTPServer myServer;
        //private int eventId = 1;
        private EventLog EVENT_LOG;
        private storage_int ST;

        private RecorderSetup RECORDER_DATA;
        private recorder_CAMS[] RECORDER_CAMS;
        private string CURRENT_DIR = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        private string CURRENT_DATA_DIR = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "data");
        private string CURRENT_INT_DIR = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "interface");

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
        public class RecorderSetup
        {
            public string recorderURL { get; set; }
            public int recorderURLPort { get; set; }
            public string recorderLogin { get; set; }
            public string recorderPassword { get; set; }
            public string recorderArchiveDir { get; set; }
        }
        public class CamsSetup
        {
            public string CamID { get; set; }
            public string CamName { get; set; }
            public string CamIP { get; set; }
            public string CamDescription { get; set; }
            public string CamLogin { get; set; }
            public string CamPassword { get; set; }
            public bool camAutoRecconect { get; set; }
        }

        #endregion

        public AlianceRacorder_sazonov()
        {
            InitializeComponent();
            ST = new storage_int(CURRENT_DATA_DIR, EVENT_LOG);

            PrepairLog();
            GetData();
            SetRegestryKey();
        }

        #region Управление службой
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

            EVENT_LOG.WriteEntry("Служба успешно запущена");
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
        #endregion

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
            try
            {
                CamsSetup[] cams = ST.GetCamsArray();
                RECORDER_DATA = ST.GetRecorderSetup();

                string log = "";
                log += "------------------------------------------\n";
                log += "данные регистратора\n";
                log += "------------------------------------------\n";
                log += "Архив - " + RECORDER_DATA.recorderArchiveDir + "\n";
                log += "Адрес веб сервера - " + RECORDER_DATA.recorderURL + "\n";
                log += "Порт веб сервера - " + RECORDER_DATA.recorderURLPort + "\n";
                
                if (cams.Length > 0)
                {
                    log += "------------------------------------------\n";
                    log += "данные по камерам\n";
                    log += "------------------------------------------\n";

                    foreach (CamsSetup cam in cams)
                    {
                        log += "Камера - " + cam.CamName + ", адрес: " + cam.CamIP + ", идентификатор: " + cam.CamID + "\nОписание: " + cam.CamDescription + "\n\n";
                    }
                }
                
                EVENT_LOG.WriteEntry(log);

                RECORDER_CAMS = new recorder_CAMS[cams.Length];
                for (int i = 0; i < cams.Length; i++)
                {
                    recorder_CAMS _cam = new recorder_CAMS();
                    _cam.camName = cams[i].CamName;
                    _cam.camIP = cams[i].CamIP;
                    _cam.camDescription = cams[i].CamDescription;
                    _cam.camLogin = cams[i].CamLogin;
                    _cam.camPassword = cams[i].CamPassword;
                    Uri URL_data = new Uri(cams[i].CamIP);
                    string camIPwithAuth = URL_data.Scheme + "://" + _cam.camLogin + ":" + _cam.camPassword + "@" + URL_data.Host + URL_data.PathAndQuery;
                    _cam.CamClient = new rtsp_client(camIPwithAuth, 0, RECORDER_DATA.recorderArchiveDir, EVENT_LOG, cams[i].CamName, cams[i].camAutoRecconect);
                    RECORDER_CAMS[i] = _cam;
                }      
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