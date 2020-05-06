namespace Rtsp
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using Rtsp.Messages;

    /// Rtsp lister
    public class RtspListener : IDisposable
    {
        private IRtspTransport _transport;

        private Thread _listenTread;
        private Stream _stream;

        private int _sequenceNumber;

        private Dictionary<int, RtspRequest> _sentMessage = new Dictionary<int, RtspRequest>();

        /// Initializes a new instance of the <see cref="RtspListener"/> class from a TCP connection.
        /// <param name="connection">The connection.</param>
        public RtspListener(IRtspTransport connection)
        {
            if (connection == null)
                throw new ArgumentNullException("connection");
            Contract.EndContractBlock();

            _transport = connection;
            _stream = connection.GetStream();
        }

        /// Gets the remote address.
        /// <value>The remote adress.</value>
        public string RemoteAdress
        {
            get
            {
                return _transport.RemoteAddress;
            }
        }

        /// Starts this instance.
        public void Start()
        {
            _listenTread = new Thread(new ThreadStart(DoJob));
            _listenTread.Name = "DoJob";
            _listenTread.Start();
        }

        
        /// Stops this instance.   
        public void Stop()
        {
            // brutally  close the TCP socket....
            // I hope the teardown was sent elsewhere
            _transport.Close();

        }

        
        /// Enable auto reconnect.        
        public bool AutoReconnect { get; set; }

        
        /// Occurs when message is received.        
        public event EventHandler<RtspChunkEventArgs> MessageReceived;

        
        /// Raises the <see cref="E:MessageReceived"/> event.       
        /// <param name="e">The <see cref="Rtsp.RtspChunkEventArgs"/> instance containing the event data.</param>
        protected void OnMessageReceived(RtspChunkEventArgs e)
        {
            EventHandler<RtspChunkEventArgs> handler = MessageReceived;

            if (handler != null)
                handler(this, e);
        }
 
        /// Occurs when Data is received.       
        public event EventHandler<RtspChunkEventArgs> DataReceived;

        /// Raises the <see cref="E:DataReceived"/> event.
        /// <param name="rtspChunkEventArgs">The <see cref="Rtsp.RtspChunkEventArgs"/> instance containing the event data.</param>
        protected void OnDataReceived(RtspChunkEventArgs rtspChunkEventArgs)
        {
            EventHandler<RtspChunkEventArgs> handler = DataReceived;

            if (handler != null)
                handler(this, rtspChunkEventArgs);
        }
        
        /// Does the reading job.
        /// <remarks>
        /// This method read one message from TCP connection.
        /// If it a response it add the associate question.
        /// The stopping is made by the closing of the TCP connection.
        /// </remarks>
        private void DoJob()
        {
            try
            {
                //TODO - log
                while (_transport.Connected)
                {
                    // La lectuer est blocking sauf si la connection est coupé
                    RtspChunk currentMessage = ReadOneMessage(_stream);

                    if (currentMessage != null)
                    {
                        if (!(currentMessage is RtspData))
                        {
                            // on logue le tout
                            if (currentMessage.SourcePort != null)
                            {
                                //TODO - log
                                //TODO - log currentMessage.LogMessage();
                            }
                        }
                        if (currentMessage is RtspResponse)
                        {

                            RtspResponse response = currentMessage as RtspResponse;
                            lock (_sentMessage)
                            {
                                // add the original question to the response.
                                RtspRequest originalRequest;
                                if (_sentMessage.TryGetValue(response.CSeq, out originalRequest))
                                {
                                    _sentMessage.Remove(response.CSeq);
                                    response.OriginalRequest = originalRequest;
                                }
                                else
                                {
                                    //TODO - log                               
                                }
                            }
                            OnMessageReceived(new RtspChunkEventArgs(response));
                        }
                        else if (currentMessage is RtspRequest)
                        {
                            OnMessageReceived(new RtspChunkEventArgs(currentMessage));
                        }
                        else if (currentMessage is RtspData)
                        {
                            OnDataReceived(new RtspChunkEventArgs(currentMessage));
                        }

                    }
                    else
                    {
                        _stream.Close();
                        _transport.Close();
                    }
                }
            }
            catch (IOException error)
            {
                //TODO - log
                _stream.Close();
                _transport.Close();
            }
            catch (SocketException error)
            {
                //TODO - log
                _stream.Close();
                _transport.Close();
            }
            catch (ObjectDisposedException error)
            {
                //TODO - log
            }
            catch (Exception error)
            {
                //TODO - log
                throw;
            }

            //TODO - log  "Connection Close";
        }

        [Serializable]
        private enum ReadingState
        {
            NewCommand,
            Headers,
            Data,
            End,
            InterleavedData,
            MoreInterleavedData,
        }

        /// Sends the message.
        /// <param name="message">A message.</param>
        /// <returns><see cref="true"/> if it is Ok, otherwise <see cref="false"/></returns>
        public bool SendMessage(RtspMessage message)
        {
            if (message == null)
                throw new ArgumentNullException("message");
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if(!AutoReconnect)
                    return false;

                //TODO - log "Reconnect to a client, strange !!
                try
                {
                    Reconnect();
                }
                catch (SocketException)
                {
                    // on a pas put se connecter on dit au manager de plus compter sur nous
                    return false;
                }
            }

            // if it it a request  we store the original message
            // and we renumber it.
            //TODO handle lost message (for example every minute cleanup old message)
            if (message is RtspRequest)
            {
                RtspMessage originalMessage = message;
                // Do not modify original message
                message = message.Clone() as RtspMessage;
                _sequenceNumber++;
                message.CSeq = _sequenceNumber;
                lock (_sentMessage)
                {  
                    _sentMessage.Add(message.CSeq, originalMessage as RtspRequest);
                }
            }

            //TODO - log Send Message
            //TODO - log message.LogMessage();
            message.SendTo(_stream);
            return true;
        }

        
        /// Reconnect this instance of RtspListener.
        
        /// <exception cref="System.Net.Sockets.SocketException">Error during socket </exception>
        public void Reconnect()
        {
            //if it is already connected do not reconnect
            if (_transport.Connected)
                return;

            // If it is not connected listenthread should have die.
            if (_listenTread != null && _listenTread.IsAlive)
                _listenTread.Join();

            if (_stream != null)
                _stream.Dispose();

            // reconnect 
            _transport.Reconnect();
            _stream = _transport.GetStream();

            // If listen thread exist restart it
            if (_listenTread != null)
                Start();
        }

        
        /// Reads one message.
        
        /// <param name="commandStream">The Rtsp stream.</param>
        /// <returns>Message readen</returns>
        public RtspChunk ReadOneMessage(Stream commandStream)
        {
            if (commandStream == null)
                throw new ArgumentNullException("commandStream");
            Contract.EndContractBlock();

            ReadingState currentReadingState = ReadingState.NewCommand;
            // current decode message , create a fake new to permit compile.
            RtspChunk currentMessage = null;

            int size = 0;
            int byteReaden = 0;
            List<byte> buffer = new List<byte>(256);
            string oneLine = String.Empty;
            while (currentReadingState != ReadingState.End)
            {

                // if the system is not reading binary data.
                if (currentReadingState != ReadingState.Data && currentReadingState != ReadingState.MoreInterleavedData)
                {
                    oneLine = String.Empty;
                    bool needMoreChar = true;
                    // I do not know to make readline blocking
                    while (needMoreChar)
                    {
                        int currentByte = commandStream.ReadByte();

                        switch (currentByte)
                        {
                            case -1:
                                // the read is blocking, so if we got -1 it is because the client close;
                                currentReadingState = ReadingState.End;
                                needMoreChar = false;
                                break;
                            case '\n':
                                oneLine = ASCIIEncoding.UTF8.GetString(buffer.ToArray());
                                buffer.Clear();
                                needMoreChar = false;
                                break;
                            case '\r':
                                // simply ignore this
                                break;
                            case '$': // if first caracter of packet is $ it is an interleaved data packet
                                if (currentReadingState == ReadingState.NewCommand && buffer.Count == 0)
                                {
                                    currentReadingState = ReadingState.InterleavedData;
                                    needMoreChar = false;
                                }
                                else
                                    goto default;
                                break;
                            default:
                                buffer.Add((byte)currentByte);
                                break;
                        }
                    }
                }

                switch (currentReadingState)
                {
                    case ReadingState.NewCommand:
                        currentMessage = RtspMessage.GetRtspMessage(oneLine);
                        currentReadingState = ReadingState.Headers;
                        break;
                    case ReadingState.Headers:
                        string line = oneLine;
                        if (string.IsNullOrEmpty(line))
                        {
                            currentReadingState = ReadingState.Data;
                            ((RtspMessage)currentMessage).InitialiseDataFromContentLength();
                        }
                        else
                        {
                            ((RtspMessage)currentMessage).AddHeader(line);
                        }
                        break;
                    case ReadingState.Data:
                        if (currentMessage.Data.Length > 0)
                        {
                            // Read the remaning data
                            int byteCount = commandStream.Read(currentMessage.Data, byteReaden,
                                                               currentMessage.Data.Length - byteReaden);
                            if (byteCount <= 0) {
                                currentReadingState = ReadingState.End;
                                break;
                            }
                            byteReaden += byteCount;
                            //TODO - log 
                        }
                        // if we haven't read all go there again else go to end. 
                        if (byteReaden >= currentMessage.Data.Length)
                            currentReadingState = ReadingState.End;
                        break;
                    case ReadingState.InterleavedData:
                        currentMessage = new RtspData();
                        int channelByte = commandStream.ReadByte();
                        if (channelByte == -1) {
                            currentReadingState = ReadingState.End;
                            break;
                        }
                        ((RtspData)currentMessage).Channel = channelByte;

                        int sizeByte1 = commandStream.ReadByte();
                        if (sizeByte1 == -1) {
                            currentReadingState = ReadingState.End;
                            break;
                        }
                        int sizeByte2 = commandStream.ReadByte();
                        if (sizeByte2 == -1) {
                            currentReadingState = ReadingState.End;
                            break;
                        }
                        size = (sizeByte1 << 8) + sizeByte2;
                        currentMessage.Data = new byte[size];
                        currentReadingState = ReadingState.MoreInterleavedData;
                        break;
                    case ReadingState.MoreInterleavedData:
                        // apparently non blocking
                        {
                            int byteCount = commandStream.Read(currentMessage.Data, byteReaden, size - byteReaden);
                            if (byteCount <= 0) {
                                currentReadingState = ReadingState.End;
                                break;
                            }
                            byteReaden += byteCount;
                            if (byteReaden < size)
                                currentReadingState = ReadingState.MoreInterleavedData;
                            else
                                currentReadingState = ReadingState.End;
                            break;
                        }
                    default:
                        break;
                }
            }
            if (currentMessage != null)
                currentMessage.SourcePort = this;
            return currentMessage;
        }

        
        /// Begins the send data.
        
        /// <param name="aRtspData">A Rtsp data.</param>
        /// <param name="asyncCallback">The async callback.</param>
        /// <param name="aState">A state.</param>
        public IAsyncResult BeginSendData(RtspData aRtspData, AsyncCallback asyncCallback, object state)
        {
            if (aRtspData == null)
                throw new ArgumentNullException("aRtspData");
            Contract.EndContractBlock();

            return BeginSendData(aRtspData.Channel, aRtspData.Data, asyncCallback, state);
        }

        
        /// Begins the send data.
        
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        /// <param name="asyncCallback">The async callback.</param>
        /// <param name="aState">A state.</param>
        public IAsyncResult BeginSendData(int channel, byte[] frame, AsyncCallback asyncCallback, object state)
        {
            if (frame == null)
                throw new ArgumentNullException("frame");
            if (frame.Length > 0xFFFF)
                throw new ArgumentException("frame too large", "frame");
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if(!AutoReconnect)
                    return null; // cannot write when transport is disconnected

                //TODO - log Reconnect to a client, strange !!
                Reconnect();
            }

            byte[] data = new byte[4 + frame.Length]; // add 4 bytes for the header
            data[0] = 36; // '$' character
            data[1] = (byte)channel;
            data[2] = (byte)((frame.Length & 0xFF00) >> 8);
            data[3] = (byte)((frame.Length & 0x00FF));
            System.Array.Copy(frame,0,data,4,frame.Length);
            return _stream.BeginWrite(data, 0, data.Length, asyncCallback, state);
        }

        
        /// Ends the send data.
        
        /// <param name="result">The result.</param>
        public void EndSendData(IAsyncResult result)
        {
            try
            {
                _stream.EndWrite(result);
            } catch (Exception e)
            {
                // Error, for example stream has already been Disposed
                //TODO - log Error during end send (can be ignored)
                result = null;
            }
        }

        
        /// Send data (Synchronous)
        
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        public void SendData(int channel, byte[] frame)
        {
            if (frame == null)
                throw new ArgumentNullException("frame");
            if (frame.Length > 0xFFFF)
                throw new ArgumentException("frame too large", "frame");
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if(!AutoReconnect)
                    throw new Exception("Connection is lost");

                //TODO - log "Reconnect to a client, strange !!");
                Reconnect();
            }

            byte[] data = new byte[4 + frame.Length]; // add 4 bytes for the header
            data[0] = 36; // '$' character
            data[1] = (byte)channel;
            data[2] = (byte)((frame.Length & 0xFF00) >> 8);
            data[3] = (byte)((frame.Length & 0x00FF));
            System.Array.Copy(frame, 0, data, 4, frame.Length);
            lock (_stream) {
                _stream.Write(data, 0, data.Length);
            }
        }


        #region IDisposable Membres

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
                if (_stream != null)
                    _stream.Dispose();

            }
        }

        #endregion
    }
}
