using System;
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    [DisallowMultipleComponent]
    public class DeviceIpDisplay : MonoBehaviour
    {
        [Tooltip("Text component where the IP address will be displayed. Defaults to the current object.")]
        [SerializeField] private TextMeshProUGUI targetLabel;

        [Tooltip("Optional prefix to display before the IP address.")]
        [SerializeField] private string prefix = "Device IP: ";

        private static string _cachedIp;

        private void Awake()
        {
            if (targetLabel == null)
            {
                targetLabel = GetComponent<TextMeshProUGUI>();
            }
        }

        private void OnEnable()
        {
            UpdateLabel();
        }

        public void Refresh()
        {
            UpdateLabel();
        }

        private void UpdateLabel()
        {
            if (targetLabel == null)
                return;

            if (string.IsNullOrEmpty(_cachedIp))
            {
                _cachedIp = TryGetLocalIPv4();
            }

            targetLabel.text = string.IsNullOrEmpty(prefix)
                ? _cachedIp
                : $"{prefix}{_cachedIp}";
        }

        private static string TryGetLocalIPv4()
        {
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var props = networkInterface.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            && !IPAddress.IsLoopback(addr.Address))
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // ignored – fall back to host name lookup below
            }

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var addr in host.AddressList)
                {
                    if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return addr.ToString();
                }
            }
            catch (Exception)
            {
                // ignored – leave as fallback below
            }

            return "Unavailable";
        }
    }
}
