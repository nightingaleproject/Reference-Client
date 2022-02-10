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
        private readonly IServiceProvider Services;
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
            public String Response  {get; set;}

           
        }

        // POST: Submission Records
        // Wraps each record in a FHIR Submission message and queues the message to be sent to the NVSS API Server
        [HttpPost]
        [Route("submissions")]
        public async Task<ActionResult> SubmissionRecordHandler([FromBody] List<object> textList)
        {               
            try {
                foreach (object text in textList)
                {
                    // Create a submission message for the record
                    DeathRecord record = new DeathRecord(text.ToString(), true);
                    var message = new DeathRecordSubmission(record);
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
        public async Task<ActionResult> SubmissionRecordHandler([FromBody] object recordJson)
        {               
            try {
                // Create a submission message for the record
                DeathRecord record = new DeathRecord(recordJson.ToString(), true);
                var message = new DeathRecordSubmission(record);
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
        public async Task<ActionResult> UpdateRecordHandler([FromBody] List<object> textList)
        {             
            try {
                foreach (object text in textList)
                {
                    // Create a submission message for the record
                    DeathRecord record = new DeathRecord(text.ToString(), true);
                    var message = new DeathRecordUpdate(record);
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
        public async Task<ActionResult> UpdateRecordHandler([FromBody] object recordJson)
        {             
            try {
                // Create a submission message for the record
                DeathRecord record = new DeathRecord(recordJson.ToString(), true);
                var message = new DeathRecordUpdate(record);
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
        public async Task<ActionResult> VoidRecordHandler([FromBody] List<object> textList)
        {               
            try {
                foreach (object text in textList)
                {
                    // Create a submission message for the record
                    DeathRecord record = new DeathRecord(text.ToString(), true);
                    var message = new VoidMessage(record);
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
        public async Task<ActionResult> VoidRecordHandler([FromBody] object recordJson)
        {               
            try {
                // Create a submission message for the record
                DeathRecord record = new DeathRecord(recordJson.ToString(), true);
                var message = new VoidMessage(record);
                InsertMessageItem(message);
            } catch (Exception e){
                Console.WriteLine("Error Handling Record: {0}", e);
                return BadRequest();
            }
            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // GET: Record Status
        // Retrieves the most recent MessageItem with business identifiers that match the provided parameters
        // deathYear: the year of death in the VRDR record
        // jurisditionId: the jurisdiction Id in the VRDR record
        // certNo: the 5 digit certificate number in the VRDR record
        [HttpGet("status/{deathYear}/{jurisdictionId}/{certNo}")]
        public async Task<ActionResult<RecordResponse>> GetRecordStatus(uint deathYear, string jurisdictionId, string certNo)
        {
            try 
            {            
                // Return the most recent message with the given business identifiers and it's status
                using (var scope = _scopeFactory.CreateScope()){
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    uint certNoInt = UInt32.Parse(certNo);
                    var message = context.MessageItems.Where(s => s.CertificateNumber == certNoInt && s.DeathYear == deathYear && s.DeathJurisdictionID == jurisdictionId).OrderByDescending(s => s.CreatedDate).FirstOrDefault();
                    if (message == null) {
                        Console.WriteLine("Error Retrieving status, no record was found for the provided identifiers.");
                        return NotFound("Record not found");
                    }
                    
                    RecordResponse resp = new RecordResponse();
                    resp.Message = message;
                    // check if there is a response message
                    if (message.Status == MessageStatus.Error.ToString() || message.Status == MessageStatus.AcknowledgedAndCoded.ToString())
                    {
                        // get the most recent response message
                        var response = context.ResponseItems.Where(s => s.CertificateNumber == certNoInt && s.DeathYear == deathYear && s.DeathJurisdictionID == jurisdictionId).OrderByDescending(s => s.CreatedDate).FirstOrDefault();
                        resp.Response = response.Message;
                    }
                    return resp;
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
                    item.StateAuxiliaryIdentifier = message.StateAuxiliaryIdentifier;
                    item.CertificateNumber = message.CertificateNumber;
                    item.DeathJurisdictionID = message.DeathJurisdictionID;
                    item.DeathYear = message.DeathYear;
                    Console.WriteLine("Business IDs {0}, {1}, {2}", message.DeathYear, message.CertificateNumber, message.DeathJurisdictionID);
                    
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
