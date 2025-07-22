using BFDR;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NVSSClient.Controllers;
using NVSSClient.Models;
using StackExchange.Profiling.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VR;
using VRDR;

namespace NVSSClient.Services
{

    // The TimedHostedService runs every x seconds to pull new messages from the db, submit to the NVSS FHIR API Server, 
    // check for responses, and resend messages that haven't had a response in x time
    public class TimedHostedService : IHostedService, IDisposable
    {
        private static String lastUpdated = new DateTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
        private VR.Client client;
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

            // Parse the credentials config
            String authUrl = Startup.StaticConfig.GetConnectionString("AuthServer");
            string clientId = Startup.StaticConfig.GetValue<string>("Authentication:ClientId");
            string clientSecret = Startup.StaticConfig.GetValue<string>("Authentication:ClientSecret");
            string username = Startup.StaticConfig.GetValue<string>("Authentication:Username");
            string pass = Startup.StaticConfig.GetValue<string>("Authentication:Password");
            string scope = Startup.StaticConfig.GetValue<string>("Authentication:Scope");
            VR.Credentials creds = new VR.Credentials(authUrl, clientId, clientSecret, username, pass, scope);

            // Parse the config to create the client instance
            string apiUrl = Startup.StaticConfig.GetConnectionString("ApiServer");
            Boolean localDev = Startup.StaticConfig.GetValue<Boolean>("LocalTesting");
            if (localDev)
            {
                apiUrl = Startup.StaticConfig.GetConnectionString("LocalServer");
            }
            client = new VR.Client(apiUrl, localDev, creds);
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
            ResendAllMessages();
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
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Send Messages that have not yet been sent, i.e. status is "Pending"
            var items = context.MessageItems.Where(s => s.Status == Models.MessageStatus.Pending.ToString()).ToList();
            var vrdrItems = items.Where(item => item.VitalRecordType == "VRDR").ToList();
            var brdrBirthItems = items.Where(item => item.VitalRecordType == "BFDR-BIRTH").ToList();
            var brdrFatalItems = items.Where(item => item.VitalRecordType == "BFDR-FETALDEATH").ToList();
            SubmitMessages(vrdrItems, CreatePathFromMessageFields(vrdrItems[0]));
            SubmitMessages(brdrBirthItems, CreatePathFromMessageFields(brdrBirthItems[0]));
            SubmitMessages(brdrFatalItems, CreatePathFromMessageFields(brdrFatalItems[0]));
            //scope (and context) gets destroyed here
        }

        private string CreatePathFromMessageFields(MessageItem item)
        {
            string optionalPath = "VRDR/VRDR_STU3_0";
            if (item !=null && !item.VitalRecordType.IsNullOrWhiteSpace() && !item.IJE_Version.IsNullOrWhiteSpace())
            {
                optionalPath = item.VitalRecordType.ToString() + "/" + item.IJE_Version;
            }
            return optionalPath;
        }

        private string CreatePathFromResponseFields(ResponseItem item)
        {
            string optionalPath = "VRDR/VRDR_STU3_0";
            if (item != null && !item.VitalRecordType.IsNullOrWhiteSpace() && !item.IJE_Version.IsNullOrWhiteSpace())
            {
                optionalPath = item.VitalRecordType.ToString() + "/" + item.IJE_Version;
            }
            return optionalPath;
        }


        // SubmitMessages   Sends them to the NVSS FHIR API
        public async void SubmitMessages(List<MessageItem> items, string path)
        {
            if (items.Count == 0) return;
            // scope the db context, its not meant to last the whole life cycle
            // and we need to deconflict for other db calls
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                List<CommonMessage> messages = items.Select(item => CommonMessage.ParseGenericMessage(item.Message.ToString(), true)).ToList();

                //               List <CommonMessage> messages = items.Select(item => BaseMessage.Parse(item.Message.ToString(), true)).ToList();
                _logger.LogInformation($">>> Submitting new messages to NCHS (count: {messages.Count()})...");
                List<HttpResponseMessage> responses = await client.PostMessagesAsync(messages, 20, path); // POST messages in batches of 20
                for (int idx = 0; idx < items.Count; idx++)
                {
                    MessageItem item = items[idx];
                    CommonMessage message = messages[idx];
                    HttpResponseMessage response = responses[idx];
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($">>> Successfully submitted {message.MessageId} of type {message.GetType().Name}");
                        item.Status = Models.MessageStatus.Sent.ToString();
                        DateTime currentTime = DateTime.UtcNow;
                        int resend = Int32.Parse(Configuration["ResendInterval"]);
                        TimeSpan resendWindow = new TimeSpan(0, 0, 0, resend);
                        DateTime expireTime = currentTime.Add(resendWindow);
                        item.ExpirationDate = expireTime;
                        context.Update(item);
                        context.SaveChanges();
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _logger.LogError($">>> Unauthorized error submitting {message.MessageId}, status: {response.StatusCode}");
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        _logger.LogError($">>> Error submitting {message.MessageId} due to an issue with the submission, status: {response.StatusCode}");
                        item.Status = Models.MessageStatus.Error.ToString();
                        context.Update(item);
                        context.SaveChanges();
                    }
                    else
                    {
                        _logger.LogError($">>> Error submitting {message.MessageId}, status: {response.StatusCode}");
                    }
                }
            }
        }//scope (and context) gets destroyed here


        public void ResendAllMessages()
        {
            // scope the db context, its not meant to last the whole life cycle
            // and we need to deconflict for other db calls
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Only selected unacknowledged Messages that have expired
            // Don't resend ack'd messages or messages in an error state
            DateTime currentTime = DateTime.UtcNow;
            var items = context.MessageItems.Where(s => s.Status != Models.MessageStatus.Acknowledged.ToString() && s.Status != Models.MessageStatus.AcknowledgedAndCoded.ToString() && s.Status != Models.MessageStatus.Error.ToString() && s.ExpirationDate < currentTime).ToList();
            var vrdrItems = items.Where(item => item.VitalRecordType == "VRDR").ToList();
            var brdrBirthItems = items.Where(item => item.VitalRecordType == "BFDR-BIRTH").ToList();
            var brdrFatalItems = items.Where(item => item.VitalRecordType == "BFDR-FETALDEATH").ToList();
            ResendMessages(vrdrItems, CreatePathFromMessageFields(vrdrItems[0]));
            ResendMessages(brdrBirthItems, CreatePathFromMessageFields(brdrBirthItems[0]));
            ResendMessages(brdrFatalItems, CreatePathFromMessageFields(brdrFatalItems[0]));
        }

        // ResendMessages supports reliable delivery of messages, it finds Messages in the DB that have not been acknowledged 
        // and have exceeded their expiration date. It resends the selected Messages to the NVSS FHIR API
        public async void ResendMessages(List<MessageItem> items, string path)
        {
            if (items.Count == 0) return;
            // scope the db context, its not meant to last the whole life cycle
            // and we need to deconflict for other db calls
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                List<CommonMessage> messages = items.Select(item => CommonMessage.ParseGenericMessage(item.Message.ToString(), true)).ToList();
                _logger.LogInformation($">>> Resubmitting messages to NCHS (count: {messages.Count()})...");
                List<HttpResponseMessage> responses = await client.PostMessagesAsync(messages, 20, path); // POST messages in batches of 20
                for (int idx = 0; idx < items.Count; idx++)
                {
                    MessageItem item = items[idx];
                    CommonMessage message = messages[idx];
                    HttpResponseMessage response = responses[idx];
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($">>> Successfully submitted {message.MessageId} of type {message.GetType().Name}");
                        item.Status = Models.MessageStatus.Sent.ToString();
                        item.Retries = item.Retries + 1;
                        DateTime sentTime = DateTime.UtcNow;
                        // the exponential backoff multiplies the resend interval by the number of retries
                        int resend = Int32.Parse(Configuration["ResendInterval"]) * item.Retries;
                        TimeSpan resendWindow = new TimeSpan(0, 0, 0, resend);
                        DateTime expireTime = sentTime.Add(resendWindow);
                        item.ExpirationDate = expireTime;

                        context.Update(item);
                        context.SaveChanges();
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _logger.LogError($">>> Unauthorized error submitting {message.MessageId}, status: {response.StatusCode}");
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        _logger.LogError($">>> Error submitting {message.MessageId} due to an issue with the submission, status: {response.StatusCode}");
                        item.Status = Models.MessageStatus.Error.ToString();
                        context.Update(item);
                        context.SaveChanges();
                    }
                    else
                    {
                        _logger.LogError($">>> Error submitting {message.MessageId}, status: {response.StatusCode}");
                    }
                }
            } //scope (and context) gets destroyed here
        }

        // PollForResponses makes a GET request to the NVSS FHIR API server for new Messages
        // the became available since the lastUpdated time stamp
        private async void PollForResponses()
        {
            // GetMessageResponsesAsync will retrieve any new message responses from the server
            _logger.LogInformation($">>> Retrieving new messages from NCHS...");
            HttpResponseMessage response = await client.GetMessageResponsesAsync();
            if (response.IsSuccessStatusCode)
            {
                var content = response.Content.ReadAsStringAsync().Result;
                // if there are new message responses, parse them
                if (!String.IsNullOrEmpty(content))
                {
                    parseBundle(content);
                }
            }
            else
            {
                _logger.LogError($"Failed to retrieve messages from the server:, { response.StatusCode}");
            }

        }

        // ParseBundle parses the bundle of bundles from NVSS FHIR API server and processes each message response
        public void parseBundle(String bundleOfBundles)
        {
            FhirJsonParser parser = new FhirJsonParser();
            Bundle bundle = parser.Parse<Bundle>(bundleOfBundles);

            foreach (var entry in bundle.Entry)
            {
                try
                {
                    CommonMessage msg = CommonMessage.Parse((Hl7.Fhir.Model.Bundle)entry.Resource);

                    // BaseMessage msg = BaseMessage.Parse<BaseMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                    string refID = null;
                    switch (msg.MessageType)
                    {
                        //VRDR MESSAGES (13)
                        case AcknowledgementMessage.MESSAGE_TYPE:
                            AcknowledgementMessage message = BaseMessage.Parse<AcknowledgementMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = message.AckedMessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received ack message: {message.MessageId} for {message.AckedMessageId}");
                            ProcessAckMessage(message);
                            break;

                        case CauseOfDeathCodingMessage.MESSAGE_TYPE:
                            CauseOfDeathCodingMessage codCodeMsg = BaseMessage.Parse<CauseOfDeathCodingMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = codCodeMsg.CodedMessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received coding message: {codCodeMsg.MessageId}");
                            ProcessResponseMessage(codCodeMsg, refID);
                            break;

                        case DemographicsCodingMessage.MESSAGE_TYPE:
                            DemographicsCodingMessage demCodeMsg = BaseMessage.Parse<DemographicsCodingMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = demCodeMsg.CodedMessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received demographics coding message: {demCodeMsg.MessageId}");
                            ProcessResponseMessage(demCodeMsg, refID);
                            break;

                        case CauseOfDeathCodingUpdateMessage.MESSAGE_TYPE:
                            CauseOfDeathCodingUpdateMessage codUpdateMsg = BaseMessage.Parse<CauseOfDeathCodingUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = codUpdateMsg.CodedMessageId; // Added refID assignment
                           _logger.LogInformation($"*** Received coding update message: {codUpdateMsg.MessageId}");
                            ProcessResponseMessage(codUpdateMsg, refID);
                            break;

                        case DemographicsCodingUpdateMessage.MESSAGE_TYPE:
                            DemographicsCodingUpdateMessage demUpdateMsg = BaseMessage.Parse<DemographicsCodingUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = demUpdateMsg.CodedMessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received demographics coding update message: {demUpdateMsg.MessageId}");
                            ProcessResponseMessage(demUpdateMsg, refID);
                            break;

                        case ExtractionErrorMessage.MESSAGE_TYPE:
                            ExtractionErrorMessage errMsg = BaseMessage.Parse<ExtractionErrorMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = errMsg.FailedMessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received extraction error: {errMsg.MessageId}");
                            ProcessResponseMessage(errMsg, refID);
                            break;

                        case StatusMessage.MESSAGE_TYPE:
                            StatusMessage statusMsg = BaseMessage.Parse<StatusMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = statusMsg.StatusedMessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received status error: {statusMsg.MessageId}");
                            ProcessResponseMessage(statusMsg, refID);
                            break;

                        case IndustryOccupationCodingMessage.MESSAGE_TYPE:
                            IndustryOccupationCodingMessage indOccMsg = BaseMessage.Parse<IndustryOccupationCodingMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = indOccMsg.CodedMessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received industry occupation coding message: {indOccMsg.MessageId}");
                            ProcessResponseMessage(indOccMsg, refID);
                            break;

                        case IndustryOccupationCodingUpdateMessage.MESSAGE_TYPE:
                            IndustryOccupationCodingUpdateMessage indOccUpdateMsg = BaseMessage.Parse<IndustryOccupationCodingUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = indOccUpdateMsg.CodedMessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received industry occupation coding update message: {indOccUpdateMsg.MessageId}");
                            ProcessResponseMessage(indOccUpdateMsg, refID);
                            break;

                        case DeathRecordVoidMessage.MESSAGE_TYPE:
                            DeathRecordVoidMessage deathRecordVoidMsg = BaseMessage.Parse<DeathRecordVoidMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = deathRecordVoidMsg.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received death record void message: {deathRecordVoidMsg.MessageId}");
                            ProcessResponseMessage(deathRecordVoidMsg, refID);
                            break;

                        case DeathRecordSubmissionMessage.MESSAGE_TYPE:
                            DeathRecordSubmissionMessage deathRecordSubmissionMsg = BaseMessage.Parse<DeathRecordSubmissionMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = deathRecordSubmissionMsg.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received death record submission message: {deathRecordSubmissionMsg.MessageId}");
                            ProcessResponseMessage(deathRecordSubmissionMsg, refID);
                            break;

                        case DeathRecordUpdateMessage.MESSAGE_TYPE:
                            DeathRecordUpdateMessage deathRecordUpdateMsg = BaseMessage.Parse<DeathRecordUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = deathRecordUpdateMsg.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received death record update message: {deathRecordUpdateMsg.MessageId}");
                            ProcessResponseMessage(deathRecordUpdateMsg, refID);
                            break;

                        case DeathRecordAliasMessage.MESSAGE_TYPE:
                            DeathRecordAliasMessage deathRecordAliasMsg = BaseMessage.Parse<DeathRecordAliasMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = deathRecordAliasMsg.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received death record alias message: {deathRecordAliasMsg.MessageId}");
                            ProcessResponseMessage(deathRecordAliasMsg, refID);
                            break;


                        // BRDR Messages (8)
                        case BirthRecordSubmissionMessage.MESSAGE_TYPE:
                            BirthRecordSubmissionMessage birthRecordSubmissionMessage = BFDRBaseMessage.Parse<BirthRecordSubmissionMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = birthRecordSubmissionMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received birth record update message: {birthRecordSubmissionMessage.MessageId}");
                            ProcessResponseMessage(birthRecordSubmissionMessage, refID);
                            break;
                        case BirthRecordStatusMessage.MESSAGE_TYPE:
                            BirthRecordStatusMessage birthRecordStatusMessage = BFDRBaseMessage.Parse<BirthRecordStatusMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = birthRecordStatusMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received birth record update message: {birthRecordStatusMessage.MessageId}");
                            ProcessResponseMessage(birthRecordStatusMessage, refID);
                            break;
                        case BirthRecordUpdateMessage.MESSAGE_TYPE:
                            BirthRecordUpdateMessage birthRecordUpdateMessage = BFDRBaseMessage.Parse<BirthRecordUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = birthRecordUpdateMessage.MessageId; // Added refID assignment
                           _logger.LogInformation($"*** Received birth record update message: {birthRecordUpdateMessage.MessageId}");
                            ProcessResponseMessage(birthRecordUpdateMessage, refID);
                            break;

                        case BirthRecordVoidMessage.MESSAGE_TYPE:
                            BirthRecordVoidMessage birthRecordVoidMessage = BFDRBaseMessage.Parse<BirthRecordVoidMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = birthRecordVoidMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received birth record update message: {birthRecordVoidMessage.MessageId}");
                            ProcessResponseMessage(birthRecordVoidMessage, refID);
                            break;

                        case BirthRecordParentalDemographicsCodingMessage.MESSAGE_TYPE:
                            BirthRecordParentalDemographicsCodingMessage birthRecordParentalDemographicsCodingMessage =
                                BFDRBaseMessage.Parse<BirthRecordParentalDemographicsCodingMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = birthRecordParentalDemographicsCodingMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received birth record update message: {birthRecordParentalDemographicsCodingMessage.MessageId}");
                            ProcessResponseMessage(birthRecordParentalDemographicsCodingMessage, refID);
                            break;
                        case BirthRecordParentalDemographicsCodingUpdateMessage.MESSAGE_TYPE:
                            BirthRecordParentalDemographicsCodingUpdateMessage birthRecordParentalDemographicsCodingUpdateMessage = BFDRBaseMessage.Parse<BirthRecordParentalDemographicsCodingUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = birthRecordParentalDemographicsCodingUpdateMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received birth record parental demographics coding update message: {birthRecordParentalDemographicsCodingUpdateMessage.MessageId}");
                            ProcessResponseMessage(birthRecordParentalDemographicsCodingUpdateMessage, refID);
                            break;
                        case BirthRecordErrorMessage.MESSAGE_TYPE:
                            BirthRecordErrorMessage birthRecordErrorMessage = BFDRBaseMessage.Parse<BirthRecordErrorMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = birthRecordErrorMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received birth record update message: {birthRecordErrorMessage.MessageId}");
                            ProcessResponseMessage(birthRecordErrorMessage, refID);
                            break;
                        case BirthRecordAcknowledgementMessage.MESSAGE_TYPE:
                            BirthRecordAcknowledgementMessage birthRecordAcknowledgementMessage = BFDRBaseMessage.Parse<BirthRecordAcknowledgementMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = birthRecordAcknowledgementMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received birth record update message: {birthRecordAcknowledgementMessage.MessageId}");
                            ProcessResponseMessage(birthRecordAcknowledgementMessage, refID);
                            break;

                        //BFDR-FETALDEATH messages (10)
                        case FetalDeathRecordSubmissionMessage.MESSAGE_TYPE:
                            FetalDeathRecordSubmissionMessage fetalDeathRecordSubmissionMessage = BFDRBaseMessage.Parse<FetalDeathRecordSubmissionMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = fetalDeathRecordSubmissionMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received fetal death record submission message: {fetalDeathRecordSubmissionMessage.MessageId}");
                            ProcessResponseMessage(fetalDeathRecordSubmissionMessage, refID);
                            break;

                        case FetalDeathRecordUpdateMessage.MESSAGE_TYPE:
                            FetalDeathRecordUpdateMessage fetalDeathRecordUpdateMessage = BFDRBaseMessage.Parse<FetalDeathRecordUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = fetalDeathRecordUpdateMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received fetal death record update message: {fetalDeathRecordUpdateMessage.MessageId}");
                            ProcessResponseMessage(fetalDeathRecordUpdateMessage, refID);
                            break;

                        case CodedCauseOfFetalDeathMessage.MESSAGE_TYPE:
                            CodedCauseOfFetalDeathMessage codedCauseOfFetalDeathMessage = BFDRBaseMessage.Parse<CodedCauseOfFetalDeathMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = codedCauseOfFetalDeathMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received coded cause of fetal death update message: {codedCauseOfFetalDeathMessage.MessageId}");
                            ProcessResponseMessage(codedCauseOfFetalDeathMessage, refID);
                            break;
                        case CodedCauseOfFetalDeathUpdateMessage.MESSAGE_TYPE:
                            CodedCauseOfFetalDeathUpdateMessage codedCauseOfFetalDeathUpdateMessage = BFDRBaseMessage.Parse<CodedCauseOfFetalDeathUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = codedCauseOfFetalDeathUpdateMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received coded cause of fetal death update message: {codedCauseOfFetalDeathUpdateMessage.MessageId}");
                            ProcessResponseMessage(codedCauseOfFetalDeathUpdateMessage, refID);
                            break;

                        case FetalDeathRecordVoidMessage.MESSAGE_TYPE:
                            FetalDeathRecordVoidMessage fetalDeathRecordVoidMessage = BFDRBaseMessage.Parse<FetalDeathRecordVoidMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = fetalDeathRecordVoidMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received fetal death record void message: {fetalDeathRecordVoidMessage.MessageId}");
                            ProcessResponseMessage(fetalDeathRecordVoidMessage, refID);
                            break;

                        case FetalDeathRecordStatusMessage.MESSAGE_TYPE:
                            FetalDeathRecordStatusMessage fetalDeathRecordStatusMessage = BFDRBaseMessage.Parse<FetalDeathRecordStatusMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = fetalDeathRecordStatusMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received fetal death record status message: {fetalDeathRecordStatusMessage.MessageId}");
                            ProcessResponseMessage(fetalDeathRecordStatusMessage, refID);
                            break;

                        case FetalDeathRecordParentalDemographicsCodingMessage.MESSAGE_TYPE:
                            FetalDeathRecordParentalDemographicsCodingMessage fetalDeathRecordParentalDemographicsCodingMessage = BFDRBaseMessage.Parse<FetalDeathRecordParentalDemographicsCodingMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = fetalDeathRecordParentalDemographicsCodingMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received fetal death record parental demographics coding message: {fetalDeathRecordParentalDemographicsCodingMessage.MessageId}");
                            ProcessResponseMessage(fetalDeathRecordParentalDemographicsCodingMessage, refID);
                            break;

                        case FetalDeathRecordParentalDemographicsCodingUpdateMessage.MESSAGE_TYPE:
                            FetalDeathRecordParentalDemographicsCodingUpdateMessage fetalDeathRecordParentalDemographicsCodingUpdateMessage = BFDRBaseMessage.Parse<FetalDeathRecordParentalDemographicsCodingUpdateMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = fetalDeathRecordParentalDemographicsCodingUpdateMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received fetal death record parental demographics coding update message: {fetalDeathRecordParentalDemographicsCodingUpdateMessage.MessageId}");
                            ProcessResponseMessage(fetalDeathRecordParentalDemographicsCodingUpdateMessage, refID);
                            break;

                        case FetalDeathRecordErrorMessage.MESSAGE_TYPE:
                            FetalDeathRecordErrorMessage fetalDeathRecordErrorMessage = BFDRBaseMessage.Parse<FetalDeathRecordErrorMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = fetalDeathRecordErrorMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received fetal death record error message: {fetalDeathRecordErrorMessage.MessageId}");
                            ProcessResponseMessage(fetalDeathRecordErrorMessage, refID);
                            break;

                        case FetalDeathRecordAcknowledgementMessage.MESSAGE_TYPE:
                            FetalDeathRecordAcknowledgementMessage fetalDeathRecordAcknowledgementMessage = BFDRBaseMessage.Parse<FetalDeathRecordAcknowledgementMessage>((Hl7.Fhir.Model.Bundle)entry.Resource);
                            refID = fetalDeathRecordAcknowledgementMessage.MessageId; // Added refID assignment
                            _logger.LogInformation($"*** Received fetal death record acknowledgement message: {fetalDeathRecordAcknowledgementMessage.MessageId}");
                            ProcessAckMessage(fetalDeathRecordAcknowledgementMessage);
                            break;

                        default:
                            _logger.LogInformation($"*** Unknown message type");
                            break;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogInformation($"*** Error parsing message: {e}");
                    // NCHS does not handle extraction errors, but storing an extraction message in the client
                    // db will help keep a record of errors so they can be reported to NCHS manually
                    // Create and insert the extraction error, but set the status to error so it is not sent to NCHS
                    try
                    {
                        Hl7.Fhir.Model.Bundle innerBundle = (Hl7.Fhir.Model.Bundle)entry.Resource;
                        var headerEntry = innerBundle.Entry.FirstOrDefault(entry2 => entry2.Resource is MessageHeader);
                        if (headerEntry == null)
                        {
                            throw new System.ArgumentException($"Failed to find a Bundle Entry containing a Message Header");
                        }
                        // attempt to create message header to extract meta data
                        MessageHeader header = (MessageHeader)headerEntry?.Resource;
                        // to create the extraction error, pass in the message Id, 
                        // the destination endpoint, and the source 
                        ExtractionErrorMessage extError = new ExtractionErrorMessage(entry.Resource.Id, header?.Source?.Endpoint, _jurisdictionEndPoint);

                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            extError.MessageSource = _jurisdictionEndPoint;

                            MessageItem item = new MessageItem();
                            item.Uid = extError.MessageId;
                            item.Message = extError.ToJson().ToString();

                            // Business Identifiers
                            item.StateAuxiliaryIdentifier = extError.StateAuxiliaryId;
                            item.CertificateNumber = extError.CertNo;
                            item.JurisdictionID = extError.JurisdictionId;
                            item.EventYear = extError.DeathYear;
                            _logger.LogInformation("Business IDs {0}, {1}, {2}", extError.DeathYear, extError.CertNo, extError.JurisdictionId);

                            // Status info
                            item.Status = Models.MessageStatus.Error.ToString();
                            item.Retries = 0;

                            // insert new message
                            context.MessageItems.Add(item);
                            context.SaveChanges();
                            _logger.LogInformation($"Inserted message {item.Uid}");
                        }
                        _logger.LogInformation($"*** Successfully stored extraction error message for error reporting {entry.Resource.Id}");
                    }
                    catch (Exception e2)
                    {
                        // If we reach this point, the FHIR API Server should eventually resend the initial message 
                        // and we will try to process it again.
                        // If the parsing continues to fail, these logs will track the failures for debugging
                        _logger.LogInformation($"*** Failed to store extraction error message for message {entry.Resource.Id}, error: {e2} ");
                    }
                }
            }
        }

        // ProcessAckMessage parses an AckMessage from the server
        // and updates the status of the Message it acknowledged. 
        public void ProcessAckMessage(CommonMessage message)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // find the message the ack is for
                    var original = context.MessageItems.Where(s => s.Uid == message.MessageId).FirstOrDefault();
                    if (original == null)
                    {
                        _logger.LogInformation($"*** Warning: ACK received for unknown message {message.MessageId}");
                        return;
                    }

                    // update message status if this message was not yet acknowledged
                    if (original.Status == Models.MessageStatus.Sent.ToString())
                    {
                        original.Status = Models.MessageStatus.Acknowledged.ToString();
                        context.Update(original);
                        context.SaveChanges();
                        _logger.LogInformation($"*** Successfully acked message {original.Uid}");
                    }
                    else
                    {
                        _logger.LogInformation($"*** Ignored acknowledgement for previously acknowledged or coded message {original.Uid}");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogInformation($"*** Error processing acknowledgement of {message.MessageId}");
                _logger.LogInformation("\nException Caught!");
                _logger.LogInformation("*** Message :{0} ", e.Message);
            }
        }

        // ProcessResponseMessage processes codings, coding updates, and extraction errors
        public async void ProcessResponseMessage(CommonMessage message, String refID)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // check if this response message is a duplicate
                    // if it is a duplicate resend the ack
                    var responseItems = context.ResponseItems.Where(m => m.Uid == message.MessageId).ToList();
                    int count = responseItems.Count;
                    if (count > 0)
                    {
                        _logger.LogInformation($"*** Received duplicate message with Id: {message.MessageId}, ignore and resend ack");

                        CommonMessage ackMessage = null;
                        String path = CreatePathFromResponseFields(responseItems[0]);
                        if (responseItems[0].VitalRecordType == "BFDR-BIRTH")
                        {
                            ackMessage = new BirthRecordAcknowledgementMessage(message);
                           // path = vitalType + "/" + "BFDR_STU3_0";
                        } else if (responseItems[0].VitalRecordType == "BFDR-FETALDEATH")
                        {
                            ackMessage = new FetalDeathRecordAcknowledgementMessage(message);
                           // path = vitalType + "/" + "BFDR_STU3_0";
                        } else
                        {
                            ackMessage = new AcknowledgementMessage(message);
                           // path = "VRDR" + "/" + "VRDR_STU3_0";
                        }

                        HttpResponseMessage rsp = 
                            await client.PostMessageAsync(CommonMessage.ParseGenericMessage(ackMessage.ToJson().ToString(), true), path);
                        if (!rsp.IsSuccessStatusCode)
                        {
                            _logger.LogInformation($"*** Failed to send ack for message {message.MessageId}");
                        }
                        return;
                    }

                    // find the original message this response message is linked to


                    if (String.IsNullOrEmpty(refID))
                    {
                        // TODO determine if an error message should be sent in this case
                        _logger.LogInformation($"*** Warning: Response received for unknown message {refID} ({message.MessageId} {message.EventYear} {message.JurisdictionId} {message.CertNo})");
                        return;
                    }
                    // there should only be one message with the given reference id
                    MessageItem original = context.MessageItems.Where(s => s.Uid == refID).FirstOrDefault();

                    if (original == null)
                    {
                        // TODO determine if an error message should be sent in this case
                        _logger.LogInformation($"*** Warning: Response received for unknown message {refID} ({message.MessageId} {message.EventYear} {message.JurisdictionId} {message.CertNo})");
                        return;
                    }

                    printLogMessage(message, refID, original);
                    context.Update(original);

                    // insert response message in db
                    ResponseItem response = new ResponseItem();
                    response.Uid = message.MessageId;
                    response.ReferenceUid = refID;
                    response.StateAuxiliaryIdentifier = message.StateAuxiliaryId;
                    response.CertificateNumber = message.CertNo;
                    response.JurisdictionID = message.JurisdictionId;
                    response.EventYear = message.EventYear;//message.DeathYear;
                    response.Message = message.ToJson().ToString();
                    response.VitalRecordType = original.VitalRecordType;
                    response.IJE_Version = original.IJE_Version;
                    context.ResponseItems.Add(response);

                    context.SaveChanges();
                    _logger.LogInformation($"*** Successfully recorded {message.GetType().Name} message {message.MessageId}");

                    // create ACK message for coding response messages, status messages and extraction errors do not get ack'd
                    if( message.MessageType != ExtractionErrorMessage.MESSAGE_TYPE && message.MessageType != StatusMessage.MESSAGE_TYPE &&
                            message.MessageType != FetalDeathRecordStatusMessage.MESSAGE_TYPE && message.MessageType != BirthRecordStatusMessage.MESSAGE_TYPE)
                    {
                        CommonMessage ackMessage = null;
                        string path = CreatePathFromMessageFields(original); // original.VitalRecordType + "/" + original.IJE_Version;
                        if (original.VitalRecordType == "BFDR-BIRTH")
                        {
                            ackMessage = new BirthRecordAcknowledgementMessage(message);
                            
                        }
                        else if (original.VitalRecordType == "BFDR-FETALDEATH")
                        {
                            ackMessage = new FetalDeathRecordAcknowledgementMessage(message);
             
                        }
                        else // default to VRDR
                        {
                            ackMessage = new AcknowledgementMessage(message);
               
                        }

                        HttpResponseMessage resp = await client.PostMessageAsync(ackMessage, path);
                        if (!resp.IsSuccessStatusCode)
                        {
                            _logger.LogInformation($"*** Failed to send ack for message {message.MessageId}");
                        }
                    }

                }
            }
            catch (Exception e)
            {
                _logger.LogInformation($"*** Error processing incoming coding or error message {message.MessageId}");
                _logger.LogInformation("\nException Caught!");
                _logger.LogInformation("*** Message :{0} ", e.Message);
            }
        }
        private void printLogMessage(CommonMessage message, string refID,  MessageItem original) 
        {
            string messageType = message.MessageType;
            original.Status = Models.MessageStatus.AcknowledgedAndCoded.ToString();
            _logger.LogInformation("*** Updating status to AcknowledgedAndCoded for {0} {1} {2} {3}", refID, message.EventYear, message.JurisdictionId, message.CertNo);
        }
    }
}