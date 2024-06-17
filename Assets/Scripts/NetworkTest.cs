using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class NetworkTest : MonoBehaviour
{
    [SerializeField] string hostname;
    [SerializeField] IPAddress[] ips;

    // Start is called before the first frame update
    void Start()
    {
        hostname = Dns.GetHostName();
        ips = Dns.GetHostAddresses(hostname);
        foreach (IPAddress ip in ips.Where(ip => ip.AddressFamily.Equals(AddressFamily.InterNetwork)))
        {
            Debug.Log(ip);
        }
    }

    // Update is called once per frame
    void Update()
    {

    }
}
