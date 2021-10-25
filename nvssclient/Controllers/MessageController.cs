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
    public class MessageController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IServiceProvider Services;
        private static String cs = "Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres";
        private static String jurisdictionEndPoint = "https://example.com/jurisdiction/message/endpoint"; // make part of the configuration

        private static NpgsqlConnection con = new NpgsqlConnection(cs);
        private readonly IServiceScopeFactory _scopeFactory;

        public MessageController(AppDbContext context, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _scopeFactory = scopeFactory;
        }


        // POST: Records
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        // Receives a list of records to submit to the FHIR API
        [HttpPost]
        public async Task<ActionResult> PostIncomingRecords([FromBody] List<object> textList)
        {               
            try {
                using (var scope = _scopeFactory.CreateScope()){
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    foreach (object text in textList)
                    {
                        // Create a submission message for the record
                        DeathRecord record = new DeathRecord(text.ToString(), true);
                        var message = new DeathRecordSubmission(record);
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
            } catch {
                return BadRequest();
            }

            // return HTTP status code 204 (No Content)
            return NoContent();
        }

        // GET: Message Status
        [HttpGet]
        public async Task<ActionResult<List<MessageItem>>> GetMessageStatus()
        {
            // Return a list of messages and their status
            using (var scope = _scopeFactory.CreateScope()){
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var messages = context.MessageItems.ToList();
                return messages;
            }
        }
    }
}
