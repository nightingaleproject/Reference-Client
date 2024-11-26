using System;
using System.Text.Json;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Model;
using Hl7.Fhir.ElementModel;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRDR;
using NVSSClient.Models;
using NVSSClient.Controllers;
using NVSSClient.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace NVSSClient.tests 
{
     [Collection("ClientIntegrationTests")]
    public class TimedServiceShould : IClassFixture<CustomWebApplicationFactory<NVSSClient.Startup>>, IDisposable
    {
        private readonly CustomWebApplicationFactory<NVSSClient.Startup> _factory;
        private readonly HttpClient _client;

        private ServiceProvider _serviceProvider;
 
        public TimedServiceShould(CustomWebApplicationFactory<NVSSClient.Startup> factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions{
                AllowAutoRedirect = false
            });

            Console.WriteLine(_factory.Configuration);
            _serviceProvider = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres;"))
            .AddLogging()
            .AddScoped<IConfiguration>(_ => _factory.Configuration)
            .AddSingleton<IHostedService, TimedHostedService>()
            .BuildServiceProvider();

            // Set up the test message
            using (var scope = _serviceProvider.CreateScope())
            {

                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();

                // Create "Acknowledged" test record to for the response
                DeathRecord record = new DeathRecord(File.ReadAllText(FixturePath("test-files/json/DeathRecord1.json")));
                var message = new DeathRecordSubmissionMessage(record);
                message.JurisdictionId = "FL";
                message.CertNo = 5;
                message.DeathYear = 2021;
                message.MessageSource = "https://example.com/jurisdiction/message/endpoint";

                MessageItem item = new MessageItem();
                item.Uid = "DeathCertificateDocument-Example1";
                item.Message = message.ToJson().ToString();
                
                // Business Identifiers
                item.StateAuxiliaryIdentifier = message.StateAuxiliaryId;
                item.CertificateNumber = message.CertNo;
                item.DeathJurisdictionID = message.JurisdictionId;
                item.DeathYear = message.DeathYear;
                
                // Status info
                item.Status = Models.MessageStatus.Acknowledged.ToString();
                item.Retries = 0;
                
                // insert new message
                context.MessageItems.Add(item);
                context.SaveChanges();

            }

        }

        // Removes the test message after the test is finished
        public void Dispose()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();

                // TODO remove the coding and update responses
                MessageItem message = context.MessageItems.Where(s => s.DeathJurisdictionID == "FL" && s.CertificateNumber == 5 && s.DeathYear == 2021).FirstOrDefault();
                context.Remove(message);

                context.SaveChanges();
            }
        }

        [Fact]
        public void ParseContent_ShouldParseExtractionErrorResponse()
        {

            int respItems; 
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                respItems = context.ResponseItems.Count();
            }

            var timedService = _serviceProvider.GetService<IHostedService>() as TimedHostedService;
            Bundle bundle = GetBundleOfBundleResponses("test-files/json/ExtractionErrorMessage.json", "http://nchs.cdc.gov/vrdr_extraction_error");
            //Todo use an await
            timedService.parseBundle(bundle.ToJson());

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var newRespItems = context.ResponseItems.Count();
                Assert.Equal(1, newRespItems - respItems);

                // Clean up, remove the extraction error
                ResponseItem response = context.ResponseItems.Where(m => m.Uid == "b27d6803-86bc-4ec5-bd43-173951ce362b").FirstOrDefault();
                context.Remove(response);

                context.SaveChanges();
            }
        }

        [Fact]
        public void ParseContent_ShouldParseCodedResponse()
        {

            int respItems; 
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                respItems = context.ResponseItems.Count();
            }

            var timedService = _serviceProvider.GetService<IHostedService>() as TimedHostedService;
            Bundle bundle = GetBundleOfBundleResponses("test-files/json/CauseOfDeathCodingMessage.json", "http://nchs.cdc.gov/vrdr_causeofdeath_coding");
            //Todo use an await
            timedService.parseBundle(bundle.ToJson());

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var newRespItems = context.ResponseItems.Count();
                Assert.Equal(1, newRespItems - respItems);

                // Clean up, remove the coding response
                ResponseItem response = context.ResponseItems.Where(m => m.Uid == "0b8543b7-aa8f-41b7-8f59-b4e1530ff68a").FirstOrDefault();
                context.Remove(response);

                context.SaveChanges();
            }
        }

        [Fact]
        public void ParseContent_ShouldParseCodingUpdateResponse()
        {

            int respItems; 
            using (var scope = _serviceProvider.CreateScope())
            {

                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                respItems = context.ResponseItems.Count();
            }

            var timedService = _serviceProvider.GetService<IHostedService>() as TimedHostedService;
            Bundle bundle = GetBundleOfBundleResponses("test-files/json/CodingUpdateMessage.json", "http://nchs.cdc.gov/vrdr_causeofdeath_coding_update");
            //Todo use an await
            timedService.parseBundle(bundle.ToJson());

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var newRespItems = context.ResponseItems.Count();
                Assert.Equal(1, newRespItems - respItems);

                // Clean up, remove the coding update response
                ResponseItem response = context.ResponseItems.Where(m => m.Uid == "43e25b3b-6501-4eeb-81de-a3efddf19692").FirstOrDefault();
                context.Remove(response);

                context.SaveChanges();
            }
        }

        [Fact]
        public void ParseContent_ShouldParseDemographicsResponse()
        {

            int respItems; 
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                respItems = context.ResponseItems.Count();
            }

            var timedService = _serviceProvider.GetService<IHostedService>() as TimedHostedService;
            Bundle bundle = GetBundleOfBundleResponses("test-files/json/DemographicsCodingMessage.json", "http://nchs.cdc.gov/vrdr_demographics_coding");
            //Todo use an await
            timedService.parseBundle(bundle.ToJson());

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var newRespItems = context.ResponseItems.Count();
                Assert.Equal(1, newRespItems - respItems);

                // Clean up, remove the coding response
                ResponseItem response = context.ResponseItems.Where(m => m.Uid == "21002ed3-c5cc-443d-b2f1-ef5022b740f4").FirstOrDefault();
                context.Remove(response);

                context.SaveChanges();
            }
        }

        [Fact]
        public void ParseContent_ShouldParseDemographicsUpdateResponse()
        {

            int respItems; 
            using (var scope = _serviceProvider.CreateScope())
            {

                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                respItems = context.ResponseItems.Count();
            }

            var timedService = _serviceProvider.GetService<IHostedService>() as TimedHostedService;
            Bundle bundle = GetBundleOfBundleResponses("test-files/json/DemographicsCodingUpdateMessage.json", "http://nchs.cdc.gov/vrdr_demographics_coding_update");
            //Todo use an await
            timedService.parseBundle(bundle.ToJson());

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var newRespItems = context.ResponseItems.Count();
                Assert.Equal(1, newRespItems - respItems);

                // Clean up, remove the coding update response
                ResponseItem response = context.ResponseItems.Where(m => m.Uid == "f569dea9-a824-4751-b7c8-34dde4b94e9a").FirstOrDefault();
                context.Remove(response);

                context.SaveChanges();
            }
        }

        [Fact]
        public void ParseContent_ShouldGenerateExtractionError()
        {

            int msgItems; 
            using (var scope = _serviceProvider.CreateScope())
            {

                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                msgItems = context.MessageItems.Count();
            }

            var timedService = _serviceProvider.GetService<IHostedService>() as TimedHostedService;
            StreamReader bundleReader = FixtureStream("test-files/json/BundleOfBundlesWithError.json");
            string bundleJson = bundleReader.ReadToEnd();
            // Todo use an await
            // This should result in an extraction error that's added to the MessageItems table
            timedService.parseBundle(bundleJson);

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var newMsgItems = context.MessageItems.Count();
                Assert.Equal(1, newMsgItems - msgItems);

                // We can leave the extraction error 

                context.SaveChanges();
            }
        }

        [Fact]
        public async void TestMaxRetriesAsync()
        {

            int msgItems; 
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                msgItems = context.MessageItems.Count();
            }

            string uid = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            using (var scope = _serviceProvider.CreateScope())
            {

                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();

                // Create an invalid FHIR DeathRecord message to guarantee failure responses from the API.
                DeathRecord record = new DeathRecord("{}");
                var message = new DeathRecordSubmissionMessage(record);
                message.JurisdictionId = "FL";
                message.CertNo = 78901;
                message.DeathYear = 2021;
                message.MessageSource = "https://example.com/jurisdiction/message/endpoint";

                MessageItem item = new MessageItem();
                item.Uid = uid;
                item.Message = message.ToJson().ToString();
                
                // Business Identifiers
                item.StateAuxiliaryIdentifier = message.StateAuxiliaryId;
                item.CertificateNumber = message.CertNo;
                item.DeathJurisdictionID = message.JurisdictionId;
                item.DeathYear = message.DeathYear;
                
                // Status info
                // item.Status = Models.MessageStatus.Acknowledged.ToString();
                item.ExpirationDate = DateTime.MinValue;
                item.Retries = 0;
                
                // insert new message
                context.MessageItems.Add(item);
                context.SaveChanges();
            }

            using (var scope = _serviceProvider.CreateScope())
            {
                var timedService = _serviceProvider.GetService<IHostedService>() as TimedHostedService;
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var newMsgItems = context.MessageItems.Count();
                Assert.Equal(1, newMsgItems - msgItems);
                Assert.Equal(1, context.MessageItems.Where(s => s.Uid == uid && s.Retries == 0).Count());
                await System.Threading.Tasks.Task.Run(() => timedService.ResendMessages());
                Assert.Equal(1, context.MessageItems.Where(s => s.Uid == uid && s.Retries == 1).Count());
                await System.Threading.Tasks.Task.Run(() => timedService.ResendMessages());
                Assert.Equal(1, context.MessageItems.Where(s => s.Uid == uid && s.Retries == 2).Count());
                await System.Threading.Tasks.Task.Run(() => timedService.ResendMessages());
                Assert.Equal(1, context.MessageItems.Where(s => s.Uid == uid && s.Retries == 3).Count());
                await System.Threading.Tasks.Task.Run(() => timedService.ResendMessages());
                Assert.Equal(1, context.MessageItems.Where(s => s.Uid == uid && s.Retries == 4).Count());
                await System.Threading.Tasks.Task.Run(() => timedService.ResendMessages());
                Assert.Equal(1, context.MessageItems.Where(s => s.Uid == uid && s.Retries == 5).Count());
                // Past 5 resends, it should stop retrying and thus not increment the number of retries.
                await System.Threading.Tasks.Task.Run(() => timedService.ResendMessages());
                Assert.Equal(1, context.MessageItems.Where(s => s.Uid == uid && s.Retries == 5).Count());
            }
        }

        private Bundle GetBundleOfBundleResponses(string filePath, string msgType)
        {
            Console.WriteLine("Test parse bundle");
            List<BaseMessage> testMessages = new List<BaseMessage>();
            switch (msgType)
            {
                case "http://nchs.cdc.gov/vrdr_causeofdeath_coding":
                    CauseOfDeathCodingMessage codedMsg = BaseMessage.Parse<CauseOfDeathCodingMessage>(FixtureStream(filePath));
                    codedMsg.JurisdictionId = "FL";
                    codedMsg.CertNo = 5;
                    codedMsg.DeathYear = 2021;
                    testMessages.Add(codedMsg);
                    break;
                case "http://nchs.cdc.gov/vrdr_causeofdeath_coding_update":
                    CauseOfDeathCodingUpdateMessage updateMsg = BaseMessage.Parse<CauseOfDeathCodingUpdateMessage>(FixtureStream(filePath));
                    updateMsg.JurisdictionId = "FL";
                    updateMsg.CertNo = 5;
                    updateMsg.DeathYear = 2021;
                    testMessages.Add(updateMsg);
                    break;
                case "http://nchs.cdc.gov/vrdr_demographics_coding":
                    DemographicsCodingMessage demMsg = BaseMessage.Parse<DemographicsCodingMessage>(FixtureStream(filePath));
                    demMsg.JurisdictionId = "FL";
                    demMsg.CertNo = 5;
                    demMsg.DeathYear = 2021;
                    testMessages.Add(demMsg);
                    break;
                case "http://nchs.cdc.gov/vrdr_demographics_coding_update":
                    DemographicsCodingUpdateMessage demUpdateMsg = BaseMessage.Parse<DemographicsCodingUpdateMessage>(FixtureStream(filePath));
                    demUpdateMsg.JurisdictionId = "FL";
                    demUpdateMsg.CertNo = 5;
                    demUpdateMsg.DeathYear = 2021;
                    testMessages.Add(demUpdateMsg);
                    break;
                case "http://nchs.cdc.gov/vrdr_extraction_error":
                    ExtractionErrorMessage errorMsg = BaseMessage.Parse<ExtractionErrorMessage>(FixtureStream(filePath));
                    errorMsg.JurisdictionId = "FL";
                    errorMsg.CertNo = 5;
                    errorMsg.DeathYear = 2021;
                    testMessages.Add(errorMsg);
                    break;
                default:
                    // TODO should create an error
                    Console.WriteLine($"Unknown message type");
                    break;
            }

            Bundle responseBundle = new Bundle();
            responseBundle.Type = Bundle.BundleType.Searchset;
            responseBundle.Timestamp = DateTime.Now;
            foreach (BaseMessage msg in testMessages)
            {
                responseBundle.AddResourceEntry((Bundle)msg, "urn:uuid:" + msg.MessageId);
            }
            return responseBundle;
        }

        private string FixturePath(string filePath)
        {
            if (Path.IsPathRooted(filePath))
            {
                return filePath;
            }
            else
            {
                return Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath);
            }
        }
        private StreamReader FixtureStream(string filePath)
        {
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath);
            }
            return File.OpenText(filePath);
        }
    }
}