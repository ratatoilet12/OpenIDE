using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections;
using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.Generic;

namespace OpenIDE.EventListener
{
	class Program
	{
		public static void Main(string[] args)
		{
			Console.WriteLine("Connecting to: " + Environment.CurrentDirectory);
			var client = new EventClient((m) => Console.WriteLine(m));
			while (true) {
				client.Connect(Environment.CurrentDirectory);
				if (!client.IsConnected) {
					Thread.Sleep(50);
					continue;
				}
				Console.WriteLine("Connected");
				while (client.IsConnected)
					Thread.Sleep(50);
				break;
			}
		}
	}

	public class EventClient : IDisposable
	{
		private Action<string> _handler;
		private string _path = null;
		private SocketClient _client = null;
		
		public bool IsConnected { get { return isConnected(); } }
		
		public EventClient(Action<string> handler)
		{
			_handler = handler;
		}
		public void Connect(string path)
		{
			_path = path;
			if (_client != null &&_client.IsConnected)
				_client.Disconnect();
			_client = null;
			isConnected();
		}

        public void Dispose()
        {
            if (_client != null && _client.IsConnected)
                _client.Disconnect();
            _client = null;
        }

		public void Send(string message)
		{
			if (!isConnected())
				return;
			_client.Send(message);
		}
		
		private bool isConnected()
		{
			try
			{
				if (_client != null && _client.IsConnected)
					return true;
				var instance = new EventEndpointLocator().GetInstance(_path);
				if (instance == null)
					return false;
				_client = new SocketClient();
				_client.Connect(instance.Port, (m) => _handler(m));
				if (_client.IsConnected)
					return true;
				_client = null;
				return false;
			}
			catch
			{
				return false;
			}
		}
	}
	
	class EventEndpointLocator
	{
		public Instance GetInstance(string path)
		{
			var instances = getInstances(path);
			return instances.Where(x => path.StartsWith(x.Key) && canConnectTo(x))
				.OrderByDescending(x => x.Key.Length)
				.FirstOrDefault();
		}
		
		private IEnumerable<Instance> getInstances(string path)
		{
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name.Replace(Path.DirectorySeparatorChar.ToString(), "-");
            var filename = string.Format("*.OpenIDE.Events.{0}.pid", user);
			var dir = FS.GetTempPath();
			if (Directory.Exists(dir))
			{
				foreach (var file in Directory.GetFiles(dir, filename))
				{
					var instance = Instance.Get(file, File.ReadAllLines(file));
					if (instance != null)
						yield return instance;
				}
			}
		}
		
		private bool canConnectTo(Instance info)
		{
			var client = new SocketClient();
			client.Connect(info.Port, (s) => {});
			var connected = client.IsConnected;
			client.Disconnect();
			if (!connected) {
                try {
                    Process.GetProcessById(info.ProcessID);
                } catch {
                    File.Delete(info.File);
                }
            }
			return connected;
		}
	}

	class Instance
	{
		public string File { get; private set; }
		public int ProcessID { get; private set; }
		public string Key { get; private set; }
		public int Port { get; private set; }
		
		public Instance(string file, int processID, string key, int port)
		{
			File = file;
			ProcessID = processID;
			Key = key;
			Port = port;
		}
		
		public static Instance Get(string file, string[] lines)
		{
			if (lines.Length != 2)
				return null;
			int processID;
            var pid = Path.GetFileName(file).Substring(0, Path.GetFileName(file).IndexOf("."));
			if (!int.TryParse(pid, out processID))
				return null;
			int port;
			if (!int.TryParse(lines[1], out port))
				return null;
			return new Instance(file, processID, lines[0], port);
		}
	}
	
	class SocketClient
	{
		private NetworkStream _stream;
        readonly byte[] _buffer = new byte[1000000];
        private int _currentPort;
        private readonly MemoryStream _readBuffer = new MemoryStream();
        private Queue queue = new Queue();
		private bool IsSending = false;
		private Action<string> _onMessage;
		class MessageArgs : EventArgs { public string Message { get; set; } }
		private event EventHandler<MessageArgs> _messageReceived;
		
		public bool IsConnected { get; private set; }
		
		public SocketClient()
		{
			IsConnected = false;
		}

        public void Connect(int port, Action<string> onMessage)
        {
			_onMessage = onMessage;
            Connect(port, 0);
        }

        private void Connect(int port, int retryCount)
        {
            if (retryCount >= 5)
                return;
			try {
	            var client = new TcpClient();
	            client.Connect("127.0.0.1", port);
	            _currentPort = port;
	            _stream = client.GetStream();
	            _stream.BeginRead(_buffer, 0, _buffer.Length, ReadCompleted, _stream);
				IsConnected = true;
			} 
			catch 
			{
                Reconnect(retryCount);
			}
        }

        public void Disconnect()
        {
			try {
				IsConnected = false;
	            _stream.Close();
	            _stream.Dispose();
			}
			catch
			{}
        }

        private void Reconnect(int retryCount)
        {
            retryCount++;
            _readBuffer.SetLength(0);
			Disconnect();
			Connect(_currentPort, retryCount);
		}

        private void ReadCompleted(IAsyncResult result)
        {
            var stream = (NetworkStream)result.AsyncState;
            try
            {
                var x = stream.EndRead(result);
                if(x == 0) Reconnect(0);
                for (var i = 0; i < x;i++)
                {
                    if (_buffer[i] == 0)
                    {
                        var data = _readBuffer.ToArray();
                        var actual = Encoding.UTF8.GetString(data, 0, data.Length);
						if (_messageReceived != null)
							_messageReceived(this, new MessageArgs() { Message = actual });
                        _onMessage(actual);
                        _readBuffer.SetLength(0);
                    }
                    else
                    {
                        _readBuffer.WriteByte(_buffer[i]);
                    }
                }
                stream.BeginRead(_buffer, 0, _buffer.Length, ReadCompleted, stream);
            }
            catch (Exception ex)
            {
                WriteError(ex);
                Reconnect(0);
            }
        }


        public void Send(string message)
        {
            if (IsSending)
                throw new Exception("Cannot call send while doing SendAndWait, make up your mind");
            lock (queue)
            {
                queue.Enqueue(message);
                if(!IsSending) {
					SendFromQueue();                      
                }
            }
        }

        public void SendAndWait(string message)
        {
            Send(message);
            IsSending = true;
            var timeout = DateTime.Now;
            while (IsSending && DateTime.Now.Subtract(timeout).TotalMilliseconds < 8000)
                Thread.Sleep(10);
        }

		public string Request(string message)
		{
			string recieved= null;
			var correlationID = "correlationID=" + Guid.NewGuid().ToString() + "|";
			var messageToSend = correlationID + message;
			EventHandler<MessageArgs> msgHandler = (o,a) => {
					if (a.Message.StartsWith(correlationID) && a.Message != messageToSend)
						recieved = a.Message
							.Substring(
								correlationID.Length,
								a.Message.Length - correlationID.Length);
				};
			_messageReceived += msgHandler;
			Send(messageToSend);
			var timeout = DateTime.Now;
            while (DateTime.Now.Subtract(timeout).TotalMilliseconds < 8000)
			{
				if (recieved != null)
					break;
                Thread.Sleep(10);
			}
			_messageReceived -= msgHandler;
			return recieved;
		}

        private void WriteCompleted(IAsyncResult result)
        {
            var client = (NetworkStream)result.AsyncState;
            try
            {
                client.EndWrite(result);
                lock(queue)
                {
		    		IsSending = false;
                    if (queue.Count > 0)
                        SendFromQueue();
                }
                
            }
            catch (Exception ex)
            {
                WriteError(ex);
				Reconnect(0);
            }
        }

        private void SendFromQueue()
        {
            string message = null;
            lock (queue)
            {
                if (!IsSending && queue.Count > 0)
                    message = queue.Dequeue().ToString();
            }
            if (message != null)
            {
                try
                {
					byte[] toSend = Encoding.UTF8.GetBytes(message).Concat(new byte[] { 0x0 }).ToArray();
                    _stream.BeginWrite(toSend, 0, toSend.Length, WriteCompleted, _stream);
                }
                catch
                {
                }
            }
        }

        private void WriteError(Exception ex)
        {
        }
	}

    public class FS
    {
        public static string GetTempPath()
        {
            if (OS.IsOSX) {
                return "/tmp";
            }
            return Path.GetTempPath();
        }

        public static string GetTempFilePath()
        {
            var tmpfile = GetTempFileName();
            if (OS.IsOSX)
                tmpfile = Path.Combine("/tmp", Path.GetFileName(tmpfile));
            return tmpfile;
        }

        public static string GetTempDir()
        {
            if (OS.IsOSX)
                return "/tmp";
            return Path.GetTempPath();
        }

        public static string GetTempFileName()
        {
            return Path.GetTempFileName();
        }
    }

    static class OS
    {
        private static bool? _isWindows;
        private static bool? _isUnix;
        private static bool? _isOSX;

        public static bool IsWindows {
            get {
                if (_isWindows == null) {
                    _isWindows = 
                        Environment.OSVersion.Platform == PlatformID.Win32S ||
                        Environment.OSVersion.Platform == PlatformID.Win32NT ||
                        Environment.OSVersion.Platform == PlatformID.Win32Windows ||
                        Environment.OSVersion.Platform == PlatformID.WinCE ||
                        Environment.OSVersion.Platform == PlatformID.Xbox;
                }
                return (bool) _isWindows;
            }
        }

        public static bool IsPosix {
            get {
                return IsUnix || IsOSX;
            }
        }


        public static bool IsUnix {
            get {
                if (_isUnix == null)
                    setUnixAndLinux();
                return (bool) _isUnix;
            }
        }

        public static bool IsOSX {
            get {
                if (_isOSX == null)
                    setUnixAndLinux();
                return (bool) _isOSX;
            }
        }

        private static void setUnixAndLinux()
        {
            try
            {
                if (IsWindows) {
                    _isOSX = false;
                    _isUnix = false;
                } else  {
                    var process = new Process
                                      {
                                          StartInfo =
                                              new ProcessStartInfo("uname", "-a")
                                                  {
                                                      RedirectStandardOutput = true,
                                                      WindowStyle = ProcessWindowStyle.Hidden,
                                                      UseShellExecute = false,
                                                      CreateNoWindow = true
                                                  }
                                      };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    _isOSX = output.Contains("Darwin Kernel");
                    _isUnix = !_isOSX;
                }
            }
            catch
            {
                _isOSX = false;
                _isUnix = false;
            }
        }
    }
}
