using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SafetyProto.Gameplay.Networking.EvaluatorDashboard
{
    /// <summary>
    /// Lightweight WebSocket server intended for on-device evaluator dashboards.
    /// Supports text-frame broadcast only; clients are expected to be trusted (local network).
    /// </summary>
    public class EvaluatorWebSocketServer
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int DefaultTimeoutMs = 5000;

        private readonly List<ClientConnection> _clients = new();
        private readonly object _clientLock = new();

        private TcpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;

        public bool HasConnections
        {
            get
            {
                lock (_clientLock)
                {
                    return _clients.Count > 0;
                }
            }
        }

        public void StartServer(int port)
        {
            if (_running)
                return;

            _running = true;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            _listenerThread = new Thread(ListenLoop) { IsBackground = true };
            _listenerThread.Start();
        }

        public void StopServer()
        {
            _running = false;

            try
            {
                _listener?.Stop();
            }
            catch
            {
                // ignored
            }

            lock (_clientLock)
            {
                foreach (var conn in _clients)
                {
                    conn.Dispose();
                }
                _clients.Clear();
            }

            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                try { _listenerThread.Join(100); } catch { /* ignore */ }
            }
        }

        public void BroadcastJson(string json)
        {
            if (!_running || string.IsNullOrEmpty(json))
                return;

            var payload = Encoding.UTF8.GetBytes(json);
            var frame = BuildFrame(payload);

            List<ClientConnection> disconnected = null;

            lock (_clientLock)
            {
                foreach (var conn in _clients)
                {
                    if (!conn.IsAlive)
                    {
                        disconnected ??= new List<ClientConnection>();
                        disconnected.Add(conn);
                        continue;
                    }

                    conn.Outgoing.Enqueue(frame);
                }

                if (disconnected != null)
                {
                    foreach (var dead in disconnected)
                    {
                        _clients.Remove(dead);
                        dead.Dispose();
                    }
                }
            }
        }

        public void Broadcast<T>(string eventType, T payload)
        {
            var envelope = new Envelope<T> { eventType = eventType, payload = payload };
            BroadcastJson(JsonUtility.ToJson(envelope));
        }

        private void ListenLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    client.NoDelay = true;
                    HandleClientHandshake(client);
                }
                catch (SocketException)
                {
                    // Listener stopped or network issue; exit loop if not running.
                    if (!_running)
                        break;
                }
                catch
                {
                    try { client?.Close(); } catch { /* ignore */ }
                }
            }
        }

        private void HandleClientHandshake(TcpClient client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var requestLine = reader.ReadLine();
            if (string.IsNullOrEmpty(requestLine) || !requestLine.StartsWith("GET", StringComparison.OrdinalIgnoreCase))
            {
                client.Close();
                return;
            }

            string webSocketKey = null;
            string path = "/";
            var parts = requestLine.Split(' ');
            if (parts.Length >= 2)
                path = parts[1];

            string line;
            while (!string.IsNullOrEmpty(line = reader.ReadLine()))
            {
                if (line.StartsWith("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                {
                    var keyParts = line.Split(':');
                    if (keyParts.Length >= 2)
                    {
                        webSocketKey = keyParts[1].Trim();
                    }
                }
            }

            if (string.IsNullOrEmpty(webSocketKey))
            {
                client.Close();
                return;
            }

            var acceptKey = ComputeWebSocketAccept(webSocketKey);
            var responseBuilder = new StringBuilder();
            responseBuilder.Append("HTTP/1.1 101 Switching Protocols\r\n");
            responseBuilder.Append("Upgrade: websocket\r\n");
            responseBuilder.Append("Connection: Upgrade\r\n");
            responseBuilder.Append("Sec-WebSocket-Accept: ").Append(acceptKey).Append("\r\n");
            responseBuilder.Append("\r\n");

            var responseBytes = Encoding.ASCII.GetBytes(responseBuilder.ToString());
            stream.Write(responseBytes, 0, responseBytes.Length);

            client.ReceiveTimeout = DefaultTimeoutMs;
            client.SendTimeout = DefaultTimeoutMs;

            var connection = new ClientConnection(client, stream, path);
            lock (_clientLock)
            {
                _clients.Add(connection);
            }

            var watcher = new Thread(() => ClientReadLoop(connection)) { IsBackground = true };
            watcher.Start();
        }

        private void ClientReadLoop(ClientConnection conn)
        {
            var buffer = new byte[2048];
            try
            {
                var stream = conn.Stream;
                while (_running && conn.IsAlive)
                {
                    if (!ReadExact(stream, buffer, 0, 2, out var headerRead))
                        break;

                    var b0 = buffer[0];
                    var b1 = buffer[1];
                    var fin = (b0 & 0x80) != 0;
                    var opcode = b0 & 0x0F;
                    var masked = (b1 & 0x80) != 0;
                    ulong payloadLen = (uint)(b1 & 0x7F);

                    if (payloadLen == 126)
                    {
                        if (!ReadExact(stream, buffer, 0, 2, out _))
                            break;
                        payloadLen = (ulong)((buffer[0] << 8) | buffer[1]);
                    }
                    else if (payloadLen == 127)
                    {
                        if (!ReadExact(stream, buffer, 0, 8, out _))
                            break;
                        payloadLen = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            payloadLen = (payloadLen << 8) | buffer[i];
                        }
                    }

                    byte[] maskKey = null;
                    if (masked)
                    {
                        maskKey = new byte[4];
                        if (!ReadExact(stream, maskKey, 0, 4, out _))
                            break;
                    }

                    if (payloadLen > (ulong)buffer.Length)
                    {
                        buffer = new byte[payloadLen];
                    }

                    if (!ReadExact(stream, buffer, 0, (int)payloadLen, out var payloadRead))
                        break;

                    var payload = buffer;
                    if (masked && payloadRead > 0)
                    {
                        for (int i = 0; i < payloadRead; i++)
                        {
                            payload[i] = (byte)(payload[i] ^ maskKey[i % 4]);
                        }
                    }

                    switch (opcode)
                    {
                        case 0x9: // Ping
                            conn.Outgoing.Enqueue(BuildControlFrame(0xA, payload, payloadRead));
                            break;
                        case 0x8: // Close
                            DisconnectClient(conn);
                            return;
                        default:
                            // Ignore other opcodes for now (text/binary not needed)
                            break;
                    }
                }
            }
            catch
            {
                // ignore read failures; connection will be closed
            }
            finally
            {
                DisconnectClient(conn);
            }
        }

        private static string ComputeWebSocketAccept(string key)
        {
            var combined = Encoding.ASCII.GetBytes(key + WebSocketGuid);
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(combined);
            return Convert.ToBase64String(hash);
        }

        private static byte[] BuildFrame(byte[] payload)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x81); // FIN + text frame

            var length = payload.Length;
            if (length <= 125)
            {
                ms.WriteByte((byte)length);
            }
            else if (length <= ushort.MaxValue)
            {
                ms.WriteByte(126);
                ms.WriteByte((byte)((length >> 8) & 0xFF));
                ms.WriteByte((byte)(length & 0xFF));
            }
            else
            {
                ms.WriteByte(127);
                var len = (ulong)length;
                for (int i = 7; i >= 0; i--)
                {
                    ms.WriteByte((byte)((len >> (8 * i)) & 0xFF));
                }
            }

            ms.Write(payload, 0, payload.Length);
            return ms.ToArray();
        }

        private static byte[] BuildControlFrame(byte opcode, byte[] payload, int payloadLength)
        {
            using var ms = new MemoryStream();
            ms.WriteByte((byte)(0x80 | opcode)); // FIN + opcode

            if (payloadLength <= 125)
            {
                ms.WriteByte((byte)payloadLength);
            }
            else if (payloadLength <= ushort.MaxValue)
            {
                ms.WriteByte(126);
                ms.WriteByte((byte)((payloadLength >> 8) & 0xFF));
                ms.WriteByte((byte)(payloadLength & 0xFF));
            }
            else
            {
                ms.WriteByte(127);
                var len = (ulong)payloadLength;
                for (int i = 7; i >= 0; i--)
                {
                    ms.WriteByte((byte)((len >> (8 * i)) & 0xFF));
                }
            }

            if (payload != null && payloadLength > 0)
            {
                ms.Write(payload, 0, payloadLength);
            }
            return ms.ToArray();
        }

        private static bool ReadExact(NetworkStream stream, byte[] buffer, int offset, int count, out int bytesRead)
        {
            bytesRead = 0;
            while (bytesRead < count)
            {
                var read = stream.Read(buffer, offset + bytesRead, count - bytesRead);
                if (read <= 0)
                    return false;
                bytesRead += read;
            }
            return true;
        }

        [Serializable]
        private class Envelope<T>
        {
            public string eventType;
            public T payload;
        }

        private sealed class ClientConnection : IDisposable
        {
            public readonly TcpClient Client;
            public readonly NetworkStream Stream;
            public readonly ConcurrentQueue<byte[]> Outgoing = new();
            public readonly CancellationTokenSource Cts = new();
            public readonly Thread SenderThread;
            public readonly string Path;

            public bool IsAlive => !Cts.IsCancellationRequested;

            public ClientConnection(TcpClient client, NetworkStream stream, string path)
            {
                Client = client;
                Stream = stream;
                Path = path;
                SenderThread = new Thread(SenderLoop) { IsBackground = true };
                SenderThread.Start();
            }

            private void SenderLoop()
            {
                while (!Cts.IsCancellationRequested)
                {
                    if (Outgoing.TryDequeue(out var frame))
                    {
                        try
                        {
                            Stream.Write(frame, 0, frame.Length);
                        }
                        catch
                        {
                            Cts.Cancel();
                            break;
                        }
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
            }

            public void Dispose()
            {
                Cts.Cancel();
                try { SenderThread?.Join(50); } catch { /* ignore */ }
                try { Client.Close(); } catch { /* ignore */ }
            }
        }

        private void DisconnectClient(ClientConnection conn)
        {
            lock (_clientLock)
            {
                _clients.Remove(conn);
            }
            conn.Dispose();
        }
    }
}
