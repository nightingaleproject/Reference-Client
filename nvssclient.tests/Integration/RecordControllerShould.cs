using System;
using System.Text.Json;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using VRDR;
using NVSSClient.Controllers;
using NVSSClient.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NVSSClient.tests {
    public class RecordControllerShould : IClassFixture<CustomWebApplicationFactory<NVSSClient.Startup>>
    {
        private readonly CustomWebApplicationFactory<NVSSClient.Startup> _factory;
        private readonly HttpClient _client;
    
        public RecordControllerShould(CustomWebApplicationFactory<NVSSClient.Startup> factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions{
                AllowAutoRedirect = false
            });
        }

        [Fact]
        public async Task GetMessageStatus_ReturnsMessages()
        {
            var response = await _client.GetAsync("/record");
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync();
            //Assert, for now just check it's not empty
            Assert.False(String.IsNullOrWhiteSpace(responseString));
        }
        

        [Fact]
        public async Task PostRecord_ShouldSaveMessage()
        {
            var records = GetTestRecords();
            // Submit that Death Record
            HttpRequestMessage postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:4300/record");
            var json = JsonSerializer.Serialize(records);
            postRequest.Content = new StringContent(json);
            postRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await _client.SendAsync(postRequest);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        private List<String> GetTestRecords()
        {
            List<String> testRecords = new List<String>();
            DeathRecord record = new DeathRecord(File.ReadAllText(FixturePath("test-files/json/DeathRecord1.json")));
            testRecords.Add(record.ToJson());
            return testRecords;
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
    }
}