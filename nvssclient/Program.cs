using System;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using VRDR;
using RestSharp;
using NVSSClient.Services;

namespace NVSSClient
{
    class Program{
        private static String token = "";
        public static void Main(string[] args) 
        {
            CreateHostBuilder(args).Build().Run();
        }
        

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
        
        public IConfiguration Configuration { get; }
        static HttpClient client = new HttpClient();
        public static String GetAuthorizeToken()
        {
            
            String authUrl = Startup.StaticConfig.GetConnectionString("AuthServer");
            var rclient = new RestClient(authUrl);
            var request = new RestRequest(Method.POST);

            string clientId = Startup.StaticConfig.GetValue<string>("Authentication:ClientId");
            string clientSecret = Startup.StaticConfig.GetValue<string>("Authentication:ClientSecret");
            string username = Startup.StaticConfig.GetValue<string>("Authentication:Username");
            string pass = Startup.StaticConfig.GetValue<string>("Authentication:Password");
            String paramString = String.Format("grant_type=password&client_id={0}&client_secret={1}&username={2}&password={3}", clientId, clientSecret, username, pass);
            request.AddHeader("content-type", "application/x-www-form-urlencoded");
            request.AddParameter("application/x-www-form-urlencoded", paramString, ParameterType.RequestBody);
            
            IRestResponse response = rclient.Execute(request);
            string content = response.Content;
            if (!String.IsNullOrEmpty(content))
            {
                //read response 
                JObject json = JObject.Parse(content);
                if (json["access_token"] != null)
                {
                    String newtoken = json["access_token"].ToString();
                    return newtoken;
                }
                
            }
            return "";
        }

        // GET request to the API server for any new messages
        public static String GetMessageResponsesAsync(String lastUpdated)
        {
            string apiUrl = Startup.StaticConfig.GetConnectionString("ApiServer");
            var address = apiUrl;
            Console.WriteLine($"Get messages since: {lastUpdated}");

            if (String.IsNullOrEmpty(token))
            {
                token = GetAuthorizeToken();
            }
            string authorization = token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authorization);


            if (lastUpdated != null){
                address = apiUrl + "?lastUpdated=" + lastUpdated;
            }
            var response = client.GetAsync(address).Result;
            
            if (response.IsSuccessStatusCode)
            {
                var content = response.Content.ReadAsStringAsync().Result;
                Console.WriteLine(content);
                return content;
            }
            else
            {
                Console.WriteLine(response.StatusCode);
                return "";
            }

        }

        // POSTS a message to the API server
        public static Boolean PostMessageAsync(BaseMessage message)
        {
            string apiUrl = Startup.StaticConfig.GetConnectionString("ApiServer");
            var json = message.ToJSON();

            var data = new StringContent(json, Encoding.UTF8, "application/json");

            using var client = new HttpClient();

            // TODO add auth token
            if (String.IsNullOrEmpty(token))
            {
                token = GetAuthorizeToken();
            }
            string authorization = token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authorization);

            var response = client.PostAsync(apiUrl, data).Result;
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Successfully submitted {message.MessageId}");
                return true;
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // unauthorized, refresh token
                return false;
            }
            else
            {
                Console.WriteLine($"Error submitting {message.MessageId}");
                return false;
            }
        }
    }
}