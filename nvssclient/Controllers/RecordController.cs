using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        private static String jurisdictionEndPoint = "https://example.com/jurisdiction/message/endpoint"; // make part of the configuration

        private static NpgsqlConnection con = new NpgsqlConnection(cs);
        private readonly IServiceScopeFactory _scopeFactory;

        public RecordController(AppDbContext context, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _scopeFactory = scopeFactory;
        }


        // POST: Submission Records
        // Receives a new record to send to the FHIR API
        [HttpPost]
        [Route("submission")]
        public async Task<ActionResult> SubmissionRecordHandler([FromBody] List<object> textList)
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
        // Receives a record update to send to the FHIR API
        [HttpPost]
        [Route("update")]
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

        // POST: Void Records
        // Receives a record void to send to the FHIR API
        [HttpPost]
        [Route("void")]
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

        // GET: Record Status
        [HttpGet]
        public async Task<ActionResult<List<MessageItem>>> GetRecordStatus()
        {
            try 
            {            
                // Return a list of messages and their status
                using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var messages = context.MessageItems.ToList();
                return messages;
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("Error Retrieving status: {0}", e);
                return BadRequest();
            }

        }

        public void InsertMessageItem(BaseMessage message){
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                message.MessageSource = jurisdictionEndPoint;

                // Save it with it's business ids and status info
                MessageItem item = new MessageItem();
                item.Uid = message.MessageId;
                item.StateAuxiliaryIdentifier = message.StateAuxiliaryIdentifier;
                item.CertificateNumber = message.CertificateNumber;
                item.DeathJurisdictionID = message.DeathJurisdictionID;
                item.Message = message.ToJson().ToString();
                item.Status = Models.MessageStatus.Pending;
                item.Retries = 0;
                
                // insert new message
                context.MessageItems.Add(item);
                context.SaveChanges();
                Console.WriteLine($"Inserted message {item.Uid}");   
            }
        }
    }
}
