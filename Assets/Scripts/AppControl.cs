using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using UnityEngine;
using UnityEngine.UIElements;

using SimpleFileBrowser;

public class AppControl : MonoBehaviour
{
    string FilePath => Path.Combine(Application.streamingAssetsPath, $"{typeof(Setting)}.json");

    [SerializeField] Setting setting;
    // Start is called before the first frame update
    void Start()
    {
        var doc = GetComponent<UIDocument>();
        var root = doc.rootVisualElement;
        var treeSource = doc.visualTreeAsset;
        var container = root.Q<ScrollView>("Container");

        setting.settings.ForEach(setting =>
        {
            var ui = SetupSettingUI(setting);
            container.Add(ui);
        });

        VisualElement SetupSettingUI(ListenerSetting listenerSetting)
        {
            var ui = treeSource.CloneTree("ListenerSetting").Q("ListenerSetting");

            var messageField = ui.Q<TextField>("Message");
            var protocolField = ui.Q<EnumField>("ProtocolField");
            var ipField = ui.Q<DropdownField>("IPField");
            var portField = ui.Q<TextField>("PortField");

            var fileNameField = ui.Q<TextField>("FileField");
            var openButton = ui.Q<Button>("OpenButton");
            var killToggle = ui.Q<Toggle>("KillToggle");

            {
                messageField.value = listenerSetting.message;
                messageField.RegisterValueChangedCallback(evt => listenerSetting.message = evt.newValue);

                protocolField.value = listenerSetting.type;
                protocolField.RegisterValueChangedCallback(evt => listenerSetting.type = (ProtocolType)evt.newValue);

                var ips = Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(ip=>ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(ip => ip.ToString());
                ipField.choices.AddRange(ips);
                ipField.value = listenerSetting.ip;
                ipField.RegisterValueChangedCallback(evt => listenerSetting.ip = evt.newValue);
                if (ipField.index < 0)
                {
                    ipField.index = 0;
                    listenerSetting.ip = ipField.value;
                }

                portField.value = listenerSetting.port.ToString();
                portField.RegisterValueChangedCallback(evt =>
                {
                    if (int.TryParse(evt.newValue, out var val))
                        listenerSetting.port = val;
                    else
                        portField.SetValueWithoutNotify(evt.previousValue);
                });
            }
            {
                fileNameField.value = listenerSetting.filePath;
                fileNameField.RegisterValueChangedCallback(evt => listenerSetting.filePath = evt.newValue);
                openButton.clicked += () => {
                    FileBrowser.ShowLoadDialog(fileName => fileNameField.value = fileName[0], () => { }, FileBrowser.PickMode.Files, title:"Select File");
                };
                killToggle.value = listenerSetting.useKillMessage;
                killToggle.RegisterValueChangedCallback(evt => listenerSetting.useKillMessage = evt.newValue);
            }

            return ui;
        }
    }

    private void OnDestroy()
    {
        var json = JsonUtility.ToJson(setting);
        File.WriteAllText(FilePath, json);
    }

    public enum ProtocolType
    {
        UDP, TCP
    }

    [System.Serializable]
    public struct Setting
    {
        public List<ListenerSetting> settings;
    }
    [System.Serializable]
    public class ListenerSetting
    {
        public string message;
        public ProtocolType type;
        public string ip;
        public int port;
        public string filePath;
        public bool useKillMessage;
    }
}
