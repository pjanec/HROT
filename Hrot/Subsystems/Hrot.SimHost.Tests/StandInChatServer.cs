using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4681</c> (DESIGN_LLM_Commander §5a-3, L-3 ②) — an Ollama-style chat server for the rails, in process: <c>POST /api/chat</c> answers from a
    /// script (each request gets the next answer; the last one repeats), and records every request body. An answer may be delayed (a slow model) or
    /// be an HTTP error. ⚠ The run-time stand-in (<c>scripts/llm/chat_standin.py</c>) is a PROCESS that can also relay to an agent; this one is a rail
    /// fixture the test controls — the same response shape.
    /// </summary>
    public sealed class StandInChatServer : IDisposable
    {
        public sealed record Answer(string Body, int Status = 200, TimeSpan Delay = default);

        private readonly HttpListener _listener = new();
        private readonly List<Answer> _script;
        /// <summary>⭐ CE-4684 — an answer made FROM the request (a decision must echo the briefing's cycle); null = the script.</summary>
        private readonly Func<JsonObject, int, Answer>? _reply;
        private readonly CancellationTokenSource _stop = new();
        public readonly List<JsonObject> Requests = new();

        public string Endpoint { get; }

        public StandInChatServer(params Answer[] script)
        {
            _script = new List<Answer>(script);
            int port = FreePort();
            Endpoint = $"http://localhost:{port}";
            _listener.Prefixes.Add(Endpoint + "/");
            _listener.Start();
            _ = Task.Run(Serve);
        }

        /// <summary>⭐ CE-4684 — answers from <paramref name="reply"/>(request, its index).</summary>
        public StandInChatServer(Func<JsonObject, int, Answer> reply) : this(Array.Empty<Answer>()) => _reply = reply;

        /// <summary>⭐ CE-4684 — the CYCLE number of a decision request (its user message's first line).</summary>
        public static long CycleOf(JsonObject request)
        {
            var msgs = (JsonArray)request["messages"]!;
            for (int i = msgs.Count - 1; i >= 0; i--)
                if ((string?)msgs[i]!["role"] == "user" && ((string?)msgs[i]!["content"] ?? "").StartsWith("CYCLE ", StringComparison.Ordinal))
                    return long.Parse(((string)msgs[i]!["content"]!).Substring(6).Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
            return -1;
        }

        /// <summary>A message with these tool calls (<c>(name, argumentsJson)</c>; an arguments text that is not JSON is sent as a string, as a model may).</summary>
        public static Answer Calls(params (string Name, string Arguments)[] calls)
        {
            var list = new JsonArray();
            foreach (var (name, args) in calls)
            {
                JsonNode? a;
                try { a = JsonNode.Parse(args); } catch (System.Text.Json.JsonException) { a = JsonValue.Create(args); }
                list.Add(new JsonObject { ["function"] = new JsonObject { ["name"] = name, ["arguments"] = a } });
            }
            return new Answer(new JsonObject
            {
                ["model"] = "stand-in", ["done"] = true,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "", ["tool_calls"] = list },
            }.ToJsonString());
        }

        /// <summary>A message with plain text and no tool call.</summary>
        public static Answer Text(string text) => new(new JsonObject
        {
            ["model"] = "stand-in", ["done"] = true, ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text },
        }.ToJsonString());

        private async Task Serve()
        {
            int n = 0;
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) { return; }
                string body;
                using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = await r.ReadToEndAsync().ConfigureAwait(false);
                Answer answer;
                lock (Requests)
                {
                    var request = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
                    Requests.Add(request);
                    answer = _reply != null ? _reply(request, n) : _script.Count == 0 ? Text("") : _script[Math.Min(n, _script.Count - 1)];
                    n++;
                }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (answer.Delay > TimeSpan.Zero) await Task.Delay(answer.Delay, _stop.Token).ConfigureAwait(false);
                        var bytes = Encoding.UTF8.GetBytes(answer.Body);
                        ctx.Response.StatusCode = answer.Status;
                        ctx.Response.ContentType = "application/json";
                        await ctx.Response.OutputStream.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
                        ctx.Response.Close();
                    }
                    catch (Exception) { try { ctx.Response.Abort(); } catch (Exception) { } }
                });
            }
        }

        /// <summary>A port nothing listens on (a server that is down).</summary>
        public static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); _listener.Close(); } catch (Exception) { }
        }
    }
}
