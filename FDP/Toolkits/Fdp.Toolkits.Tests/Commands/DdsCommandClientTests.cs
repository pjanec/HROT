using CycloneDDS.Core;
using CycloneDDS.Runtime;
using CycloneDDS.Schema; // For [DdsTopic], [DdsKey]
using Fdp.Toolkit.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace Fdp.Toolkit.Commands.Tests
{
    [DdsTopic]
    [DdsManaged]
    public partial struct TestRequest
    {
        [DdsKey]
        public Guid RequestId;
        public string Message;
    }

    [DdsTopic]
    [DdsManaged]
    public partial struct TestAck
    {
        [DdsKey]
        public Guid RequestId;
        public string Reply;
    }

    [TestClass]
    public class DdsCommandClientTests
    {
        [TestMethod]
        public async Task SendAsync_ReceivesAck_Success()
        {
            // Setup
            // ⭐ AQ107 A — isolation used to be a private topic NAME per test; the names are derived from the types now, so each test takes a private DOMAIN.
            using var participant = new DdsParticipant(67u);
            
            // Client Side
            using var client = new DdsCommandClient<TestRequest, TestAck>(
                participant,
                req => req.RequestId,
                ack => ack.RequestId);

            // Server Side (Simulated)
            // Note: the topic names are derived from the types (AQ107 A)
            using var serverReader = new DdsReader<TestRequest>(participant);
            using var serverWriter = new DdsWriter<TestAck>(participant);

            // Start Server Logic in background
            var serverTask = Task.Run(async () =>
            {
                // Wait for request
                // Simple polling for test
                for (int i = 0; i < 50; i++)
                {
                    try
                    {
                        if (TryHandleRequest())
                        {
                            return; // Done
                        }
                    }
                    catch { } // method might throw if no data/timeout

                    await Task.Delay(100);
                }

                // Synchronous local function: DdsLoan/DdsSample are ref structs and
                // cannot be used inside this async method's state machine.
                bool TryHandleRequest()
                {
                    using var samples = serverReader.Take(1);
                    foreach (var sample in samples)
                    {
                        if (sample.Info.ValidData != 0)
                        {
                            var req = sample.Data;
                            // Send Ack
                            var ack = new TestAck
                            {
                                RequestId = req.RequestId,
                                Reply = "Ack: " + req.Message
                            };
                            serverWriter.Write(ack);
                            return true;
                        }
                    }
                    return false;
                }
            });

            // Action
            var request = new TestRequest { RequestId = Guid.NewGuid(), Message = "Hello" };
            var ackReceived = await client.SendAsync(request, 5000);

            // Assert
            Assert.AreEqual(request.RequestId, ackReceived.RequestId);
            Assert.AreEqual("Ack: Hello", ackReceived.Reply);

            await serverTask;
        }

        [TestMethod]
        public async Task SendAsync_Timeout_ThrowsTimeoutException()
        {
            // Setup (own domain: another test's server must not answer this one's request)
            using var participant = new DdsParticipant(68u);
            using var client = new DdsCommandClient<TestRequest, TestAck>(
                participant,
                req => req.RequestId,
                ack => ack.RequestId);

            // Action & Assert
            var request = new TestRequest { RequestId = Guid.NewGuid(), Message = "Hello" };
            await Assert.ThrowsExceptionAsync<TimeoutException>(async () =>
            {
                await client.SendAsync(request, 1000); // 1s timeout
            });
        }
    }
}
