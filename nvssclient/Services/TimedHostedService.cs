using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NVSSClient.Models;
using NVSSClient.Controllers;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Model;
using Hl7.Fhir.ElementModel;

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
        private static String lastUpdated = new DateTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
        static readonly HttpClient client = new HttpClient();

        private int executionCount = 0;
        private readonly ILogger<TimedHostedService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private Timer _timer;

        public TimedHostedService(ILogger<TimedHostedService> logger, IServiceScopeFactory scopeFactory, IConfiguration configuration)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            Configuration = configuration;
            
            // Check for persistent data 
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                PersistentState dbState = context.PersistentState.Where(s => s.Name == "lastUpdated").FirstOrDefault();
                if (dbState != null){
                    String lastUpdated = dbState.Value;
                    //lastUpdated = lu.ToString("yyyy-MM-ddTHH:mm:ss.fffffff"); // regex? 
                }
                Console.WriteLine("LastUpdated: {0}", lastUpdated);
            }
        }
        public IConfiguration Configuration { get; }
        public System.Threading.Tasks.Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service running.");
            int interval = Int32.Parse(Configuration["PollingInterval"]);
            _timer = new Timer(DoWork, null, TimeSpan.Zero, 
                TimeSpan.FromSeconds(interval));

            return System.Threading.Tasks.Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            var count = Interlocked.Increment(ref executionCount);

            _logger.LogInformation(
                "Timed Hosted Service is working. Count: {Count}", count);
            
            // Step 1, submit new records in the db
            // TODO change this to a listening endpoint and submit messages on receipt
            SubmitNewMessages();
            // Step 2, poll for response messages from the server
            PollForResponses();
            // Step 3, check for messages that haven't received an ack in X amount of time
            ResendMessages();
        }

        public System.Threading.Tasks.Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        // Retrieve records from database and send to the endpoint

        public void SubmitNewMessages()
        {
            // scope the db context, its not meant to last the whole life cycle
            // and we need to deconflict for other db calls
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
               
                // Send messages that have not yet been sent
                var items = context.MessageItems.Where(s => s.Status == Models.MessageStatus.Pending.ToString()).ToList();
                foreach (MessageItem item in items)
                {
                    BaseMessage msg = BaseMessage.Parse(item.Message.ToString(), true);
                    Boolean success = Program.PostMessageAsync(msg);
                    if (success)
                    {
                        item.Status = Models.MessageStatus.Sent.ToString();          
                        DateTime currentTime = DateTime.Now;
                        int resend = Int32.Parse(Configuration["ResendInterval"]);
                        TimeSpan resendWindow = new TimeSpan(0,0,0,resend);
                        DateTime expireTime = currentTime.Add(resendWindow);
                        item.ExpirationDate = expireTime;
                        context.Update(item);
                        context.SaveChanges();
                    }
                    // TODO do we need to capture errors in the db?
                }
            } //scope (and context) gets destroyed here
        }

        public void ResendMessages()
        {
            // scope the db context, its not meant to last the whole life cycle
            // and we need to deconflict for other db calls
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
               
                // only get unacknowledged ones that have expired
                DateTime currentTime = DateTime.Now;

                var items = context.MessageItems.Where(s => s.Status != Models.MessageStatus.Acknowledged.ToString() && s.ExpirationDate < currentTime).ToList();
                foreach (MessageItem item in items)
                {
                    BaseMessage msg = BaseMessage.Parse(item.Message.ToString(), true);
                    Boolean success = Program.PostMessageAsync(msg);
                    if (success)
                    {
                        item.Status = Models.MessageStatus.Sent.ToString();
                        DateTime sentTime = DateTime.Now;
                        int resend = Int32.Parse(Configuration["ResendInterval"]);
                        TimeSpan resendWindow = new TimeSpan(0,0,0,resend);
                        DateTime expireTime = sentTime.Add(resendWindow);
                        item.ExpirationDate = expireTime;
                        item.Retries = item.Retries + 1;
                        context.Update(item);
                        context.SaveChanges();
                    }
                }
            } //scope (and context) gets destroyed here
        }

        private void PollForResponses()
        {
            // Get the datetime now so we don't risk losing any messages, we might get duplicates but we can filter them out
            DateTime nextUpdated = DateTime.Now;
            var content = Program.GetMessageResponsesAsync(lastUpdated);
            if (!String.IsNullOrEmpty(content))
            {
                parseBundle(content);
            }
            lastUpdated = nextUpdated.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
            SaveTimestamp(lastUpdated);
        }

        // Save the last updated timestamp to persist so we don't get repeat messages on a restart
        private void SaveTimestamp(String now)
        {
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                PersistentState dbState = context.PersistentState.Where(s => s.Name == "lastUpdated").FirstOrDefault();
                if (dbState == null)
                {
                    dbState = new PersistentState();
                    dbState.Name = "lastUpdated";
                    dbState.Value = now;
                    context.PersistentState.Add(dbState);
                    context.SaveChanges();
                    return;
                }
                // update the time
                dbState.Value = now;
                context.Update(dbState);
                context.SaveChanges();
            }
        }

        // parses the bundle of bundles from nchs and processes each message response
        public void parseBundle(String bundleOfBundles){
            FhirJsonParser parser = new FhirJsonParser();
            Bundle bundle = parser.Parse<Bundle>(bundleOfBundles);
            
            foreach (var entry in bundle.Entry)
            {
                try
                {
                    BaseMessage msg = BaseMessage.Parse<BaseMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                    //Bundle innerbundle = (Hl7.Fhir.Model.Bundle)entry.Resource;
                    switch (msg.MessageType)
                    {
                        case "http://nchs.cdc.gov/vrdr_acknowledgement":
                            AckMessage message = BaseMessage.Parse<AckMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            ProcessAckMessage(message);
                            Console.WriteLine($"Received ask message: {message.MessageId} for {message.AckedMessageId}");
                            break;
                        case "http://nchs.cdc.gov/vrdr_coding":
                            CodingResponseMessage codeMsg = BaseMessage.Parse<CodingResponseMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            //ProcessMessage(codeMsg);
                            Console.WriteLine($"Received coding: {codeMsg.MessageId}");
                            break;
                        case "http://nchs.cdc.gov/vrdr_coding_update":
                            CodingUpdateMessage updateMsg = BaseMessage.Parse<CodingUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            //ProcessMessage(updateMsg);
                            Console.WriteLine($"Received coding update: {updateMsg.MessageId}");
                            break;
                        case "http://nchs.cdc.gov/vrdr_extraction_error":
                            ExtractionErrorMessage errMsg = BaseMessage.Parse<ExtractionErrorMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            ProcessExtractionErrorMessage(errMsg);
                            Console.WriteLine($"Received extraction error: {errMsg.MessageId}");
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

        // Acknowledgements are relevant to specific messages, not a message series (coding response, updates)
        public void ProcessAckMessage(AckMessage message)
        {
            try 
            {
                using (var scope = _scopeFactory.CreateScope()){
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    // find the message the ack is for
                    var original = context.MessageItems.Where(s => s.Uid == message.AckedMessageId).First();

                    // update message status
                    original.Status = Models.MessageStatus.Acknowledged.ToString();
                    context.Update(original);
                    context.SaveChanges();
                    Console.WriteLine($"Successfully acked message {message.AckedMessageId}");
                }
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.AckedMessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
            }
        }

        // Process extraction errors, codings, and coding updates 
        // Extraction errors, coding, and updates are relevant to specific messages
        public void ProcessExtractionErrorMessage(ExtractionErrorMessage message)
        {
            try 
            {
                using (var scope = _scopeFactory.CreateScope()){
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    // check for a duplicate
                    // if a duplicate resend the ack
                    int count = context.ResponseItems.Where(m => m.Uid == message.MessageId).Count();
                    if (count > 0) {
                        Console.WriteLine($"Received duplicate message with Id: {message.MessageId}, ignore and resend ack");
                        
                        // create ACK message for the extraction error
                        AckMessage ackDuplicate = new AckMessage(message);
                        // either send acks async or create another table or field for acks, they would have the same identifiers so we don't want them in the messageItem table as is
                        Boolean success = Program.PostMessageAsync(BaseMessage.Parse(ackDuplicate.ToJson().ToString(), true));
                        if (!success)
                        {
                            Console.WriteLine($"Failed to send ack for message {message.MessageId}");
                        }
                        return;
                    }

                    // find the message the error is for
                    var original = context.MessageItems.Where(s => s.DeathJurisdictionID == message.DeathJurisdictionID && s.StateAuxiliaryIdentifier == message.StateAuxiliaryIdentifier).First();
                    original.Status = Models.MessageStatus.Error.ToString();
                    context.Update(original);

                    // insert response message in db
                    ResponseItem response = new ResponseItem();
                    response.Uid = message.MessageId;
                    response.StateAuxiliaryIdentifier = message.StateAuxiliaryIdentifier;
                    response.CertificateNumber = message.CertificateNumber;
                    response.DeathJurisdictionID = message.DeathJurisdictionID;
                    response.DeathYear = message.DeathYear;
                    response.Message = message.ToJson().ToString();
                    context.ResponseItems.Add(response);

                    Console.WriteLine("Created response");

                    context.SaveChanges();
                    Console.WriteLine($"Successfully recorded {message.MessageType} message {message.MessageId}");

                    // create ACK message for the extraction error
                    AckMessage ack = new AckMessage(message);
                    Boolean sent = Program.PostMessageAsync(ack);
                    if (!sent)
                    {
                        Console.WriteLine($"Failed to send ack for message {message.MessageId}");
                    }
                }
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
            }
        }
    }
}