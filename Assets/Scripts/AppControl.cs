using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityEngine;
using UnityEngine.UIElements;

using SimpleFileBrowser;
using System.Net.Sockets;
using System.Diagnostics;

using Debug = UnityEngine.Debug;
using System;
using System.Runtime.InteropServices;

public class AppControl : MonoBehaviour
{

    [DllImport("User32.dll")]
    private static extern int SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    string FilePath => Path.Combine(Application.streamingAssetsPath, $"{typeof(Setting)}.json");

    [SerializeField] Setting setting;
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

            var messageField = ui.Q<TextField>("Message");
            var protocolField = ui.Q<EnumField>("ProtocolField");
            var portField = ui.Q<IntegerField>("PortField");

            var fileNameField = ui.Q<TextField>("FileField");
            var openButton = ui.Q<Button>("OpenButton");

            {
                messageField.value = listenerSetting.message;
                messageField.RegisterValueChangedCallback(evt => listenerSetting.message = evt.newValue);

                protocolField.value = listenerSetting.type;
                protocolField.RegisterValueChangedCallback(evt => listenerSetting.type = (ProtocolType)evt.newValue);

                portField.value = listenerSetting.port;
                portField.RegisterValueChangedCallback(evt => listenerSetting.port = evt.newValue);
            }
            {
                fileNameField.value = listenerSetting.filePath;
                fileNameField.RegisterValueChangedCallback(evt => listenerSetting.filePath = evt.newValue);
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

            ipField.RegisterValueChangedCallback(evt =>
            {
                if (!IPAddress.TryParse(evt.newValue, out var val))
                    ipField.SetValueWithoutNotify(evt.previousValue);
            });

            button.clicked += () =>
            {
                var message = messageField.value;
                var type = (ProtocolType)typeField.value;
                var data = System.Text.Encoding.UTF8.GetBytes(message);
                var port = portField.value;
                if (IPAddress.TryParse(ipField.value, out var ip))
                {
                    var remoteEP = new IPEndPoint(ip, port);
                    Debug.Log(remoteEP.ToString());
                    switch (type)
                    {
                        case ProtocolType.UDP:
                            using (var client = new UdpClient())
                            {
                                client.Send(data, data.Length, remoteEP);
                            }
                            break;
                        case ProtocolType.TCP:
                            using (var client = new TcpClient(ipField.value, port))
                            using (var stream = client.GetStream())
                            {
                                stream.Write(data, 0, data.Length);
                            }
                            break;
                    }
                }
            };
        }
    }

    async Awaitable StartListenerAsync(ListenerSetting listenerSetting)
    {
        await Awaitable.BackgroundThreadAsync();
        Process process = null;
        _listening = true;

        if (listenerSetting.type == ProtocolType.UDP)
        {
            Debug.Log("wait for udp");
            await ReceiveUDPAsync(listenerSetting.port);
        }
        else
        {
            Debug.Log("wait for tcp");
            await ReceiveTCPAsync(listenerSetting.port);
        }

        StartListenerAsync(listenerSetting).Cancel();

        async Awaitable ReceiveUDPAsync(int port)
        {
            using (var client = new UdpClient(port))
            {
                while (_listening)
                {
                    var result = await client.ReceiveAsync();
                    var buffer = result.Buffer;
                    var message = System.Text.Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                    OnMessageReceive(message).Cancel();
                }
            }
        }

        async Awaitable ReceiveTCPAsync(int port)
        {
            var buffer = new byte[4096];
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            while (_listening)
            {
                using (var client = await listener.AcceptTcpClientAsync())
                using (var stream = client.GetStream())
                {
                    var size = await stream.ReadAsync(buffer, 0, buffer.Length);
                    var message = System.Text.Encoding.UTF8.GetString(buffer, 0, size);
                    OnMessageReceive(message).Cancel();
                }
            }
            listener.Stop();
        }

        async Awaitable OnMessageReceive(string msg)
        {
            await Awaitable.MainThreadAsync();
            if (msg == listenerSetting.message)
            {
                if (process != null && !process.HasExited)
                    process.Kill();
                process = Process.Start(listenerSetting.filePath);

                while (process.MainWindowHandle == (IntPtr)0)
                    await Awaitable.WaitForSecondsAsync(0.1f);

                IntPtr HWND_TOPMOST = new IntPtr(-1);
                const uint SWP_NOSIZE = 0x0001;
                const uint SWP_SHOWWINDOW = 0x0040;
                Debug.Log("hWnd:" + process.MainWindowHandle);

                SetWindowPos(process.MainWindowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            if (msg == listenerSetting.message + "-kill")
            {
                if (process != null && !process.HasExited)
                    process.Kill();
            }
        }
    }

    private void OnDestroy()
    {
        _listening = false;
        var json = JsonUtility.ToJson(setting);
        File.WriteAllText(FilePath, json);
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
        public string message;
        public ProtocolType type;
        public int port;
        public string filePath;
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
