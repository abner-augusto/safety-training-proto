using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SafetyProto.Gameplay.Networking.EvaluatorDashboard
{
    /// <summary>
    /// Ultra-small HTTP server to serve embedded dashboard files directly from Resources.
    /// Only supports GET for index.html, app.js, and optional style.css.
    /// </summary>
    public class MiniHttpServer
    {
        private readonly Dictionary<string, Route> _routes;

        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public MiniHttpServer(byte[] indexBytes, byte[] appBytes, byte[] styleBytes = null)
        {
            _routes = new Dictionary<string, Route>
            {
                { "/", new Route(indexBytes ?? Array.Empty<byte>(), "text/html; charset=utf-8") },
                { "/index.html", new Route(indexBytes ?? Array.Empty<byte>(), "text/html; charset=utf-8") },
                { "/app.js", new Route(appBytes ?? Array.Empty<byte>(), "application/javascript") }
            };

            if (styleBytes != null)
            {
                _routes["/style.css"] = new Route(styleBytes, "text/css");
            }
        }

        public void Start(int port)
        {
            if (_running)
                return;

            _running = true;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            _thread = new Thread(ListenLoop) { IsBackground = true };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            if (_thread != null && _thread.IsAlive)
            {
                try { _thread.Join(100); } catch { /* ignore */ }
            }
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
                    HandleClient(client);
                }
                catch (SocketException)
                {
                    if (!_running)
                        break;
                }
                catch
                {
                    try { client?.Close(); } catch { /* ignore */ }
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 5000;
                var buffer = new byte[1024];
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    return;

                var requestLine = GetRequestLine(buffer, read);
                var path = ParsePath(requestLine);
                if (string.IsNullOrEmpty(path))
                    return;

                if (_routes.TryGetValue(path, out var route))
                {
                    WriteResponse(stream, route.ContentType, route.Body);
                }
                else
                {
                    WriteNotFound(stream);
                }
            }
        }

        private static string GetRequestLine(byte[] buffer, int length)
        {
            for (int i = 0; i < length - 1; i++)
            {
                if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n')
                {
                    return Encoding.ASCII.GetString(buffer, 0, i);
                }
            }
            return Encoding.ASCII.GetString(buffer, 0, length);
        }

        private static string ParsePath(string requestLine)
        {
            if (string.IsNullOrEmpty(requestLine))
                return null;

            var parts = requestLine.Split(' ');
            if (parts.Length < 2)
                return null;

            var method = parts[0].ToUpperInvariant();
            if (method != "GET")
                return null;

            var path = parts[1];
            var queryIndex = path.IndexOf('?');
            if (queryIndex >= 0)
            {
                path = path.Substring(0, queryIndex);
            }

            if (string.IsNullOrEmpty(path))
                return "/";

            return path;
        }

        private static void WriteResponse(NetworkStream stream, string contentType, byte[] body)
        {
            var builder = new StringBuilder();
            builder.Append("HTTP/1.1 200 OK\r\n");
            builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
            builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            builder.Append("Connection: close\r\n");
            builder.Append("Cache-Control: no-cache\r\n");
            builder.Append("\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        private static void WriteNotFound(NetworkStream stream)
        {
            const string bodyText = "404 Not Found";
            var body = Encoding.UTF8.GetBytes(bodyText);
            var builder = new StringBuilder();
            builder.Append("HTTP/1.1 404 Not Found\r\n");
            builder.Append("Content-Type: text/plain\r\n");
            builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            builder.Append("Connection: close\r\n");
            builder.Append("Cache-Control: no-cache\r\n");
            builder.Append("\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
        }

        private readonly struct Route
        {
            public readonly byte[] Body;
            public readonly string ContentType;

            public Route(byte[] body, string contentType)
            {
                Body = body ?? Array.Empty<byte>();
                ContentType = contentType;
            }
        }
    }
}
