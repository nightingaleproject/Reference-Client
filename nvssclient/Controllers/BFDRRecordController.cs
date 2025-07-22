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
using BFDR;
using Microsoft.Extensions.DependencyInjection;

namespace NVSSClient.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class BFDRRecordController : ControllerBase
    {
        private readonly AppDbContext _context;
        private static String cs = "Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres";
        private readonly String _jurisdictionEndPoint;
        private static NpgsqlConnection con = new NpgsqlConnection(cs);
        private readonly IServiceScopeFactory _scopeFactory;

        public BFDRRecordController(AppDbContext context, IServiceScopeFactory scopeFactory)
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
                    BirthRecord record = new BirthRecord(text.ToString(), true);
                    var message = new BirthRecordSubmissionMessage(record);
                    InsertBFDRMessageItem(message);
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
                BirthRecord record = new BirthRecord(recordJson.ToString(), true);
                var message = new BirthRecordSubmissionMessage(record);
                InsertBFDRMessageItem(message);
            
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
                    BirthRecord record = new BirthRecord(text.ToString(), true);
                    var message = new BirthRecordSubmissionMessage(record);
                    InsertBFDRMessageItem(message);
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
                BirthRecord record = new BirthRecord(recordJson.ToString(), true);
                var message = new BirthRecordUpdateMessage(record);
                InsertBFDRMessageItem(message);
                
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
                    BirthRecord record = new BirthRecord(recordJson.ToString(), true);
                    
                    var message = new BirthRecordVoidMessage(record);
                    message.BlockCount = blockCount;
                    InsertBFDRMessageItem(message);
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
                BirthRecord record = new BirthRecord(recordJson.ToString(), true);
                
                var message = new BirthRecordVoidMessage(record);
                message.BlockCount = blockCount;
                InsertBFDRMessageItem(message);
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
        public ActionResult<List<MessageItem>> GetAllBFDRRecordStatus()
        {
            try 
            {            
                // Return the most recent message with the given business identifiers and it's status
                using (var scope = _scopeFactory.CreateScope()){

                    // get a distinct list of business identifiers submitted to the API
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    List<MessageItem> uniqueBussinessIds = context.MessageItems.OrderByDescending(s => s.CreatedDate).Select(p => new MessageItem()
                    {
                        JurisdictionID = p.JurisdictionID,
                        CertificateNumber = p.CertificateNumber,
                        EventYear = p.EventYear
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
                        var message = context.MessageItems.Where(s => s.CertificateNumber == ids.CertificateNumber && s.EventYear == ids.EventYear && s.JurisdictionID == ids.JurisdictionID).OrderByDescending(s => s.CreatedDate).FirstOrDefault();
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
        // birthYear: the year of birth in the BFDR record
        // jurisditionId: the jurisdiction Id in the BFDR record
        // certNo: the 5 digit certificate number in the BFDR record
        [HttpGet("{birthYear}/{jurisdictionId}/{certNo}")]
        public ActionResult<List<RecordResponse>> GetBFDRRecordStatus(uint eventYear, string jurisdictionId, string certNo)
        {
            try 
            {            
                // Return all messages with the given business identifiers and their responses if available
                using (var scope = _scopeFactory.CreateScope()){
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    uint certNoInt = UInt32.Parse(certNo);
                    List<MessageItem> messages = context.MessageItems.Where(s => s.CertificateNumber == certNoInt && s.EventYear == eventYear && s.JurisdictionID == jurisdictionId).OrderByDescending(s => s.CreatedDate).ToList();
                    if (messages == null) {
                        Console.WriteLine("Error, no messages were found for the provided identifiers.");
                        return NotFound("Record not found");
                    }
                    
                    List<RecordResponse> recordResponses = new List<RecordResponse>();
                    foreach(MessageItem msg in messages)
                    {
                        RecordResponse recordResp = new RecordResponse();
                        recordResp.Message = msg;
                        // get the response messages with the provided message reference uid
                        List<ResponseItem> respMsgs = context.ResponseItems.Where(s => s.ReferenceUid == msg.Uid).OrderBy(s => s.CreatedDate).ToList();
                        recordResp.Responses = respMsgs;

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

        // InsertBFDRMessageItem inserts the given message into the MessageItem table to get picked up by the TimedHostedService
        public void InsertBFDRMessageItem(BFDRBaseMessage message){
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
                    item.JurisdictionID = message.JurisdictionId;
                    item.EventYear = message.EventYear;
                    Console.WriteLine("Business IDs {0}, {1}, {2}", message.EventYear, message.CertNo, message.JurisdictionId);

                    item.IJE_Version = "BFDR_STU3_0";
                    item.VitalRecordType = "BFDR-BIRTH";

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
