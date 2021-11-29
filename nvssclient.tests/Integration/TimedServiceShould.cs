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
using Moq;

namespace NVSSClient.tests {
    public class TimedServiceShould : IClassFixture<CustomWebApplicationFactory<NVSSClient.Startup>>
    {
        private readonly CustomWebApplicationFactory<NVSSClient.Startup> _factory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly HttpClient _client;
 
        public TimedServiceShould(CustomWebApplicationFactory<NVSSClient.Startup> factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions{
                AllowAutoRedirect = false
            });

        }

        [Fact]
        public void ParseContent_ShouldParseCodedResponse()
        {
            Console.WriteLine(_factory.Configuration);
            ServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton<IHostedService, TimedHostedService>()
            .AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres;"))
            .AddLogging()
            .AddScoped<IConfiguration>(_ => _factory.Configuration)
            .BuildServiceProvider();

            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.EnsureCreated();
                var respItems = context.ResponseItems.Count();

                var timedService = serviceProvider.GetService<IHostedService>() as TimedHostedService;
                Bundle bundle = GetBundleOfBundleResponses();
                //Todo use an await
                timedService.parseBundle(bundle.ToJson());

                var newRespItems = context.ResponseItems.Count();

                Assert.Equal(1, newRespItems - respItems);
            }
        }

        private Bundle GetBundleOfBundleResponses()
        {
            Console.WriteLine("Test parse bundle");
            List<BaseMessage> testMessages = new List<BaseMessage>();
            ExtractionErrorMessage message = BaseMessage.Parse<ExtractionErrorMessage>(FixtureStream("test-files/json/ExtractionErrorMessage.json"));
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