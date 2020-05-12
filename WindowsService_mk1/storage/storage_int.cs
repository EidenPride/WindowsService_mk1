using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using static WindowsService_AlianceRacorder_sazonov.AlianceRacorder_sazonov;

namespace WindowsService_AlianceRacorder_sazonov.DB
{
    class storage_int
    {
        private string CURRENT_DATA_DIR;
        private EventLog EVENT_LOG;

        private string recorder_data = "rec_dat";
        private string cams_data = "cams_dat";
        private string RECORDER_DATA_FILE;
        private string CAMS_DATA_FILE;

        public storage_int(string _CURRENT_DATA_DIR, EventLog _EVENT_LOG)
        {
            this.CURRENT_DATA_DIR = _CURRENT_DATA_DIR;
            this.EVENT_LOG = _EVENT_LOG;

            init();    
        }

        private void init()
        {
            //ПолучитьФайл регистратора
            RECORDER_DATA_FILE = Path.Combine(CURRENT_DATA_DIR, recorder_data);
            if (File.Exists(RECORDER_DATA_FILE))
            {
                //Проверить файл настроек регистратора
                if (!RecDataChecked())
                {
                    File.Delete(RECORDER_DATA_FILE);
                    //Создать дефолтный файл настроек регика
                    CreateRecData();
                }
            }
            else
            {
                //Создать дефолтный файл настроек регика
                CreateRecData();
            }

            //ПолучитьФайл камер
            CAMS_DATA_FILE = Path.Combine(CURRENT_DATA_DIR, cams_data);
            if (File.Exists(CAMS_DATA_FILE))
            {
                //Проверить файл настроек камер
                if (!CamsDataChecked())
                {
                    File.Delete(CAMS_DATA_FILE);
                    //Создать дефолтный файл настроек камер
                    CreateCamsData();
                }
            }
            else
            {
                //Создать дефолтный файл настроек камер
                CreateCamsData();
            }
        }

        public RecorderSetup GetRecorderSetup()
        {
            string json = File.ReadAllText(RECORDER_DATA_FILE);

            return JsonConvert.DeserializeObject<RecorderSetup>(json);
        }
        public CamsSetup[] GetCamsArray() {
            string json = File.ReadAllText(CAMS_DATA_FILE);

            return JsonConvert.DeserializeObject<CamsSetup[]>(json);
        }     
        //Приватные функции
        /*private void DB_insert_RecorderSetup(string Recorder_Data, bool async = false) 
        {
            var Collection = DB.GetCollection<BsonDocument>("RecorderSetup");
            var doc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(Recorder_Data);
            if (async)
            {
                Collection.InsertOneAsync(doc);
            }
            else
            {
                Collection.InsertOne(doc);
            }
        }
        private void DB_insert_CameraSetup(string Cam_Data, bool async = false)
        {
            var Collection = DB.GetCollection<BsonDocument>("CamsSetup");
            var doc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(Cam_Data);
            if (async)
            {
                Collection.InsertOneAsync(doc);
            }
            else
            {
                Collection.InsertOne(doc);
            }
        }*/
        private bool RecDataChecked()
        {
            bool check = false;
            string json = File.ReadAllText(RECORDER_DATA_FILE);
            try
            {
                RecorderSetup RecSetup = JsonConvert.DeserializeObject<RecorderSetup>(json);
                if (RecSetup.recorderURLPort != null && !RecSetup.recorderArchiveDir.Equals(""))
                {
                    check = true;
                }
            }
            catch (Exception Error)
            { 
                //TODO log
            }

            return check;
        }
        private void CreateRecData() 
        {
            RecorderSetup RecSetup = new RecorderSetup();
            RecSetup.recorderURL = "";
            RecSetup.recorderURLPort = 8085;
            RecSetup.recorderLogin = "admin";
            RecSetup.recorderPassword = "admin";
            RecSetup.recorderArchiveDir = @"F:\cam_arch";

            File.WriteAllText(@RECORDER_DATA_FILE, JsonConvert.SerializeObject(RecSetup, Formatting.Indented));
        }
        private bool CamsDataChecked()
        {
            bool check = false;
            string json = File.ReadAllText(CAMS_DATA_FILE);
            try
            {
                CamsSetup[] CamsSetup = JsonConvert.DeserializeObject<CamsSetup[]>(json);
                check = true;
            }
            catch (Exception Error)
            {
                //TODO log
            }

            return check;
        }
        private void CreateCamsData()
        {
            CamsSetup[] cams = new CamsSetup[2];
            //Потом удалить нахуй!!! только для теста
            CamsSetup CamSetup1 = new CamsSetup();
            CamSetup1.CamID = Guid.NewGuid().ToString();
            CamSetup1.CamName = "Camera1";
            CamSetup1.CamIP = "rtsp://192.168.1.16";
            CamSetup1.CamDescription = "Camera1 - подвал";
            CamSetup1.CamLogin = "admin";
            CamSetup1.CamPassword = "123456";
            CamSetup1.camAutoRecconect = false;
            cams[0] = CamSetup1;

            CamsSetup CamSetup2 = new CamsSetup();
            CamSetup2.CamID = Guid.NewGuid().ToString();
            CamSetup2.CamName = "Camera2";
            CamSetup2.CamIP = "rtsp://192.168.1.17";
            CamSetup2.CamDescription = "Camera2 - подвал";
            CamSetup2.CamLogin = "admin";
            CamSetup2.CamPassword = "123456";
            CamSetup2.camAutoRecconect = true;
            cams[1] = CamSetup2;

            File.WriteAllText(@CAMS_DATA_FILE, JsonConvert.SerializeObject(cams, Formatting.Indented));
            //Потом удалить нахуй!!! только для теста
        }
    }
}
