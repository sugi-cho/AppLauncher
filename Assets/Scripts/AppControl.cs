using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityEngine;
using UnityEngine.UIElements;

using SimpleFileBrowser;
using System.Net.Sockets;
using System.Diagnostics;

using System;
using System.Runtime.InteropServices;
using System.Linq;

public class AppControl : MonoBehaviour
{

    [DllImport("User32.dll")]
    private static extern int SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    string FilePath => Path.Combine(Application.streamingAssetsPath, $"{typeof(Setting)}.json");

    [SerializeField] Setting setting;
    event Action onCancel;

    string _logText;
    bool _listening;

    // Start is called before the first frame update
    void Start()
    {
        var json = "";

        if (!File.Exists(FilePath))
        {
            json = JsonUtility.ToJson(setting);
            File.WriteAllText(FilePath, json);
        }
        else
        {
            json = File.ReadAllText(FilePath);
            setting = JsonUtility.FromJson<Setting>(json);
        }

        var doc = GetComponent<UIDocument>();
        var root = doc.rootVisualElement;
        var treeSource = doc.visualTreeAsset;
        var container = root.Q<ScrollView>("Container");
        var senderView = root.Q("TestMessageSend");

        SetupLogUI();
        setting.settings.ForEach(setting =>
        {
            var ui = SetupSettingUI(setting);
            container.Add(ui);
            StartListenerAsync(setting).Cancel();
        });
        SetupSenderUI(setting.senderSetting);

        VisualElement SetupSettingUI(ListenerSetting listenerSetting)
        {
            var ui = treeSource.CloneTree("ListenerSetting").Q("ListenerSetting");

            var foldout = ui.Q<Foldout>("ListenerSetting");
            var messageField = ui.Q<TextField>("Message");
            var protocolField = ui.Q<EnumField>("ProtocolField");
            var portField = ui.Q<IntegerField>("PortField");
            var reconnectButton = ui.Q<Button>("Reconnect");

            var fileNameField = ui.Q<TextField>("FileField");
            var openButton = ui.Q<Button>("OpenButton");

            {
                foldout.text = listenerSetting.ToString();
                listenerSetting.onValueChanged += () => foldout.text = listenerSetting.ToString();

                messageField.value = listenerSetting.Message;
                messageField.RegisterValueChangedCallback(evt => listenerSetting.Message = evt.newValue);

                protocolField.value = listenerSetting.Type;
                protocolField.RegisterValueChangedCallback(evt => listenerSetting.Type = (ProtocolType)evt.newValue);

                var ipList = Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(ip => ip.AddressFamily.Equals(AddressFamily.InterNetwork))
                    .Select(ip => ip.ToString());
                portField.label = string.Join(",", ipList) + ":";
                portField.value = listenerSetting.Port;
                portField.RegisterValueChangedCallback(evt => listenerSetting.Port = evt.newValue);

                reconnectButton.clicked += () =>
                {
                    onCancel?.Invoke();
                    _logText += "canceled\n";
                };
            }
            {
                fileNameField.value = listenerSetting.FilePath;
                fileNameField.RegisterValueChangedCallback(evt => listenerSetting.FilePath = evt.newValue);
                openButton.clicked += () =>
                {
                    FileBrowser.ShowLoadDialog(fileName => fileNameField.value = fileName[0], () => { }, FileBrowser.PickMode.Files, title: "Select File");
                };
            }

            return ui;
        }

        void SetupSenderUI(SenderSetting senderSetting)
        {
            var messageField = senderView.Q<TextField>("MessageField");
            var typeField = senderView.Q<EnumField>();
            var button = senderView.Q<Button>();
            var ipField = senderView.Q<TextField>("IPField");
            var portField = senderView.Q<IntegerField>("PortField");

            messageField.value = senderSetting.message;
            typeField.value = senderSetting.type;
            ipField.value = senderSetting.ip;
            portField.value = senderSetting.port;

            messageField.RegisterValueChangedCallback(evt => senderSetting.message = evt.newValue);
            typeField.RegisterValueChangedCallback(evt => senderSetting.type = (ProtocolType)evt.newValue);
            ipField.RegisterValueChangedCallback(evt => senderSetting.ip = evt.newValue);
            portField.RegisterValueChangedCallback(evt => senderSetting.port = evt.newValue);

            button.clicked += () =>
            {
                try
                {
                    var message = messageField.value;
                    var type = (ProtocolType)typeField.value;
                    var data = System.Text.Encoding.UTF8.GetBytes(message);
                    var port = portField.value;
                    var ip = Dns.GetHostAddresses(ipField.value)
                    .Where(address => address.AddressFamily.Equals(AddressFamily.InterNetwork))
                    .FirstOrDefault();
                    var remoteEP = new IPEndPoint(ip, port);
                    switch (type)
                    {
                        case ProtocolType.UDP:
                            using (var client = new UdpClient())
                            {
                                client.Send(data, data.Length, remoteEP);
                                _logText += $"send udp message({message}) to {remoteEP}\n";
                            }
                            break;
                        case ProtocolType.TCP:
                            using (var client = new TcpClient(ipField.value, port))
                            using (var stream = client.GetStream())
                            {
                                stream.Write(data, 0, data.Length);
                                _logText += $"send tcp message({message}) to {remoteEP}\n";
                            }
                            break;
                    }
                }
                catch (Exception ex) { _logText += ex.ToString() + "\n"; }
            };
        }

        void SetupLogUI()
        {
            var logField = root.Q<TextField>("LogField");
            var copyButton = root.Q<Button>("CopyButton");
            copyButton.clicked += () => GUIUtility.systemCopyBuffer = logField.value;
            HandleLogUpdateAsync().Cancel();

            async Awaitable HandleLogUpdateAsync()
            {
                var prev = _logText;
                while (true)
                {
                    if (_logText != prev)
                    {
                        logField.value = _logText;
                        prev = _logText;
                    }
                    await Awaitable.NextFrameAsync(destroyCancellationToken);
                }
            }
        }
    }

    async Awaitable StartListenerAsync(ListenerSetting listenerSetting)
    {
        _logText += $"start listening {listenerSetting}\n";
        await Awaitable.BackgroundThreadAsync();
        var process = (Process)null;
        var cancelAction = (Action)null;
        _listening = true;

        if (listenerSetting.Type == ProtocolType.UDP)
        {
            await ReceiveUDPAsync(listenerSetting.Port);
        }
        else
        {
            await ReceiveTCPAsync(listenerSetting.Port);
        }

        onCancel -= cancelAction;
        if (_listening)
            StartListenerAsync(listenerSetting).Cancel();

        async Awaitable ReceiveUDPAsync(int port)
        {
            using (var client = new UdpClient(port))
            {
                cancelAction = () =>
                {
                    client.Close();
                    _logText += "UDPClient.Close\n";
                };
                onCancel += cancelAction;
                try
                {
                    while (_listening)
                    {
                        _logText += "waiting UDPClient Receive...\n";
                        var result = await client.ReceiveAsync();
                        var buffer = result.Buffer;
                        var message = System.Text.Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                        OnMessageReceive(message).Cancel();
                    }
                }
                catch (Exception ex) { _logText += ex + "\n"; }
            }
        }

        async Awaitable ReceiveTCPAsync(int port)
        {
            var buffer = new byte[4096];
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            _logText += "TCPListener.Start\n";

            cancelAction = () =>
            {
                listener.Stop();
                _logText += "TCPListener.Stop";
            };
            onCancel += cancelAction;
            try
            {
                while (_listening)
                {
                    _logText += "Listening TCPListener...\n";
                    using (var client = await listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    {
                        var size = await stream.ReadAsync(buffer, 0, buffer.Length);
                        var message = System.Text.Encoding.UTF8.GetString(buffer, 0, size);
                        OnMessageReceive(message).Cancel();
                    }
                }
            }
            catch (Exception ex) { _logText += ex + "\n"; }
        }

        async Awaitable OnMessageReceive(string msg)
        {
            await Awaitable.MainThreadAsync();
            if (msg == listenerSetting.Message)
            {
                if (process != null && !process.HasExited)
                    process.Kill();
                process = Process.Start(listenerSetting.FilePath);

                while (process.MainWindowHandle == (IntPtr)0)
                    await Awaitable.WaitForSecondsAsync(0.1f);

                IntPtr HWND_TOPMOST = new IntPtr(-1);
                const uint SWP_NOSIZE = 0x0001;
                const uint SWP_SHOWWINDOW = 0x0040;
                _logText += $"SetWindowPos hWnd: {process.MainWindowHandle}\n";

                SetWindowPos(process.MainWindowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            if (msg == listenerSetting.Message + "-kill")
            {
                if (process != null && !process.HasExited)
                    process.Kill();
            }
        }
    }

    private void OnDestroy()
    {
        var json = JsonUtility.ToJson(setting);
        File.WriteAllText(FilePath, json);
        _listening = false;
        onCancel?.Invoke();
    }

    public enum ProtocolType
    {
        UDP, TCP
    }

    [Serializable]
    public struct Setting
    {
        public List<ListenerSetting> settings;
        public SenderSetting senderSetting;
    }
    [Serializable]
    public class ListenerSetting
    {
        public string Message
        {
            get => message;
            set
            {
                message = value;
                onValueChanged?.Invoke();
            }
        }
        [SerializeField] string message;
        public ProtocolType Type
        {
            get => type; set
            {
                type = value;
                onValueChanged?.Invoke();
            }
        }
        [SerializeField] ProtocolType type;
        public int Port
        {
            get => port; set
            {
                port = value;
                onValueChanged?.Invoke();
            }
        }
        [SerializeField] int port;
        public string FilePath
        {
            get => filePath;
            set
            {
                filePath = value;
                onValueChanged?.Invoke();
            }
        }
        [SerializeField] string filePath;

        public override string ToString()
            => $"{message},{type}: {port} => {filePath}";
        public event Action onValueChanged;
    }
    [Serializable]
    public class SenderSetting
    {
        public string message;
        public ProtocolType type;
        public string ip;
        public int port;
    }
}
