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
using nvssclient.lib;
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
        
    // The TimedHostedService runs every x seconds to pull new messages from the db, submit to the NVSS FHIR API Server, 
    // check for responses, and resend messages that haven't had a response in x time
    public class TimedHostedService : IHostedService, IDisposable
    {
        private static String lastUpdated = new DateTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
        private Client client;
        private int executionCount = 0;
        private readonly ILogger<TimedHostedService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private Timer _timer;
        private String _jurisdictionEndPoint;

        public TimedHostedService(ILogger<TimedHostedService> logger, IServiceScopeFactory scopeFactory, IConfiguration configuration)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            Configuration = configuration;
            
            // Check the persistent data for the last updated timestamp 
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                PersistentState dbState = context.PersistentState.OrderBy(p => p.CreatedDate).FirstOrDefault();
                if (dbState != null){
                    lastUpdated = dbState.LastUpdated.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
                }
                Console.WriteLine("*** LastUpdated: {0}", lastUpdated);
            }

            // Parse the credentials config
            String authUrl = Startup.StaticConfig.GetConnectionString("AuthServer");
            string clientId = Startup.StaticConfig.GetValue<string>("Authentication:ClientId");
            string clientSecret = Startup.StaticConfig.GetValue<string>("Authentication:ClientSecret");
            string username = Startup.StaticConfig.GetValue<string>("Authentication:Username");
            string pass = Startup.StaticConfig.GetValue<string>("Authentication:Password");
            Credentials creds = new Credentials(authUrl, clientId, clientSecret, username, pass);

            // Parse the config to create the client instance
            string apiUrl = Startup.StaticConfig.GetConnectionString("ApiServer");
            Boolean localDev = Startup.StaticConfig.GetValue<Boolean>("LocalTesting");
            if (localDev) {
                apiUrl = Startup.StaticConfig.GetConnectionString("LocalServer");
            }
            client = new Client(apiUrl, localDev, creds);
        }
        public IConfiguration Configuration { get; }

        // StartAsync initializes the timed hosted services and sets the time interval
        public System.Threading.Tasks.Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service running.");
            int interval = Int32.Parse(Configuration["PollingInterval"]);
            _jurisdictionEndPoint = Configuration["JurisdictionEndpoint"];
            _timer = new Timer(DoWork, null, TimeSpan.Zero, 
                TimeSpan.FromSeconds(interval));

            return System.Threading.Tasks.Task.CompletedTask;
        }

        // DoWork runs at each time interval
        private void DoWork(object state)
        {
            var count = Interlocked.Increment(ref executionCount);

            _logger.LogInformation(
                "Timed Hosted Service is working. Count: {Count}", count);
            
            // Step 1, submit new records in the db
            SubmitNewMessages();
            // Step 2, poll for response messages from the server
            PollForResponses();
            // Step 3, check for messages that haven't received an ack in X amount of time
            ResendMessages();
        }

        // StopAsync stops the timed hosted service
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

        // SubmitNewMessages retrieves new Messages from the database and sends them to the NVSS FHIR API
        public void SubmitNewMessages()
        {
            // scope the db context, its not meant to last the whole life cycle
            // and we need to deconflict for other db calls
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
               
                // Send Messages that have not yet been sent, i.e. status is "Pending"
                var items = context.MessageItems.Where(s => s.Status == Models.MessageStatus.Pending.ToString()).ToList();
                foreach (MessageItem item in items)
                {
                    BaseMessage msg = BaseMessage.Parse(item.Message.ToString(), true);
                    Boolean success = client.PostMessageAsync(msg);
                    if (success)
                    {
                        item.Status = Models.MessageStatus.Sent.ToString();          
                        DateTime currentTime = DateTime.UtcNow;
                        int resend = Int32.Parse(Configuration["ResendInterval"]);
                        TimeSpan resendWindow = new TimeSpan(0,0,0,resend);
                        DateTime expireTime = currentTime.Add(resendWindow);
                        item.ExpirationDate = expireTime;
                        context.Update(item);
                        context.SaveChanges();
                    }
                }
            } //scope (and context) gets destroyed here
        }

        // ResendMessages supports reliable delivery of messages, it finds Messages in the DB that have not been acknowledged 
        // and have exceeded their expiration date. It resends the selected Messages to the NVSS FHIR API
        public void ResendMessages()
        {
            // scope the db context, its not meant to last the whole life cycle
            // and we need to deconflict for other db calls
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
               
                // Only selected unacknowledged Messages that have expired
                // Don't resend ack'd messages or messages in an error state
                DateTime currentTime = DateTime.UtcNow;
                var items = context.MessageItems.Where(s => s.Status != Models.MessageStatus.Acknowledged.ToString() && s.Status != Models.MessageStatus.AcknowledgedAndCoded.ToString() && s.Status != Models.MessageStatus.Error.ToString() && s.ExpirationDate < currentTime).ToList();
                foreach (MessageItem item in items)
                {
                    BaseMessage msg = BaseMessage.Parse(item.Message.ToString(), true);
                    Boolean success = client.PostMessageAsync(msg);
                    if (success)
                    {
                        item.Status = Models.MessageStatus.Sent.ToString();
                        item.Retries = item.Retries + 1;
                        DateTime sentTime = DateTime.UtcNow;
                        // the exponential backoff multiplies the resend interval by the number of retries
                        int resend = Int32.Parse(Configuration["ResendInterval"]) * item.Retries;
                        TimeSpan resendWindow = new TimeSpan(0,0,0,resend);
                        DateTime expireTime = sentTime.Add(resendWindow);
                        item.ExpirationDate = expireTime;
                        
                        context.Update(item);
                        context.SaveChanges();
                    }
                }
            } //scope (and context) gets destroyed here
        }

        // PollForResponses makes a GET request to the NVSS FHIR API server for new Messages
        // the became available since the lastUpdated time stamp
        private void PollForResponses()
        {
            // Get the datetime now so we don't risk missing any messages, we might get duplicates but we can filter them out
            DateTime nextUpdated = DateTime.UtcNow;
            var content = client.GetMessageResponsesAsync(lastUpdated);
            if (!String.IsNullOrEmpty(content))
            {
                parseBundle(content);
            }
            SaveTimestamp(nextUpdated);
            lastUpdated = nextUpdated.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
        }

        // SaveTimestamp saves the last updated timestamp to the persistent database so we don't get repeat messages on a restart
        private void SaveTimestamp(DateTime now)
        {
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                PersistentState dbState = context.PersistentState.FirstOrDefault();
                if (dbState == null)
                {
                    dbState = new PersistentState();
                    dbState.LastUpdated = now;
                    context.PersistentState.Add(dbState);
                    context.SaveChanges();
                    return;
                }
                // update the time
                dbState.LastUpdated = now;
                context.Update(dbState);
                context.SaveChanges();
            }
        }

        // TODO move to library?
        // ParseBundle parses the bundle of bundles from NVSS FHIR API server and processes each message response
        public void parseBundle(String bundleOfBundles){
            FhirJsonParser parser = new FhirJsonParser();
            Bundle bundle = parser.Parse<Bundle>(bundleOfBundles);
            
            foreach (var entry in bundle.Entry)
            {
                try
                {
                    BaseMessage msg = BaseMessage.Parse<BaseMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                    switch (msg.MessageType)
                    {
                    case "http://nchs.cdc.gov/vrdr_acknowledgement":
                        AckMessage message = BaseMessage.Parse<AckMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                        Console.WriteLine($"*** Received ack message: {message.MessageId} for {message.AckedMessageId}");
                        ProcessAckMessage(message);
                        break;
                    case "http://nchs.cdc.gov/vrdr_coding":
                        CodingResponseMessage codeMsg = BaseMessage.Parse<CodingResponseMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                        Console.WriteLine($"*** Received coding message: {codeMsg.MessageId}");
                        ProcessResponseMessage(codeMsg);
                        break;
                    case "http://nchs.cdc.gov/vrdr_coding_update":
                        CodingUpdateMessage updateMsg = BaseMessage.Parse<CodingUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                        Console.WriteLine($"*** Received coding update message: {updateMsg.MessageId}");
                        ProcessResponseMessage(updateMsg);
                        break;
                    case "http://nchs.cdc.gov/vrdr_extraction_error":
                        ExtractionErrorMessage errMsg = BaseMessage.Parse<ExtractionErrorMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                        Console.WriteLine($"*** Received extraction error: {errMsg.MessageId}");
                        ProcessResponseMessage(errMsg);
                        break;
                    default:
                        Console.WriteLine($"*** Unknown message type");
                        break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"*** Error parsing message: {e}");
                    // Extraction errors require acks so we insert them in the DB to send with other messages to NCHS
                    // Wrap this in another try catch so we can see any failures to create the extraction error in our logs
                    try 
                    {
                        Hl7.Fhir.Model.Bundle innerBundle = (Hl7.Fhir.Model.Bundle)entry.Resource;
                        var headerEntry = innerBundle.Entry.FirstOrDefault( entry2 => entry2.Resource.ResourceType == ResourceType.MessageHeader );
                        if (headerEntry == null)
                        {
                            throw new System.ArgumentException($"Failed to find a Bundle Entry containing a Message Header");
                        }
                        MessageHeader header = (MessageHeader)headerEntry?.Resource;
                        // to create the extraction error, pass in the message Id, 
                        // the destination endpoint, and the source 
                        ExtractionErrorMessage extError = new ExtractionErrorMessage(entry.Resource.Id, header?.Source?.Endpoint, _jurisdictionEndPoint);
                        
                        using (var scope = _scopeFactory.CreateScope()){
                            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            extError.MessageSource = _jurisdictionEndPoint;

                            MessageItem item = new MessageItem();
                            item.Uid = extError.MessageId;
                            item.Message = extError.ToJson().ToString();
                            
                            // Business Identifiers
                            item.StateAuxiliaryIdentifier = extError.StateAuxiliaryIdentifier;
                            item.CertificateNumber = extError.CertificateNumber;
                            item.DeathJurisdictionID = extError.DeathJurisdictionID;
                            item.DeathYear = extError.DeathYear;
                            Console.WriteLine("Business IDs {0}, {1}, {2}", extError.DeathYear, extError.CertificateNumber, extError.DeathJurisdictionID);
                            
                            // Status info
                            item.Status = Models.MessageStatus.Pending.ToString();
                            item.Retries = 0;
                            
                            // insert new message
                            context.MessageItems.Add(item);
                            context.SaveChanges();
                            Console.WriteLine($"Inserted message {item.Uid}");   
                        }
                        Console.WriteLine($"*** Successfully queued extraction error message for message {entry.Resource.Id}");
                    }
                    catch (Exception e2)
                    {
                        // If we reach this point, the FHIR API Server should eventually resend the initial message 
                        // and we will try to process it again.
                        // If the parsing continues to fail, these logs will track the failures for debugging
                        Console.WriteLine($"*** Failed to queue extraction error message for message {entry.Resource.Id}, error: {e2} ");
                    }
                }
            }
        }

        // TODO move to library?
        // ProcessAckMessage parses an AckMessage from the server
        // and updates the status of the Message it acknowledged. 
        public void ProcessAckMessage(AckMessage message)
        {
            try 
            {
                using (var scope = _scopeFactory.CreateScope()){
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    // find the message the ack is for
                    var original = context.MessageItems.Where(s => s.Uid == message.AckedMessageId).FirstOrDefault();
                    if (original == null)
                    {
                        Console.WriteLine($"*** Warning: ACK received for unknown message {message.AckedMessageId}");
                        return;
                    }

                    // update message status if this message was not yet acknowledged
                    if (original.Status == Models.MessageStatus.Sent.ToString())
                    {
                        original.Status = Models.MessageStatus.Acknowledged.ToString();
                        context.Update(original);
                        context.SaveChanges();
                        Console.WriteLine($"*** Successfully acked message {original.Uid}");
                    }
                    else
                    {
                        Console.WriteLine($"*** Ignored acknowledgement for previously acknowledged or coded message {original.Uid}");
                    }
                }
            } catch (Exception e)
            {
                Console.WriteLine($"*** Error processing acknowledgement of {message.AckedMessageId}");
                Console.WriteLine("\nException Caught!");
                Console.WriteLine("*** Message :{0} ",e.Message);
            }
        }

        // TODO move to library?
        // ProcessResponseMessage processes codings, coding updates, and extraction errors
        public void ProcessResponseMessage(BaseMessage message)
        {
            try 
            {
                using (var scope = _scopeFactory.CreateScope()){
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    // check if this response message is a duplicate
                    // if it is a duplicate resend the ack
                    int count = context.ResponseItems.Where(m => m.Uid == message.MessageId).Count();
                    if (count > 0) {
                        Console.WriteLine($"*** Received duplicate message with Id: {message.MessageId}, ignore and resend ack");
                        
                        // create ACK message for the response
                        AckMessage ackDuplicate = new AckMessage(message);
                        Boolean success = client.PostMessageAsync(BaseMessage.Parse(ackDuplicate.ToJson().ToString(), true));
                        if (!success)
                        {
                            Console.WriteLine($"*** Failed to send ack for message {message.MessageId}");
                        }
                        return;
                    }

                    // find the latest Message with the same business identifiers as the coding response
                    var original = context.MessageItems.Where(s => s.DeathJurisdictionID == message.DeathJurisdictionID && s.CertificateNumber == message.CertificateNumber && s.DeathYear == message.DeathYear).FirstOrDefault();
                    if (original == null)
                    {
                        // TODO determine if an error message should be sent in this case
                        Console.WriteLine($"*** Warning: Response received for unknown message {message.MessageId} ({message.DeathYear} {message.DeathJurisdictionID} {message.CertificateNumber})");
                        return;
                    }
                    // Update the status
                    switch (message.MessageType)
                    {
                        case "http://nchs.cdc.gov/vrdr_coding":
                            original.Status = Models.MessageStatus.AcknowledgedAndCoded.ToString();
                            Console.WriteLine("*** Updating status to AcknowledgedAndCoded for {0} {1} {2}", message.DeathYear, message.DeathJurisdictionID, message.CertificateNumber);
                            break;
                        case "http://nchs.cdc.gov/vrdr_coding_update":
                            original.Status = Models.MessageStatus.AcknowledgedAndCoded.ToString();
                            Console.WriteLine("*** Updating status to AcknowledgedAndCoded for {0} {1} {2}", message.DeathYear, message.DeathJurisdictionID, message.CertificateNumber);
                            break;
                        case "http://nchs.cdc.gov/vrdr_extraction_error":
                            original.Status = Models.MessageStatus.Error.ToString();
                            Console.WriteLine("*** Updating status to Error for {0} {1} {2}", message.DeathYear, message.DeathJurisdictionID, message.CertificateNumber);
                            break;
                        default:
                            // TODO should create an error
                            Console.WriteLine($"*** Unknown message type {message.MessageType}");
                            break;
                    }
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

                    context.SaveChanges();
                    Console.WriteLine($"*** Successfully recorded {message.GetType().Name} message {message.MessageId}");

                    // create ACK message for the extraction error
                    AckMessage ack = new AckMessage(message);
                    Boolean sent = client.PostMessageAsync(ack);
                    if (!sent)
                    {
                        Console.WriteLine($"*** Failed to send ack for message {message.MessageId}");
                    }
                }
            } catch (Exception e)
            {
                Console.WriteLine($"*** Error processing incoming coding or error message {message.MessageId}");
                Console.WriteLine("\nException Caught!");
                Console.WriteLine("*** Message :{0} ",e.Message);
            }
        }
    }
}