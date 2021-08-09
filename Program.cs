using System;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Timers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Npgsql;
using VRDR;

namespace NVSSClient
{
    class Program{

        private static String apiUrl = "https://localhost:5001/bundles";
        private static String lastUpdated;

        private static int interval = 10;
        static readonly HttpClient client = new HttpClient();
        private static System.Timers.Timer getMsgsTimer;
        public static void Main(string[] args)
        {

            var cs = "Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres";

            using var con = new NpgsqlConnection(cs);
            con.Open();

            var sql = "SELECT * FROM record";

            using var cmd = new NpgsqlCommand(sql, con);

            using NpgsqlDataReader rdr = cmd.ExecuteReader();

            // read each death record and create a submission message to send to the api
            // while (rdr.Read())
            // {
            //     var record = rdr.GetString(1);
            //     try {
            //         DeathRecord deathRecord = new DeathRecord(record, true);
            //         var message = new DeathRecordSubmission(deathRecord);
            //         message.MessageSource = "https://example.com/jurisdiction/message/endpoint";
            //         postMessage(message);
            //     } catch {
            //         Console.WriteLine($"Unable to parse record");
            //     }

            // }

            // set the time to check for new messages
            SetTimer();
            Console.WriteLine("\nPress enter to exit the application");
            Console.ReadLine();
            getMsgsTimer.Stop();
            getMsgsTimer.Dispose();

            Console.WriteLine("Exiting...");
            
        }

        private static void SetTimer()
        {
            // set time with a 30 sec interval
            getMsgsTimer = new System.Timers.Timer(interval * 1000);
            getMsgsTimer.Elapsed += OnGetMessageTimer;
            getMsgsTimer.AutoReset = true;
            getMsgsTimer.Enabled = true;
        }
        public static void postMessage(BaseMessage message)
        {
            
            var json = message.ToJSON();

            var data = new StringContent(json, Encoding.UTF8, "application/json");

            using var client = new HttpClient();

            var response = client.PostAsync(apiUrl, data).Result;
            if (response.IsSuccessStatusCode){
                Console.WriteLine($"Successfully submitted {message.MessageId}");
            }
            else {
                Console.WriteLine($"Error submitting {message.MessageId}");
            }
        }

        private static void OnGetMessageTimer(Object source, ElapsedEventArgs el)
        {
            try
            {
                var address = apiUrl;
                Console.WriteLine($"Get messages since: {lastUpdated}");
                if (!string.IsNullOrWhiteSpace(lastUpdated)){
                    address = apiUrl + "?lastUpdated=" + lastUpdated;
                }

                
                var content = client.GetStringAsync(address).Result;
                
                // update the last updated time
                lastUpdated = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
                parseBundle(content);
                //Console.WriteLine(content);

            }
            catch(Exception e)
            {
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
            }
        }


        // parses the bundle from nchs and processes each message response
        public static void parseBundle(String bundle){
            var array = JArray.Parse(bundle);
            foreach (var item in array)
            {
                try
                {
                    String msgJson = item["message"].ToString();
                    BaseMessage msg = BaseMessage.Parse(msgJson, true);
                    // TODO change this to the id of the initial message

                    switch (msg.MessageType)
                    {
                        case "http://nchs.cdc.gov/vrdr_acknowledgement":
                            //message = new AckMessage(bundle);
                            Console.WriteLine($"Received ask message: {msg.MessageId}");
                            break;
                        case "http://nchs.cdc.gov/vrdr_coding":
                            //message = new CodingResponseMessage(bundle);
                            Console.WriteLine($"Received coding response: {msg.MessageId}");
                            break;
                        case "http://nchs.cdc.gov/vrdr_coding_update":
                            //message = new CodingUpdateMessage(bundle);
                            Console.WriteLine($"Received coding update: {msg.MessageId}");
                            break;
                        case "http://nchs.cdc.gov/vrdr_extraction_error":
                            //message = new ExtractionErrorMessage(bundle, message);
                            Console.WriteLine($"Received extraction error: {msg.MessageId}");
                            break;
                        default:
                            Console.WriteLine($"Uknown message type");
                            break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error parsing message: {e}");
                }
            }
        }

        // updates each message status in the queue
        public static void updateMessageStatus(){}

    }

}