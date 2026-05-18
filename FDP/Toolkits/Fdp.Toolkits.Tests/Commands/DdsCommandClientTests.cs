using CycloneDDS.Core;
using CycloneDDS.Runtime;
using CycloneDDS.Schema; // For [DdsTopic], [DdsKey]
using Fdp.Toolkit.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace Fdp.Toolkit.Commands.Tests
{
    [DdsTopic("TestRequest")]
    [DdsManaged]
    public partial struct TestRequest
    {
        [DdsKey]
        public Guid RequestId;
        public string Message;
    }

    [DdsTopic("TestAck")]
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
            using var participant = new DdsParticipant();
            
            // Client Side
            using var client = new DdsCommandClient<TestRequest, TestAck>(
                participant,
                "TestRequest",
                "TestAck",
                req => req.RequestId,
                ack => ack.RequestId);

            // Server Side (Simulated)
            // Note: DdsTopic<T> not available, using topic name string directly in Reader/Writer constructor
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
                                return; // Done
                            }
                        }
                    }
                    catch { } // method might throw if no data/timeout
                    
                    await Task.Delay(100);
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
            // Setup
            using var participant = new DdsParticipant();
            using var client = new DdsCommandClient<TestRequest, TestAck>(
                participant,
                "TestRequest_Timeout",
                "TestAck_Timeout",
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
