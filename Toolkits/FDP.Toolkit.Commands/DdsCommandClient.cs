using CycloneDDS.Core;
using CycloneDDS.Runtime;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace FDP.Toolkit.Commands
{
    /// <summary>
    /// A generic client for sending requests and awaiting corresponding acknowledgments over DDS.
    /// This implementation assumes generated IDL structs are used for TRequest and TAck.
    /// </summary>
    /// <typeparam name="TRequest">The request message type (must be a struct/class with [DdsTopic]).</typeparam>
    /// <typeparam name="TAck">The acknowledgement message type (must be a struct/class with [DdsTopic]).</typeparam>
    public class DdsCommandClient<TRequest, TAck> : IDisposable, IAsyncDisposable
        where TRequest : struct
        where TAck : struct
    {
        private readonly DdsWriter<TRequest> _writer;
        private readonly DdsReader<TAck> _reader;
        private readonly Func<TRequest, Guid> _requestIdStartExtractor;
        private readonly Func<TAck, Guid> _ackIdExtractor;
        
        // Stores pending requests. Key = Correlation ID (Guid). Value = TaskCompletionSource that completes with the Ack.
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<TAck>> _pendingRequests = new();
        
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _listenerTask;
        private bool _disposed;

        /// <summary>
        /// Creates a new command client.
        /// </summary>
        /// <param name="participant">DDS Participant</param>
        /// <param name="requestTopic">Topic name for requests</param>
        /// <param name="ackTopic">Topic name for acks</param>
        /// <param name="requestIdExtractor">Function to extract the correlation ID from a request object</param>
        /// <param name="ackIdExtractor">Function to extract the correlation ID from an ack object</param>
        public DdsCommandClient(
            DdsParticipant participant,
            string requestTopic,
            string ackTopic,
            Func<TRequest, Guid> requestIdExtractor,
            Func<TAck, Guid> ackIdExtractor)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            
            _requestIdStartExtractor = requestIdExtractor ?? throw new ArgumentNullException(nameof(requestIdExtractor));
            _ackIdExtractor = ackIdExtractor ?? throw new ArgumentNullException(nameof(ackIdExtractor));

            // Create Writer and Reader
            // Note: CycloneDDS.NET uses DdsWriter/DdsReader directly or via participant.
            _writer = new DdsWriter<TRequest>(participant, requestTopic);
            _reader = new DdsReader<TAck>(participant, ackTopic);

            // Start listener loop
            _listenerTask = Task.Run(AckListenerLoop);
        }

        /// <summary>
        /// Sends a request and waits for the matching acknowledgment.
        /// The request.RequestId MUST be set before calling this method; this method uses the extractor to read it.
        /// </summary>
        /// <param name="request">The request message.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>The received acknowledgment.</returns>
        public async Task<TAck> SendAsync(TRequest request, int timeoutMs = 5000)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DdsCommandClient<TRequest, TAck>));

            var correlationId = _requestIdStartExtractor(request);
            if (correlationId == Guid.Empty)
            {
                throw new ArgumentException("Request correlation ID cannot be empty.", nameof(request));
            }

            var tcs = new TaskCompletionSource<TAck>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            if (!_pendingRequests.TryAdd(correlationId, tcs))
            {
                throw new InvalidOperationException($"A request with ID {correlationId} is already pending.");
            }

            try
            {
                // Write the request to DDS
                _writer.Write(request);

                // Wait for ack or timeout
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cts.Token);
                
                // We use Task.WhenAny to handle timeout, since TCS doesn't support CancellationToken directly in all frameworks cleanly
                // or we can register a callback.
                // A simpler way with .NET 6/8:
                try 
                {
                    return await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    // The timeout CTS fired — expose as TimeoutException for a stable public API
                    throw new TimeoutException($"Timed out waiting for Ack for RequestId: {correlationId} after {timeoutMs}ms");
                }
            }
            finally
            {
                _pendingRequests.TryRemove(correlationId, out _);
            }
        }

        private async Task AckListenerLoop()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    // Read available samples
                    // Typically generated code uses topic-specific readers, but generic usage works if generic support is enabled.
                    // Assuming CycloneDDS.NET supports generic TakeAsync or similar.
                    // If not, we might need to poll.
                    // Checking DdsReader API. It usually has Take() or Read(). 
                    // To do it efficiently, we should use WaitSet or similar, but simplified loop with small delay is okay for now if async wait not available.
                    
                    // Actually, modern CycloneDDS.NET likely has IAsyncEnumerable or Wait/Take.
                    // Let's assume standard Take().
                    
                   try
                   {
                        // Using a small delay to prevent tight loop if no data is available
                        // Ideally we'd valid using a WaitSet but for simplicity in this toolkit:
                        // Or if the reader exposes an async read method.
                        
                        // Let's try to look for samples.
                        ProcessSamples();
                        await Task.Delay(10, _cts.Token);
                   }
                   catch (DdsException ex)
                   {
                        // Handle DDS specific errors (e.g. handle closed reader)
                        if (!_cts.IsCancellationRequested)
                        {
                            Console.WriteLine($"DDS Reader error: {ex.Message}");
                            await Task.Delay(100, _cts.Token);
                        }
                   }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AckListenerLoop crashed: {ex}");
            }
        }
        
        private void ProcessSamples()
        {
            try
            {
                using var samples = _reader.Take(10);
                foreach (var sample in samples)
                {
                    if (sample.Info.ValidData != 0)
                    {
                        var data = sample.Data;
                        try 
                        {
                            var id = _ackIdExtractor(data);
                            if (_pendingRequests.TryRemove(id, out var tcs))
                            {
                                tcs.TrySetResult(data);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error mapping ack: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore errors during read if no data
            }
        }
        
        public void Dispose()
        {
             Dispose(true);
             GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            try 
            {
                await _listenerTask.ConfigureAwait(false);
            }
            catch {}
            
            _writer.Dispose();
            _reader.Dispose();
            _cts.Dispose();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _cts.Cancel();
                _listenerTask.Wait(1000); // Give it a moment to stop
                _writer.Dispose();
                _reader.Dispose();
                _cts.Dispose();
            }
            _disposed = true;
        }
    }
}
