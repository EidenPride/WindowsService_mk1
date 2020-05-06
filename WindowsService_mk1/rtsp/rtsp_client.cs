using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;

namespace WindowsService_AlianceRacorder_sazonov.rtsp
{
    public class rtsp_client
    {
        //параметры
        private EventLog EVENT_LOG;

        private int bufferization = 0;
        private string video_archive_path = "";
        private string rstp_url = "";
        private string video_rec_uid = "";
        private string session_time_stamp = "";

        private FileStream fs_v = null;   // файловый поток для записи видео
        private FileStream fs_a = null;   // файловый поток для записи аудио

        private string stream_video_format = "";
        private string stream_audio_format = "";

        private byte[] vps = null;
        private byte[] sps = null;
        private byte[] pps = null;

        rtsp_connector rtsp_con = null;

        #region Инициализация и внешние функции

        //конструктор класса
        public rtsp_client(string Cam_URL, int buff, string archive, EventLog _event_log)
        {
            set_buffer(buff);
            set_archive_dir(archive);
            this.EVENT_LOG = _event_log;            
            this.rstp_url = Cam_URL;
            this.session_time_stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // создадим коннектор потока
            rtsp_con = new rtsp_connector();

            //Видео блок
            // Получим SPS/PPS из SDP описания
            // или из видео потока H264
            rtsp_con.Received_SPS_PPS += (byte[] _sps, byte[] _pps) => {
                stream_video_format = "h264";
                sps = _sps;
                pps = _pps;
            };
            rtsp_con.Received_VPS_SPS_PPS += (byte[] _vps, byte[] _sps, byte[] _pps) => {
                stream_video_format = "h265";
                vps = _vps;
                sps = _sps;
                pps = _pps;
            };
            // NALs. Так-же могут включать SPS/PPS для H264
            rtsp_con.Received_NALs += (List<byte[]> nal_units) => {
                if (fs_v != null)
                {
                    foreach (byte[] nal_unit in nal_units)
                    {
                        fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Запись начального значения
                        fs_v.Write(nal_unit, 0, nal_unit.Length);                 // Запись NAL
                    }
                    fs_v.Flush(true);
                }
            };           

            //Звуковой блок
            rtsp_con.Received_G711 += (string format, List<byte[]> g711) => {
                if (format.Equals("PCMU"))
                {
                    stream_audio_format = "PCMU";
                }
                else if (format.Equals("PCMA"))
                {
                    stream_audio_format = "PCMA";
                }
                if (fs_a != null)
                {
                    foreach (byte[] data in g711)
                    {
                        fs_a.Write(data, 0, data.Length);
                    }
                }
            };
            rtsp_con.Received_AMR += (string format, List<byte[]> amr) => {
                if (format.Equals("AMR"))
                {
                    stream_audio_format = "AMR";
                }
                if (fs_a != null)
                {
                    foreach (byte[] data in amr)
                    {
                        fs_a.Write(data, 0, data.Length);
                    }
                }
            };
            rtsp_con.Received_AAC += (string format, List<byte[]> aac, uint ObjectType, uint FrequencyIndex, uint ChannelConfiguration) => {
                stream_audio_format = "AAC";
                if (fs_a != null)
                {
                    foreach (byte[] data in aac)
                    {
                        // ASDT header format
                        int protection_absent = 1;
                        //                        int profile = 2; // Profile 2 = AAC Low Complexity (LC)
                        //                        int sample_freq = 4; // 4 = 44100 Hz
                        //                        int channel_config = 2; // 2 = Stereo

                        Rtsp.BitStream bs = new Rtsp.BitStream();
                        bs.AddValue(0xFFF, 12); // (a) Start of data
                        bs.AddValue(0, 1); // (b) Version ID, 0 = MPEG4
                        bs.AddValue(0, 2); // (c) Layer always 2 bits set to 0
                        bs.AddValue(protection_absent, 1); // (d) 1 = No CRC
                        bs.AddValue((int)ObjectType - 1, 2); // (e) MPEG Object Type / Profile, minus 1
                        bs.AddValue((int)FrequencyIndex, 4); // (f)
                        bs.AddValue(0, 1); // (g) private bit. Always zero
                        bs.AddValue((int)ChannelConfiguration, 3); // (h)
                        bs.AddValue(0, 1); // (i) originality
                        bs.AddValue(0, 1); // (j) home
                        bs.AddValue(0, 1); // (k) copyrighted id
                        bs.AddValue(0, 1); // (l) copyright id start
                        bs.AddValue(data.Length + 7, 13); // (m) AAC data + size of the ASDT header
                        bs.AddValue(2047, 11); // (n) buffer fullness ???
                        int num_acc_frames = 1;
                        bs.AddValue(num_acc_frames - 1, 1); // (o) num of AAC Frames, minus 1

                        // If Protection was On, there would be a 16 bit CRC
                        if (protection_absent == 0) bs.AddValue(0xABCD /*CRC*/, 16); // (p)

                        byte[] header = bs.ToArray();

                        fs_a.Write(header, 0, header.Length);
                        fs_a.Write(data, 0, data.Length);
                    }
                }
            };

            // Подключиться к источнику потока
            rtsp_con.Connect(rstp_url, rtsp_connector.RTP_TRANSPORT.TCP);
        }

        // внешние функции
        public void set_Rec_UID(string Rec_UID) {
            this.video_rec_uid = Rec_UID;
        }
        public string get_Session_TimeStamp() {
            return session_time_stamp;
        }
        public void play() 
        {
            if (rtsp_con != null)
            {
                if (!rtsp_con.StreamingFinished())
                {
                    rtsp_con.Play();
                    EVENT_LOG.WriteEntry("RTSP clinet play");
                }
            }
        }
        public void pause()
        {
            if (rtsp_con != null)
            {
                if (!rtsp_con.StreamingFinished())
                {
                    rtsp_con.Pause();
                }
            }
        }
        public void stop()
        {
            if (rtsp_con != null)
            {
                if (!rtsp_con.StreamingFinished())
                {
                    rtsp_con.Stop();
                }
            }
        }
        public void rec(string _video_rec_uid)
        {
            if (!this.video_rec_uid.Equals(_video_rec_uid))
            {
                if (!this.video_rec_uid.Equals(""))
                {
                    stop_rec(this.video_rec_uid);
                }
            }
            else 
            {
                return;
            }

            this.video_rec_uid = _video_rec_uid;
            string filename = Path.Combine(video_archive_path, "rtsp_" + session_time_stamp + "_" + video_rec_uid);
            if (rtsp_con != null)
            {
                if (!rtsp_con.StreamingFinished())
                {
                    if (!stream_video_format.Equals(""))
                    {
                        switch (stream_video_format)
                        {
                            case "h264":
                                if (fs_v == null)
                                {
                                    filename += ".264";
                                    fs_v = new FileStream(filename, FileMode.Create);
                                }
                                if (fs_v != null)
                                {
                                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);
                                    fs_v.Write(sps, 0, sps.Length);
                                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);
                                    fs_v.Write(pps, 0, pps.Length);
                                    fs_v.Flush(true);
                                }
                                break;
                            case "h265":
                                if (fs_v == null)
                                {
                                    filename += ".265";
                                    fs_v = new FileStream(filename, FileMode.Create);
                                }
                                if (fs_v != null)
                                {
                                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);
                                    fs_v.Write(vps, 0, vps.Length);
                                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);
                                    fs_v.Write(sps, 0, sps.Length);
                                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);
                                    fs_v.Write(pps, 0, pps.Length);
                                    fs_v.Flush(true);
                                }
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        //TODO - log нет формата
                    }

                    if (!stream_audio_format.Equals(""))
                    {
                        switch (stream_audio_format)
                        {
                            case "PCMU":
                                if (fs_a == null)
                                {
                                    filename += ".ul";
                                    fs_a = new FileStream(filename, FileMode.Create);
                                }
                                break;
                            case "PCMA":
                                if (fs_a == null)
                                {
                                    filename += ".al";
                                    fs_a = new FileStream(filename, FileMode.Create);
                                }
                                break;
                            case "AMR":
                                if (fs_a == null)
                                {
                                    filename += ".amr";
                                    fs_a = new FileStream(filename, FileMode.Create);
                                    byte[] header = new byte[] { 0x23, 0x21, 0x41, 0x4D, 0x52, 0x0A }; // #!AMR<0x0A>
                                    fs_a.Write(header, 0, header.Length);
                                }
                                break;
                            case "AAC":
                                if (fs_a == null)
                                {
                                    filename += ".aac";
                                    fs_a = new FileStream(filename, FileMode.Create);
                                }
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        //TODO - log нет формата
                    }
                }
            }

        }
        public void stop_rec(string _video_rec_uid) 
        {
            video_rec_uid = "";
            if (fs_v != null) 
            {
                fs_v.Close();
            }
            fs_v = null;
            if (fs_a != null)
            {
                fs_a.Close();
            }
            fs_a = null;

            /*stream_video_format = "";
            stream_audio_format = "";*/
        }
        public void set_buffer(int buff) 
        {
            if (buff >= 0)
            {
                this.bufferization = buff;
            }
        }
        public void set_archive_dir(string path) 
        {
            if (Directory.Exists(path))
            {
                this.video_archive_path = path;
            }
        }

        #endregion
    }
}
