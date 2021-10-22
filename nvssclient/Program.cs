using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NVSSClient.Services;

namespace NVSSClient
{
    class Program{

        private static String apiUrl = "https://localhost:5001/bundles";
        private static String token = "";
        public static void Main(string[] args)
            => CreateHostBuilder(args).Build().Run();

        // EF Core uses this method at design time to access the DbContext   
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<TimedHostedService>();
                });
        
        static HttpClient client = new HttpClient();
        public static String GetAuthorizeToken()
        {
            var client = new RestClient("https://YOUR_DOMAIN/oauth/token");
            var request = new RestRequest(Method.POST);
            request.AddHeader("content-type", "application/x-www-form-urlencoded");
            request.AddParameter("application/x-www-form-urlencoded", "grant_type=client_credentials&client_id=YOUR_CLIENT_ID&client_secret=YOUR_CLIENT_SECRET&audience=YOUR_API_IDENTIFIER", ParameterType.RequestBody);
            IRestResponse response = client.Execute(request);
            if (response.IsSuccessStatusCode)
            {
                //read response 
                JObject json = JObject.Parse(response);
                token = json["access_token"];
                return token;
            }
            return "";
        }

        // Makes a GET request to the API server for any new messages
        public static async Task<String> GetMessageResponsesAsync(DateTime lastUpdated, String authToken)
        {
            // Capture the time just before we make the request
            //String nextUpdated = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
            
            // retrieve new messages
            // TODO add auth token
            string authorization = authToken;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authorization);

            var address = apiUrl;
            Console.WriteLine($"Get messages since: {lastUpdated}");
            if (!string.IsNullOrWhiteSpace(lastUpdated)){
                address = apiUrl + "?lastUpdated=" + lastUpdated;
            }
            var content = client.GetStringAsync(address).Result;
            
            if (content.IsSuccessStatusCode)
            {
                return content;
            } 
            else if (content.HttpStatusCode == 401)
            {
                // unauthorized, refresh token
            }
            // TODO move this to where we make the call
            //parseBundle(content);

            // update the time
            //lastUpdated = nextUpdated;
        }

        // POSTS a message to the API server
        public static async Task<HttpStatusCode> PostMessageAsync(BaseMessage message, String authToken)
        {
            
            var json = message.ToJSON();

            var data = new StringContent(json, Encoding.UTF8, "application/json");

            using var client = new HttpClient();

            // TODO add auth token
            string authorization = authToken;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authorization);

            var response = client.PostAsync(apiUrl, data).Result;
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Successfully submitted {message.MessageId}");
                return true;
            }
            else if (response.HttpStatusCode == 401)
            {
                // unauthorized, refresh token
            }
            else
            {
                Console.WriteLine($"Error submitting {message.MessageId}");
                return false;
            }
        }
    }
}