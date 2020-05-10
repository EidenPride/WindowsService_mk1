using Rtsp;
using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Timers;

namespace WindowsService_AlianceRacorder_sazonov.rtsp
{
    class rtsp_connector
    {
        #region Значения 
        // колбэки для приложения
        public event Received_SPS_PPS_Delegate Received_SPS_PPS;
        public event Received_VPS_SPS_PPS_Delegate Received_VPS_SPS_PPS;
        public event Received_NALs_Delegate Received_NALs;
        public event Received_G711_Delegate Received_G711;
        public event Received_AMR_Delegate Received_AMR;
        public event Received_AAC_Delegate Received_AAC;

        // делегирование функций колбэков
        public delegate void Received_SPS_PPS_Delegate(byte[] sps, byte[] pps); // H264
        public delegate void Received_VPS_SPS_PPS_Delegate(byte[] vps, byte[] sps, byte[] pps); // H265
        public delegate void Received_NALs_Delegate(List<byte[]> nal_units); // H264 or H265
        public delegate void Received_G711_Delegate(String format, List<byte[]> g711);
        public delegate void Received_AMR_Delegate(String format, List<byte[]> amr);
        public delegate void Received_AAC_Delegate(String format, List<byte[]> aac, uint ObjectType, uint FrequencyIndex, uint ChannelConfiguration);

        //Объекты библиотеки 
        H264Payload _H264Payload = new H264Payload();
        public enum RTP_TRANSPORT { UDP, TCP, MULTICAST, UNKNOWN };
        private enum RTSP_STATUS { WaitingToConnect, Connecting, ConnectFailed, Connected };

        // переменные
        RtspTcpTransport rtsp_socket = null; // RTSP соединение
        volatile RTSP_STATUS rtsp_socket_status = RTSP_STATUS.WaitingToConnect;
        RtspListener rtsp_listner = null;   // слушатель TCP потока RTSP
        RTP_TRANSPORT rtp_transport = RTP_TRANSPORT.UNKNOWN; // Вариант использования RTP по UDP или TCP использует RTSP сокет
        UDPSocket video_udp_pair = null;       // Видео UPD порты для RTP через UDP или MUSLICAST вариант
        UDPSocket audio_udp_pair = null;       // Аудио UPD порты для RTP через UDP или MUSLICAST вариант

        private String url = "";    // RTSP URL (логин & пароль будут вырезаны)
        String username = "";       // Логин
        String password = "";       // Пароль
        String hostname = "";       // RTSP Server хост или IP-адресс
        int port = 0;               // RTSP Server TCP Номер порта
       
        String session = "";        // RTSP Сессия
        String auth_type = null;
        String realm = null;
        String nonce = null;
        uint ssrc = 12345;
        
        Uri video_uri = null;            // URI используется для Видео
        int video_payload = -1;          // Payload Type для видео. (чаще 96 перове динамическое payload значение. Bosch использует 35)
        int video_data_channel = -1;     // RTP Номер канала для видео потока RTP или номер порта UDP
        int video_rtcp_channel = -1;     // RTP Номер канала для видео потока RTCP сообщений статуса или нмер порта UDP
        bool h264_sps_pps_fired = false; // Истина если SDP включает параметры sprop-Parameter-Set для H264 видео
        bool h265_vps_sps_pps_fired = false; // Истина если SDP включает sprop-vps, sprop-sps и sprop_pps для H265 видео
        string video_codec = "";         // Кодеки используемые с Payload Types 96..127 ("H264")

        Uri audio_uri = null;            // URI используется для Аудио
        int audio_payload = -1;          // Payload Type для аудио. (чаще 96 перове динамическое payload значение)
        int audio_data_channel = -1;     // RTP Номер канала для аудио потока RTP или номер порта UDP
        int audio_rtcp_channel = -1;     // RTP Номер канала для аудио потока RTCP сообщений статуса или нмер порта UDP
        string audio_codec = "";         // Кодеки используемые с Payload Types("PCMA" or "AMR")

        bool server_supports_get_parameter = false; // Используется c RTSP удержанием
        bool server_supports_set_parameter = false; // Используется c RTSP удержанием
        System.Timers.Timer keepalive_timer = null; // Используется c RTSP удержанием

        H264Payload h264Payload = null;
        H265Payload h265Payload = null;
        G711Payload g711Payload = new G711Payload();
        AMRPayload amrPayload = new AMRPayload();
        AACPayload aacPayload = null;

        byte[] vps = null;
        byte[] sps = null;
        byte[] pps = null;

        List<Rtsp.Messages.RtspRequestSetup> setup_messages = new List<Rtsp.Messages.RtspRequestSetup>(); // Сообщения SETUP

        #endregion

        #region Функции публичные
        public void Connect(String url, RTP_TRANSPORT rtp_transport) {
            RtspUtils.RegisterUri();
            //_logger.Debug("Connecting to " + url);
            this.url = url;
            // Используем URI для получения логина и пароля и формирования нового адреса
            try
            {
                Uri uri = new Uri(this.url);
                hostname = uri.Host;
                port = uri.Port;

                if (uri.UserInfo.Length > 0)
                {
                    username = uri.UserInfo.Split(new char[] { ':' })[0];
                    password = uri.UserInfo.Split(new char[] { ':' })[1];
                    this.url = uri.GetComponents((UriComponents.AbsoluteUri & ~UriComponents.UserInfo), UriFormat.UriEscaped);
                }
            }
            catch
            {
                username = null;
                password = null;
            }

            // Подключение к RTSP Серверу. RTSP сессия TCP соединение
            rtsp_socket_status = RTSP_STATUS.Connecting;
            try
            {
                rtsp_socket = new Rtsp.RtspTcpTransport(hostname, port);
            }
            catch
            {
                rtsp_socket_status = RTSP_STATUS.ConnectFailed;
                //_logger.Warn("Error - did not connect");
                return;
            }

            if (rtsp_socket.Connected == false)
            {
                rtsp_socket_status = RTSP_STATUS.ConnectFailed;
                //_logger.Warn("Error - did not connect");
                return;
            }

            rtsp_socket_status = RTSP_STATUS.Connected;

            // Создаем слушателя RTSP с использованием сокета для отправки RTSP собщений и прослушивания ответов
            rtsp_listner = new Rtsp.RtspListener(rtsp_socket);
            rtsp_listner.AutoReconnect = false; // авто переподключение
            rtsp_listner.MessageReceived += Rtsp_MessageReceived;
            rtsp_listner.DataReceived += Rtp_DataReceived;
            rtsp_listner.Start(); // Запуск слушателья, при получении сообщений испольщуется событие MessageReceived, данных DataReceived

            // Проверяем rtp транспорт
            // Если странсопрт RTP - TCP тогда, направляем пакеты в RTSP поток
            // Если странсопрт RTP - UDP тогда, инициализируем два сокета (один для видео, один для статус сообщений)
            // Если странсопрт RTP - MULTICAST тогда, ожидаем SETUP сообщение для получения message Multicast адреса от сервера RTSP
            this.rtp_transport = rtp_transport;
            if (rtp_transport == RTP_TRANSPORT.UDP)
            {
                video_udp_pair = new Rtsp.UDPSocket(50000, 51000); // создает диапазон в 500 пар (1000 адресов) для попытки использования
                video_udp_pair.DataReceived += Rtp_DataReceived;
                video_udp_pair.Start(); // начинаем слушать видео данные на UDP портах
                audio_udp_pair = new Rtsp.UDPSocket(50000, 51000); // // создает диапазон в 500 пар (1000 адресов) для попытки использования
                audio_udp_pair.DataReceived += Rtp_DataReceived;
                audio_udp_pair.Start(); // начинаем слушать аудио данные на UDP портах
            }
            if (rtp_transport == RTP_TRANSPORT.TCP)
            {
                // Балдеем.... Ничего не делаем данные. Данные придут в RTSP слушатель
            }
            if (rtp_transport == RTP_TRANSPORT.MULTICAST)
            {
                // Балдеем.... Открываетм мультикаст UDP сокет после комманды SETUP
            }

            // Отправляем OPTIONS
            // В полученом сообщении хендлер отправит DESCRIBE, SETUP и PLAY
            Rtsp.Messages.RtspRequest options_message = new Rtsp.Messages.RtspRequestOptions();
            options_message.RtspUri = new Uri(this.url);
            rtsp_listner.SendMessage(options_message);
        }

        // Вернет true если соединение отсутствует, или соеденино но сокет отвалился.
        public bool StreamingFinished()
        {
            if (rtsp_socket_status == RTSP_STATUS.ConnectFailed) return true;
            if (rtsp_socket_status == RTSP_STATUS.Connected && rtsp_socket.Connected == false) return true;
            else return false;
        }

        public void Pause()
        {
            if (rtsp_listner != null)
            {
                // Отправить PAUSE
                Rtsp.Messages.RtspRequest pause_message = new Rtsp.Messages.RtspRequestPause();
                pause_message.RtspUri = new Uri(url);
                pause_message.Session = session;
                if (auth_type != null)
                {
                    AddAuthorization(pause_message, username, password, auth_type, realm, nonce, url);
                }
                rtsp_listner.SendMessage(pause_message);
            }
        }

        public void Play()
        {
            if (rtsp_listner != null)
            {
                // Отправить PLAY
                Rtsp.Messages.RtspRequest play_message = new Rtsp.Messages.RtspRequestPlay();
                play_message.RtspUri = new Uri(url);
                play_message.Session = session;
                if (auth_type != null)
                {
                    AddAuthorization(play_message, username, password, auth_type, realm, nonce, url);
                }
                rtsp_listner.SendMessage(play_message);
            }
        }

        public void Stop()
        {
            if (rtsp_listner != null)
            {
                // Send TEARDOWN
                Rtsp.Messages.RtspRequest teardown_message = new Rtsp.Messages.RtspRequestTeardown();
                teardown_message.RtspUri = new Uri(url);
                teardown_message.Session = session;
                if (auth_type != null)
                {
                    AddAuthorization(teardown_message, username, password, auth_type, realm, nonce, url);
                }
                rtsp_listner.SendMessage(teardown_message);
            }

            // Отсановить keepalive тимер
            if (keepalive_timer != null) keepalive_timer.Stop();

            // Очистить UDP сокеты
            if (video_udp_pair != null) video_udp_pair.Stop();
            if (audio_udp_pair != null) audio_udp_pair.Stop();

            // Сбросить RTSP сессию
            if (rtsp_listner != null)
            {
                rtsp_listner.Stop();
            }
        }
        #endregion

        #region Функции приватные
        // RTSP сообщения OPTIONS, DESCRIBE, SETUderP, PLAYи т.д.
        private void Rtsp_MessageReceived(object sen, Rtsp.RtspChunkEventArgs e)
        {
            Rtsp.Messages.RtspResponse message = e.Message as Rtsp.Messages.RtspResponse;

            // Если код ответа  401 - Unauthorised Error, переотправим сообщение с авторизацией
            // используем наиболее часто встречающуюся 'realm' and 'nonce'
            if (message.IsOk == false)
            {
                if (message.ReturnCode == 401 && (message.OriginalRequest.Headers.ContainsKey(RtspHeaderNames.Authorization) == true))
                    {
                        // Авторизация не прошла. - в лог
                        Stop(); // - остановим трансляцию
                        return;
                    }

                // Проверяем есть ли заголовок аутентификации в запросе.
                if (message.ReturnCode == 401 && message.Headers.ContainsKey(RtspHeaderNames.WWWAuthenticate))
                    {
                        // Process the WWW-Authenticate header
                        // EG:   Basic realm="AProxy"
                        // EG:   Digest realm="AXIS_WS_ACCC8E3A0A8F", nonce="000057c3Y810622bff50b36005eb5efeae118626a161bf", stale=FALSE
                        // EG:   Digest realm="IP Camera(21388)", nonce="534407f373af1bdff561b7b4da295354", stale="FALSE"

                        String www_authenticate = message.Headers[RtspHeaderNames.WWWAuthenticate];
                        String auth_params = "";

                        if (www_authenticate.StartsWith("basic", StringComparison.InvariantCultureIgnoreCase))
                        {
                            auth_type = "Basic";
                            auth_params = www_authenticate.Substring(5);
                        }
                        if (www_authenticate.StartsWith("digest", StringComparison.InvariantCultureIgnoreCase))
                        {
                            auth_type = "Digest";
                            auth_params = www_authenticate.Substring(6);
                        }

                        string[] items = auth_params.Split(new char[] { ',' }); // ВАЖНО, (does not handle Commas in Quotes)

                        foreach (string item in items)
                        {
                            // Разделим на символы и обновим realm и nonce
                            string[] parts = item.Trim().Split(new char[] { '=' }, 2); // макс 2 части в массив результата
                            if (parts.Count() >= 2 && parts[0].Trim().Equals("realm"))
                            {
                                realm = parts[1].Trim(new char[] { ' ', '\"' }); // срежем кавычки и пробелы
                            }
                            else if (parts.Count() >= 2 && parts[0].Trim().Equals("nonce"))
                            {
                                nonce = parts[1].Trim(new char[] { ' ', '\"' }); // срежем кавычки и пробелы
                        }
                        }
                    }

                RtspMessage resend_message = message.OriginalRequest.Clone() as RtspMessage;

                if (auth_type != null)
                    {
                        AddAuthorization(resend_message, username, password, auth_type, realm, nonce, url);
                    }
                rtsp_listner.SendMessage(resend_message);
                return;
            }

            // Ответ на запрос OPTIONS, запускаем Keepalive таймер и отправляем запрос DESCRIBE
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestOptions)
            {
                // Проверяем возможности
                // The Public: заголовок содержит листинг комманд поддерживаемых сервером RTSP
                // {[DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE, OPTIONS, ANNOUNCE, RECORD, GET_PARAMETER]}
                if (message.Headers.ContainsKey(RtspHeaderNames.Public))
                {
                    string[] parts = message.Headers[RtspHeaderNames.Public].Split(',');
                    foreach (String part in parts)
                    {
                        if (part.Trim().ToUpper().Equals("GET_PARAMETER")) server_supports_get_parameter = true;
                        if (part.Trim().ToUpper().Equals("SET_PARAMETER")) server_supports_set_parameter = true;
                    }
                }

                if (keepalive_timer == null)
                {
                    // Запуск таймера отправляющего пинги на сервер RTSP каждые 5 секунд
                    keepalive_timer = new System.Timers.Timer();
                    keepalive_timer.Elapsed += Timer_Elapsed;
                    keepalive_timer.Interval = 5 * 1000;
                    keepalive_timer.Enabled = true;

                    // Отправляем DESCRIBE
                    Rtsp.Messages.RtspRequest describe_message = new Rtsp.Messages.RtspRequestDescribe();
                    describe_message.AddHeader("Accept:application/sdp");
                    describe_message.RtspUri = new Uri(url);
                    if (auth_type != null)
                    {
                        AddAuthorization(describe_message, username, password, auth_type, realm, nonce, url);
                    }
                    rtsp_listner.SendMessage(describe_message);
                }
                else
                {
                    // Если таймер уже существует ответ пришел на запрос пинга
                    // отправлять запрос DESCRIBE не требуется
                    // балдеем....
                }
            }

            // Ответ на запрос DESCRIBE, получаем SDP и отправляем запрос SETUP
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestDescribe)
            {

                // проверяем статус
                if (message.IsOk == false)
                {
                    return;
                }

                // проверяем SDP
                Rtsp.Sdp.SdpFile sdp_data;
                using (StreamReader sdp_stream = new StreamReader(new MemoryStream(message.Data)))
                {
                    sdp_data = Rtsp.Sdp.SdpFile.Read(sdp_stream);
                }

                // RTP and RTCP 'каналы' используются для TCP Interleaved mode (RTP через RTSP)
                // Это каналы которые нам нужны. Камера подтверждает каналы в SETUP ответе.
                // но, a Panasonic указывает другие каналы в ответе - внимательно!.
                int next_free_rtp_channel = 0;
                int next_free_rtcp_channel = 1;

                // Получаем каждый 'Media' атрибут в SDP
                for (int x = 0; x < sdp_data.Medias.Count; x++)
                {
                    bool audio = (sdp_data.Medias[x].MediaType == Rtsp.Sdp.Media.MediaTypes.audio);
                    bool video = (sdp_data.Medias[x].MediaType == Rtsp.Sdp.Media.MediaTypes.video);

                    if (video && video_payload != -1) continue; // уже есть видео payload. ничего не делаем
                    if (audio && audio_payload != -1) continue; // уже есть аудио payload. ничего не делаем
                    if (audio || video)
                    {
                        // ищем атрибуты для контроля, rtpmap and fmtp
                        // (fmtp применимо только для видео)
                        String control = "";  // "track" или "stream id"
                        Rtsp.Sdp.AttributFmtp fmtp = null; // получим SPS и PPS в base64 (h264 видео)
                        foreach (Rtsp.Sdp.Attribut attrib in sdp_data.Medias[x].Attributs)
                        {
                            if (attrib.Key.Equals("control"))
                            {
                                String sdp_control = attrib.Value;
                                if (sdp_control.ToLower().StartsWith("rtsp://"))
                                {
                                    control = sdp_control; //абсолютный путь
                                }
                                else
                                {
                                    control = url + "/" + sdp_control; //относительный путь
                                }
                                if (video) video_uri = new Uri(control);
                                if (audio) audio_uri = new Uri(control);
                            }
                            if (attrib.Key.Equals("fmtp"))
                            {
                                fmtp = attrib as Rtsp.Sdp.AttributFmtp;
                            }
                            if (attrib.Key.Equals("rtpmap"))
                            {
                                Rtsp.Sdp.AttributRtpMap rtpmap = attrib as Rtsp.Sdp.AttributRtpMap;
                                // Проверим поддержиаем ли мы используемый кодек
                                String[] valid_video_codecs = { "H264", "H265" };
                                String[] valid_audio_codecs = { "PCMA", "PCMU", "AMR", "MPEG4-GENERIC" /* для aac */}; // ВАЖНО, некоторые "mpeg4-generic" нрег
                                if (video && Array.IndexOf(valid_video_codecs, rtpmap.EncodingName.ToUpper()) >= 0)
                                {
                                    // нашли валидный кодек
                                    video_codec = rtpmap.EncodingName.ToUpper();
                                    video_payload = sdp_data.Medias[x].PayloadType;
                                }
                                if (audio && Array.IndexOf(valid_audio_codecs, rtpmap.EncodingName.ToUpper()) >= 0)
                                {
                                    audio_codec = rtpmap.EncodingName.ToUpper();
                                    audio_payload = sdp_data.Medias[x].PayloadType;
                                }
                            }
                        }

                        // Создадим H264 RTP парсер
                        if (video && video_codec.Contains("H264"))
                        {
                            h264Payload = new Rtsp.H264Payload();
                        }

                        // Если rtpmap содержит H264 тогда разделим fmtp чтоб получить sprop-parameter-sets которые содержат SPS and PPS в base64
                        if (video && video_codec.Contains("H264") && fmtp != null)
                        {
                            var param = Rtsp.Sdp.H264Parameters.Parse(fmtp.FormatParameter);
                            var sps_pps = param.SpropParameterSets;
                            if (sps_pps.Count() >= 2)
                            {
                                byte[] sps = sps_pps[0];
                                byte[] pps = sps_pps[1];
                                if (Received_SPS_PPS != null)
                                {
                                    Received_SPS_PPS(sps, pps);
                                }
                                h264_sps_pps_fired = true;
                            }
                        }

                        // Создадим H265 RTP парсер
                        if (video && video_codec.Contains("H265"))
                        {
                            // TODO - проверим использование DONL
                            bool has_donl = false;
                            h265Payload = new Rtsp.H265Payload(has_donl);
                        }

                        // Если rtpmap содержит H265 тогда разделим fmtp чтоб получить sprop-vps, sprop-sps and sprop-pps
                        // RFC сделал VPS, SPS and PPS ОПЦИОНАЛЬНЫМИ их может не быть, тогда вернем null
                        if (video && video_codec.Contains("H265") && fmtp != null)
                        {
                            var param = Rtsp.Sdp.H265Parameters.Parse(fmtp.FormatParameter);
                            var vps_sps_pps = param.SpropParameterSets;
                            if (vps_sps_pps.Count() >= 3)
                            {
                                byte[] vps = vps_sps_pps[0];
                                byte[] sps = vps_sps_pps[1];
                                byte[] pps = vps_sps_pps[2];
                                if (Received_VPS_SPS_PPS != null)
                                {
                                    Received_VPS_SPS_PPS(vps, sps, pps);
                                }
                                h265_vps_sps_pps_fired = true;
                            }
                        }

                        // Создадим AAC RTP парсер
                        // Пример fmtp - "96 profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1490"
                        // Пример fmtp - ""96 streamtype=5;profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1210"
                        if (audio && audio_codec.Contains("MPEG4-GENERIC") && fmtp.GetParameter("mode").ToLower().Equals("aac-hbr"))
                        {
                            // Получим config (0x1490 or 0x1210)
                            aacPayload = new Rtsp.AACPayload(fmtp.GetParameter("config"));
                        }


                        // Отправим SETUP RTSP запрос если совпал Payload декодер
                        if (video && video_payload == -1) continue;
                        if (audio && audio_payload == -1) continue;

                        RtspTransport transport = null;

                        if (rtp_transport == RTP_TRANSPORT.TCP)
                        {
                            // Сервер прослаивает RTP пакеты с RTSP соединением
                            // Пример для TCP (RTP через RTSP)   Transport: RTP/AVP/TCP;interleaved=0-1
                            if (video)
                            {
                                video_data_channel = next_free_rtp_channel;
                                video_rtcp_channel = next_free_rtcp_channel;
                            }
                            if (audio)
                            {
                                audio_data_channel = next_free_rtp_channel;
                                audio_rtcp_channel = next_free_rtcp_channel;
                            }
                            transport = new RtspTransport()
                            {
                                LowerTransport = RtspTransport.LowerTransportType.TCP,
                                Interleaved = new PortCouple(next_free_rtp_channel, next_free_rtcp_channel), // Канал 0 для RTP видео данных. Канал 1 для RTCP статус ответов
                            };

                            next_free_rtp_channel += 2;
                            next_free_rtcp_channel += 2;
                        }
                        if (rtp_transport == RTP_TRANSPORT.UDP)
                        {
                            int rtp_port = 0;
                            int rtcp_port = 0;
                            // Сервер отправляет RTP пакеты для соединения UDP портов (один для данных, один для rtcp контрольных сообщений)
                            // Пример UDP Transport: RTP/AVP;unicast;client_port=8000-8001
                            if (video)
                            {
                                video_data_channel = video_udp_pair.data_port;     // Используется DataReceived
                                video_rtcp_channel = video_udp_pair.control_port;  // Используется DataReceived
                                rtp_port = video_udp_pair.data_port;
                                rtcp_port = video_udp_pair.control_port;
                            }
                            if (audio)
                            {
                                audio_data_channel = audio_udp_pair.data_port;     // Используется DataReceived
                                audio_rtcp_channel = audio_udp_pair.control_port;  // Используется DataReceived
                                rtp_port = audio_udp_pair.data_port;
                                rtcp_port = audio_udp_pair.control_port;
                            }
                            transport = new RtspTransport()
                            {
                                LowerTransport = RtspTransport.LowerTransportType.UDP,
                                IsMulticast = false,
                                ClientPort = new PortCouple(rtp_port, rtcp_port), //UDP порт для данных (видел или аудио). UDP Port для RTCP контрольных сообщений
                            };
                        }
                        if (rtp_transport == RTP_TRANSPORT.MULTICAST)
                        {
                            // Сервер отправляет RTP пакеты для соединения UDP портов (один для данных, один для rtcp контрольных сообщений)
                            // Использование Multicast адреса и порта из ответа на SETUP сообщение
                            // Пример для MULTICAST     Transport: RTP/AVP;multicast
                            if (video)
                            {
                                video_data_channel = 0; // получим эту информацию из ответа на SETUP
                                video_rtcp_channel = 0; // получим эту информацию из ответа на SETUP
                            }
                            if (audio)
                            {
                                audio_data_channel = 0; // получим эту информацию из ответа на SETUP
                                audio_rtcp_channel = 0; // получим эту информацию из ответа на SETUP
                            }
                            transport = new RtspTransport()
                            {
                                LowerTransport = RtspTransport.LowerTransportType.UDP,
                                IsMulticast = true
                            };
                        }

                        // Сосдаем мообщение SETUP
                        Rtsp.Messages.RtspRequestSetup setup_message = new Rtsp.Messages.RtspRequestSetup();
                        setup_message.RtspUri = new Uri(control);
                        setup_message.AddTransport(transport);
                        if (auth_type != null)
                        {
                            AddAuthorization(setup_message, username, password, auth_type, realm, nonce, url);
                        }

                        // Добавим соощение SETUP к списку сообжений к отправке
                        setup_messages.Add(setup_message);

                    }
                }
                // Отправим первое сообшение SETUP и удалим его из листа отправки сообщений
                rtsp_listner.SendMessage(setup_messages[0]);
                setup_messages.RemoveAt(0);
            }

            // Ответ на запрос SETUP:
            // 1 - Проверить изменились ли каналы (Panasonic камеры меняют каналы)
            // 2 - Проверить есть ли еще SETUP коменды на отправку(если отправялем SETUP для видео и аудио)
            // 3 - Отправить запрос PLAY для начала трансляции если все запросы SETUP отправлены
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestSetup)
            {
                // Проверить статус
                if (message.IsOk == false)
                {
                    return;
                }

                session = message.Session; // Сессия используется в Play, Pause, Teardown и т.д
                if (message.Timeout > 0 && message.Timeout > keepalive_timer.Interval / 1000)
                {
                    keepalive_timer.Interval = message.Timeout * 1000 / 2;
                }

                // Проверить заголовок Transport
                if (message.Headers.ContainsKey(RtspHeaderNames.Transport))
                {
                    RtspTransport transport = RtspTransport.Parse("RTP/AVP;unicast");
                    //RtspTransport transport = RtspTransport.Parse(message.Headers[RtspHeaderNames.Transport]);

                    // Проверить Multicast в Transport в заголовке
                    if (transport.IsMulticast)
                    {
                        String multicast_address = transport.Destination;
                        video_data_channel = transport.Port.First;
                        video_rtcp_channel = transport.Port.Second;

                        // Создать Пару UDP Сокетов в Multicast
                        video_udp_pair = new Rtsp.UDPSocket(multicast_address, video_data_channel, multicast_address, video_rtcp_channel);
                        video_udp_pair.DataReceived += Rtp_DataReceived;
                        video_udp_pair.Start();

                        // TODO - Нужно усоздать audio_udp_pair для Multicast
                    }

                    // Проверить изменились ли каналы (Panasonic камеры меняют каналы)
                    if (transport.LowerTransport == RtspTransport.LowerTransportType.TCP)
                    {
                        if (message.OriginalRequest.RtspUri == video_uri)
                        {
                            video_data_channel = transport.Interleaved.First;
                            video_rtcp_channel = transport.Interleaved.Second;
                        }
                        if (message.OriginalRequest.RtspUri == audio_uri)
                        {
                            audio_data_channel = transport.Interleaved.First;
                            audio_rtcp_channel = transport.Interleaved.Second;
                        }

                    }
                }

                // Проверить есть ли еще SETUP коменды на отправку, отправить и удалить из списка
                if (setup_messages.Count > 0)
                {
                    // send the next SETUP message, after adding in the 'session'
                    Rtsp.Messages.RtspRequestSetup next_setup = setup_messages[0];
                    next_setup.Session = session;
                    rtsp_listner.SendMessage(next_setup);

                    setup_messages.RemoveAt(0);
                }

                else
                {
                    // отправить PLAY
                    Rtsp.Messages.RtspRequest play_message = new Rtsp.Messages.RtspRequestPlay();
                    play_message.RtspUri = new Uri(url);
                    play_message.Session = session;
                    if (auth_type != null)
                    {
                        AddAuthorization(play_message, username, password, auth_type, realm, nonce, url);
                    }
                    rtsp_listner.SendMessage(play_message);
                }
            }

            // Ответ PLAY, начинаем получение видео
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestPlay)
            {
                // проверить статус
                if (message.IsOk == false)
                {
                    //TODO - лог
                    return;
                }

                //TODO - лог
            }
        }

        private void Rtp_DataReceived(object sender, Rtsp.RtspChunkEventArgs e) 
        {
            Rtsp.Messages.RtspData data_received = e.Message as Rtsp.Messages.RtspData;
            // Определяем к какому каналу относятся полученные данные.
            // Video Channel - Video Control Channel (RTCP)
            // Audio Channel - Audio Control Channel (RTCP)
            if (data_received.Channel == video_rtcp_channel || data_received.Channel == audio_rtcp_channel)
            {
                //TODO - log
                // RTCP пакет
                // - Version, Padding and Receiver Report Count
                // - Packet Type
                // - Length
                // - SSRC
                // - payload
                // Возможны мульти пакеты RTCP передаваемые вмест, просмотреть каждый
                long packetIndex = 0;
                while (packetIndex < e.Message.Data.Length)
                {
                    int rtcp_version = (e.Message.Data[packetIndex + 0] >> 6);
                    int rtcp_padding = (e.Message.Data[packetIndex + 0] >> 5) & 0x01;
                    int rtcp_reception_report_count = (e.Message.Data[packetIndex + 0] & 0x1F);
                    byte rtcp_packet_type = e.Message.Data[packetIndex + 1]; // Значения от 200 до 207
                    uint rtcp_length = (uint)(e.Message.Data[packetIndex + 2] << 8) + (uint)(e.Message.Data[packetIndex + 3]); // number of 32 bit words
                    uint rtcp_ssrc = (uint)(e.Message.Data[packetIndex + 4] << 24) + (uint)(e.Message.Data[packetIndex + 5] << 16)
                        + (uint)(e.Message.Data[packetIndex + 6] << 8) + (uint)(e.Message.Data[packetIndex + 7]);

                    // 200 = SR = Sender Report
                    // 201 = RR = Receiver Report
                    // 202 = SDES = Source Description
                    // 203 = Bye = Goodbye
                    // 204 = APP = Application Specific Method
                    // 207 = XR = Extended Reports
                    //TODO - log
                    if (rtcp_packet_type == 200)
                    {
                        // Получить ответ отправителя
                        // Используем его для конвертации временной отметки RTP в UTC время
                        UInt32 ntp_msw_seconds = (uint)(e.Message.Data[packetIndex + 8] << 24) + (uint)(e.Message.Data[packetIndex + 9] << 16)
                        + (uint)(e.Message.Data[packetIndex + 10] << 8) + (uint)(e.Message.Data[packetIndex + 11]);

                        UInt32 ntp_lsw_fractions = (uint)(e.Message.Data[packetIndex + 12] << 24) + (uint)(e.Message.Data[packetIndex + 13] << 16)
                        + (uint)(e.Message.Data[packetIndex + 14] << 8) + (uint)(e.Message.Data[packetIndex + 15]);

                        UInt32 rtp_timestamp = (uint)(e.Message.Data[packetIndex + 16] << 24) + (uint)(e.Message.Data[packetIndex + 17] << 16)
                        + (uint)(e.Message.Data[packetIndex + 18] << 8) + (uint)(e.Message.Data[packetIndex + 19]);

                        double ntp = ntp_msw_seconds + (ntp_lsw_fractions / UInt32.MaxValue);

                        // NTP Most Signigicant Word is relative to 0h, 1 Jan 1900 - нулевая отметка времени
                        // This will wrap around in 2036
                        DateTime time = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                        time = time.AddSeconds((double)ntp_msw_seconds); // добавляем 'double' (whole&fraction)
                        //TODO - log
                        // Отправляем ответ получателя
                        try
                        {
                            byte[] rtcp_receiver_report = new byte[8];
                            int version = 2;
                            int paddingBit = 0;
                            int reportCount = 0; // пустой отчет
                            int packetType = 201; // отчет получателя
                            int length = (rtcp_receiver_report.Length / 4) - 1; // num 32 bit words minus 1
                            rtcp_receiver_report[0] = (byte)((version << 6) + (paddingBit << 5) + reportCount);
                            rtcp_receiver_report[1] = (byte)(packetType);
                            rtcp_receiver_report[2] = (byte)((length >> 8) & 0xFF);
                            rtcp_receiver_report[3] = (byte)((length >> 0) & 0XFF);
                            rtcp_receiver_report[4] = (byte)((ssrc >> 24) & 0xFF);
                            rtcp_receiver_report[5] = (byte)((ssrc >> 16) & 0xFF);
                            rtcp_receiver_report[6] = (byte)((ssrc >> 8) & 0xFF);
                            rtcp_receiver_report[7] = (byte)((ssrc >> 0) & 0xFF);

                            if (rtp_transport == RTP_TRANSPORT.TCP)
                            {
                                // Отправить через RTSP соединение
                                rtsp_listner.SendData(video_rtcp_channel, rtcp_receiver_report);
                            }
                            if (rtp_transport == RTP_TRANSPORT.UDP || rtp_transport == RTP_TRANSPORT.MULTICAST)
                            {
                                // Отправить как UDP пакет
                                //"TODO - Need to implement RTCP over UDP"
                            }
                        }
                        catch
                        {
                            //TODO - log
                        }
                    }
                    packetIndex = packetIndex + ((rtcp_length + 1) * 4);
                }
                return;
            }
            if (data_received.Channel == video_data_channel || data_received.Channel == audio_data_channel)
            {
                // Получение Video или Audio данных на корректном канале.
                // RTP Packet Header
                // 0 - Version, P, X, CC, M, PT and Sequence Number
                //32 - Timestamp
                //64 - SSRC
                //96 - CSRCs (optional)
                //nn - Extension ID and Length
                //nn - Extension header

                int rtp_version = (e.Message.Data[0] >> 6);
                int rtp_padding = (e.Message.Data[0] >> 5) & 0x01;
                int rtp_extension = (e.Message.Data[0] >> 4) & 0x01;
                int rtp_csrc_count = (e.Message.Data[0] >> 0) & 0x0F;
                int rtp_marker = (e.Message.Data[1] >> 7) & 0x01;
                int rtp_payload_type = (e.Message.Data[1] >> 0) & 0x7F;
                uint rtp_sequence_number = ((uint)e.Message.Data[2] << 8) + (uint)(e.Message.Data[3]);
                uint rtp_timestamp = ((uint)e.Message.Data[4] << 24) + (uint)(e.Message.Data[5] << 16) + (uint)(e.Message.Data[6] << 8) + (uint)(e.Message.Data[7]);
                uint rtp_ssrc = ((uint)e.Message.Data[8] << 24) + (uint)(e.Message.Data[9] << 16) + (uint)(e.Message.Data[10] << 8) + (uint)(e.Message.Data[11]);

                int rtp_payload_start = 4 // V,P,M,SEQ
                                    + 4 // time stamp
                                    + 4 // ssrc
                                    + (4 * rtp_csrc_count); // zero or more csrcs

                uint rtp_extension_id = 0;
                uint rtp_extension_size = 0;
                if (rtp_extension == 1)
                {
                    rtp_extension_id = ((uint)e.Message.Data[rtp_payload_start + 0] << 8) + (uint)(e.Message.Data[rtp_payload_start + 1] << 0);
                    rtp_extension_size = ((uint)e.Message.Data[rtp_payload_start + 2] << 8) + (uint)(e.Message.Data[rtp_payload_start + 3] << 0) * 4; // units of extension_size is 4-bytes
                    rtp_payload_start += 4 + (int)rtp_extension_size;  // расширение заголовки и расширение payload
                }

                /*TODO - log .Debug("RTP Data"
                                   + " V=" + rtp_version
                                   + " P=" + rtp_padding
                                   + " X=" + rtp_extension
                                   + " CC=" + rtp_csrc_count
                                   + " M=" + rtp_marker
                                   + " PT=" + rtp_payload_type
                                   + " Seq=" + rtp_sequence_number
                                   + " Time (MS)=" + rtp_timestamp / 90 // convert from 90kHZ clock to msy k
                                   + " SSRC=" + rtp_ssrc
                                   + " Size=" + e.Message.Data.Length);*/

                // Проверить видео тип payload в RTP пакете на совпадение Payload Type из SDP
                if (data_received.Channel == video_data_channel && rtp_payload_type != video_payload)
                {
                    //TODO - log _logger.Debug("Ignoring this Video RTP payload");
                    return;
                }
                // Проверить аудио тип payload в RTP пакете на совпадение Payload Type из SDP
                else if (data_received.Channel == audio_data_channel && rtp_payload_type != audio_payload)
                {
                    //TODO - log _logger.Debug("Ignoring this Audio RTP payload");
                    return;
                }
                else if (data_received.Channel == video_data_channel && rtp_payload_type == video_payload && video_codec.Equals("H264"))
                {
                    // H264 RTP Пакет
                    // Если rtp_marker - '1' это конец передачи пакета.
                    // Если rtp_marker - '0' нужно аккумулировать пакеты с одинаковой временной меткой
                    // ToDo - Проверить Timestamp
                    // Добавить RTP пакет к tempoary_rtp list пока не получим последний 'Frame'

                    byte[] rtp_payload = new byte[e.Message.Data.Length - rtp_payload_start]; // payload with RTP header removed
                    System.Array.Copy(e.Message.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload

                    List<byte[]> nal_units = _H264Payload.Process_H264_RTP_Packet(rtp_payload, rtp_marker); // this will cache the Packets until there is a Frame

                    if (nal_units == null)
                    {
                        // нет пакетов RTP чтоб собрать кадр видео video
                    }
                    else
                    {
                        // Если у нас нет SPS и PPS найденых в SDP тогда ищем SPS и PPS
                        // в NALs из запускаем Received_SPS_PPS событие.
                        // Суммируем SPS and PPS из кадров.
                        if (h264_sps_pps_fired == false)
                        {
                            // Check this frame for SPS and PPS
                            /*byte[] sps = null;
                            byte[] pps = null;*/
                            foreach (byte[] nal_unit in nal_units)
                            {
                                if (nal_unit.Length > 0)
                                {
                                    int nal_ref_idc = (nal_unit[0] >> 5) & 0x03;
                                    int nal_unit_type = nal_unit[0] & 0x1F;

                                    if (nal_unit_type == 7) sps = nal_unit; // SPS
                                    if (nal_unit_type == 8) pps = nal_unit; // PPS
                                }
                            }
                            if (sps != null && pps != null)
                            {
                                // запускаем Received_SPS_PPS событие
                                if (Received_SPS_PPS != null)
                                {
                                    Received_SPS_PPS(sps, pps);
                                }
                                h264_sps_pps_fired = true;
                            }
                        }

                        // Есть кадр во NAL Units. Закидываем его в файл
                        if (Received_NALs != null)
                        {
                            Received_NALs(nal_units);
                        }
                    }
                }
                else if (data_received.Channel == video_data_channel && rtp_payload_type == video_payload && video_codec.Equals("H265"))
                {
                    // H265 RTP пакет
                    // Если rtp_marker - '1' это конец передачи пакета.
                    // Если rtp_marker - '0' нужно аккумулировать пакеты с одинаковой временной меткой
                    // Добавить RTP пакет к tempoary_rtp list пока не получим последний 'Frame'
                    byte[] rtp_payload = new byte[e.Message.Data.Length - rtp_payload_start]; // payload с удаленным RTP заголовком
                    System.Array.Copy(e.Message.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // копируем payload

                    List<byte[]> nal_units = h265Payload.Process_H265_RTP_Packet(rtp_payload, rtp_marker); // кэшируем пакеты пока не собрался кадр

                    if (nal_units == null)
                    {
                        // нет пакетов RTP чтоб собрать кадр видео video
                    }
                    else
                    {
                        // Если у нас нет SPS и PPS найденых в SDP тогда ищем SPS и PPS
                        // в NALs из запускаем Received_VPS_SPS_PPS событие.
                        // Суммируем SPS and PPS из кадров.
                        if (h265_vps_sps_pps_fired == false)
                        {
                            // Проверяем кадр на наличие VPS, SPS и PPS
                            foreach (byte[] nal_unit in nal_units)
                            {
                                if (nal_unit.Length > 0)
                                {
                                    int nal_unit_type = (nal_unit[0] >> 1) & 0x3F;

                                    if (nal_unit_type == 32) vps = nal_unit; // VPS
                                    if (nal_unit_type == 33) sps = nal_unit; // SPS
                                    if (nal_unit_type == 34) pps = nal_unit; // PPS
                                }
                            }
                            if (vps != null && sps != null && pps != null)
                            {
                                // запускаем Received_VPS_SPS_PPS событие
                                if (Received_VPS_SPS_PPS != null)
                                {
                                    Received_VPS_SPS_PPS(vps, sps, pps);
                                }
                                h265_vps_sps_pps_fired = true;
                            }
                        }

                        // Есть кадр во NAL Units. Закидываем его в файл
                        if (Received_NALs != null)
                        {
                            Received_NALs(nal_units);
                        }
                    }
                }
                else if (data_received.Channel == audio_data_channel && (rtp_payload_type == 0 || rtp_payload_type == 8 || audio_codec.Equals("PCMA") || audio_codec.Equals("PCMU")))
                {
                    // G711 PCMA или G711 PCMU
                    byte[] rtp_payload = new byte[e.Message.Data.Length - rtp_payload_start]; // payload с удалленным RTP заголовком
                    System.Array.Copy(e.Message.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // копируем payload

                    List<byte[]> audio_frames = g711Payload.Process_G711_RTP_Packet(rtp_payload, rtp_marker);

                    if (audio_frames == null)
                    {
                        // нет пакетов RTP чтоб собрать аудио
                    }
                    else
                    {
                        // кишем аудиокадр в файл
                        if (Received_G711 != null)
                        {
                            Received_G711(audio_codec, audio_frames);
                        }
                    }
                }
                else if (data_received.Channel == audio_data_channel && rtp_payload_type == audio_payload && audio_codec.Equals("AMR"))
                {
                    // AMR
                    byte[] rtp_payload = new byte[e.Message.Data.Length - rtp_payload_start]; // payload с удалленным RTP заголовком
                    System.Array.Copy(e.Message.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // копируем payload

                    List<byte[]> audio_frames = amrPayload.Process_AMR_RTP_Packet(rtp_payload, rtp_marker);

                    if (audio_frames == null)
                    {
                        // нет данных
                    }
                    else
                    {
                        // Пишем в аудиокадр в файл
                        if (Received_AMR != null)
                        {
                            Received_AMR(audio_codec, audio_frames);
                        }
                    }
                }
                else if (data_received.Channel == audio_data_channel && rtp_payload_type == audio_payload && audio_codec.Equals("MPEG4-GENERIC") && aacPayload != null)
                {
                    // AAC
                    byte[] rtp_payload = new byte[e.Message.Data.Length - rtp_payload_start]; // payload с удалленным RTP заголовком
                    System.Array.Copy(e.Message.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // копируем payload

                    List<byte[]> audio_frames = aacPayload.Process_AAC_RTP_Packet(rtp_payload, rtp_marker);

                    if (audio_frames == null)
                    {
                        // какая то ошибка
                    }
                    else
                    {
                        // Пишем аудиокадр в файл
                        if (Received_AAC != null)
                        {
                            Received_AAC(audio_codec, audio_frames, aacPayload.ObjectType, aacPayload.FrequencyIndex, aacPayload.ChannelConfiguration);
                        }
                    }
                }
                else if (data_received.Channel == video_data_channel && rtp_payload_type == 26)
                {
                    //TODO - log _logger.Warn("No parser has been written for JPEG RTP packets. Please help write one");
                    return; // ignore this data
                }
                else
                {
                    //TODO - log _logger.Warn("No parser for RTP payload " + rtp_payload_type);
                }
            }


        }

        // Создаем Basic or Digest авторизацию
        private void AddAuthorization(RtspMessage message, string username, string password, string auth_type, string realm, string nonce, string url)
        {
            if (username == null || username.Length == 0) return;
            if (password == null) return;
            //if (password == null || password.Length == 0) return;
            if (realm == null || realm.Length == 0) return;
            if (auth_type.Equals("Digest") && (nonce == null || nonce.Length == 0)) return;
            if (auth_type.Equals("Basic"))
            {
                byte[] credentials = System.Text.Encoding.UTF8.GetBytes(username + ":" + password);
                String credentials_base64 = Convert.ToBase64String(credentials);
                String basic_authorization = "Basic " + credentials_base64;

                message.Headers.Add(RtspHeaderNames.Authorization, basic_authorization);

                return;
            }
            else if (auth_type.Equals("Digest"))
            {
                string method = message.Method; // DESCRIBE, SETUP, PLAY etc

                MD5 md5 = System.Security.Cryptography.MD5.Create();
                String hashA1 = CalculateMD5Hash(md5, username + ":" + realm + ":" + password);
                String hashA2 = CalculateMD5Hash(md5, method + ":" + url);
                String response = CalculateMD5Hash(md5, hashA1 + ":" + nonce + ":" + hashA2);

                const String quote = "\"";
                String digest_authorization = "Digest username=" + quote + username + quote + ", "
                    + "realm=" + quote + realm + quote + ", "
                    + "nonce=" + quote + nonce + quote + ", "
                    + "uri=" + quote + url + quote + ", "
                    + "response=" + quote + response + quote;

                message.Headers.Add(RtspHeaderNames.Authorization, digest_authorization);

                return;
            }
            else
            {
                return;
            }
        }

        // MD5 (н рег)
        private string CalculateMD5Hash(MD5 md5_session, string input)
        {
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hash = md5_session.ComputeHash(inputBytes);

            StringBuilder output = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                output.Append(hash[i].ToString("x2"));
            }

            return output.ToString();
        }

        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Отправк удерживающих сообшений
            // The ONVIF Standard uses SET_PARAMETER as "an optional method to keep an RTSP session alive"
            // RFC 2326 (RTSP Standard) гласит "GET_PARAMETER with no entity body may be used to test client or server liveness("ping")"
            // Данные метод использует GET_PARAMETER
            if (server_supports_get_parameter)
            {
                Rtsp.Messages.RtspRequest getparam_message = new Rtsp.Messages.RtspRequestGetParameter();
                getparam_message.RtspUri = new Uri(url);
                getparam_message.Session = session;
                if (auth_type != null)
                {
                    AddAuthorization(getparam_message, username, password, auth_type, realm, nonce, url);
                }
                rtsp_listner.SendMessage(getparam_message);

            }
            else
            {
                Rtsp.Messages.RtspRequest options_message = new Rtsp.Messages.RtspRequestOptions();
                options_message.RtspUri = new Uri(url);
                if (auth_type != null)
                {
                    AddAuthorization(options_message, username, password, auth_type, realm, nonce, url);
                }
                rtsp_listner.SendMessage(options_message);
            }
        }
        #endregion
    }
}
