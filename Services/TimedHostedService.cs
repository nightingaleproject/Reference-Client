using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NVSSClient.Models;
using NVSSClient.Controllers;

using System.Text;
using System.Net.Http;
using System.Text.Json;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRDR;

namespace NVSSClient.Services
{
    // a timed service that runs every x seconds to pull new messages from the db and submit, 
    // check for responses, resend messages that haven't had a response in x time
    public class TimedHostedService : IHostedService, IDisposable
    {
        //private readonly AppDbContext _context;

        private static String jurisdictionEndPoint = "https://example.com/jurisdiction/message/endpoint"; // make part of the configuration
        private static String apiUrl = "https://localhost:5001/bundles";
        private static String lastUpdated = new DateTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
        private static int interval = 10;
        static readonly HttpClient client = new HttpClient();

        private int executionCount = 0;
        private readonly ILogger<TimedHostedService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private Timer _timer;

        public TimedHostedService(ILogger<TimedHostedService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service running.");

            _timer = new Timer(DoWork, null, TimeSpan.Zero, 
                TimeSpan.FromSeconds(60));

            return Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            var count = Interlocked.Increment(ref executionCount);

            _logger.LogInformation(
                "Timed Hosted Service is working. Count: {Count}", count);
            
            // Step 1, submit new records in the db
            // TODO change this to a listening endpoint and submit messages on receipt
            SubmitMessages();
            // Step 2, poll for response messages from the server
            PollForResponses();

            // Step 3, check for messages that haven't received an ack in X amount of time
            
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        // Retrieve records from database and send to the endpoint

        public void SubmitMessages()
        {
            // scope the db context, its not meant to last the whole life cycle
            // and we need to deconflict for other db calls
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                //do what you need
                var items = context.MessageItems; // TODO change this to only get unacknowledged ones
                Console.WriteLine($"Found records {count}", items.Count);
                foreach (MessageItem item in items)
                {
                    BaseMessage msg = BaseMessage.Parse(item.Message.ToString(), true);
                    postMessage(msg);
                }
            } //scope (and context) gets destroyed here
        }

    //    public void InsertMessage(MessageItem item)
    //     {
    //         try 
    //         {
    //             using (var scope = _scopeFactory.CreateScope()){
    //                 var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    //                 // Create MessageItem
    //                 item.Status = Models.MessageStatus.Sent;
    //                 item.Retries = 0;
    //                 item.SentOn = DateTime.UtcNow;

    //                 // insert new message
    //                 context.MessageItems.Add(item);
    //                 context.SaveChanges();
    //                 Console.WriteLine($"Inserted message {item.Uid}");
    //             }

    //         } catch (Exception e)
    //         {
    //             Console.WriteLine($"Error saving message {item.Uid}");
    //             Console.WriteLine("\nException Caught!");	
    //             Console.WriteLine("Message :{0} ",e.Message);
    //         }
    //     }


        private void PollForResponses()
        {
            // Capture the time just before we make the request
            String nextUpdated = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
            
            // retrieve new messages
            var address = apiUrl;
            Console.WriteLine($"Get messages since: {lastUpdated}");
            if (!string.IsNullOrWhiteSpace(lastUpdated)){
                address = apiUrl + "?lastUpdated=" + lastUpdated;
            }
            var content = client.GetStringAsync(address).Result;
            
            parseBundle(content);

            // update the time
            lastUpdated = nextUpdated;
        }

        // Post the message to the NCHS API
        public void postMessage(BaseMessage message)
        {
            
            var json = message.ToJSON();

            var data = new StringContent(json, Encoding.UTF8, "application/json");

            using var client = new HttpClient();

            var response = client.PostAsync(apiUrl, data).Result;
            if (response.IsSuccessStatusCode){
                Console.WriteLine($"Successfully submitted {message.MessageId}");
            }
            else {
                //updateMessageStatus(message, Status.Error);
                Console.WriteLine($"Error submitting {message.MessageId}");
            }
        }

        // parses the bundle from nchs and processes each message response
        public static void parseBundle(String bundle){
            var array = JArray.Parse(bundle);
            foreach (var item in array)
            {
                try
                {
                    String msgJson = item["message"].ToString();
                    BaseMessage msg = BaseMessage.Parse(msgJson, true);
                    // TODO change this to the id of the initial message

                    switch (msg.MessageType)
                    {
                        case "http://nchs.cdc.gov/vrdr_acknowledgement":
                            AckMessage message = new AckMessage(msg);
                            //acknowledgeMessage(message);
                            Console.WriteLine($"Received ask message: {msg.MessageId} for {message.AckedMessageId}");
                            break;
                        case "http://nchs.cdc.gov/vrdr_coding":
                            //message = new CodingResponseMessage(bundle);
                            Console.WriteLine($"Received coding response: {msg.MessageId}");
                            break;
                        case "http://nchs.cdc.gov/vrdr_coding_update":
                            //message = new CodingUpdateMessage(bundle);
                            Console.WriteLine($"Received coding update: {msg.MessageId}");
                            break;
                        case "http://nchs.cdc.gov/vrdr_extraction_error":
                            //message = new ExtractionErrorMessage(bundle, message);
                            //updateMessageStatus(msg, Status.Error);
                            Console.WriteLine($"Received extraction error: {msg.MessageId}");
                            break;
                        default:
                            Console.WriteLine($"Unknown message type");
                            break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error parsing message: {e}");
                }
            }
        }
    }
}