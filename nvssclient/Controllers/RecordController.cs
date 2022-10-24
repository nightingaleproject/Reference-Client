using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NVSSClient.Models;
using Npgsql;
using VRDR;
using Microsoft.Extensions.DependencyInjection;

namespace NVSSClient.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class RecordController : ControllerBase
    {
        private readonly AppDbContext _context;
        private static String cs = "Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres";
        private readonly String _jurisdictionEndPoint;
        private static NpgsqlConnection con = new NpgsqlConnection(cs);
        private readonly IServiceScopeFactory _scopeFactory;

        public RecordController(AppDbContext context, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _scopeFactory = scopeFactory;
            _jurisdictionEndPoint = Startup.StaticConfig.GetValue<string>("JurisdictionEndpoint");
        }

        public class RecordResponse
        {
            public MessageItem Message {get; set;}          
            public List<ResponseItem> Responses  {get; set;}

           
        }

        // POST: Submission Records
        // Wraps each record in a FHIR Submission message and queues the message to be sent to the NVSS API Server
        [HttpPost]
        [Route("submissions")]
        public IActionResult SubmissionRecordHandler([FromBody] List<object> textList)
        {               
            try {
                foreach (object text in textList)
                {
                    // Create a submission message for the record
                    DeathRecord record = new DeathRecord(text.ToString(), true);
                    var message = new DeathRecordSubmissionMessage(record);
                    InsertMessageItem(message);
                }
            } catch (Exception e){
                Console.WriteLine("Error Handling Record: {0}", e);
                return BadRequest();
            }

            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // POST: Submission Records
        // Wraps the record in a FHIR Submission message and queues the message to be sent to the NVSS API Server
        [HttpPost]
        [Route("submission")]
        public IActionResult SubmissionRecordHandler([FromBody] object recordJson)
        {               
            try {
                // Create a submission message for the record
                DeathRecord record = new DeathRecord(recordJson.ToString(), true);
                var message = new DeathRecordSubmissionMessage(record);
                InsertMessageItem(message);
            
            } catch (Exception e){
                Console.WriteLine("Error Handling Record: {0}", e);
                return BadRequest();
            }

            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // POST: Update Records
        // Wraps each record in a FHIR Update message and queues the message to be sent to the NVSS API Server
        [HttpPost]
        [Route("updates")]
        public IActionResult UpdateRecordHandler([FromBody] List<object> textList)
        {             
            try {
                foreach (object text in textList)
                {
                    // Create a submission message for the record
                    DeathRecord record = new DeathRecord(text.ToString(), true);
                    var message = new DeathRecordUpdateMessage(record);
                    InsertMessageItem(message);
                }
            } catch (Exception e){
                Console.WriteLine("Error Handling Record: {0}", e);
                return BadRequest();
            }

            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // POST: Update Records
        //Wraps the record in a FHIR Update message and queues the message to be sent to the NVSS API Server
        [HttpPost]
        [Route("update")]
        public IActionResult UpdateRecordHandler([FromBody] object recordJson)
        {             
            try {
                // Create a submission message for the record
                DeathRecord record = new DeathRecord(recordJson.ToString(), true);
                var message = new DeathRecordUpdateMessage(record);
                InsertMessageItem(message);
                
            } catch (Exception e){
                Console.WriteLine("Error Handling Record: {0}", e);
                return BadRequest();
            }

            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // POST: Void Records
        // Wraps each record in a FHIR Void message and queues the message to be sent to the NVSS API Server
        [HttpPost]
        [Route("voids")]
        public IActionResult VoidRecordHandler([FromBody] List<object> textList)
        {               
            try {
                foreach (object text in textList)
                {
                    // Create a submission message for the record
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(text.ToString());
                    object recordJson = dict["record"];
                    uint? blockCount = UInt32.Parse(dict["block_count"].ToString());
                    DeathRecord record = new DeathRecord(recordJson.ToString(), true);
                    
                    var message = new DeathRecordVoidMessage(record);
                    message.BlockCount = blockCount;
                    InsertMessageItem(message);
                }
            } catch (Exception e){
                Console.WriteLine("Error Handling Record: {0}", e);
                return BadRequest();
            }

            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // POST: Void Records
        // Wraps the record in a FHIR Void message and queues the message to be sent to the NVSS API Server
        [HttpPost]
        [Route("void")]
        public IActionResult VoidRecordHandler([FromBody] Dictionary<string,object> json)
        {               
            try {
                // Create a submission message for the record
                object recordJson = json["record"];
                uint? blockCount = UInt32.Parse(json["block_count"].ToString());
                DeathRecord record = new DeathRecord(recordJson.ToString(), true);
                
                var message = new DeathRecordVoidMessage(record);
                message.BlockCount = blockCount;
                InsertMessageItem(message);
            } catch (Exception e){
                Console.WriteLine("Error Handling Record: {0}", e);
                return BadRequest();
            }
            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // POST: Alias Records
        // Wraps each record in a FHIR Alias message and queues the message to be sent to the NVSS API Server
        [HttpPost]
        [Route("aliases")]
        public IActionResult AliasRecordHandler([FromBody] List<object> textList)
        {               
            try {
                foreach (object text in textList)
                {
                    // Create a submission message for the record
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(text.ToString());
                    object recordJson = dict["record"];
                    DeathRecord record = new DeathRecord(recordJson.ToString(), true);
                    string firstName = dict["alias_decedent_first_name"].ToString();
                    string lastName = dict["alias_decedent_last_name"].ToString();
                    string middleName = dict["alias_decedent_middle_name"].ToString();
                    string suffixName = dict["alias_decedent_name_suffix"].ToString();
                    string fatherSurname = dict["alias_father_surname"].ToString();
                    string ssn = dict["alias_social_security_number"].ToString();

                    var message = new DeathRecordAliasMessage(record);
                    message.AliasDecedentFirstName = firstName;
                    message.AliasDecedentLastName = lastName;
                    message.AliasDecedentMiddleName = middleName;
                    message.AliasDecedentNameSuffix = suffixName;
                    message.AliasFatherSurname = fatherSurname;
                    message.AliasSocialSecurityNumber = ssn;
                    InsertMessageItem(message);
                }
            } catch (Exception e){
                Console.WriteLine("Error Handling Record: {0}", e);
                return BadRequest();
            }

            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // POST: Alias Records
        // Wraps the record in a FHIR Alias message, sets the alias values and queues the message to be sent to the NVSS API Server
        [HttpPost]
        [Route("alias")]
        public IActionResult AliasRecordHandler([FromBody] Dictionary<string, object> data)
        {               
            try {
                // Create a submission message for the record
                object recordJson = data["record"];
                DeathRecord record = new DeathRecord(recordJson.ToString(), true);
                string firstName = data["alias_decedent_first_name"].ToString();
                string lastName = data["alias_decedent_last_name"].ToString();
                string middleName = data["alias_decedent_middle_name"].ToString();
                string suffixName = data["alias_decedent_name_suffix"].ToString();
                string fatherSurname = data["alias_father_surname"].ToString();
                string ssn = data["alias_social_security_number"].ToString();

                var message = new DeathRecordAliasMessage(record);
                message.AliasDecedentFirstName = firstName;
                message.AliasDecedentLastName = lastName;
                message.AliasDecedentMiddleName = middleName;
                message.AliasDecedentNameSuffix = suffixName;
                message.AliasFatherSurname = fatherSurname;
                message.AliasSocialSecurityNumber = ssn;

                InsertMessageItem(message);
            } catch (Exception e){
                Console.WriteLine("Error Handling Record: {0}", e);
                return BadRequest();
            }
            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // GET: List of Records
        // Retrieves return all the unique business identifier sets and the status for the latest message that was sent with those business ids
        [HttpGet]
        public ActionResult<List<MessageItem>> GetAllRecordStatus()
        {
            try 
            {            
                // Return the most recent message with the given business identifiers and it's status
                using (var scope = _scopeFactory.CreateScope()){

                    // get a distinct list of business identifiers submitted to the API
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    List<MessageItem> uniqueBussinessIds = context.MessageItems.OrderByDescending(s => s.CreatedDate).Select(p => new MessageItem()
                    {
                        DeathJurisdictionID = p.DeathJurisdictionID,
                        CertificateNumber = p.CertificateNumber,
                        DeathYear = p.DeathYear
                    }).Distinct().ToList();
                    if (uniqueBussinessIds == null) {
                        Console.WriteLine("No Records were found");
                        // return an empty list
                        return new List<MessageItem>();
                    }
                    
                    // for each set of business ids, find the latest submitted message and its status
                    List<MessageItem> latestMsgs = new List<MessageItem>();
                    foreach (MessageItem ids in uniqueBussinessIds)
                    {
                        var message = context.MessageItems.Where(s => s.CertificateNumber == ids.CertificateNumber && s.DeathYear == ids.DeathYear && s.DeathJurisdictionID == ids.DeathJurisdictionID).OrderByDescending(s => s.CreatedDate).FirstOrDefault();
                        latestMsgs.Add(message);
                    }

                    return latestMsgs;
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("Error Retrieving record statuses: {0}", e);
                return BadRequest();
            }

        }

        // GET: Record Status
        // Retrieves all the messages sent with the specified business ids
        // deathYear: the year of death in the VRDR record
        // jurisditionId: the jurisdiction Id in the VRDR record
        // certNo: the 5 digit certificate number in the VRDR record
        [HttpGet("{deathYear}/{jurisdictionId}/{certNo}")]
        public ActionResult<List<RecordResponse>> GetRecordStatus(uint deathYear, string jurisdictionId, string certNo)
        {
            try 
            {            
                // Return all messages with the given business identifiers and their responses if available
                using (var scope = _scopeFactory.CreateScope()){
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    uint certNoInt = UInt32.Parse(certNo);
                    List<MessageItem> messages = context.MessageItems.Where(s => s.CertificateNumber == certNoInt && s.DeathYear == deathYear && s.DeathJurisdictionID == jurisdictionId).OrderByDescending(s => s.CreatedDate).ToList();
                    if (messages == null) {
                        Console.WriteLine("Error, no messages were found for the provided identifiers.");
                        return NotFound("Record not found");
                    }
                    
                    List<RecordResponse> recordResponses = new List<RecordResponse>();
                    foreach(MessageItem msg in messages)
                    {
                        RecordResponse recordResp = new RecordResponse();
                        recordResp.Message = msg;
                        // check if there are response messages 
                        if (msg.Status == MessageStatus.Error.ToString() || msg.Status == MessageStatus.AcknowledgedAndCoded.ToString())
                        {
                            // get the response messages with these business ids
                            // TODO once we've added in response identifiers to link responses to submissions
                            // change the conditions from business ids to check for the response identifier
                            // so responses are specifically linked to the message that generated them
                            List<ResponseItem> respMsgs = context.ResponseItems.Where(s => s.ReferenceUid == msg.Uid).OrderBy(s => s.CreatedDate).ToList();
                            recordResp.Responses = respMsgs;
                        }
                        recordResponses.Add(recordResp);

                    }

                    return recordResponses;
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("Error Retrieving status: {0}", e);
                return BadRequest();
            }

        }

        // InsertMessageItem inserts the given message into the MessageItem table to get picked up by the TimedHostedService
        public void InsertMessageItem(BaseMessage message){
            try
            {
                using (var scope = _scopeFactory.CreateScope()){
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    message.MessageSource = _jurisdictionEndPoint;

                    MessageItem item = new MessageItem();
                    item.Uid = message.MessageId;
                    item.Message = message.ToJson().ToString();
                    
                    // Business Identifiers
                    item.StateAuxiliaryIdentifier = message.StateAuxiliaryId;
                    item.CertificateNumber = message.CertNo;
                    item.DeathJurisdictionID = message.JurisdictionId;
                    item.DeathYear = message.DeathYear;
                    Console.WriteLine("Business IDs {0}, {1}, {2}", message.DeathYear, message.CertNo, message.JurisdictionId);
                    
                    // Status info
                    item.Status = Models.MessageStatus.Pending.ToString();
                    item.Retries = 0;
                    
                    // insert new message
                    context.MessageItems.Add(item);
                    context.SaveChanges();
                    Console.WriteLine($"Inserted message {item.Uid}");   
                }
            }
            catch 
            {
                Console.WriteLine($"Error saving message {message.MessageId} for submission"); 
            }

        }
    }
}
