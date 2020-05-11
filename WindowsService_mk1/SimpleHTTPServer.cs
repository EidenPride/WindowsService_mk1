// MIT License - Copyright (c) 2016 Can Güney Aksakalli
// https://aksakalli.github.io/2014/02/24/simple-http-server-with-csparp.html

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Threading;
using System.Diagnostics;
using WindowsService_AlianceRacorder_sazonov.rtsp;
using Newtonsoft.Json;

class SimpleHTTPServer
{
    private readonly string[] _indexFiles = {
        "index.html",
        "index.htm",
        "default.html",
        "default.htm"
    };

    private static IDictionary<string, string> _mimeTypeMappings = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) {
        #region extension to MIME type list
        {".asf", "video/x-ms-asf"},
        {".asx", "video/x-ms-asf"},
        {".avi", "video/x-msvideo"},
        {".bin", "application/octet-stream"},
        {".cco", "application/x-cocoa"},
        {".crt", "application/x-x509-ca-cert"},
        {".css", "text/css"},
        {".deb", "application/octet-stream"},
        {".der", "application/x-x509-ca-cert"},
        {".dll", "application/octet-stream"},
        {".dmg", "application/octet-stream"},
        {".ear", "application/java-archive"},
        {".eot", "application/octet-stream"},
        {".exe", "application/octet-stream"},
        {".flv", "video/x-flv"},
        {".gif", "image/gif"},
        {".hqx", "application/mac-binhex40"},
        {".htc", "text/x-component"},
        {".htm", "text/html"},
        {".html", "text/html"},
        {".ico", "image/x-icon"},
        {".img", "application/octet-stream"},
        {".iso", "application/octet-stream"},
        {".jar", "application/java-archive"},
        {".jardiff", "application/x-java-archive-diff"},
        {".jng", "image/x-jng"},
        {".jnlp", "application/x-java-jnlp-file"},
        {".jpeg", "image/jpeg"},
        {".jpg", "image/jpeg"},
        {".js", "application/x-javascript"},
        {".mml", "text/mathml"},
        {".mng", "video/x-mng"},
        {".mov", "video/quicktime"},
        {".mp3", "audio/mpeg"},
        {".mpeg", "video/mpeg"},
        {".mpg", "video/mpeg"},
        {".msi", "application/octet-stream"},
        {".msm", "application/octet-stream"},
        {".msp", "application/octet-stream"},
        {".pdb", "application/x-pilot"},
        {".pdf", "application/pdf"},
        {".pem", "application/x-x509-ca-cert"},
        {".pl", "application/x-perl"},
        {".pm", "application/x-perl"},
        {".png", "image/png"},
        {".prc", "application/x-pilot"},
        {".ra", "audio/x-realaudio"},
        {".rar", "application/x-rar-compressed"},
        {".rpm", "application/x-redhat-package-manager"},
        {".rss", "text/xml"},
        {".run", "application/x-makeself"},
        {".sea", "application/x-sea"},
        {".shtml", "text/html"},
        {".sit", "application/x-stuffit"},
        {".swf", "application/x-shockwave-flash"},
        {".tcl", "application/x-tcl"},
        {".tk", "application/x-tcl"},
        {".txt", "text/plain"},
        {".war", "application/java-archive"},
        {".wbmp", "image/vnd.wap.wbmp"},
        {".wmv", "video/x-ms-wmv"},
        {".xml", "text/xml"},
        {".xpi", "application/x-xpinstall"},
        {".zip", "application/zip"},
        #endregion
    };
    private string CURRENT_DIR;
    private string CURRENT_INT_DIR;
    private DirectoryInfo RECORDER_LIBDIR;
    private EventLog EVENT_LOG; 
    private string REC_FOLDER;
    private recorder_CAMS[] RECORDER_CAMS;

    private Thread _serverThread;
    private HttpListener _listener;
    private int _port;

    static private string CAM_FUNC_REC = "rec";
    static private string CAM_FUNC_STOP = "stop";
    //static private string CAM_FUNC_PAUSE = "pause";

    public int Port
    {
        get { return _port; }
        private set { }
    }

    public SimpleHTTPServer(recorderInfo RECORDER_DATA, recorder_CAMS[] RECORDER_CAMS, EventLog EVENT_LOG, string CURRENT_DIR, string CURRENT_INT_DIR)
    {
        int port;

        if (RECORDER_DATA.recorderURLPort == null)
        {
            EVENT_LOG.WriteEntry("Не удалось получить порт - " + RECORDER_DATA.recorderURLPort + ", запушен порт - 8089");
            port = 8089;
        }
        else
        {
            //get an empty port
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
        }
        
        this.Initialize(port, RECORDER_DATA, RECORDER_CAMS, EVENT_LOG, CURRENT_DIR, CURRENT_INT_DIR);
    }

    public void Stop()
    {
        // Остановить запись на всех камерах

        // Остановить веб сервер
        _serverThread.Abort();
        _listener.Stop();
    }

    private void Listen()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://*:" + _port.ToString() + "/");
        _listener.Start();
        while (true)
        {
            try
            {
                HttpListenerContext context = _listener.GetContext();
                Process(context);
            }
            catch (Exception ex)
            {
                EVENT_LOG.WriteEntry("Error listen - " + ex.ToString());
            }
        }
    }

    private void Process(HttpListenerContext context)
    {
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET");
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;

        string filename = context.Request.Url.AbsolutePath;
        string localpath = context.Request.Url.LocalPath;
        string query = context.Request.Url.Query;
        string pathandquery = context.Request.Url.PathAndQuery;

        filename = filename.Substring(1);

        if (string.IsNullOrEmpty(filename))
        {
            foreach (string indexFile in _indexFiles)
            {
                if (File.Exists(Path.Combine(CURRENT_INT_DIR, indexFile)))
                {
                    filename = indexFile;
                    break;
                }
            }
        }
        filename = Path.Combine(CURRENT_INT_DIR, filename);
        if (File.Exists(filename))
        {
            try
            {
                Stream input = new FileStream(filename, FileMode.Open);

                //Adding permanent http response headers
                string mime;
                context.Response.ContentType = _mimeTypeMappings.TryGetValue(Path.GetExtension(filename), out mime) ? mime : "application/octet-stream";
                context.Response.ContentLength64 = input.Length;
                context.Response.AddHeader("Date", DateTime.Now.ToString("r"));
                context.Response.AddHeader("Last-Modified", System.IO.File.GetLastWriteTime(filename).ToString("r"));

                byte[] buffer = new byte[1024 * 16];
                int nbytes;
                while ((nbytes = input.Read(buffer, 0, buffer.Length)) > 0)
                    context.Response.OutputStream.Write(buffer, 0, nbytes);
                input.Close();

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.OutputStream.Flush();
            }
            catch (Exception ex)
            {
                EVENT_LOG.WriteEntry("Error read input - " + ex.ToString());
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }

        }
        else if (localpath.IndexOf("camcommand") != -1)
        {
            string _cam = "";
            string _action = "";
            string _uid = "";

            string[] commands = query.Split('&');

            foreach (string command in commands)
            {
                if (command.IndexOf("cam") != -1)
                {
                    _cam = command.Substring(command.IndexOf("cam") + 4);
                }
                else if (command.IndexOf("action") != -1)
                {
                    _action = command.Substring(command.IndexOf("action") + 7);
                }
                else if (command.IndexOf("uid") != -1)
                {
                    _uid = command.Substring(command.IndexOf("uid") + 4);
                }
            }

            EVENT_LOG.WriteEntry("Cam action - " + _action);

            if (_action.Equals(CAM_FUNC_REC))
            {
                try
                {
                    var destination = Path.Combine(REC_FOLDER, "Rec_" + _uid + ".mpg");
                    EVENT_LOG.WriteEntry("Cam - " + _cam);
                    recorder_CAMS camData = GetCamPlayerByID(_cam);
                    EVENT_LOG.WriteEntry("camData - " + camData);

                    if (camData != null)
                    {
                        CameraJSONAnswer reply = new CameraJSONAnswer();
                        reply.CamID = _cam;
                        reply.CamAction = _action;

                        EVENT_LOG.WriteEntry("Cam action - " + destination);
                        if (camData.CamClient.cam_online())
                        {
                            camData.CamClient.rec(_uid);
                            camData.nowRec = destination;
                            context.Response.ContentType = "application/json";
                            reply.CamStatus = "Camera - Online, start recording stream to file";
                        }
                        else
                        {
                            reply.CamStatus = "Camera - OffLine, please check camera connection";
                        }
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(reply, Formatting.Indented));
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    }

                }
                catch (Exception ex)
                {
                    EVENT_LOG.WriteEntry("Ошибка VLC - " + ex.ToString());
                }
            }
            else if (_action.Equals(CAM_FUNC_STOP))
            {
                recorder_CAMS camData = GetCamPlayerByID(_cam);
                camData.CamClient.stop_rec(_uid);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }

            context.Response.OutputStream.Close();
        }
        else if (localpath.IndexOf("servercommand") != -1)
        {

            string[] commands = query.Split('&');

            foreach (string command in commands)
            {
                if (command.IndexOf("status") != -1)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                }
            }
            
            context.Response.OutputStream.Close();
        };
    }

    private recorder_CAMS GetCamPlayerByID(string CamID) {
        recorder_CAMS camData = null;
        foreach (recorder_CAMS _camData in RECORDER_CAMS) 
        {
            if (_camData.camName.Equals(CamID)) 
            {
                //EVENT_LOG.WriteEntry("VLC find cam - " + CamID);
                camData = _camData;
            };
        }

        return camData;
    }

    private void Initialize(int port, recorderInfo RECORDER_DATA, recorder_CAMS[] RECORDER_CAMS, EventLog EVENT_LOG, string CURRENT_DIR, string CURRENT_INT_DIR)
    {
        this.CURRENT_DIR = CURRENT_DIR;
        this.CURRENT_INT_DIR = CURRENT_INT_DIR;
        this.EVENT_LOG = EVENT_LOG;
        this.RECORDER_CAMS = RECORDER_CAMS;
        this.REC_FOLDER = RECORDER_DATA.recorderArchiveDir;
        this.RECORDER_LIBDIR = new DirectoryInfo(Path.Combine(CURRENT_DIR, "libvlc", IntPtr.Size == 4 ? "win-x86" : "win-x64"));

        this._port = port;
        
        _serverThread = new Thread(this.Listen);
        _serverThread.Start();
    }

    public class CameraJSONAnswer
    {
        public string CamID { get; set; }
        public string CamAction { get; set; }
        public string CamStatus { get; set; }
    }
}