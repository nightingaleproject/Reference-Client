using System;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Timers;
using System.Threading.Tasks;
using Npgsql;
using VRDR;

namespace NVSSClient
{
    class Program{
        static readonly HttpClient client = new HttpClient();
        public static void Main(string[] args)
        {

            var cs = "Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres";

            using var con = new NpgsqlConnection(cs);
            con.Open();

            var sql = "SELECT * FROM record";

            using var cmd = new NpgsqlCommand(sql, con);

            using NpgsqlDataReader rdr = cmd.ExecuteReader();

            // read each death record and create a submission message to send to the api
            while (rdr.Read())
            {
                var record = rdr.GetString(1);
                try {
                    DeathRecord deathRecord = new DeathRecord(record, true);
                    var message = new DeathRecordSubmission(deathRecord);
                    message.MessageSource = "https://example.com/jurisdiction/message/endpoint";
                    postMessage(message);
                } catch {
                    Console.WriteLine($"Unable to parse record");
                }

            }
         
            getMessages();
            
        }

        public static void postMessage(BaseMessage message)
        {
            
            var json = message.ToJSON();

            var data = new StringContent(json, Encoding.UTF8, "application/json");

            var url = "https://localhost:5001/bundles";
            using var client = new HttpClient();

            var response = client.PostAsync(url, data).Result;
            if (response.IsSuccessStatusCode){
                Console.WriteLine($"Successfully submitted {message.MessageId}");
            }
            else {
                Console.WriteLine($"Error submitting {message.MessageId}");
            }
        }

        public static void getMessages()
        {
            try
            {

                var content = client.GetStringAsync("https://localhost:5001/bundles").Result;

                Console.WriteLine(content);

            }
            catch(Exception e)
            {
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
            }
        }


        // parses the bundle from nchs and processes each message response
        public static void parseBundle(){}

        // updates each message status in the queue
        public static void updateMessageStatus(){}

    }

}