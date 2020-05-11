using LiteDB;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsService_AlianceRacorder_sazonov.DB
{
    class DB_int
    {
        private string CURRENT_DATA_DIR;
        private EventLog EVENT_LOG;

        private LiteDatabase db;

        //DB - классы
        private class RecorderSetup 
        {
            public string SetupName { get; set; }
            public string SetupValstring { get; set; }
            public int SetupValint { get; set; }
            public bool SetupValbool { get; set; }
            public DateTime SetupValDateTime { get; set; }
            public long SetupVallong { get; set; }
        }
        private class CamsSetup
        {
            public string CamID { get; set; }
            public string CamName { get; set; }
            public string CamIP { get; set; }
            public string CamDescription { get; set; }
            public string CamLogin { get; set; }
            public string CamPassword { get; set; }
            public bool camAutoRecconect { get; set; }
        }
        //DB - классы

        public DB_int(string _CURRENT_DATA_DIR, EventLog _EVENT_LOG)
        {
            this.CURRENT_DATA_DIR = _CURRENT_DATA_DIR;
            this.EVENT_LOG = _EVENT_LOG;

            init();    
        }

        private void init()
        {
            db = new LiteDatabase(@CURRENT_DATA_DIR+@"\data.dat");
            basefirststart();
        }
        private void basefirststart()
        {
            IEnumerable<string> collections = db.GetCollectionNames(); 
            if (collections.Count() == 0) { preloadsetup(); }
        }
        public recorderInfo GetRecorderSetup()
        {
            recorderInfo rec_info = new recorderInfo();
            var collection = db.GetCollection<RecorderSetup>("RecorderSetup");
            var query = collection.FindAll();
            foreach (var setup in query)
            {
                switch (setup.SetupName)
                {
                    case "recorderURL":
                        rec_info.recorderURL = setup.SetupValstring;
                        break;
                    case "recorderURLPort":
                        rec_info.recorderURLPort = setup.SetupValint;
                        break;
                    case "recorderLogin":
                        rec_info.recorderLogin = setup.SetupValstring;
                        break;
                    case "recorderPassword":
                        rec_info.recorderPassword = setup.SetupValstring;
                        break;
                    case "recorderArchiveDir":
                        rec_info.recorderArchiveDir = setup.SetupValstring;
                        break;
                    default:
                        break;
                }
            }

            return rec_info;
        }
        public camsInfoCam[] GetCamsArray() {
            var collection = db.GetCollection<CamsSetup>("CamsSetup");
            var query = collection.FindAll();
            camsInfoCam[] CamsArray = new camsInfoCam[query.Count()];
            int i = 0;
            foreach (var _cam in query)
            {
                camsInfoCam cam = new camsInfoCam();
                cam.CamUID = _cam.CamID;
                cam.CamName = _cam.CamName;
                cam.CamIP = _cam.CamIP;
                cam.CamDescription = _cam.CamDescription;
                cam.CamLogin = _cam.CamLogin;
                cam.CamPassword = _cam.CamPassword;
                cam.camAutoRecconect = _cam.camAutoRecconect;
                CamsArray[i] = cam;
                i++;
            }

            return null;
        }
        private void preloadsetup()
        {
            var _RecorderSetup = db.GetCollection<RecorderSetup>("RecorderSetup");
            // Create unique index in SetupName field
            _RecorderSetup.EnsureIndex(x => x.SetupName, true);

            var set_recorderURL = new RecorderSetup
            {
                SetupName = "recorderURL",
                SetupValstring = ""
            };
            _RecorderSetup.Insert(set_recorderURL);
            var set_recorderURLPort = new RecorderSetup
            {
                SetupName = "recorderURLPort",
                SetupValint = 8085
            };
            _RecorderSetup.Insert(set_recorderURLPort);
            var set_recorderLogin = new RecorderSetup
            {
                SetupName = "recorderLogin",
                SetupValstring = "admin"
            };
            _RecorderSetup.Insert(set_recorderLogin);
            var set_recorderPassword = new RecorderSetup
            {
                SetupName = "recorderPassword",
                SetupValstring = ""
            };
            _RecorderSetup.Insert(set_recorderPassword);
            var set_recorderArchiveDir = new RecorderSetup
            {
                SetupName = "recorderArchiveDir",
                SetupValstring = @"F:\cam_arch"
            };
            _RecorderSetup.Insert(set_recorderArchiveDir);

            //Потом удалить нахуй!!! только для теста
            var _CamsSetup = db.GetCollection<CamsSetup>("CamsSetup");
            // Create unique index in SetupName field
            _CamsSetup.EnsureIndex(x => x.CamID, true);
            var set_Cam = new CamsSetup
            {
                CamID = Guid.NewGuid().ToString(),
                CamName = "Camera1",
                CamIP = "rtsp://192.168.1.16",
                CamDescription = "Camera1 - подвал",
                CamLogin = "admin",
                CamPassword = "123456",
                camAutoRecconect = false
            };
            _CamsSetup.Insert(set_Cam);
            set_Cam = new CamsSetup
            {
                CamID = Guid.NewGuid().ToString(),
                CamName = "Camera2",
                CamIP = "rtsp://192.168.1.17",
                CamDescription = "Camera2 - подвал",
                CamLogin = "admin",
                CamPassword = "123456",
                camAutoRecconect = false
            };
            _CamsSetup.Insert(set_Cam);
            //Потом удалить нахуй!!! только для теста
        }
    }
}
