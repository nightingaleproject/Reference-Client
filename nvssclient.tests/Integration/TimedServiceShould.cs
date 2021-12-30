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
    public class TimedServiceShould : IClassFixture<CustomWebApplicationFactory<NVSSClient.Startup>>
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
            .AddSingleton<IHostedService, TimedHostedService>()
            .AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres;"))
            .AddLogging()
            .AddScoped<IConfiguration>(_ => _factory.Configuration)
            .BuildServiceProvider();
        }

        [Fact]
        public void ParseContent_ShouldParseCodedResponse()
        {
            Console.WriteLine(_factory.Configuration);

            int respItems; 
            MessageItem item;
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

                item = new MessageItem();
                item.Uid = message.MessageId;
                item.Message = message.ToJson().ToString();
                
                // Business Identifiers
                item.StateAuxiliaryIdentifier = message.StateAuxiliaryIdentifier;
                item.CertificateNumber = message.CertificateNumber;
                item.DeathJurisdictionID = message.DeathJurisdictionID;
                item.DeathYear = message.DeathYear;
                Console.WriteLine("Business IDs {0}, {1}, {2}", message.DeathYear, message.CertificateNumber, message.DeathJurisdictionID);
                
                // Status info
                item.Status = Models.MessageStatus.Acknowledged.ToString();
                item.Retries = 0;
                
                // insert new message
                context.MessageItems.Add(item);
                context.SaveChanges();

                respItems = context.ResponseItems.Count();
            }

            var timedService = _serviceProvider.GetService<IHostedService>() as TimedHostedService;
            Bundle bundle = GetBundleOfBundleResponses();
            //Todo use an await
            timedService.parseBundle(bundle.ToJson());

            using (var scope = _serviceProvider.CreateScope())
            {

                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var newRespItems = context.ResponseItems.Count();

                Assert.Equal(1, newRespItems - respItems);

                context.Remove(item);

                ResponseItem response = context.ResponseItems.Where(m => m.Uid == "b27d6803-86bc-4ec5-bd43-173951ce362b").FirstOrDefault();
                context.Remove(response);

                context.SaveChanges();
            }
        }

        private Bundle GetBundleOfBundleResponses()
        {
            Console.WriteLine("Test parse bundle");
            List<BaseMessage> testMessages = new List<BaseMessage>();
            ExtractionErrorMessage message = BaseMessage.Parse<ExtractionErrorMessage>(FixtureStream("test-files/json/ExtractionErrorMessage.json"));
            message.DeathJurisdictionID = "FL";
            message.CertificateNumber = 5;
            message.DeathYear = 2021;

            testMessages.Add(message);

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