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
                        var recordId = record.Identifier;
                        var message = new DeathRecordSubmission(record);
                        message.MessageSource = jurisdictionEndPoint;

                        // Save it with it's business ids and status info
                        MessageItem item = new MessageItem();
                        item.Uid = message.MessageId;
                        item.StateAuxiliaryIdentifier = message.StateAuxiliaryIdentifier;
                        item.CertificateNumber = message.CertificateNumber;
                        item.DeathJurisdictionID = message.DeathJurisdictionID;
                        item.Message = message.ToJson().ToString();
                        item.Record = recordId;
                        item.Status = Models.MessageStatus.Pending;
                        item.Retries = 0;
                        item.SentOn = DateTime.UtcNow;
                        
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

        // DB functions

        public void UpdateMessageStatus(BaseMessage message, MessageStatus status)
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

        public void UpdateMessageResponse(BaseMessage message, String response)
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

        public static void UpdateMessageForResend(BaseMessage message)
        {
            try 
            {
                using var con = new NpgsqlConnection(cs);
                con.Open();
                // add response to the message
                var sql = "UPDATE message SET last_submission=NOW(), retry = retry + 1, status = @status WHERE uid=@uid;"; 
                using var cmd = new NpgsqlCommand(sql, con);
                
                // set status to sent, will update if it fails
                cmd.Parameters.AddWithValue("status", ((int)MessageStatus.Sent)); 
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
