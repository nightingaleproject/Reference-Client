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

namespace NVSSClient.tests {
    public class TimedServiceShould : IClassFixture<CustomWebApplicationFactory<NVSSClient.Startup>>, IDisposable
    {
        private readonly CustomWebApplicationFactory<NVSSClient.Startup> _factory;
        private readonly IServiceScopeFactory _scopeFactory;
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
                var message = new DeathRecordSubmission(record);
                message.DeathJurisdictionID = "FL";
                message.CertificateNumber = 5;
                message.DeathYear = 2021;
                message.MessageSource = "https://example.com/jurisdiction/message/endpoint";

                MessageItem item = new MessageItem();
                item.Uid = message.MessageId;
                item.Message = message.ToJson().ToString();
                
                // Business Identifiers
                item.StateAuxiliaryIdentifier = message.StateAuxiliaryIdentifier;
                item.CertificateNumber = message.CertificateNumber;
                item.DeathJurisdictionID = message.DeathJurisdictionID;
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
            Bundle bundle = GetBundleOfBundleResponses("test-files/json/CauseOfDeathCodingMessage.json", "http://nchs.cdc.gov/vrdr_coding");
            //Todo use an await
            timedService.parseBundle(bundle.ToJson());

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var newRespItems = context.ResponseItems.Count();
                Assert.Equal(1, newRespItems - respItems);

                // Clean up, remove the coding response
                ResponseItem response = context.ResponseItems.Where(m => m.Uid == "a3a1ff4e-fc50-47eb-b3af-442e5fceadd1").FirstOrDefault();
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
            Bundle bundle = GetBundleOfBundleResponses("test-files/json/CodingUpdateMessage.json", "http://nchs.cdc.gov/vrdr_coding_update");
            //Todo use an await
            timedService.parseBundle(bundle.ToJson());

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var newRespItems = context.ResponseItems.Count();
                Assert.Equal(1, newRespItems - respItems);

                // Clean up, remove the coding update response
                ResponseItem response = context.ResponseItems.Where(m => m.Uid == "a3a1ff4e-fc50-47eb-b3af-442e5fceadd1").FirstOrDefault();
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
            //Todo use an await
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

        private Bundle GetBundleOfBundleResponses(string filePath, string msgType)
        {
            Console.WriteLine("Test parse bundle");
            List<BaseMessage> testMessages = new List<BaseMessage>();
            switch (msgType)
            {
                case "http://nchs.cdc.gov/vrdr_coding":
                    CodingResponseMessage codedMsg = BaseMessage.Parse<CodingResponseMessage>(FixtureStream(filePath));
                    codedMsg.DeathJurisdictionID = "FL";
                    codedMsg.CertificateNumber = 5;
                    codedMsg.DeathYear = 2021;
                    testMessages.Add(codedMsg);
                    break;
                case "http://nchs.cdc.gov/vrdr_coding_update":
                    CodingUpdateMessage updateMsg = BaseMessage.Parse<CodingUpdateMessage>(FixtureStream(filePath));
                    updateMsg.DeathJurisdictionID = "FL";
                    updateMsg.CertificateNumber = 5;
                    updateMsg.DeathYear = 2021;
                    testMessages.Add(updateMsg);
                    break;
                case "http://nchs.cdc.gov/vrdr_extraction_error":
                    ExtractionErrorMessage errorMsg = BaseMessage.Parse<ExtractionErrorMessage>(FixtureStream(filePath));
                    errorMsg.DeathJurisdictionID = "FL";
                    errorMsg.CertificateNumber = 5;
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