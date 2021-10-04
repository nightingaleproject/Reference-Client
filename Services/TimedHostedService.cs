using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NVSSClient.Models;

using System.Text;
using System.Net.Http;
using System.Text.Json;
using System.Linq;
using Newtonsoft.Json.Linq;
using Npgsql;
using VRDR;

namespace NVSSClient.Services
{
    // a timed service that runs every x seconds to pull new messages from the db and submit, 
    // check for responses, resend messages that haven't had a response in x time
    public class TimedHostedService : IHostedService, IDisposable
    {
        private readonly AppDbContext _context;

        private static String jurisdictionEndPoint = "https://example.com/jurisdiction/message/endpoint";
        private static String apiUrl = "https://localhost:5001/bundles";
        private static String cs = "Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres";

        private static NpgsqlConnection con = new NpgsqlConnection(cs);
        private static String lastUpdated = new DateTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
        private static int interval = 10;
        static readonly HttpClient client = new HttpClient();
        public enum Status : int { 
            Sent = 1,
            Error = 2,
            Acknowledged = 3
        }


        private int executionCount = 0;
        private readonly ILogger<TimedHostedService> _logger;
        private Timer _timer;

        public TimedHostedService(AppDbContext context, ILogger<TimedHostedService> logger)
        {
            _logger = logger;
            _context = context;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service running.");

            _timer = new Timer(DoWork, null, TimeSpan.Zero, 
                TimeSpan.FromSeconds(20));

            return Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            var count = Interlocked.Increment(ref executionCount);

            _logger.LogInformation(
                "Timed Hosted Service is working. Count: {Count}", count);
            
            // Step 1, submit new records in the db
            SubmitRecords();

            // Step 2, poll for response messages from the server
            PollForResponses();

            // Step 3, check for messages that haven't received an ack in X amount of time
            
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        // Retrieve records from database and send to the endpoint
        private void SubmitRecords()
        {
            var records = _context.RecordItems;
            Int32 numRecords = records.Count();
            _logger.LogInformation("Retrieved {records} from db.", numRecords);
            foreach (RecordItem record in records)
            {
                var recordId = record.Id;
                var jsonStr = record.Record;

                DeathRecord deathRecord = new DeathRecord(jsonStr, true);
                var message = new DeathRecordSubmission(deathRecord);
                message.MessageSource = jurisdictionEndPoint;
                //insertMessage(message, recordId);
                postMessage(message);
            }

        }

        private void PollForResponses()
        {
            // Capture the time just before we make the request
            String nextUpdated = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
            
            // retrieve new messages
            var address = apiUrl;
            Console.WriteLine($"Get messages since: {lastUpdated}");
            if (!string.IsNullOrWhiteSpace(lastUpdated)){
                address = apiUrl + "?lastUpdated=" + lastUpdated;
            }
            var content = client.GetStringAsync(address).Result;
            
            parseBundle(content);

            // update the time
            lastUpdated = nextUpdated;
        }

        // Post the message to the NCHS API
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
                //updateMessageStatus(message, Status.Error);
                Console.WriteLine($"Error submitting {message.MessageId}");
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
                            AckMessage message = new AckMessage(msg);
                            //acknowledgeMessage(message);
                            Console.WriteLine($"Received ask message: {msg.MessageId} for {message.AckedMessageId}");
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
                            //updateMessageStatus(msg, Status.Error);
                            Console.WriteLine($"Received extraction error: {msg.MessageId}");
                            break;
                        default:
                            Console.WriteLine($"Unknown message type");
                            break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error parsing message: {e}");
                }
            }
        }

        // DB functions, todo move to a controller?
        public static void insertMessage(BaseMessage message, int recordId)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                // insert new message
                var sql = "INSERT INTO message(state_auxiliary_id, cert_number, nchs_id) VALUES (@state_auxiliary_id, @cert_number, @nchs_id)"; //, cert_number, nchs_id, record_id, status_id, @cert, @nchs, @record, @status
                using var cmd = new NpgsqlCommand(sql, con);
                
                // business identifiers, these are the identifiers that tie together related messages (Submission, Ack, Coding Response etc.)
                cmd.Parameters.AddWithValue("state_auxiliary_id", message.StateAuxiliaryIdentifier);
                cmd.Parameters.AddWithValue("cert_number", message.CertificateNumber);
                cmd.Parameters.AddWithValue("nchs_id", message.DeathJurisdictionID);
                
                // cmd.Parameters.AddWithValue("record", recordId);
                // cmd.Parameters.AddWithValue("status", ((int)Status.Sent)); 

                cmd.Prepare();        
                Console.WriteLine("\nPrepared");

                cmd.ExecuteNonQuery();
                con.Close();
            } catch (Exception e)
            {
                Console.WriteLine($"Error saving message {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
                con.Close();
            }
        }

        public static void updateMessageStatus(BaseMessage message, Status status)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                
                // insert new message
                var sql = "UPDATE message SET(status_id) VALUES (@status) WHERE state_auxiliary_id=@state AND cert_number=@cert AND nchs_id=@nchs;"; 
                
                using var cmd = new NpgsqlCommand(sql, con);
                // set status 
                cmd.Parameters.AddWithValue("status", ((int)status)); 
                
                // identifiers
                cmd.Parameters.AddWithValue("state", message.StateAuxiliaryIdentifier);
                cmd.Parameters.AddWithValue("cert", message.CertificateNumber);
                cmd.Parameters.AddWithValue("nchs", message.DeathJurisdictionID);

                cmd.Prepare();
                cmd.ExecuteNonQuery();
                con.Close();
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
                con.Close();
            }
        }

        // Acknowledgements are relevant to specific messages, not a message series (coding response, updates)
        public static void acknowledgeMessage(AckMessage message)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                
                // insert new message
                var sql = "UPDATE message SET(status_id) VALUES (@status) WHERE uid=@uid;"; 
                using var cmd = new NpgsqlCommand(sql, con);

                // set the acked message to acknowledged
                cmd.Parameters.AddWithValue("status", ((int)Status.Acknowledged)); 
                cmd.Parameters.AddWithValue("uid", message.AckedMessageId);

                cmd.Prepare();
                cmd.ExecuteNonQuery();
                con.Close();
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
                con.Close();
            }
        }

        public static void updateMessageResponse(BaseMessage message, String response)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                // add response to the message
                var sql = "UPDATE message SET(response) VALUES (@response) WHERE state_auxiliary_id=@state AND cert_number=@cert AND nchs_id=@nchs;;"; 
                using var cmd = new NpgsqlCommand(sql, con);
                
                // set status to sent, will update if it fails
                cmd.Parameters.AddWithValue("response", response);
                
                // identifiers
                cmd.Parameters.AddWithValue("state", message.StateAuxiliaryIdentifier);
                cmd.Parameters.AddWithValue("cert", message.CertificateNumber);
                cmd.Parameters.AddWithValue("nchs", message.DeathJurisdictionID);
                
                cmd.Prepare();
                cmd.ExecuteNonQuery();
                con.Close();
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
                con.Close();
            }
        }

        public static void updateMessageForResend(BaseMessage message)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                // add response to the message
                var sql = "UPDATE message SET last_submission=NOW(), retry = retry + 1, status = @status WHERE uid=@uid;"; 
                using var cmd = new NpgsqlCommand(sql, con);
                
                // set status to sent, will update if it fails
                cmd.Parameters.AddWithValue("status", ((int)Status.Sent)); 
                cmd.Parameters.AddWithValue("uid", message.MessageId);
                
                cmd.Prepare();
                cmd.ExecuteNonQuery();
                con.Close();
            } catch (Exception e)
            {
                Console.WriteLine($"Error updating message status {message.MessageId}");
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ",e.Message);
                con.Close();
            }
        }

    }
}