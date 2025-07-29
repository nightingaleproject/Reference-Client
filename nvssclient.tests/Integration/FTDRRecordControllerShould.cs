using BFDR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NVSSClient.Controllers;
using NVSSClient.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using VRDR;
using Xunit;

/**
    INSTRUCTIONS: Record Controller Tests are called explicity as part of the git runner.
    Any new or updated tests should be updated in .github/workflows/run-tests.yml
**/

namespace NVSSClient.tests 
{
    [Collection("ClientIntegrationTests")]
    public class ftdrrecordControllerShould : IClassFixture<CustomWebApplicationFactory<NVSSClient.Startup>>
    {
        private readonly CustomWebApplicationFactory<NVSSClient.Startup> _factory;
        private readonly HttpClient _client;
    
        public ftdrrecordControllerShould(CustomWebApplicationFactory<NVSSClient.Startup> factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions{
                AllowAutoRedirect = false
            });
        }
        

        [Fact]
        public async Task PostRecords_ShouldSaveMessage()
        {
            var records = GetTestRecords("test-files/json/FatalDeathRecord.json");
            // Submit that Death Record
            HttpRequestMessage postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:4300/ftdrrecord/submissions");
            var json = JsonSerializer.Serialize(records);
            postRequest.Content = new StringContent(json);
            postRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await _client.SendAsync(postRequest);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task PostRecord_ShouldSaveMessage()
        {
            var records = GetTestRecords("test-files/json/FatalDeathRecord.json");
            // Submit that Death Record
            HttpRequestMessage postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:4300/ftdrrecord/submission");
            var json = JsonSerializer.Serialize(records[0]);
            postRequest.Content = new StringContent(json);
            postRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await _client.SendAsync(postRequest);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task PostVoidRecords_ShouldSaveMessage()
        {
            List<Dictionary<string, object>> dataList = new List<Dictionary<string, object>>();
            var records = GetTestRecords("test-files/json/FatalDeathRecord.json");
            foreach (string record in records)
            {
                Dictionary<string, object> data = new Dictionary<string, object>();
                data["record"] = record;
                data["block_count"] = 1;
                dataList.Add(data);
            }

            // Submit that Death Record
            HttpRequestMessage postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:4300/ftdrrecord/voids");
            var json = JsonSerializer.Serialize(dataList);
            postRequest.Content = new StringContent(json);
            postRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await _client.SendAsync(postRequest);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task PostVoidRecord_ShouldSaveMessage()
        {
            var records = GetTestRecords("test-files/json/FatalDeathRecord.json");

            Dictionary<string, object> data = new Dictionary<string, object>();
            data["record"] = records[0];
            data["block_count"] = 1;

            // Submit that Death Record
            HttpRequestMessage postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:4300/ftdrrecord/void");
            var json = JsonSerializer.Serialize(data);
            postRequest.Content = new StringContent(json);
            postRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await _client.SendAsync(postRequest);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task GetMessageStatus_ReturnsMessages()
        {
            var response = await _client.GetAsync("/ftdrrecord/2020/NY/182");
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync();
            //Assert, for now just check it's not empty
            Assert.False(String.IsNullOrWhiteSpace(responseString));
        }

        [Fact]
        public async Task GetMessages_ReturnsMessages()
        {
            var response = await _client.GetAsync("/ftdrrecord");
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync();
            //Assert, for now just check it's not empty
            Assert.False(String.IsNullOrWhiteSpace(responseString));
        }

        private List<String> GetTestRecords(string filepath)
        {
            List<String> testRecords = new List<String>();
            FetalDeathRecord record = new FetalDeathRecord(File.ReadAllText(FixturePath(filepath)));
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