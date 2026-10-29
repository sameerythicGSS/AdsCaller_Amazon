using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace OutBuild
{
    // Loopback only; no runtime component, Python, Node, firewall rule or SDK required.
    [InitializeOnLoad]
    public static class WebPreview
    {
        private sealed class Session
        {
            internal TcpListener listener;
            internal readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            internal readonly ConcurrentDictionary<TcpClient, byte> clients = new ConcurrentDictionary<TcpClient, byte>();
            internal readonly SemaphoreSlim slots = new SemaphoreSlim(8);
            internal string root, error = "";
            internal bool isolate;
            internal int port, requests;
            internal long bytes;
        }
        private static Session session;
        public static bool Running { get { return session != null; } }
        public static string Url { get { return session == null ? "" : "http://127.0.0.1:" + session.port + "/"; } }
        public static string Root { get { return session?.root ?? ""; } }
        public static string Error { get { return session?.error ?? ""; } }
        public static int Requests { get { return session == null ? 0 : Volatile.Read(ref session.requests); } }
        public static long Bytes { get { return session == null ? 0 : Interlocked.Read(ref session.bytes); } }
        static WebPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }
        public static void Start(string root, int port, bool isolate)
        {
            root = WebArtifacts.Root(root);
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Use 0 for an available port or 1–65535.");
            Stop();
            var next = new Session { root = root, isolate = isolate, listener = new TcpListener(IPAddress.Loopback, port) };
            next.listener.Start();
            next.port = ((IPEndPoint)next.listener.LocalEndpoint).Port;
            session = next;
            _ = Serve(next);
        }
        public static void Stop()
        {
            var previous = session; session = null;
            if (previous == null) return;
            previous.cancellation.Cancel(); previous.listener.Stop();
            foreach (var client in previous.clients.Keys) client.Close();
        }
        private static async Task Serve(Session state)
        {
            try
            {
                while (!state.cancellation.IsCancellationRequested)
                {
                    await state.slots.WaitAsync(state.cancellation.Token).ConfigureAwait(false);
                    TcpClient client;
                    try { client = await state.listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                    catch { state.slots.Release(); throw; }
                    state.clients.TryAdd(client, 0);
                    if (state.cancellation.IsCancellationRequested) { client.Close(); state.clients.TryRemove(client, out _); state.slots.Release(); break; }
                    _ = Task.Run(() => Respond(state, client));
                }
            }
            catch (Exception e) { if (!state.cancellation.IsCancellationRequested) state.error = e.Message; }
        }
        private static void Respond(Session state, TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 5000; client.SendTimeout = 15000;
                    using (var stream = client.GetStream())
                    {
                        var header = new StringBuilder();
                        while (header.Length < 16384)
                        {
                            state.cancellation.Token.ThrowIfCancellationRequested();
                            int value = stream.ReadByte();
                            if (value < 0) return;
                            header.Append((char)value);
                            int length = header.Length;
                            if (length >= 4 && header[length - 4] == '\r' && header[length - 3] == '\n' && header[length - 2] == '\r' && header[length - 1] == '\n') break;
                        }
                        string[] request = header.ToString().Split('\n')[0].Trim().Split(' ');
                        Interlocked.Increment(ref state.requests);
                        if (header.Length >= 16384 || request.Length != 3 || (request[0] != "GET" && request[0] != "HEAD"))
                        { SendError(stream, "405 Method Not Allowed"); return; }
                        string file;
                        try { file = WebArtifacts.Resolve(state.root, request[1]); }
                        catch { SendError(stream, "404 Not Found"); return; }
                        using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            string encoding = WebArtifacts.Encoding(file);
                            string response = "HTTP/1.1 200 OK\r\nContent-Type: " + WebArtifacts.Mime(file) + "\r\nContent-Length: " + input.Length + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n";
                            if (encoding.Length > 0) response += "Content-Encoding: " + encoding + "\r\nVary: Accept-Encoding\r\n";
                            if (state.isolate) response += "Cross-Origin-Opener-Policy: same-origin\r\nCross-Origin-Embedder-Policy: require-corp\r\n";
                            Write(stream, response + "\r\n");
                            if (request[0] == "HEAD") return;
                            byte[] buffer = new byte[65536]; int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                state.cancellation.Token.ThrowIfCancellationRequested();
                                stream.Write(buffer, 0, count); Interlocked.Add(ref state.bytes, count);
                            }
                        }
                    }
                }
            }
            catch (IOException) { /* Browser disconnects and reloads are normal. */ }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception e) { state.error = e.Message; }
            finally { state.clients.TryRemove(client, out _); state.slots.Release(); }
        }
        private static void SendError(Stream stream, string status) { Write(stream, "HTTP/1.1 " + status + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); }
        private static void Write(Stream stream, string text) { byte[] bytes = Encoding.ASCII.GetBytes(text); stream.Write(bytes, 0, bytes.Length); }
    }
}
